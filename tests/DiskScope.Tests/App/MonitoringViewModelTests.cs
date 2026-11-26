using System.Diagnostics;
using DiskScope.App.Controls;
using DiskScope.App.ViewModels;
using DiskScope.Core;
using DiskScope.Diagnostics;
using DiskScope.History;
using DiskScope.Monitoring;
using DiskScope.Monitoring.CpuRam;
using DiskScope.Processes;
using DiskScope.Tests.Monitoring;

namespace DiskScope.Tests.App;

public sealed class MonitoringViewModelTests
{
    private static MonitoringViewModel Create(double? cpu, out AlertEngine alerts)
    {
        var collectors = new List<IMetricCollector>
        {
            cpu is { } v
                ? new MonitoringSchedulerTests.FakeCollector("CPU", CpuCollector.Total, () => v)
                : new MonitoringSchedulerTests.FakeCollector("CPU", CpuCollector.Total, () => throw new InvalidOperationException("no source")),
        };
        alerts = new AlertEngine(DefaultRules.Create());
        return new MonitoringViewModel(collectors, new InMemoryMetricStore(), alerts, new MonitoringClock());
    }

    [Fact]
    public void Refresh_ShowsLatestValueAndTrend()
    {
        MonitoringViewModel vm = Create(42, out _);

        vm.Scheduler.CollectOnce();
        vm.Refresh();

        // The fake collector declares MetricUnit.Count, so the value is formatted as a count.
        Assert.Equal("42", vm.CpuTile!.ValueText);
        Assert.True(vm.CpuTile.IsAvailable);
        Assert.Single(vm.CpuTile.Points);
        Assert.Equal("avg 42, max 42 over 1 min", vm.CpuTile.PeakText);

        // One point cannot carry a slope: the tile says so instead of printing a flat "+0".
        Assert.Equal("trend n/a: needs 30 s of history", vm.CpuTile.TrendText);
    }

    [Fact]
    public void Tile_WithEnoughHistory_ShowsSignedChangeOverWindow()
    {
        var tile = new MetricTileViewModel(new MetricDescriptor("mem.used", "In use", MetricUnit.Bytes, "test"), TimeSpan.FromMinutes(10));
        DateTime t = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const double Gb = 1024.0 * 1024 * 1024;

        // 1 GB per 5 minutes, i.e. +2 GB over the 10-minute window.
        MetricPoint[] points = Enumerable.Range(0, 6).Select(i => new MetricPoint(t.AddMinutes(i), i * 0.2 * Gb, i * 0.2 * Gb, i * 0.2 * Gb, 1)).ToArray();
        tile.Update(MetricValue.Measured("mem.used", Gb), points, t.AddMinutes(5), double.NaN);

        Assert.Equal("trend +2.0 GB per 10 min", tile.TrendText);
    }

    [Fact]
    public void Refresh_FailingCollector_ShowsNotAvailable_NeverZero()
    {
        MonitoringViewModel vm = Create(null, out _);

        vm.Scheduler.CollectOnce();
        vm.Refresh();

        Assert.Equal("n/a", vm.CpuTile!.ValueText);
        Assert.False(vm.CpuTile.IsAvailable);
        Assert.Empty(vm.CpuTile.Points);
        Assert.Contains("1 failed collections", vm.StatusText);
    }

    [Fact]
    public void Refresh_ThrowingSubscriber_ShowsSubscriberFailures()
    {
        MonitoringViewModel vm = Create(1, out _);
        vm.Scheduler.BatchPublished += _ => throw new InvalidOperationException("subscriber");

        vm.Scheduler.CollectOnce();
        vm.Scheduler.CollectOnce();
        vm.Refresh();

        Assert.Equal(2, vm.Scheduler.SubscriberFailures);
        Assert.Contains("2 subscriber failures", vm.StatusText);
    }

    [Fact]
    public void Refresh_NoSubscriberFailure_ShowsZeroNotHidden()
    {
        MonitoringViewModel vm = Create(1, out _);

        vm.Scheduler.CollectOnce();
        vm.Refresh();

        Assert.Contains("0 subscriber failures", vm.StatusText);
    }

    [Fact]
    public void Tiles_ForMissingMetrics_AreNull()
    {
        MonitoringViewModel vm = Create(1, out _);

        Assert.Null(vm.MemoryTile);
        Assert.Empty(vm.GpuAdapters);
        Assert.Equal("GPU collector not configured.", vm.GpuStatus);
    }

    [Fact]
    public void RuleEdit_InvalidInput_IsRejected_ValidInputConvertsMegabytes()
    {
        Create(1, out AlertEngine alerts);
        var rule = new RuleViewModel(alerts.Rules.Single(r => r.Id == DefaultRules.LowAvailableMemory), alerts);

        rule.ThresholdText = "abc";
        rule.Apply();
        Assert.StartsWith("Threshold:", rule.Error);
        Assert.Equal(512.0 * 1024 * 1024, alerts.Rules.Single(r => r.Id == DefaultRules.LowAvailableMemory).Threshold);

        rule.ThresholdText = "256";
        rule.Enabled = false;
        rule.Apply();
        ThresholdRule updated = alerts.Rules.Single(r => r.Id == DefaultRules.LowAvailableMemory);
        Assert.Equal(256.0 * 1024 * 1024, updated.Threshold);
        Assert.False(updated.Enabled);
        Assert.Empty(rule.Error);
    }

    [Fact]
    public void RuleEdit_PercentAbove100_IsRejected()
    {
        Create(1, out AlertEngine alerts);
        var rule = new RuleViewModel(alerts.Rules.Single(r => r.Id == DefaultRules.HighCpu), alerts);

        rule.ThresholdText = "150";
        rule.Apply();

        Assert.Contains("cannot exceed 100", rule.Error);
    }

    [Fact]
    public void Sparkline_SplitsRunsOnGaps()
    {
        DateTime t = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        MetricPoint P(int s) => new(t.AddSeconds(s), 1, 1, 1, 1);

        List<List<MetricPoint>> runs = SparklineControl.SplitRuns([P(0), P(1), P(2), P(10), P(11)]);

        Assert.Equal(2, runs.Count);
        Assert.Equal(3, runs[0].Count);
        Assert.Empty(SparklineControl.SplitRuns([]));
    }

    private static ProcessSnapshot Proc(int pid, int ppid, string name, long created) => new()
    {
        Key = new ProcessKey(pid, created),
        ParentProcessId = ppid,
        Name = name,
        ThreadCount = 1,
        IsAccessible = true,
        PrivateBytes = 1024,
    };

    private static ProcessesViewModel.Snapshot Snap(params ProcessSnapshot[] processes)
    {
        var cpu = processes.ToDictionary(p => p.Key, _ => (double?)null);
        return new(new ProcessReadResult(processes, 0, TimeSpan.Zero),
            cpu,
            ProcessTree.Build(processes),
            new Dictionary<ProcessKey, string>(),
            new ApplicationConsumerTracker(@"C:\Windows", -1).Update(processes, cpu, DateTime.UtcNow));
    }

    [Fact]
    public void Consumers_GroupedLargestFirst_RowsUpdatedInPlace_AndExitedOnesRemoved()
    {
        var vm = new ProcessesViewModel();
        ProcessSnapshot Sized(int pid, string name, long bytes) => Proc(pid, 0, name, pid * 10) with { ImagePath = @"C:\Apps\" + name, PrivateBytes = bytes };

        vm.Apply(Snap(Sized(1, "a.exe", 100), Sized(2, "b.exe", 300), Sized(3, "b.exe", 300), Sized(4, "c.exe", 50)));

        Assert.Equal(new[] { "b.exe", "a.exe", "c.exe" }, vm.Consumers.Select(c => c.Name).ToArray());
        Assert.Equal("2", vm.Consumers[0].ProcessCountText);
        ConsumerRowViewModel a = vm.Consumers[1];

        vm.Apply(Snap(Sized(1, "a.exe", 1000), Sized(2, "b.exe", 300)));

        Assert.Equal(new[] { "a.exe", "b.exe" }, vm.Consumers.Select(c => c.Name).ToArray());
        Assert.Same(a, vm.Consumers[0]);
        Assert.Equal("1,000 B", a.PrivateBytesText);
        Assert.StartsWith("Closing it returns about n/a of RAM and 1,000 B of commit.", a.Advice);
        Assert.Contains("2 user applications", vm.ConsumerSummary);
    }

    [Fact]
    public void Processes_TreeOrderWithDepth_AndInPlaceUpdateKeepsRows()
    {
        var vm = new ProcessesViewModel { ShowTree = true };
        vm.Apply(Snap(Proc(1, 0, "root.exe", 10), Proc(2, 1, "child.exe", 20), Proc(3, 0, "another.exe", 5)));

        Assert.Equal(new[] { "another.exe", "root.exe", "child.exe" }, vm.Rows.Select(r => r.Name).ToArray());
        Assert.Equal(1, vm.Rows.Single(r => r.Pid == 2).Depth);
        ProcessRowViewModel child = vm.Rows.Single(r => r.Pid == 2);
        vm.Selected = child;

        vm.Apply(Snap(Proc(1, 0, "root.exe", 10), Proc(2, 1, "child.exe", 20), Proc(4, 1, "new.exe", 30)));

        Assert.Same(child, vm.Rows.Single(r => r.Pid == 2));
        Assert.Same(child, vm.Selected);
        Assert.DoesNotContain(vm.Rows, r => r.Pid == 3);
        Assert.Equal(new[] { "root.exe", "child.exe", "new.exe" }, vm.Rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Processes_FilterByNameOrPid_IsFlat()
    {
        var vm = new ProcessesViewModel();
        vm.Apply(Snap(Proc(1, 0, "root.exe", 10), Proc(22, 1, "child.exe", 20)));

        vm.FilterText = "child";
        Assert.Equal(22, Assert.Single(vm.Rows).Pid);
        Assert.Equal(0, vm.Rows[0].Depth);

        vm.FilterText = "1";
        Assert.Equal("root.exe", Assert.Single(vm.Rows).Name);
    }

    [Fact]
    public void ProcessRow_InaccessibleProcess_ShowsNotAvailable()
    {
        var row = new ProcessRowViewModel(new ProcessSnapshot
        {
            Key = new ProcessKey(4, 0),
            ParentProcessId = 0,
            Name = "System",
            ThreadCount = 200,
            IsAccessible = false,
        });

        Assert.Equal("n/a", row.PrivateBytesText);
        Assert.Equal("n/a", row.CpuText);
        Assert.Equal("access denied", row.Path);
        Assert.Equal("n/a", row.Started);
    }
}
