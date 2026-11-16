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
