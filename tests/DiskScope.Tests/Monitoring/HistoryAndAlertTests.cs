using DiskScope.Core;
using DiskScope.Diagnostics;
using DiskScope.History;

namespace DiskScope.Tests.Monitoring;

public sealed class HistoryAndAlertTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static MetricBatch Batch(DateTime t, params MetricValue[] values) =>
        new("test", t, values, Array.Empty<CollectorError>(), TimeSpan.Zero);

    [Fact]
    public void Store_AggregatesIntoBuckets_AndLeavesGapsForUnavailable()
    {
        var store = new InMemoryMetricStore([new RetentionTier(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1))]);
        store.Append(Batch(T0, MetricValue.Measured("m", 10)));
        store.Append(Batch(T0.AddSeconds(5), MetricValue.Measured("m", 30)));
        store.Append(Batch(T0.AddSeconds(12), MetricValue.Unavailable("m")));
        store.Append(Batch(T0.AddSeconds(25), MetricValue.Measured("m", 5)));

        IReadOnlyList<MetricPoint> points = store.Query("m", T0, T0.AddMinutes(1));

        Assert.Equal(2, points.Count); // bucket 10-20 s has no available value: a gap, not a zero
        Assert.Equal(new MetricPoint(T0, 20, 10, 30, 2), points[0]);
        Assert.Equal(5, points[1].Average);
    }

    [Fact]
    public void Store_RingKeepsOnlyCapacity_AndQueryFallsBackToCoarserTier()
    {
        var store = new InMemoryMetricStore(
        [
            new RetentionTier(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)),
            new RetentionTier(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10)),
        ]);
        for (int s = 0; s < 60; s++)
        {
            store.Append(Batch(T0.AddSeconds(s), MetricValue.Measured("m", s)));
        }

        IReadOnlyList<MetricPoint> recent = store.Query("m", T0.AddSeconds(55), T0.AddSeconds(59));
        IReadOnlyList<MetricPoint> all = store.Query("m", T0, T0.AddSeconds(59));

        Assert.Equal(5, recent.Count); // fine tier still covers the last 10 s
        Assert.Equal(6, all.Count); // fine tier dropped the start: 10 s buckets
        Assert.Equal(4.5, all[0].Average);
        Assert.Equal(10, all[0].Samples);
    }

    [Fact]
    public void Store_RangeStartingBeforeFirstValue_UsesFinestTier()
    {
        // Regression: right after start-up no tier covers "now - 1 min", and the query used to fall back to the
        // 1 min tier, giving a single point instead of the 10 raw seconds.
        var store = new InMemoryMetricStore();
        for (int s = 0; s < 10; s++)
        {
            store.Append(Batch(T0.AddSeconds(s), MetricValue.Measured("m", s)));
        }

        Assert.Equal(10, store.Query("m", T0.AddSeconds(9 - 60), T0.AddSeconds(9)).Count);
    }

    [Fact]
    public void Store_InvalidTiers_Throw()
    {
        Assert.Throws<ArgumentException>(() => new InMemoryMetricStore([]));
        Assert.Throws<ArgumentException>(() => new InMemoryMetricStore(
            [new RetentionTier(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1)), new RetentionTier(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5))]));
    }

    private static ThresholdRule CpuRule => DefaultRules.Create().Single(r => r.Id == DefaultRules.HighCpu);

    private static List<AlertEvent> Feed(AlertEngine engine, int fromSecond, int toSecond, double value)
    {
        var changed = new List<AlertEvent>();
        for (int s = fromSecond; s <= toSecond; s++)
        {
            changed.AddRange(engine.Evaluate(Batch(T0.AddSeconds(s), MetricValue.Derived("cpu.total", value))));
        }

        return changed;
    }

    [Fact]
    public void Alert_RaisedOnlyAfterMinDuration_ThenEndsAfterRecovery()
    {
        var engine = new AlertEngine([CpuRule]);

        Assert.Empty(Feed(engine, 0, 29, 95)); // 29 s over the threshold: not yet
        AlertEvent raised = Assert.Single(Feed(engine, 30, 40, 97));
        Assert.Equal(T0, raised.StartedUtc);
        Assert.Null(raised.EndedUtc);
        Assert.Equal(FindingLevel.Alert, raised.Level);

        AlertEvent ended = Assert.Single(Feed(engine, 41, 52, 20));
        Assert.Equal(T0.AddSeconds(41), ended.EndedUtc);
        Assert.Equal(97, ended.Evidence[^1].Value);
    }

    [Fact]
    public void Alert_ShortDipDoesNotEndEpisode_AndComebackWithinCooldownReopensIt()
    {
        var engine = new AlertEngine([CpuRule]);
        Feed(engine, 0, 40, 95);
        Feed(engine, 41, 45, 50); // 5 s dip < 10 s recovery
        Feed(engine, 46, 60, 95);
        Assert.Single(engine.Events);
        Assert.Null(engine.Events[0].EndedUtc);

        Feed(engine, 61, 75, 10); // ends at 61
        Feed(engine, 80, 115, 95); // back 19 s after the end, inside the 1 min cooldown

        AlertEvent only = Assert.Single(engine.Events);
        Assert.Null(only.EndedUtc);
    }

    [Fact]
    public void Alert_UnavailableValues_DoNotStartOrEndEpisodes()
    {
        var engine = new AlertEngine([CpuRule]);
        for (int s = 0; s < 100; s++)
        {
            engine.Evaluate(Batch(T0.AddSeconds(s), MetricValue.Unavailable("cpu.total")));
        }

        Assert.Empty(engine.Events);
    }

    [Fact]
    public void Alert_DisabledOrRaisedThreshold_IsRespected_AndAcknowledgeWorks()
    {
        var engine = new AlertEngine([CpuRule]);
        engine.UpdateRule(CpuRule with { Threshold = 99 });
        Assert.Empty(Feed(engine, 0, 60, 95));

        engine.UpdateRule(CpuRule with { Enabled = false });
        Assert.Empty(Feed(engine, 61, 200, 100));

        engine.UpdateRule(CpuRule);
        AlertEvent e = Feed(engine, 201, 240, 100).First();
        Assert.True(engine.Acknowledge(e.Id));
        Assert.True(engine.Events.Single().Acknowledged);
        Assert.False(engine.Acknowledge(Guid.NewGuid()));
    }

    [Fact]
    public void Alert_BelowRule_LowMemory()
    {
        ThresholdRule rule = DefaultRules.Create().Single(r => r.Id == DefaultRules.LowAvailableMemory);
        var engine = new AlertEngine([rule]);
        for (int s = 0; s <= 10; s++)
        {
            engine.Evaluate(Batch(T0.AddSeconds(s), MetricValue.Measured("mem.physical.available", 100.0 * 1024 * 1024)));
        }

        Assert.Contains("below 512.0 MB", Assert.Single(engine.Events).Explanation);
    }

    [Fact]
    public void Growth_SteadyRise_IsSuspected_PlateauAndNoiseAreNot()
    {
        var detector = new MemoryGrowthDetector<int>();
        const long Mb = 1024 * 1024;
        for (int m = 0; m <= 8; m++)
        {
            detector.Observe(1, T0.AddMinutes(m), (500 + 40 * m) * Mb); // steady +40 MB/min
            detector.Observe(2, T0.AddMinutes(m), (m < 2 ? 500 + 200 * m : 900) * Mb); // jump then plateau
            detector.Observe(3, T0.AddMinutes(m), (500 + (m % 2 == 0 ? 300 : 0)) * Mb); // oscillation
        }

        GrowthSuspicion<int> s = Assert.Single(detector.Evaluate());
        Assert.Equal(1, s.Key);
        Assert.Equal(40.0 * Mb, s.GrowthBytesPerMinute, 1);
    }

    [Fact]
    public void Growth_ShortObservation_IsNotSuspected_AndForgetDropsExited()
    {
        var detector = new MemoryGrowthDetector<int>();
        for (int m = 0; m < 4; m++)
        {
            detector.Observe(1, T0.AddMinutes(m), (100 + 500 * m) * 1024L * 1024);
        }

        Assert.Empty(detector.Evaluate()); // 3 min < 5 min minimum span

        detector.Forget(T0.AddMinutes(10));
        Assert.Equal(0, detector.TrackedCount);
    }

    [Fact]
    public void Formatter_UnknownIsNotZero()
    {
        Assert.Equal("n/a", MetricFormatter.Format(null, MetricUnit.Bytes));
        Assert.Equal("1.5 GB", MetricFormatter.Format(1.5 * 1024 * 1024 * 1024, MetricUnit.Bytes));
        Assert.Equal("12.3 %", MetricFormatter.Format(12.34, MetricUnit.Percent));
    }
}
