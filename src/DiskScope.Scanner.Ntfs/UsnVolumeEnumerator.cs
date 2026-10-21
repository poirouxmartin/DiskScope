using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskScope.Scanner.Ntfs;

/// <summary>Totals of one FSCTL_ENUM_USN_DATA pass over a volume.</summary>
public sealed record UsnEnumerationStats(int Calls, long Accepted, long UnsupportedVersion, long Malformed, int TruncatedBuffers, TimeSpan Elapsed);

/// <summary>
/// Prototype reader for FSCTL_ENUM_USN_DATA (MFT_ENUM_DATA_V0, so NTFS returns USN_RECORD_V2).
/// Needs an elevated process: opening \\.\X: fails with ERROR_ACCESS_DENIED otherwise, raised as Win32Exception.
/// Read-only: the volume handle is opened with GENERIC_READ and full sharing.
/// Not verified on a real volume yet (dev machine not elevated): see docs/NTFS-RISKS.md section 8.
/// </summary>
public static unsafe partial class UsnVolumeEnumerator
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ_WRITE = 3;
    private const uint FILE_SHARE_ALL = 7;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileIdInfo = 18;

    // CTL_CODE(FILE_DEVICE_FILE_SYSTEM = 9, 44, METHOD_NEITHER = 3, FILE_ANY_ACCESS = 0).
    private const uint FSCTL_ENUM_USN_DATA = 0x000900B3;

    // End of enumeration. Not stated on the FSCTL_ENUM_USN_DATA page; to confirm with the elevated probe.
    private const int ERROR_HANDLE_EOF = 38;
    private const int BufferSize = 1024 * 1024;

    /// <summary>File reference of the root directory, as USN records name it in ParentFileReferenceNumber. No elevation needed.</summary>
    public static UInt128 GetRootReference(char driveLetter)
    {
        using SafeFileHandle h = CreateFileW($"{driveLetter}:\\", 0, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (h.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // FILE_ID_INFO: VolumeSerialNumber u64 @0, FileId FILE_ID_128 @8.
        byte* info = stackalloc byte[24];
        if (!GetFileInformationByHandleEx(h, FileIdInfo, info, 24))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return System.Buffers.Binary.BinaryPrimitives.ReadUInt128LittleEndian(new ReadOnlySpan<byte>(info + 8, 16));
    }

    public static UsnEnumerationStats Enumerate(char driveLetter, UsnRecordStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var sw = Stopwatch.StartNew();
        using SafeFileHandle volume = CreateFileW($"\\\\.\\{driveLetter}:", GENERIC_READ, FILE_SHARE_READ_WRITE, 0, OPEN_EXISTING, 0, 0);
        if (volume.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        byte* buffer = (byte*)NativeMemory.Alloc(BufferSize);
        try
        {
            // MFT_ENUM_DATA_V0: StartFileReferenceNumber u64, LowUsn i64, HighUsn i64.
            long* input = stackalloc long[3];
            input[0] = 0;
            input[1] = 0;
            input[2] = long.MaxValue;

            int calls = 0, truncated = 0;
            long accepted = 0, unsupported = 0, malformed = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                calls++;
                if (!DeviceIoControl(volume, FSCTL_ENUM_USN_DATA, input, 24, buffer, BufferSize, out uint returned, 0))
                {
                    int code = Marshal.GetLastPInvokeError();
                    if (code == ERROR_HANDLE_EOF)
                    {
                        break;
                    }

                    throw new Win32Exception(code);
                }

                UsnParseResult r = UsnRecordParser.Parse(new ReadOnlySpan<byte>(buffer, (int)returned), store);
                accepted += r.Accepted;
                unsupported += r.UnsupportedVersion;
                malformed += r.Malformed;
                truncated += r.Truncated ? 1 : 0;
                if (returned <= sizeof(ulong) || r.NextStartReference == (ulong)input[0])
                {
                    break; // No progress: stop rather than loop forever.
                }

                input[0] = (long)r.NextStartReference;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new UsnEnumerationStats(calls, accepted, unsupported, malformed, truncated, sw.Elapsed);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle hFile, int fileInformationClass, void* lpFileInformation, uint dwBufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        void* lpInBuffer,
        uint nInBufferSize,
        void* lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);
}
