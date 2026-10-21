using System.Collections.Concurrent;
using System.Diagnostics;
using DiskScope.Core;

namespace DiskScope.Monitoring;

/// <summary>Cost and reliability of one collector, for the self-monitoring view and the benchmark.</summary>
public sealed record CollectorStats(
    string Collector,
    CollectorAvailability Availability,
    TimeSpan Interval,
    long Collections,
    long Failures,
    long SkippedTicks,
    TimeSpan LastDuration,
    TimeSpan MaxDuration,
    TimeSpan TotalDuration);

/// <summary>
/// Runs each available collector on its own background loop at its interval, isolates failures, measures the cost
/// of every collection, and publishes batches. Never touches WPF: subscribers marshal to their own thread.
/// A collector that throws publishes a batch where all its metrics are Unavailable, plus the error; the other
/// collectors keep running. Ticks missed because a collection ran long are counted, not replayed.
/// </summary>
public sealed class MonitoringScheduler : IAsyncDisposable
{
    private readonly List<Entry> _entries = new();
    private readonly MonitoringClock _clock;
    private readonly IMetricStore? _store;
    private readonly ConcurrentDictionary<string, MetricValue> _latest = new();
    private CancellationTokenSource? _cts;
    private Task[] _loops = Array.Empty<Task>();

    public MonitoringScheduler(
        IEnumerable<IMetricCollector> collectors,
        IMetricStore? store = null,
        MonitoringClock? clock = null,
        IReadOnlyDictionary<string, TimeSpan>? intervals = null)
    {
        ArgumentNullException.ThrowIfNull(collectors);
        _clock = clock ?? new MonitoringClock();
        _store = store;
        foreach (IMetricCollector collector in collectors)
        {
            TimeSpan interval = intervals is not null && intervals.TryGetValue(collector.Name, out TimeSpan custom)
                ? custom
                : collector.DefaultInterval;
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(intervals), $"Interval of {collector.Name} must be positive.");
            }

            _entries.Add(new Entry(collector, interval, SafeAvailability(collector)));
        }
    }

    /// <summary>Raised on a scheduler thread after each collection, after the store and the latest values are updated.</summary>
    public event Action<MetricBatch>? BatchPublished;

    /// <summary>Raised when the store throws; collection goes on and the batch is still published.</summary>
    public event Action<Exception>? StoreFailed;

    public long StoreFailures => Interlocked.Read(ref _storeFailures);

    private long _storeFailures;

    public IReadOnlyList<MetricDescriptor> Metrics =>
        _entries.SelectMany(e => e.Collector.Metrics).ToArray();

    /// <summary>Last published value of a metric, or null if none was published yet.</summary>
    public MetricValue? GetLatest(string metricId) =>
        _latest.TryGetValue(metricId, out MetricValue v) ? v : null;

    public IReadOnlyList<CollectorStats> GetStats() => _entries.Select(e => e.Snapshot()).ToArray();

    public void Start()
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("Already started.");
        }

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        _loops = _entries
            .Where(e => e.Availability.IsAvailable)
            .Select(e => Task.Factory.StartNew(() => RunLoopAsync(e, ct), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap())
            .ToArray();
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_loops).ConfigureAwait(false);
        _cts.Dispose();
        _cts = null;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    /// <summary>
    /// Runs every available collector once, synchronously. Used by tests and benchmarks.
    /// Refused while started: collectors are never called concurrently with themselves.
    /// </summary>
    public IReadOnlyList<MetricBatch> CollectOnce(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("CollectOnce cannot run while the scheduler is started.");
        }

        return _entries.Where(e => e.Availability.IsAvailable).Select(e => CollectAndPublish(e, cancellationToken)).ToArray();
    }

    private async Task RunLoopAsync(Entry entry, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(entry.Interval);
        CollectAndPublish(entry, ct);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                CollectAndPublish(entry, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private MetricBatch CollectAndPublish(Entry entry, CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        CollectorResult result;
        bool failed = false;
        try
        {
            result = entry.Collector.Collect(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failed = true;
            result = new CollectorResult(
                entry.Collector.Metrics.Select(m => MetricValue.Unavailable(m.Id)).ToArray(),
                new[] { new CollectorError(entry.Collector.Name, $"{ex.GetType().Name}: {ex.Message}") });
        }

        long end = Stopwatch.GetTimestamp();
        TimeSpan duration = Stopwatch.GetElapsedTime(start, end);
        entry.Record(duration, failed);

        var batch = new MetricBatch(entry.Collector.Name, _clock.At(end), result.Values, result.Errors, duration);
        foreach (MetricValue value in batch.Values)
        {
            _latest[value.Id] = value;
        }

        if (_store is not null)
        {
            try
            {
                _store.Append(batch);
            }
            catch (Exception ex)
            {
                // History may be unavailable (disk full, locked file): monitoring goes on, the loss is counted and reported.
                Interlocked.Increment(ref _storeFailures);
                StoreFailed?.Invoke(ex);
            }
        }

        BatchPublished?.Invoke(batch);
        return batch;
    }

    private static CollectorAvailability SafeAvailability(IMetricCollector collector)
    {
        try
        {
            return collector.CheckAvailability();
        }
        catch (Exception ex)
        {
            return CollectorAvailability.NotAvailable($"Availability check failed: {ex.Message}");
        }
    }

    private sealed class Entry(IMetricCollector collector, TimeSpan interval, CollectorAvailability availability)
    {
        private readonly Lock _gate = new();
        private long _collections;
        private long _failures;
        private long _skipped;
        private TimeSpan _last;
        private TimeSpan _max;
        private TimeSpan _total;

        public IMetricCollector Collector { get; } = collector;

        public TimeSpan Interval { get; } = interval;

        public CollectorAvailability Availability { get; } = availability;

        public void Record(TimeSpan duration, bool failed)
        {
            lock (_gate)
            {
                _collections++;
                _failures += failed ? 1 : 0;

                // PeriodicTimer coalesces ticks missed during a long collection; count them so the miss rate can be published.
                _skipped += duration > Interval ? (long)(duration / Interval) : 0;
                _last = duration;
                _max = duration > _max ? duration : _max;
                _total += duration;
            }
        }

        public CollectorStats Snapshot()
        {
            lock (_gate)
            {
                return new CollectorStats(Collector.Name, Availability, Interval, _collections, _failures, _skipped, _last, _max, _total);
            }
        }
    }
}
