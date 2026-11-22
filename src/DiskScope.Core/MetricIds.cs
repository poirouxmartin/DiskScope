namespace DiskScope.Core;

/// <summary>Stable ids of the fixed metrics, shared by collectors, rules and tests. Per-core, per-volume and per-adapter ids are built at runtime by their collector.</summary>
public static class MetricIds
{
    public const string CpuTotal = "cpu.total";
    public const string CpuUser = "cpu.user";
    public const string CpuKernel = "cpu.kernel";
    public const string CpuCorePrefix = "cpu.core.";

    public const string MemPhysicalTotal = "mem.physical.total";
    public const string MemPhysicalAvailable = "mem.physical.available";
    public const string MemPhysicalUsed = "mem.physical.used";
    public const string MemPhysicalLoad = "mem.physical.load";
    public const string MemCommitUsed = "mem.commit.used";
    public const string MemCommitLimit = "mem.commit.limit";
    public const string MemCommitPeak = "mem.commit.peak";
    public const string MemCommitPercent = "mem.commit.percent";
    public const string MemCache = "mem.cache";
    public const string MemPoolPaged = "mem.pool.paged";
    public const string MemPoolNonPaged = "mem.pool.nonpaged";

    public const string SysProcesses = "sys.processes";
    public const string SysThreads = "sys.threads";
    public const string SysHandles = "sys.handles";

    public const string DiskActive = "disk.active";
    public const string DiskRead = "disk.read";
    public const string DiskWrite = "disk.write";
    public const string DiskLatency = "disk.latency";
    public const string DiskQueue = "disk.queue";

    public const string SelfCpu = "self.cpu";
    public const string SelfWorkingSet = "self.workingset";
    public const string SelfPrivate = "self.private";
    public const string SelfGcHeap = "self.gcheap";
}
