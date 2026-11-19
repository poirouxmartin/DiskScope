using System.Diagnostics;
using DiskScope.Core;

namespace DiskScope.Processes;

/// <summary>
/// DiskScope's own cost, published like any other metric so the monitoring overhead is measured, not assumed
/// (spec section 14). CPU is a percentage of the whole machine.
/// </summary>
public sealed class SelfCollector : IMetricCollector
{
    public const string Cpu = MetricIds.SelfCpu;
    public const string WorkingSet = MetricIds.SelfWorkingSet;
    public const string PrivateBytes = MetricIds.SelfPrivate;
    public const string ManagedHeap = MetricIds.SelfGcHeap;

    private const string Source = "GetProcessTimes, GetProcessMemoryInfo on the current process";

    private readonly int _processors = Environment.ProcessorCount;
    private long _previousCpu = -1;
    private long _previousTimestamp;

    public string Name => "DiskScope";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(Cpu, "DiskScope CPU", MetricUnit.Percent, Source, "Delta of own user + kernel time / elapsed / logical processors"),
        new(WorkingSet, "DiskScope working set", MetricUnit.Bytes, Source),
        new(PrivateBytes, "DiskScope private bytes", MetricUnit.Bytes, Source),
        new(ManagedHeap, "DiskScope managed heap", MetricUnit.Bytes, "GC.GetTotalMemory(false)"),
    ];

    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);

    public CollectorAvailability CheckAvailability() => CollectorAvailability.Available(Source);

    public CollectorResult Collect(CancellationToken cancellationToken)
    {
        nint self = NativeMethods.GetCurrentProcess();
        long now = Stopwatch.GetTimestamp();
        MetricValue cpu = MetricValue.Unavailable(Cpu);
        if (NativeMethods.GetProcessTimes(self, out _, out _, out long kernel, out long user))
        {
            long total = kernel + user;
            if (_previousCpu >= 0 && total >= _previousCpu)
            {
                double elapsed = Stopwatch.GetElapsedTime(_previousTimestamp, now).Ticks;
                if (elapsed > 0)
                {
                    cpu = MetricValue.Derived(Cpu, Math.Min(100, 100.0 * (total - _previousCpu) / (elapsed * _processors)));
                }
            }

            _previousCpu = total;
            _previousTimestamp = now;
        }

        MetricValue ws = MetricValue.Unavailable(WorkingSet);
        MetricValue priv = MetricValue.Unavailable(PrivateBytes);
        if (NativeMethods.GetProcessMemoryInfo(self, out NativeMethods.PROCESS_MEMORY_COUNTERS_EX2 c, NativeMethods.MemoryCountersExSize))
        {
            ws = MetricValue.Measured(WorkingSet, c.WorkingSetSize);
            priv = MetricValue.Measured(PrivateBytes, c.PrivateUsage);
        }

        return CollectorResult.Of([cpu, ws, priv, MetricValue.Measured(ManagedHeap, GC.GetTotalMemory(false))]);
    }
}
