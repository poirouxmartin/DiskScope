using System.Runtime.InteropServices;
using DiskScope.Core;

namespace DiskScope.Monitoring.CpuRam;

/// <summary>
/// System memory from GlobalMemoryStatusEx (physical memory, load) and GetPerformanceInfo (commit, cache, kernel pools,
/// counts). Each source fails independently: if one call fails, only its metrics are Unavailable.
/// "Used" physical memory is derived as total - available, where available = standby + free + zero lists
/// (PERFORMANCE_INFORMATION docs), so cached standby pages count as available, as in Task Manager.
/// </summary>
public sealed class MemoryCollector : IMetricCollector
{
    public const string PhysicalTotal = MetricIds.MemPhysicalTotal;
    public const string PhysicalAvailable = MetricIds.MemPhysicalAvailable;
    public const string PhysicalUsed = MetricIds.MemPhysicalUsed;
    public const string Load = MetricIds.MemPhysicalLoad;
    public const string CommitUsed = MetricIds.MemCommitUsed;
    public const string CommitLimit = MetricIds.MemCommitLimit;
    public const string CommitPeak = MetricIds.MemCommitPeak;
    public const string CommitPercent = MetricIds.MemCommitPercent;
    public const string Cache = MetricIds.MemCache;
    public const string PoolPaged = MetricIds.MemPoolPaged;
    public const string PoolNonPaged = MetricIds.MemPoolNonPaged;
    public const string Processes = MetricIds.SysProcesses;
    public const string Threads = MetricIds.SysThreads;
    public const string Handles = MetricIds.SysHandles;

    private const string StatusSource = "GlobalMemoryStatusEx";
    private const string PerfSource = "GetPerformanceInfo";

    private static readonly string[] StatusIds = [PhysicalTotal, PhysicalAvailable, PhysicalUsed, Load];
    private static readonly string[] PerfIds = [CommitUsed, CommitLimit, CommitPeak, CommitPercent, Cache, PoolPaged, PoolNonPaged, Processes, Threads, Handles];

    public string Name => "Memory";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(PhysicalTotal, "Physical memory", MetricUnit.Bytes, StatusSource),
        new(PhysicalAvailable, "Available", MetricUnit.Bytes, StatusSource, "Standby + free + zero pages"),
        new(PhysicalUsed, "In use", MetricUnit.Bytes, StatusSource, "Total - available"),
        new(Load, "Memory load", MetricUnit.Percent, StatusSource, "dwMemoryLoad, computed by Windows"),
        new(CommitUsed, "Committed", MetricUnit.Bytes, PerfSource, "CommitTotal x PageSize"),
        new(CommitLimit, "Commit limit", MetricUnit.Bytes, PerfSource, "CommitLimit x PageSize; soft limit if the page file can grow"),
        new(CommitPeak, "Commit peak", MetricUnit.Bytes, PerfSource, "Since boot"),
        new(CommitPercent, "Commit charge", MetricUnit.Percent, PerfSource, "CommitTotal / CommitLimit"),
        new(Cache, "System cache", MetricUnit.Bytes, PerfSource, "Standby list + system working set"),
        new(PoolPaged, "Paged pool", MetricUnit.Bytes, PerfSource),
        new(PoolNonPaged, "Non-paged pool", MetricUnit.Bytes, PerfSource),
        new(Processes, "Processes", MetricUnit.Count, PerfSource),
        new(Threads, "Threads", MetricUnit.Count, PerfSource),
        new(Handles, "Handles", MetricUnit.Count, PerfSource),
    ];

    public TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);

    public CollectorAvailability CheckAvailability()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status)
            ? CollectorAvailability.Available(StatusSource + ", " + PerfSource)
            : CollectorAvailability.NotAvailable($"GlobalMemoryStatusEx failed ({Marshal.GetLastPInvokeError()})");
    }

    public CollectorResult Collect(CancellationToken cancellationToken)
    {
        var values = new List<MetricValue>(StatusIds.Length + PerfIds.Length);
        var errors = new List<CollectorError>();

        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            values.Add(MetricValue.Measured(PhysicalTotal, status.ullTotalPhys));
            values.Add(MetricValue.Measured(PhysicalAvailable, status.ullAvailPhys));
            values.Add(MetricValue.Derived(PhysicalUsed, status.ullTotalPhys - Math.Min(status.ullAvailPhys, status.ullTotalPhys)));
            values.Add(MetricValue.Measured(Load, status.dwMemoryLoad));
        }
        else
        {
            errors.Add(new CollectorError(Name, "GlobalMemoryStatusEx failed", Marshal.GetLastPInvokeError()));
            values.AddRange(StatusIds.Select(MetricValue.Unavailable));
        }

        if (NativeMethods.GetPerformanceInfo(out NativeMethods.PERFORMANCE_INFORMATION perf, (uint)Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>()))
        {
            double page = perf.PageSize;
            values.Add(MetricValue.Measured(CommitUsed, perf.CommitTotal * page));
            values.Add(MetricValue.Measured(CommitLimit, perf.CommitLimit * page));
            values.Add(MetricValue.Measured(CommitPeak, perf.CommitPeak * page));
            values.Add(perf.CommitLimit > 0
                ? MetricValue.Derived(CommitPercent, 100.0 * perf.CommitTotal / perf.CommitLimit)
                : MetricValue.Unavailable(CommitPercent));
            values.Add(MetricValue.Measured(Cache, perf.SystemCache * page));
            values.Add(MetricValue.Measured(PoolPaged, perf.KernelPaged * page));
            values.Add(MetricValue.Measured(PoolNonPaged, perf.KernelNonpaged * page));
            values.Add(MetricValue.Measured(Processes, perf.ProcessCount));
            values.Add(MetricValue.Measured(Threads, perf.ThreadCount));
            values.Add(MetricValue.Measured(Handles, perf.HandleCount));
        }
        else
        {
            errors.Add(new CollectorError(Name, "GetPerformanceInfo failed", Marshal.GetLastPInvokeError()));
            values.AddRange(PerfIds.Select(MetricValue.Unavailable));
        }

        return new CollectorResult(values, errors);
    }
}
