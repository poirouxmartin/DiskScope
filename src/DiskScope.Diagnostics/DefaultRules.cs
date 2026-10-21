using DiskScope.Core;

namespace DiskScope.Diagnostics;

/// <summary>
/// Initial alert rules (spec section 4). Metric ids are the CPU/memory collector ids, duplicated as literals so this
/// assembly does not depend on the Windows collectors. Every threshold is configurable through IAlertEngine.UpdateRule.
/// </summary>
public static class DefaultRules
{
    public const string HighCpu = "cpu.high";
    public const string LowAvailableMemory = "mem.available.low";
    public const string CommitNearLimit = "mem.commit.high";

    public static IReadOnlyList<ThresholdRule> Create() =>
    [
        new(HighCpu, "Sustained high CPU", "cpu.total", ThresholdComparison.Above, 90, MetricUnit.Percent,
            MinDuration: TimeSpan.FromSeconds(30), Recovery: TimeSpan.FromSeconds(10), Cooldown: TimeSpan.FromMinutes(1),
            FindingLevel.Alert,
            "Global CPU above 90 % for 30 s: foreground work is likely to be slowed down.",
            "Builds, compressions, games and video exports legitimately keep the CPU busy."),
        new(LowAvailableMemory, "Very low available memory", "mem.physical.available", ThresholdComparison.Below, 512.0 * 1024 * 1024, MetricUnit.Bytes,
            MinDuration: TimeSpan.FromSeconds(10), Recovery: TimeSpan.FromSeconds(10), Cooldown: TimeSpan.FromMinutes(1),
            FindingLevel.Alert,
            "Available memory (standby + free + zero pages) under 512 MB: Windows must page out to satisfy new allocations. " +
            "An absolute amount, not a percentage of RAM, because a high 'memory in use' percentage alone is normal with a large cache.",
            "On machines with little RAM, the threshold may need to be lowered."),
        new(CommitNearLimit, "Commit charge near its limit", "mem.commit.percent", ThresholdComparison.Above, 90, MetricUnit.Percent,
            MinDuration: TimeSpan.FromSeconds(10), Recovery: TimeSpan.FromSeconds(10), Cooldown: TimeSpan.FromMinutes(1),
            FindingLevel.Alert,
            "Committed memory above 90 % of the commit limit: allocations can fail once the limit is reached.",
            "With a growable page file the limit is soft and Windows may extend it."),
    ];
}
