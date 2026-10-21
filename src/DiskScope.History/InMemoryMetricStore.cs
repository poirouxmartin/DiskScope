using DiskScope.Core;

namespace DiskScope.History;

/// <summary>One retention level: values are averaged into buckets of <see cref="Resolution"/>, kept for <see cref="Duration"/>.</summary>
public sealed record RetentionTier(TimeSpan Resolution, TimeSpan Duration)
{
    /// <summary>Spec defaults (section 4): 1 s for 10 min, 5 s for 1 h, 1 min for 24 h.</summary>
    public static IReadOnlyList<RetentionTier> Defaults { get; } =
    [
        new(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10)),
        new(TimeSpan.FromSeconds(5), TimeSpan.FromHours(1)),
        new(TimeSpan.FromMinutes(1), TimeSpan.FromHours(24)),
    ];

    public int Capacity => (int)Math.Ceiling(Duration / Resolution);
}

/// <summary>
/// Local history kept in memory, with fixed-size ring buffers per metric and tier, so memory is bounded
/// (about 40 bytes x (600 + 720 + 1440) buckets per metric with the defaults) and old points are aggregated
/// instead of kept raw. Every value is written to every tier on arrival, so a coarse tier never depends on a fine
/// one having kept its data. Unavailable values are not stored: they leave gaps.
/// Not persisted: history is lost when the application exits (persistence is a later step, see ROADMAP.md).
/// Thread-safe: appends come from scheduler threads, queries from the UI thread.
/// </summary>
public sealed class InMemoryMetricStore : IMetricStore
{
    private readonly IReadOnlyList<RetentionTier> _tiers;
    private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);
    private readonly Lock _seriesGate = new();

    public InMemoryMetricStore(IReadOnlyList<RetentionTier>? tiers = null)
    {
        _tiers = tiers ?? RetentionTier.Defaults;
        if (_tiers.Count == 0)
        {
            throw new ArgumentException("At least one retention tier is required.", nameof(tiers));
        }

        for (int i = 0; i < _tiers.Count; i++)
        {
            if (_tiers[i].Resolution <= TimeSpan.Zero || _tiers[i].Duration < _tiers[i].Resolution)
            {
                throw new ArgumentException($"Tier {i}: resolution must be positive and not longer than the duration.", nameof(tiers));
            }

            if (i > 0 && _tiers[i].Resolution <= _tiers[i - 1].Resolution)
            {
                throw new ArgumentException("Tiers must be ordered from finest to coarsest resolution.", nameof(tiers));
            }
        }
    }

    public IReadOnlyList<RetentionTier> Tiers => _tiers;

    public void Append(MetricBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        foreach (MetricValue value in batch.Values)
        {
            if (value.Value is { } v)
            {
                GetSeries(value.Id).Add(batch.TimestampUtc, v);
            }
        }
    }

    public IReadOnlyList<MetricPoint> Query(string metricId, DateTime fromUtc, DateTime toUtc)
    {
        Series? series;
        lock (_seriesGate)
        {
            _series.TryGetValue(metricId, out series);
        }

        return series is null || toUtc < fromUtc ? Array.Empty<MetricPoint>() : series.Query(fromUtc, toUtc);
    }

    private Series GetSeries(string id)
    {
        lock (_seriesGate)
        {
            if (!_series.TryGetValue(id, out Series? s))
            {
                _series[id] = s = new Series(_tiers);
            }

            return s;
        }
    }

    private sealed class Series
    {
        private readonly Ring[] _rings;
        private readonly Lock _gate = new();

        public Series(IReadOnlyList<RetentionTier> tiers) => _rings = tiers.Select(t => new Ring(t)).ToArray();

        public void Add(DateTime timestampUtc, double value)
        {
            lock (_gate)
            {
                foreach (Ring ring in _rings)
                {
                    ring.Add(timestampUtc.Ticks, value);
                }
            }
        }

        public IReadOnlyList<MetricPoint> Query(DateTime fromUtc, DateTime toUtc)
        {
            lock (_gate)
            {
                // Finest tier that still covers the start of the range; otherwise the coarsest, which reaches furthest back.
                Ring chosen = _rings[^1];
                foreach (Ring ring in _rings)
                {
                    if (ring.Count > 0 && ring.OldestStartTicks <= fromUtc.Ticks)
                    {
                        chosen = ring;
                        break;
                    }
                }

                return chosen.Query(fromUtc.Ticks, toUtc.Ticks);
            }
        }
    }

    private sealed class Ring(RetentionTier tier)
    {
        private readonly long _resolution = tier.Resolution.Ticks;
        private readonly Bucket[] _buckets = new Bucket[tier.Capacity];
        private int _head = -1; // index of the newest bucket

        public int Count { get; private set; }

        public long OldestStartTicks => _buckets[(_head - Count + 1 + _buckets.Length) % _buckets.Length].Start;

        public void Add(long ticks, double value)
        {
            long start = ticks - (ticks % _resolution);
            if (Count > 0 && _buckets[_head].Start == start)
            {
                _buckets[_head].Add(value);
                return;
            }

            if (Count > 0 && start < _buckets[_head].Start)
            {
                return; // Out-of-order value older than the current bucket: dropped rather than reordering the ring.
            }

            _head = (_head + 1) % _buckets.Length;
            _buckets[_head] = new Bucket(start, value);
            Count = Math.Min(Count + 1, _buckets.Length);
        }

        public IReadOnlyList<MetricPoint> Query(long from, long to)
        {
            var result = new List<MetricPoint>();
            for (int i = Count - 1; i >= 0; i--)
            {
                ref Bucket b = ref _buckets[(_head - i + _buckets.Length) % _buckets.Length];
                if (b.Start >= from - _resolution + 1 && b.Start <= to)
                {
                    result.Add(new MetricPoint(new DateTime(b.Start, DateTimeKind.Utc), b.Sum / b.Samples, b.Min, b.Max, b.Samples));
                }
            }

            return result;
        }
    }

    private struct Bucket(long start, double value)
    {
        public readonly long Start = start;
        public double Sum = value;
        public double Min = value;
        public double Max = value;
        public int Samples = 1;

        public void Add(double value)
        {
            Sum += value;
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            Samples++;
        }
    }
}
