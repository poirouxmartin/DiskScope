using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiskScope.Maintenance;

/// <summary>
/// Read-only inventory of start-up entries: Run and RunOnce keys (current user, machine, and 32-bit view), the user and
/// common Startup folders, and scheduled tasks with a logon or boot trigger.
/// Every read error is collected in the result instead of being swallowed, so a partial inventory says it is partial.
/// "Enabled" for Run entries and Startup folder items comes from the Explorer StartupApproved keys, whose format is
/// not documented by Microsoft: an entry without an approval value is treated as enabled (Windows runs it), a value in
/// an unexpected format gives null (unknown). RunOnce entries have no approval state and are reported enabled.
/// </summary>
public static class StartupInventory
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnce32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    // Task Scheduler trigger types (TASK_TRIGGER_TYPE2): boot 8, logon 9.
    private const int TriggerBoot = 8;
    private const int TriggerLogon = 9;

    public static InventoryResult<StartupEntry> Read(bool includeScheduledTasks = true)
    {
        var items = new List<StartupEntry>();
        var errors = new List<string>();

        ReadKey(Registry.CurrentUser, "HKCU", Run, StartupSource.RunKey, "Run", items, errors);
        ReadKey(Registry.CurrentUser, "HKCU", RunOnce, StartupSource.RunOnceKey, null, items, errors);
        ReadKey(Registry.LocalMachine, "HKLM", Run, StartupSource.RunKey, "Run", items, errors);
        ReadKey(Registry.LocalMachine, "HKLM", RunOnce, StartupSource.RunOnceKey, null, items, errors);
        ReadKey(Registry.LocalMachine, "HKLM (32-bit)", Run32, StartupSource.RunKey, "Run32", items, errors);
        ReadKey(Registry.LocalMachine, "HKLM (32-bit)", RunOnce32, StartupSource.RunOnceKey, null, items, errors);

        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User", Registry.CurrentUser, items, errors);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "All users", Registry.LocalMachine, items, errors);

        if (includeScheduledTasks)
        {
            ReadTasks(items, errors);
        }

        return new InventoryResult<StartupEntry>(items, errors);
    }

    /// <summary>
    /// StartupApproved value: 12 bytes, first byte 0x02 or 0x06 observed for enabled, 0x03 or 0x07 for disabled
    /// (i.e. bit 0 set = disabled); bytes 4-11 hold the FILETIME of the disabling (zero when enabled), which matched
    /// on the dev machine. Anything else is unknown. Undocumented: kept isolated and tested.
    /// </summary>
    internal static bool? ParseApproved(object? value) =>
        value is byte[] { Length: >= 1 } bytes && (bytes[0] & ~0x05) == 0x02 ? (bytes[0] & 0x01) == 0 : null;

    /// <summary>
    /// Command text of a Run value. Windows only runs string values: a binary, DWORD or multi-string value has no
    /// command line (null), and is never shown as "System.Byte[]".
    /// </summary>
    internal static string? CommandText(object? value) => value as string;

    /// <summary>
    /// Whether the executable exists. A fully qualified path is checked directly. A bare name (rundll32.exe, cmd) is
    /// looked up in the given directories (system, Windows, PATH), with ".exe" appended when it has no extension;
    /// not found there is unknown (null), not absent: App Paths and the launcher's current directory may still
    /// resolve it. A relative path with a directory is unknown too: it is never resolved against our own directory.
    /// </summary>
    internal static (bool? Exists, string? Resolved) Locate(string? exe, IEnumerable<string> searchDirs, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return (null, null);
        }

        if (Path.IsPathFullyQualified(exe))
        {
            bool exists = fileExists(exe);
            return (exists, exists ? exe : null);
        }

        if (exe.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            return (null, null);
        }

        string name = Path.HasExtension(exe) ? exe : exe + ".exe";
        foreach (string dir in searchDirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                continue;
            }

            string candidate = Path.Combine(dir, name);
            if (Path.IsPathFullyQualified(candidate) && fileExists(candidate))
            {
                return (true, candidate);
            }
        }

        return (null, null);
    }

    private static IEnumerable<string> SearchDirectories()
    {
        yield return Environment.SystemDirectory;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Environment.ExpandEnvironmentVariables(dir.Trim('"'));
        }
    }

    private static void ReadKey(RegistryKey hive, string scope, string path, StartupSource source, string? approvedName, List<StartupEntry> items, List<string> errors)
    {
        try
        {
            using RegistryKey? key = hive.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            using RegistryKey? approved = approvedName is null ? null : hive.OpenSubKey(Approved + approvedName);
            foreach (string name in key.GetValueNames())
            {
                // Raw value: %VAR% stays visible in the command, CommandLine expands it for the executable.
                string? command = CommandText(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                bool? enabled = approvedName is null ? true : approved?.GetValue(name) is { } v ? ParseApproved(v) : true;
                StartupEntry entry = command is null
                    ? new StartupEntry(source, scope, name, $"({key.GetValueKind(name)} value, not a command line)", $@"{hive.Name}\{path}", null, null, null, enabled, string.Empty)
                    : Entry(source, scope, name, command, $@"{hive.Name}\{path}", enabled, string.Empty);
                items.Add(entry);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            errors.Add($@"{hive.Name}\{path}: {ex.Message}");
        }
    }

    private static void ReadFolder(string folder, string scope, RegistryKey hive, List<StartupEntry> items, List<string> errors)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            using RegistryKey? approved = hive.OpenSubKey(Approved + "StartupFolder");
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool? enabled = approved?.GetValue(name) is { } v ? ParseApproved(v) : true;

                // A .lnk target needs the shell link COM API; the link itself is reported and its target is not guessed.
                items.Add(Entry(StartupSource.StartupFolder, scope, name, file, folder, enabled, string.Empty));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            errors.Add($"{folder}: {ex.Message}");
        }
    }

    private static void ReadTasks(List<StartupEntry> items, List<string> errors)
    {
        Type? type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null)
        {
            errors.Add("Task Scheduler COM server (Schedule.Service) not registered");
            return;
        }

        dynamic? service = null;
        try
        {
            service = Activator.CreateInstance(type)!;
            service.Connect();
            WalkFolder(service.GetFolder(@"\"), items, errors);
        }
        catch (Exception ex) when (IsTaskReadError(ex))
        {
            errors.Add($"Task Scheduler: {ex.Message}");
        }
        finally
        {
            if (service is not null)
            {
                Marshal.FinalReleaseComObject(service);
            }
        }
    }

    // COM failures, plus what the dynamic binder raises on a missing member or a value of an unexpected type.
    private static bool IsTaskReadError(Exception ex) =>
        ex is COMException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException;

    // Internal: tests pass fake objects as dynamic.
    internal static void WalkFolder(dynamic folder, List<StartupEntry> items, List<string> errors)
    {
        string folderPath;
        try
        {
            folderPath = folder.Path;
        }
        catch (Exception ex) when (IsTaskReadError(ex))
        {
            folderPath = "?";
        }

        // A failure listing the tasks must not skip the subfolders, and the other way round.
        try
        {
            // TASK_ENUM_HIDDEN = 1: hidden tasks start things too.
            foreach (dynamic task in folder.GetTasks(1))
            {
                try
                {
                    AddTask(task, items);
                }
                catch (Exception ex) when (IsTaskReadError(ex))
                {
                    errors.Add($"{folderPath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (IsTaskReadError(ex))
        {
            errors.Add($"{folderPath}: {ex.Message}");
        }

        try
        {
            foreach (dynamic sub in folder.GetFolders(0))
            {
                WalkFolder(sub, items, errors);
            }
        }
        catch (Exception ex) when (IsTaskReadError(ex))
        {
            errors.Add($"{folderPath}: {ex.Message}");
        }
    }

    internal static void AddTask(dynamic task, List<StartupEntry> items)
    {
        dynamic definition = task.Definition;
        var triggers = new List<string>();
        bool atStartup = false;
        foreach (dynamic trigger in definition.Triggers)
        {
            int t = trigger.Type;
            atStartup |= t is TriggerBoot or TriggerLogon;
            triggers.Add(t switch
            {
                TriggerBoot => "boot",
                TriggerLogon => "logon",
                0 => "event",
                1 => "time",
                2 => "daily",
                3 => "weekly",
                4 => "monthly",
                6 => "idle",
                7 => "registration",
                11 => "session state change",
                _ => "other",
            });
        }

        if (!atStartup)
        {
            return;
        }

        string command = string.Empty;
        foreach (dynamic action in definition.Actions)
        {
            if ((int)action.Type == 0)
            {
                // TASK_ACTION_EXEC
                command = CommandLine.Join((string?)action.Path, (string?)action.Arguments);
                break;
            }

            command = "(COM handler or other non-executable action)";
        }

        items.Add(Entry(StartupSource.ScheduledTask, "Task Scheduler", (string)task.Name, command, (string)task.Path, (bool)task.Enabled, string.Join(", ", triggers.Distinct())));
    }

    private static StartupEntry Entry(StartupSource source, string scope, string name, string command, string location, bool? enabled, string trigger)
    {
        string? exe = source == StartupSource.StartupFolder ? command : CommandLine.ExtractExecutable(command);
        (bool? exists, string? resolved) = Locate(exe, SearchDirectories(), File.Exists);
        string? publisher = null;
        if (exists == true && !resolved!.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                publisher = FileVersionInfo.GetVersionInfo(resolved).CompanyName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new StartupEntry(source, scope, name, command, location, exe, exists, string.IsNullOrWhiteSpace(publisher) ? null : publisher, enabled, trigger);
    }
}
