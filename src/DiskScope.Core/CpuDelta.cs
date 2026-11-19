namespace DiskScope.Core;

/// <summary>CPU percentage from cumulative CPU time deltas of a process.</summary>
public static class CpuDelta
{
    /// <summary>
    /// CPU time delta divided by wall time and by the processor count, in percent of the whole machine, capped at 100.
    /// Null (unknown) when the wall delta is not positive or the CPU time went backwards.
    /// </summary>
    public static double? Percent(long cpuTicksDelta, long wallTicksDelta, int processorCount)
    {
        if (wallTicksDelta <= 0 || cpuTicksDelta < 0 || processorCount < 1)
        {
            return null;
        }

        return Math.Min(100.0, 100.0 * cpuTicksDelta / ((double)wallTicksDelta * processorCount));
    }
}
