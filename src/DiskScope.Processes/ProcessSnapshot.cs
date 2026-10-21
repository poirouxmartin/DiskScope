namespace DiskScope.Processes;

/// <summary>
/// Identity of a process across refreshes. A PID alone is reused by Windows after exit, so the creation time is part
/// of the key. <see cref="CreationTimeUtcTicks"/> is 0 when the process could not be opened: such a key cannot
/// detect PID reuse and <see cref="IsCreationTimeKnown"/> says so.
/// </summary>
public readonly record struct ProcessKey(int ProcessId, long CreationTimeUtcTicks)
{
    public bool IsCreationTimeKnown => CreationTimeUtcTicks != 0;
}

/// <summary>
/// One process at one refresh. Every value that needs a process handle is null when the handle could not be opened
/// (protected or system processes) or the call failed: unknown, never zero.
/// </summary>
public sealed record ProcessSnapshot
{
    public required ProcessKey Key { get; init; }

    public int ProcessId => Key.ProcessId;

    public required int ParentProcessId { get; init; }

    /// <summary>Executable file name from the Toolhelp snapshot; always present.</summary>
    public required string Name { get; init; }

    public required int ThreadCount { get; init; }

    /// <summary>False when OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) was refused.</summary>
    public required bool IsAccessible { get; init; }

    public string? ImagePath { get; init; }

    /// <summary>User + kernel time since start, in 100 ns ticks (GetProcessTimes).</summary>
    public long? CpuTimeTicks { get; init; }

    public long? WorkingSetBytes { get; init; }

    /// <summary>PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize; null on Windows builds older than 22H2 (Sept 2023).</summary>
    public long? PrivateWorkingSetBytes { get; init; }

    /// <summary>Commit charge (PrivateUsage): private memory committed for the process, resident or paged out.</summary>
    public long? PrivateBytes { get; init; }

    public int? HandleCount { get; init; }

    public long? IoReadBytes { get; init; }

    public long? IoWriteBytes { get; init; }

    public DateTime? StartTimeUtc => Key.IsCreationTimeKnown ? new DateTime(Key.CreationTimeUtcTicks, DateTimeKind.Utc) : null;
}
