using DiskScope.Core;
using DiskScope.Monitoring.Gpu;

namespace DiskScope.Tests.Monitoring;

public sealed class GpuTests
{
    private const string Luid = "0x00000000_0x0000D1F3";

    private static (GpuEngineInstance, double) S(int pid, int eng, string type, double percent) =>
        (new GpuEngineInstance(pid, Luid, 0, eng, type), percent);

    [Fact]
    public void InstanceName_IsParsed_IncludingTypesWithUnderscore()
    {
        Assert.True(GpuEngineInstance.TryParse("pid_1234_luid_0x00000000_0x0000d1f3_phys_0_eng_3_engtype_Compute_1", out GpuEngineInstance i));

        Assert.Equal(new GpuEngineInstance(1234, Luid, 0, 3, "Compute_1"), i);
        Assert.False(GpuEngineInstance.TryParse("luid_0x00000000_0x0000D1F3_phys_0", out _));
        Assert.False(GpuEngineInstance.TryParse("pid_x_luid_a_b_phys_0_eng_0_engtype_3D", out _));
    }

    [Fact]
    public void Engines_SumProcessesPerEngine_ThenBusiestEngineOfEachType()
    {
        IReadOnlyList<GpuEngineUsage> usage = GpuCounterMath.AggregateEngines(
        [
            S(10, 0, "3D", 30), S(11, 0, "3D", 25), // engine 0: 55 %
            S(10, 1, "Copy", 5), S(11, 2, "Copy", 40), // two copy engines: busiest 40 %
            S(12, 0, "3D", 70), // engine 0 now 125 %: capped at 100
        ]);

        Assert.Equal(100, usage.Single(u => u.EngineType == "3D").Percent);
        Assert.Equal(40, usage.Single(u => u.EngineType == "Copy").Percent);
    }

    [Fact]
    public void Processes_BusiestEngine_NotTheSum()
    {
        IReadOnlyDictionary<int, double> perProcess = GpuCounterMath.AggregateProcesses([S(10, 0, "3D", 30), S(10, 1, "Copy", 20)]);

        Assert.Equal(30, perProcess[10]);
    }

    [Fact]
    public void Luid_FormatMatchesCounterInstances()
    {
        Assert.Equal(Luid, GpuCounterMath.FormatLuid(0xD1F3, 0));
        Assert.Equal(Luid, GpuCounterMath.LuidOfAdapterInstance("luid_0x00000000_0x0000D1F3_phys_0"));
        Assert.Null(GpuCounterMath.LuidOfAdapterInstance("pid_1"));
    }

    [Theory]
    [InlineData("3D", "3d")]
    [InlineData("Compute_0", "compute")]
    [InlineData("VideoDecode", "videodecode")]
    [InlineData("Security", "other")]
    public void EngineTypes_MapToGroups(string type, string group) => Assert.Equal(group, GpuCollector.GroupOf(type));

    [Fact]
    public void Collector_OnThisMachine_IsHonestAboutAvailability()
    {
        using var gpu = new GpuCollector();
        CollectorAvailability availability = gpu.CheckAvailability();

        CollectorResult first = gpu.Collect(default);
        Thread.Sleep(300);
        CollectorResult second = gpu.Collect(default);

        Assert.Equal(gpu.Metrics.Count, second.Values.Count);
        if (!availability.IsAvailable)
        {
            Assert.All(second.Values, v => Assert.False(v.IsAvailable));
            return;
        }

        // Rates need two samples: no engine value is invented on the first one.
        Assert.All(first.Values.Where(v => v.Id.Contains(".engine.", StringComparison.Ordinal)), v => Assert.False(v.IsAvailable));
        Assert.All(second.Values.Where(v => v.IsAvailable && v.Id.Contains(".engine.", StringComparison.Ordinal)), v => Assert.InRange(v.Value!.Value, 0, 100));
        Assert.True(second.Values.Single(v => v.Id == GpuCollector.SharedLimitId(0)).Value > 0);
    }
}
