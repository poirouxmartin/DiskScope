namespace DiskScope.Maintenance;

public enum StartupSource
{
    RunKey,
    RunOnceKey,
    StartupFolder,
    ScheduledTask,
}

/// <summary>
/// One thing launched automatically. Impact on start-up time is not part of the record: DiskScope has not measured it,
/// and the spec forbids an invented score.
/// </summary>
/// <param name="Location">Registry key, folder or task path where the entry was found.</param>
/// <param name="ExecutablePath">Executable extracted from the command, or null when it cannot be determined.</param>
/// <param name="Publisher">CompanyName from the executable's version resource; not a verified signature.</param>
/// <param name="Trigger">For tasks: the triggers (logon, boot, time...); empty for other sources.</param>
public sealed record StartupEntry(
    StartupSource Source,
    string Scope,
    string Name,
    string Command,
    string Location,
    string? ExecutablePath,
    bool? ExecutableExists,
    string? Publisher,
    bool? Enabled,
    string Trigger);

public enum ServiceStartType
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
    Unknown = -1,
}

/// <summary>A Win32 service: SCM state plus its registry configuration.</summary>
public sealed record ServiceEntry(
    string Name,
    string DisplayName,
    string State,
    int? ProcessId,
    ServiceStartType StartType,
    bool DelayedStart,
    string? Account,
    string? ImagePath,
    IReadOnlyList<string> DependsOn,
    bool SharesProcess);

/// <summary>What could not be read, kept next to the result so a partial inventory is never presented as complete.</summary>
public sealed record InventoryResult<T>(IReadOnlyList<T> Items, IReadOnlyList<string> Errors);
