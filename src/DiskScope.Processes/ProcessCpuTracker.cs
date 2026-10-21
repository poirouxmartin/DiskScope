namespace DiskScope.Processes;

/// <summary>
/// Per-process CPU usage between two refreshes: delta of (user + kernel) time divided by elapsed wall time and by the
/// number of logical processors, in percent of the whole machine (Task Manager convention: all processes sum to at
/// most 100). Keys include the creation time, so a reused PID starts from scratch instead of producing a bogus delta.
/// </summary>
public sealed class ProcessCpuTracker
{
    private readonly int _processorCount;
    private Dictionary<ProcessKey, long> _previous = new();
    private long _previousTimestamp;

    public ProcessCpuTracker(int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(processorCount, 1);
        _processorCount = processorCount;
    }

    /// <summary>
    /// Returns CPU percent per process for this refresh. Null for a process seen for the first time, without a known
    /// creation time (PID reuse would be undetectable), without CPU time, or whose time went backwards.
    /// </summary>
    /// <param name="stopwatchTimestamp">Stopwatch.GetTimestamp() taken when <paramref name="processes"/> was read.</param>
    public IReadOnlyDictionary<ProcessKey, double?> Update(IReadOnlyList<ProcessSnapshot> processes, long stopwatchTimestamp)
    {
        ArgumentNullException.ThrowIfNull(processes);
        double elapsedTicks = _previousTimestamp == 0
            ? 0
            : System.Diagnostics.Stopwatch.GetElapsedTime(_previousTimestamp, stopwatchTimestamp).Ticks;

        var result = new Dictionary<ProcessKey, double?>(processes.Count);
        var next = new Dictionary<ProcessKey, long>(processes.Count);
        foreach (ProcessSnapshot p in processes)
        {
            double? percent = null;
            if (p.CpuTimeTicks is { } cpu && p.Key.IsCreationTimeKnown)
            {
                next[p.Key] = cpu;
                if (elapsedTicks > 0 && _previous.TryGetValue(p.Key, out long before) && cpu >= before)
                {
                    percent = Math.Min(100.0, 100.0 * (cpu - before) / (elapsedTicks * _processorCount));
                }
            }

            result[p.Key] = percent;
        }

        _previous = next;
        _previousTimestamp = stopwatchTimestamp;
        return result;
    }
}
