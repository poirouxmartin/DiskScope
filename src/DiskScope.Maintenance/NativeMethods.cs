using System.Runtime.InteropServices;

namespace DiskScope.Maintenance;

internal static unsafe partial class NativeMethods
{
    internal const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    internal const int SC_ENUM_PROCESS_INFO = 0;
    internal const uint SERVICE_WIN32 = 0x30;
    internal const uint SERVICE_STATE_ALL = 0x03;
    internal const int ERROR_MORE_DATA = 234;

    // ENUM_SERVICE_STATUS_PROCESSW: two LPWSTR then SERVICE_STATUS_PROCESS (9 DWORDs).
    [StructLayout(LayoutKind.Sequential)]
    internal struct EnumServiceStatusProcess
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

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumServicesStatusExW(nint scm, int infoLevel, uint serviceType, uint serviceState, byte* services, uint bufferSize, out uint needed, out uint returned, ref uint resume, string? group);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseServiceHandle(nint handle);
}
