using System.Diagnostics;
using DiskScope.Core;
using DiskScope.Processes;

namespace DiskScope.Tests.Processes;

public sealed class ProcessTests
{
    private static ProcessSnapshot P(int pid, int ppid, long created, long? cpu = null) => new()
    {
        Key = new ProcessKey(pid, created),
        ParentProcessId = ppid,
        Name = $"p{pid}.exe",
        ThreadCount = 1,
        IsAccessible = created != 0,
        CpuTimeTicks = cpu,
    };

    [Fact]
    public void Tree_LinksChildrenToOlderParents()
    {
        ProcessTree tree = ProcessTree.Build([P(1, 0, 100), P(2, 1, 200), P(3, 2, 300)]);

        Assert.Equal(1, Assert.Single(tree.Roots).ProcessId);
        Assert.Equal(3, Assert.Single(tree.GetChildren(new ProcessKey(2, 200))).ProcessId);
        Assert.Empty(tree.UnverifiedLinks);
    }

    [Fact]
    public void Tree_ReusedParentPid_YoungerThanChild_IsNotTheParent()
    {
        // Child 20 was started by an old PID 10 that exited; PID 10 now belongs to a newer process.
        ProcessTree tree = ProcessTree.Build([P(10, 0, 500), P(20, 10, 300)]);

        Assert.Equal(2, tree.Roots.Count);
        Assert.Empty(tree.GetChildren(new ProcessKey(10, 500)));
    }

    [Fact]
    public void Tree_UnknownCreationTime_LinkKeptButUnverified()
    {
        ProcessTree tree = ProcessTree.Build([P(4, 0, 0), P(8, 4, 900)]);

        Assert.Single(tree.Roots);
        Assert.Contains(new ProcessKey(8, 900), tree.UnverifiedLinks);
    }

    [Fact]
    public void Tree_CycleThroughUnverifiedLinks_AllProcessesStillListed()
    {
        ProcessTree tree = ProcessTree.Build([P(5, 6, 0), P(6, 5, 0)]);

        Assert.Equal(2, tree.Roots.Count);
    }

    [Fact]
    public void CpuTracker_FirstSeenUnknown_ThenDeltaOverElapsedAndCores()
    {
        var tracker = new ProcessCpuTracker(processorCount: 4);
        long t0 = 1_000_000;
        long t1 = t0 + Stopwatch.Frequency; // one second later

        IReadOnlyDictionary<ProcessKey, double?> first = tracker.Update([P(1, 0, 100, cpu: 0)], t0);
        // 2 s of CPU time over 1 s on 4 processors = 50 % of the machine.
        IReadOnlyDictionary<ProcessKey, double?> second = tracker.Update([P(1, 0, 100, cpu: 20_000_000)], t1);

        Assert.Null(first[new ProcessKey(1, 100)]);
        Assert.Equal(50.0, second[new ProcessKey(1, 100)]!.Value, 3);
    }

    [Fact]
    public void CpuTracker_ReusedPid_DoesNotInheritPreviousTime()
    {
        var tracker = new ProcessCpuTracker(1);
        tracker.Update([P(7, 0, 100, cpu: 50_000_000)], 1000);

        IReadOnlyDictionary<ProcessKey, double?> after = tracker.Update([P(7, 0, 999, cpu: 10)], 1000 + Stopwatch.Frequency);

        Assert.Null(after[new ProcessKey(7, 999)]);
    }

    [Fact]
    public void CpuTracker_NoCreationTime_IsUnknown()
    {
        var tracker = new ProcessCpuTracker(1);
        tracker.Update([P(4, 0, 0, cpu: 10)], 1000);

        Assert.Null(tracker.Update([P(4, 0, 0, cpu: 20)], 1000 + Stopwatch.Frequency)[new ProcessKey(4, 0)]);
    }

    [Fact]
    public void Reader_FindsCurrentProcess_WithHandleData()
    {
        ProcessReadResult result = ProcessReader.ReadAll();
        ProcessSnapshot self = result.Processes.Single(p => p.ProcessId == Environment.ProcessId);

        Assert.True(self.IsAccessible);
        Assert.True(self.Key.IsCreationTimeKnown);
        Assert.True(self.WorkingSetBytes > 0);
        Assert.True(self.PrivateBytes > 0);
        Assert.True(self.CpuTimeTicks > 0);
        Assert.EndsWith(".exe", self.ImagePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Processes, p => p.ProcessId == 0);
    }

    [Fact]
    public void Reader_ProcessesExitingDuringRead_DoNotThrow()
    {
        var started = new List<Process>();
        for (int i = 0; i < 10; i++)
        {
            started.Add(Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!);
        }

        for (int i = 0; i < 5; i++)
        {
            Assert.NotEmpty(ProcessReader.ReadAll().Processes);
        }

        foreach (Process p in started)
        {
            p.WaitForExit();
            p.Dispose();
        }
    }

    [Fact]
    public void SelfCollector_SecondSampleHasCpu()
    {
        var self = new SelfCollector();
        self.Collect(default);
        Thread.Sleep(100);

        CollectorResult r = self.Collect(default);

        Assert.All(r.Values, v => Assert.True(v.IsAvailable));
    }
}
