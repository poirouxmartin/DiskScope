using DiskScope.Core;
using DiskScope.Diagnostics;
using DiskScope.History;
using DiskScope.Monitoring;

namespace DiskScope.Tests.Monitoring;

public sealed class DiagnosticTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static AlertEvent Episode(string rule, int fromS, int? toS) =>
        new(Guid.NewGuid(), rule, "Sustained high CPU", FindingLevel.Alert, T0.AddSeconds(fromS), toS is { } t ? T0.AddSeconds(t) : null, "", [], false);

    private static InMemoryMetricStore DiskHistory(int fromS, int toS, double value)
    {
        var store = new InMemoryMetricStore();
        for (int s = fromS; s <= toS; s++)
        {
            store.Append(new MetricBatch("Disk", T0.AddSeconds(s), [MetricValue.Derived("disk.active", value)], [], TimeSpan.Zero));
        }

        return store;
    }

    [Fact]
    public void HighCpuEpisode_WithBusyDisk_GivesALowConfidenceHypothesis()
    {
        DiagnosticEngine engine = DiagnosticEngine.CreateDefault();
        var context = new DiagnosticContext([Episode(DefaultRules.HighCpu, 0, 60)], DiskHistory(0, 60, 85), T0.AddSeconds(120));

        (IReadOnlyList<Finding> findings, IReadOnlyList<string> errors) = engine.Evaluate(context);

        Finding f = Assert.Single(findings);
        Assert.Empty(errors);
        Assert.Equal(FindingLevel.Hypothesis, f.Level);
        Assert.Equal(Confidence.Low, f.Confidence);
        Assert.Contains("not proof of causality", f.Limits);
        Assert.Equal(85, f.Evidence[0].Value);
        Assert.NotEmpty(f.Checks);
    }

    [Fact]
    public void QuietDisk_OrMissingHistory_GivesNothing()
    {
        DiagnosticEngine engine = DiagnosticEngine.CreateDefault();

        Assert.Empty(engine.Evaluate(new DiagnosticContext([Episode(DefaultRules.HighCpu, 0, 60)], DiskHistory(0, 60, 10), T0.AddSeconds(120))).Findings);

        // Only 10 s of disk data for a 60 s episode: under the 50 % coverage, no conclusion.
        Assert.Empty(engine.Evaluate(new DiagnosticContext([Episode(DefaultRules.HighCpu, 0, 60)], DiskHistory(50, 59, 99), T0.AddSeconds(120))).Findings);
    }

    [Fact]
    public void OngoingLowMemoryEpisode_UsesNow_AndStatesThePagingMechanism()
    {
        DiagnosticEngine engine = DiagnosticEngine.CreateDefault();
        var context = new DiagnosticContext([Episode(DefaultRules.LowAvailableMemory, 0, null)], DiskHistory(0, 30, 70), T0.AddSeconds(30));

        Finding f = Assert.Single(engine.Evaluate(context).Findings);

        Assert.Equal(Confidence.Medium, f.Confidence);
        Assert.Contains("page file", f.Explanation);
        Assert.Equal(T0.AddSeconds(30), f.ToUtc);
    }

    [Fact]
    public void ThrowingRule_IsReportedNotFatal()
    {
        var engine = new DiagnosticEngine([new ThrowingRule(), DiagnosticEngine.CreateDefault().Rules[0]]);

        var (findings, errors) = engine.Evaluate(new DiagnosticContext([], new InMemoryMetricStore(), T0));

        Assert.Empty(findings);
        Assert.Contains("boom", Assert.Single(errors));
    }

    [Fact]
    public void DiskCollector_FirstSampleUnavailable_SecondInRange()
    {
        using var disk = new DiskActivityCollector();
        if (!disk.CheckAvailability().IsAvailable)
        {
            return; // Counters can be disabled on a machine; the collector then says so instead of publishing zeros.
        }

        CollectorResult first = disk.Collect(default);
        Thread.Sleep(300);
        CollectorResult second = disk.Collect(default);

        // Rate and average counters need two samples; the queue length is instantaneous and valid at once.
        Assert.All(first.Values.Where(v => v.Id != DiskActivityCollector.Queue), v => Assert.False(v.IsAvailable));
        Assert.InRange(second.Values.Single(v => v.Id == DiskActivityCollector.Active).Value!.Value, 0, 100);
        MetricValue latency = second.Values.Single(v => v.Id == DiskActivityCollector.Latency);
        Assert.True(!latency.IsAvailable || latency.Value >= 0);
    }

    private sealed class ThrowingRule : IDiagnosticRule
    {
        public string Id => "throws";

        public string Formula => "-";

        public string KnownFalsePositives => "-";

        public IReadOnlyList<Finding> Evaluate(DiagnosticContext context) => throw new InvalidOperationException("boom");
    }
}
