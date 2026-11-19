using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskScope.Scanner.Ntfs;

internal static unsafe partial class NativeMethods
{
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ_WRITE = 3;
    internal const uint FILE_SHARE_ALL = 7;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const int FileIdInfo = 18;

    // CTL_CODE(FILE_DEVICE_FILE_SYSTEM = 9, 44, METHOD_NEITHER = 3, FILE_ANY_ACCESS = 0).
    internal const uint FSCTL_ENUM_USN_DATA = 0x000900B3;

    // End of enumeration. Not stated on the FSCTL_ENUM_USN_DATA page; to confirm with the elevated probe.
    internal const int ERROR_HANDLE_EOF = 38;

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandleEx(SafeFileHandle hFile, int fileInformationClass, void* lpFileInformation, uint dwBufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        void* lpInBuffer,
        uint nInBufferSize,
        void* lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);
}
