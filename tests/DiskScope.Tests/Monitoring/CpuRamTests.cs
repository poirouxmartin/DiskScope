using DiskScope.Core;
using DiskScope.Monitoring.CpuRam;

namespace DiskScope.Tests.Monitoring;

public sealed class CpuRamTests
{
    [Fact]
    public void Usage_KernelIncludesIdle()
    {
        // 1000 ticks elapsed: 600 kernel (of which 500 idle), 400 user -> busy 500 = 50 %.
        CpuUsage? u = CpuUsageCalculator.Compute(new CpuTimes(0, 0, 0), new CpuTimes(500, 600, 400));

        Assert.Equal(new CpuUsage(50, 40, 10), u);
    }

    [Theory]
    [InlineData(0, 0, 0)] // no time elapsed
    [InlineData(10, -5, 20)] // kernel went backwards
    [InlineData(700, 600, 400)] // idle larger than kernel: inconsistent
    public void Usage_ImpossibleDelta_IsUnknownNotZero(long idle, long kernel, long user)
    {
        Assert.Null(CpuUsageCalculator.Compute(new CpuTimes(0, 0, 0), new CpuTimes(idle, kernel, user)));
    }

    [Fact]
    public void Usage_FullyIdle_IsZero()
    {
        Assert.Equal(new CpuUsage(0, 0, 0), CpuUsageCalculator.Compute(new CpuTimes(0, 0, 0), new CpuTimes(1000, 1000, 0)));
    }

    [Fact]
    public void CpuCollector_FirstSampleUnavailable_SecondInRange()
    {
        var cpu = new CpuCollector();

        CollectorResult first = cpu.Collect(default);
        Thread.Sleep(250);
        CollectorResult second = cpu.Collect(default);

        Assert.Empty(first.Errors);
        Assert.All(first.Values, v => Assert.False(v.IsAvailable));
        Assert.Equal(cpu.Metrics.Count, second.Values.Count);
        Assert.True(cpu.LogicalProcessorCount > 0);
        MetricValue total = second.Values.Single(v => v.Id == CpuCollector.Total);
        Assert.InRange(total.Value!.Value, 0, 100);
        // Every per-processor value must be computable: if KernelTime excluded idle there, idle > kernel would make them unknown.
        Assert.All(second.Values, v => Assert.InRange(v.Value!.Value, 0, 100));
    }

    [Fact]
    public void MemoryCollector_ReturnsConsistentValues()
    {
        CollectorResult r = new MemoryCollector().Collect(default);
        double Get(string id) => r.Values.Single(v => v.Id == id).Value!.Value;

        Assert.Empty(r.Errors);
        Assert.All(r.Values, v => Assert.True(v.IsAvailable));
        Assert.True(Get(MemoryCollector.PhysicalTotal) >= Get(MemoryCollector.PhysicalAvailable));
        Assert.Equal(Get(MemoryCollector.PhysicalTotal), Get(MemoryCollector.PhysicalAvailable) + Get(MemoryCollector.PhysicalUsed));
        Assert.True(Get(MemoryCollector.CommitLimit) >= Get(MemoryCollector.CommitUsed));
        Assert.True(Get(MemoryCollector.Processes) > 10);
    }
}
