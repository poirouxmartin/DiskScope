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
                string command = key.GetValue(name)?.ToString() ?? string.Empty;
                bool? enabled = approvedName is null ? true : approved?.GetValue(name) is { } v ? ParseApproved(v) : true;
                items.Add(Entry(source, scope, name, command, $@"{hive.Name}\{path}", enabled, string.Empty));
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
        catch (COMException ex)
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

    private static void WalkFolder(dynamic folder, List<StartupEntry> items, List<string> errors)
    {
        string folderPath = folder.Path;
        try
        {
            // TASK_ENUM_HIDDEN = 1: hidden tasks start things too.
            foreach (dynamic task in folder.GetTasks(1))
            {
                try
                {
                    AddTask(task, items);
                }
                catch (COMException ex)
                {
                    errors.Add($"{folderPath}: {ex.Message}");
                }
            }

            foreach (dynamic sub in folder.GetFolders(0))
            {
                WalkFolder(sub, items, errors);
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            errors.Add($"{folderPath}: {ex.Message}");
        }
    }

    private static void AddTask(dynamic task, List<StartupEntry> items)
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
                command = $"\"{(string)action.Path}\" {(string?)action.Arguments}".Trim();
                break;
            }

            command = "(COM handler or other non-executable action)";
        }

        items.Add(Entry(StartupSource.ScheduledTask, "Task Scheduler", (string)task.Name, command, (string)task.Path, (bool)task.Enabled, string.Join(", ", triggers.Distinct())));
    }

    private static StartupEntry Entry(StartupSource source, string scope, string name, string command, string location, bool? enabled, string trigger)
    {
        string? exe = source == StartupSource.StartupFolder ? command : CommandLine.ExtractExecutable(command);
        bool? exists = exe is null ? null : File.Exists(exe);
        string? publisher = null;
        if (exists == true && !exe!.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                publisher = FileVersionInfo.GetVersionInfo(exe).CompanyName;
            }
            catch (FileNotFoundException)
            {
            }
        }

        return new StartupEntry(source, scope, name, command, location, exe, exists, string.IsNullOrWhiteSpace(publisher) ? null : publisher, enabled, trigger);
    }
}
