using DiskScope.Core;
using DiskScope.Processes;

namespace DiskScope.Tests.Processes;

public sealed class ApplicationConsumerTests
{
    private const string Windows = @"C:\Windows";
    private const int Self = 999;
    private static readonly DateTime T0 = new(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);
    private const long MB = 1024 * 1024;

    private static ProcessSnapshot P(int pid, string name, string? path, long? privateBytes, long? privateWs = null, bool accessible = true) => new()
    {
        Key = new ProcessKey(pid, accessible ? 1000 + pid : 0),
        ParentProcessId = 0,
        Name = name,
        ThreadCount = 1,
        IsAccessible = accessible,
        ImagePath = accessible ? path : null,
        PrivateBytes = accessible ? privateBytes : null,
        PrivateWorkingSetBytes = accessible ? privateWs : null,
    };

    private static Dictionary<ProcessKey, double?> Cpu(params (ProcessSnapshot P, double? Percent)[] values) =>
        values.ToDictionary(v => v.P.Key, v => v.Percent);

    [Fact]
    public void Update_GroupsByImageNameIgnoringCase_AndSumsKnownValues()
    {
        var a = P(10, "browser.exe", @"C:\Apps\browser.exe", 300 * MB, 200 * MB);
        var b = P(11, "Browser.EXE", @"C:\Apps\browser.exe", 100 * MB, 50 * MB);
        var tracker = new ApplicationConsumerTracker(Windows, Self);

        ConsumerReport report = tracker.Update([a, b], Cpu((a, 3.0), (b, 1.5)), T0);

        ApplicationConsumer c = Assert.Single(report.Consumers);
        Assert.Equal(ConsumerKind.Application, c.Kind);
        Assert.Equal(2, c.ProcessCount);
        Assert.Equal(400 * MB, c.PrivateBytes);
        Assert.Equal(250 * MB, c.PrivateWorkingSetBytes);
        Assert.Equal(4.5, c.CpuPercent);
        Assert.Equal(400 * MB, report.FreeableCommitBytes);
        Assert.Equal(250 * MB, report.FreeableRamBytes);
    }

    [Fact]
    public void Update_OrdersByPrivateBytes_UnknownLast()
    {
        var tracker = new ApplicationConsumerTracker(Windows, Self);

        ConsumerReport report = tracker.Update(
            [P(1, "small.exe", @"C:\A\small.exe", 10 * MB), P(2, "secret.exe", null, null, accessible: false), P(3, "big.exe", @"C:\A\big.exe", 900 * MB)],
            Cpu(),
            T0);

        Assert.Equal(["big.exe", "small.exe", "secret.exe"], report.Consumers.Select(c => c.Name));
    }

    [Fact]
    public void Classify_WindowsDirectory_IsWindowsComponent_AndNotFreeable()
    {
        var tracker = new ApplicationConsumerTracker(Windows + @"\", Self);

        ConsumerReport report = tracker.Update([P(5, "host.exe", @"c:\windows\System32\host.exe", 500 * MB)], Cpu(), T0);

        Assert.Equal(ConsumerKind.WindowsComponent, report.Consumers[0].Kind);
        Assert.Equal(0, report.FreeableCommitBytes);
        Assert.Equal(500 * MB, report.OtherCommitBytes);
        Assert.Null(report.FreeableRamBytes);
    }

    [Fact]
    public void Classify_PathThatOnlyStartsLikeWindows_IsAnApplication()
    {
        // A sibling folder whose name starts with "Windows" is not the Windows directory.
        ConsumerKind kind = ApplicationConsumerTracker.Classify([P(5, "x.exe", @"C:\WindowsTools\x.exe", MB)], @"C:\Windows\", Self);

        Assert.Equal(ConsumerKind.Application, kind);
    }

    [Fact]
    public void Classify_OneProtectedMember_MakesTheWholeGroupNotInspectable()
    {
        ConsumerKind kind = ApplicationConsumerTracker.Classify(
            [P(20, "agent.exe", @"C:\Apps\agent.exe", MB), P(21, "agent.exe", null, null, accessible: false)],
            @"C:\Windows\",
            Self);

        Assert.Equal(ConsumerKind.NotInspectable, kind);
    }

    [Fact]
    public void Classify_SelfWinsOverEverything()
    {
        ConsumerKind kind = ApplicationConsumerTracker.Classify([P(Self, "DiskScope.App.exe", @"C:\Windows\x.exe", MB)], @"C:\Windows\", Self);

        Assert.Equal(ConsumerKind.Self, kind);
    }

    [Fact]
    public void Update_GroupWithoutAnyKnownValue_HasNullSums_NotZero()
    {
        var tracker = new ApplicationConsumerTracker(Windows, Self);

        ApplicationConsumer c = tracker.Update([P(4, "System", null, null, accessible: false)], Cpu(), T0).Consumers[0];

        Assert.Null(c.PrivateBytes);
        Assert.Null(c.WorkingSetBytes);
        Assert.Null(c.CpuPercent);
        Assert.Equal(1, c.InaccessibleCount);
    }

    [Fact]
    public void Update_KeepsHistoryOverTheWindow_AndComputesTrend()
    {
        var tracker = new ApplicationConsumerTracker(Windows, Self, TimeSpan.FromMinutes(4));
        ConsumerReport? report = null;
        for (int minute = 0; minute <= 4; minute++)
        {
            var p = P(10, "editor.exe", @"C:\Apps\editor.exe", (100 + (50 * minute)) * MB);
            report = tracker.Update([p], Cpu((p, 10.0)), T0.AddMinutes(minute));
        }

        ApplicationConsumer c = report!.Consumers[0];
        Assert.Equal(10.0, c.CpuTrend!.Average, 6);
        Assert.Equal(TimeSpan.FromMinutes(4), c.PrivateBytesTrend!.Covered);
        Assert.Equal(200 * MB, c.PrivateBytesTrend.Change!.Value, 0);
    }

    [Fact]
    public void Update_ForgetsSamplesOlderThanTheWindow_AndExitedApplications()
    {
        var tracker = new ApplicationConsumerTracker(Windows, Self, TimeSpan.FromMinutes(1));
        tracker.Update([P(10, "gone.exe", @"C:\A\gone.exe", MB)], Cpu(), T0);

        ConsumerReport report = tracker.Update([P(11, "kept.exe", @"C:\A\kept.exe", MB)], Cpu(), T0.AddMinutes(2));

        Assert.Equal(1, tracker.TrackedGroupCount);
        Assert.Equal(TimeSpan.Zero, report.Consumers[0].PrivateBytesTrend!.Covered);
    }

    [Fact]
    public void Advise_Application_StatesWhatExitReturns_AndNeverSaysSafe()
    {
        var c = new ApplicationConsumer("editor.exe", ConsumerKind.Application, 1, 0, 2048 * MB, 1024 * MB, 1500 * MB, 0.5, null, null);

        string advice = ApplicationConsumerTracker.Advise(c, TimeSpan.FromMinutes(5));

        Assert.Equal("Closing it returns about 1.0 GB of RAM and 2.0 GB of commit. Save open work first.", advice);
        Assert.DoesNotContain("safe", advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advise_Application_MentionsSustainedCpuAndGrowth()
    {
        var cpu = new MetricTrend(12.34, 10, 15, null, TimeSpan.FromMinutes(5));
        var growth = new MetricTrend(500 * MB, 400 * MB, 600 * MB, 200 * MB, TimeSpan.FromMinutes(5));
        var c = new ApplicationConsumer("job.exe", ConsumerKind.Application, 1, 0, 600 * MB, null, null, 12, cpu, growth);

        string advice = ApplicationConsumerTracker.Advise(c, TimeSpan.FromMinutes(5));

        Assert.Contains("about n/a of RAM", advice);
        Assert.Contains("Used 12.3 % of the machine's CPU on average over 5 min.", advice);
        Assert.Contains("Private memory rising: +200.0 MB per 5 min.", advice);
    }

    [Theory]
    [InlineData(ConsumerKind.WindowsComponent, "Part of Windows: do not end it.")]
    [InlineData(ConsumerKind.NotInspectable, "Could not be inspected without administrator rights: no advice.")]
    [InlineData(ConsumerKind.Self, "DiskScope itself.")]
    public void Advise_NonApplications_GiveNoEstimate(ConsumerKind kind, string expected)
    {
        var c = new ApplicationConsumer("x.exe", kind, 1, 0, 4096 * MB, 4096 * MB, null, 50, null, null);

        Assert.Equal(expected, ApplicationConsumerTracker.Advise(c, TimeSpan.FromMinutes(5)));
    }
}
