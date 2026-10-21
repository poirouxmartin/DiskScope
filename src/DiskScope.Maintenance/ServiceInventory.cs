using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiskScope.Maintenance;

/// <summary>
/// Read-only list of Win32 services: state and PID from the service control manager (EnumServicesStatusExW, which
/// silently omits services the caller may not query), configuration from HKLM\SYSTEM\CurrentControlSet\Services
/// (Start, DelayedAutostart, ObjectName, ImagePath, DependOnService). The SCM handle is opened with
/// SC_MANAGER_ENUMERATE_SERVICE only: nothing can be started, stopped or reconfigured through it.
/// </summary>
public static unsafe partial class ServiceInventory
{
    private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    private const int SC_ENUM_PROCESS_INFO = 0;
    private const uint SERVICE_WIN32 = 0x30;
    private const uint SERVICE_WIN32_SHARE_PROCESS = 0x20;
    private const uint SERVICE_STATE_ALL = 0x03;
    private const int ERROR_MORE_DATA = 234;
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    // ENUM_SERVICE_STATUS_PROCESSW: two LPWSTR then SERVICE_STATUS_PROCESS (9 DWORDs).
    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public char* ServiceName;
        public char* DisplayName;
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    public static InventoryResult<ServiceEntry> Read()
    {
        var items = new List<ServiceEntry>();
        var errors = new List<string>();
        nint scm = OpenSCManagerW(null, null, SC_MANAGER_ENUMERATE_SERVICE);
        if (scm == 0)
        {
            errors.Add("OpenSCManager: " + new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            return new InventoryResult<ServiceEntry>(items, errors);
        }

        try
        {
            uint resume = 0;
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                bool ok;
                uint needed, returned;
                fixed (byte* p = buffer)
                {
                    ok = EnumServicesStatusExW(scm, SC_ENUM_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL, p, (uint)buffer.Length, out needed, out returned, ref resume, null);
                    int error = ok ? 0 : Marshal.GetLastPInvokeError();
                    if (!ok && error != ERROR_MORE_DATA)
                    {
                        errors.Add("EnumServicesStatusEx: " + new Win32Exception(error).Message);
                        break;
                    }

                    var entries = (EnumServiceStatusProcess*)p;
                    for (int i = 0; i < returned; i++)
                    {
                        items.Add(Describe(entries[i], errors));
                    }
                }

                if (ok)
                {
                    break;
                }

                if (needed > buffer.Length)
                {
                    buffer = new byte[Math.Min((int)needed, 256 * 1024)];
                }
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }

        items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return new InventoryResult<ServiceEntry>(items, errors);
    }

    internal static string StateName(uint state) => state switch
    {
        1 => "Stopped",
        2 => "Start pending",
        3 => "Stop pending",
        4 => "Running",
        5 => "Continue pending",
        6 => "Pause pending",
        7 => "Paused",
        _ => "Unknown",
    };

    private static ServiceEntry Describe(EnumServiceStatusProcess e, List<string> errors)
    {
        string name = new(e.ServiceName);
        string display = e.DisplayName != null ? new string(e.DisplayName) : name;
        var start = ServiceStartType.Unknown;
        bool delayed = false;
        string? account = null, image = null;
        IReadOnlyList<string> depends = Array.Empty<string>();
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{ServicesKey}\{name}");
            if (key is not null)
            {
                start = key.GetValue("Start") is int s && Enum.IsDefined(typeof(ServiceStartType), s) ? (ServiceStartType)s : ServiceStartType.Unknown;
                delayed = key.GetValue("DelayedAutostart") is int d && d != 0;
                account = key.GetValue("ObjectName") as string;
                image = key.GetValue("ImagePath", null, RegistryValueOptions.None) as string;
                depends = key.GetValue("DependOnService") as string[] ?? Array.Empty<string>();
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            errors.Add($"{name}: {ex.Message}");
        }

        return new ServiceEntry(
            name,
            display,
            StateName(e.CurrentState),
            e.ProcessId != 0 ? (int)e.ProcessId : null,
            start,
            delayed,
            account,
            image,
            depends,
            (e.ServiceType & SERVICE_WIN32_SHARE_PROCESS) != 0);
    }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumServicesStatusExW(nint scm, int infoLevel, uint serviceType, uint serviceState, byte* services, uint bufferSize, out uint needed, out uint returned, ref uint resume, string? group);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);
}
