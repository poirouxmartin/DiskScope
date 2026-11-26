using DiskScope.Core;
using DiskScope.Monitoring;

namespace DiskScope.Tests.Monitoring;

public sealed class MonitoringSchedulerTests
{
    [Fact]
    public void MetricValue_NonFiniteOrUnavailable_HasNoValue()
    {
        Assert.Null(MetricValue.Derived("x", double.NaN).Value);
        Assert.False(MetricValue.Measured("x", double.PositiveInfinity).IsAvailable);
        Assert.Null(MetricValue.Unavailable("x").Value);
        Assert.Equal(42.0, MetricValue.Measured("x", 42).Value);
    }

    [Fact]
    public void ThrowingCollector_PublishesUnavailableValuesAndError_OthersUnaffected()
    {
        var good = new FakeCollector("good", "g.value", () => 7);
        var bad = new FakeCollector("bad", "b.value", () => throw new InvalidOperationException("boom"));
        var scheduler = new MonitoringScheduler([good, bad]);

        IReadOnlyList<MetricBatch> batches = scheduler.CollectOnce();

        MetricBatch badBatch = batches.Single(b => b.Collector == "bad");
        Assert.All(badBatch.Values, v => Assert.False(v.IsAvailable));
        Assert.Contains("boom", Assert.Single(badBatch.Errors).Message);
        Assert.Equal(7.0, scheduler.GetLatest("g.value")!.Value.Value);
        Assert.False(scheduler.GetLatest("b.value")!.Value.IsAvailable);
        Assert.Equal(1, scheduler.GetStats().Single(s => s.Collector == "bad").Failures);
    }

    [Fact]
    public void UnavailableCollector_IsNeverCalled()
    {
        int calls = 0;
        var off = new FakeCollector("off", "o.value", () => ++calls, available: false);
        var scheduler = new MonitoringScheduler([off]);

        Assert.Empty(scheduler.CollectOnce());
        Assert.Equal(0, calls);
        Assert.False(scheduler.GetStats().Single().Availability.IsAvailable);
        Assert.Null(scheduler.GetLatest("o.value"));
    }

    [Fact]
    public void FailingStore_IsCounted_AndBatchIsStillPublished()
    {
        var scheduler = new MonitoringScheduler([new FakeCollector("c", "c.v", () => 1)], new ThrowingStore());
        var published = new List<MetricBatch>();
        scheduler.BatchPublished += published.Add;

        scheduler.CollectOnce();

        Assert.Single(published);
        Assert.Equal(1, scheduler.StoreFailures);
    }

    [Fact]
    public async Task Started_PublishesRepeatedly_WithIncreasingTimestamps_AndRefusesCollectOnce()
    {
        var scheduler = new MonitoringScheduler(
            [new FakeCollector("c", "c.v", () => 1)],
            intervals: new Dictionary<string, TimeSpan> { ["c"] = TimeSpan.FromMilliseconds(20) });
        var stamps = new List<DateTime>();
        var enough = new TaskCompletionSource();
        scheduler.BatchPublished += b =>
        {
            lock (stamps)
            {
                stamps.Add(b.TimestampUtc);
                if (stamps.Count >= 4)
                {
                    enough.TrySetResult();
                }
            }
        };

        scheduler.Start();
        Assert.Throws<InvalidOperationException>(() => scheduler.CollectOnce());
        await enough.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await scheduler.StopAsync();

        lock (stamps)
        {
            for (int i = 1; i < stamps.Count; i++)
            {
                Assert.True(stamps[i] > stamps[i - 1]);
            }
        }
    }

    [Fact]
    public void Stats_SeparateCpuTimeFromWaiting()
    {
        // "busy" burns 50 ms of its own CPU time per collection; "idle" sleeps 50 ms and burns almost none.
        double Busy()
        {
            TimeSpan start = ThreadCpuClock.Current()!.Value;
            var guard = System.Diagnostics.Stopwatch.StartNew();
            while (ThreadCpuClock.Current()!.Value - start < TimeSpan.FromMilliseconds(50) && guard.Elapsed < TimeSpan.FromSeconds(5))
            {
            }

            return 1;
        }

        var scheduler = new MonitoringScheduler(
        [
            new FakeCollector("busy", "busy.v", Busy),
            new FakeCollector("idle", "idle.v", () => { Thread.Sleep(50); return 1; }),
        ]);

        for (int i = 0; i < 4; i++)
        {
            scheduler.CollectOnce();
        }

        CollectorStats busy = scheduler.GetStats().Single(s => s.Collector == "busy");
        CollectorStats idle = scheduler.GetStats().Single(s => s.Collector == "idle");
        Assert.True(busy.TotalCpuTime >= TimeSpan.FromMilliseconds(200), $"busy cpu {busy.TotalCpuTime}");
        Assert.True(idle.TotalDuration >= TimeSpan.FromMilliseconds(200));

        // Clock-tick accounting may charge a tick to a sleeping thread now and then, never the whole sleep.
        Assert.True(idle.TotalCpuTime < idle.TotalDuration / 2, $"idle cpu {idle.TotalCpuTime}, wall {idle.TotalDuration}");
        Assert.EndsWith(" ms", idle.MeanCpuText);
    }

    [Fact]
    public void Stats_BeforeAnyCollection_ShowUnknownCpu()
    {
        var scheduler = new MonitoringScheduler([new FakeCollector("c", "c.v", () => 1)]);

        CollectorStats stats = scheduler.GetStats().Single();

        Assert.True(double.IsNaN(stats.MeanCpuMilliseconds));
        Assert.Equal("n/a", stats.MeanCpuText);
    }

    [Fact]
    public void NonPositiveInterval_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonitoringScheduler(
            [new FakeCollector("c", "c.v", () => 1)],
            intervals: new Dictionary<string, TimeSpan> { ["c"] = TimeSpan.Zero }));
    }

    [Fact]
    public void Clock_AdvancesWithStopwatch_NotWallClock()
    {
        var anchor = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new MonitoringClock(anchor, 1000);

        Assert.Equal(anchor, clock.At(1000));
        Assert.Equal(anchor + TimeSpan.FromSeconds(1), clock.At(1000 + System.Diagnostics.Stopwatch.Frequency));
    }

    [Fact]
    public void ThrowingSubscriber_IsIsolated_OtherSubscribersAndLoopContinue()
    {
        var scheduler = new MonitoringScheduler([new FakeCollector("c", "c.v", () => 1)]);
        var received = new List<MetricBatch>();
        scheduler.BatchPublished += _ => throw new InvalidOperationException("subscriber");
        scheduler.BatchPublished += received.Add;

        scheduler.CollectOnce();
        scheduler.CollectOnce();

        Assert.Equal(2, received.Count);
        Assert.Equal(2, scheduler.SubscriberFailures);
    }

    [Fact]
    public async Task ThrowingSubscriber_WhileStarted_LoopKeepsRunning()
    {
        var scheduler = new MonitoringScheduler(
            [new FakeCollector("c", "c.v", () => 1)],
            intervals: new Dictionary<string, TimeSpan> { ["c"] = TimeSpan.FromMilliseconds(20) });
        int count = 0;
        var enough = new TaskCompletionSource();
        scheduler.BatchPublished += _ => throw new InvalidOperationException("subscriber");
        scheduler.BatchPublished += _ =>
        {
            if (Interlocked.Increment(ref count) >= 3)
            {
                enough.TrySetResult();
            }
        };

        scheduler.Start();
        await enough.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await scheduler.StopAsync();

        Assert.True(scheduler.SubscriberFailures >= 3);
    }

    internal sealed class FakeCollector(string name, string metricId, Func<double> read, bool available = true) : IMetricCollector
    {
        public string Name => name;

        public IReadOnlyList<MetricDescriptor> Metrics { get; } = [new MetricDescriptor(metricId, metricId, MetricUnit.Count, "fake")];

        public TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);

        public CollectorAvailability CheckAvailability() =>
            available ? CollectorAvailability.Available("fake") : CollectorAvailability.NotAvailable("off");

        public CollectorResult Collect(CancellationToken cancellationToken) =>
            CollectorResult.Of([MetricValue.Measured(metricId, read())]);
    }

    private sealed class ThrowingStore : IMetricStore
    {
        public void Append(MetricBatch batch) => throw new IOException("disk full");

        public IReadOnlyList<MetricPoint> Query(string metricId, DateTime fromUtc, DateTime toUtc) => [];
    }
}
