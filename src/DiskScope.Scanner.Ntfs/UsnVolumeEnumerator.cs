using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskScope.Scanner.Ntfs;

/// <summary>Totals of one NativeMethods.FSCTL_ENUM_USN_DATA pass over a volume.</summary>
public sealed record UsnEnumerationStats(int Calls, long Accepted, long UnsupportedVersion, long Malformed, int TruncatedBuffers, TimeSpan Elapsed);

/// <summary>
/// Prototype reader for NativeMethods.FSCTL_ENUM_USN_DATA (MFT_ENUM_DATA_V0, so NTFS returns USN_RECORD_V2).
/// Needs an elevated process: opening \\.\X: fails with ERROR_ACCESS_DENIED otherwise, raised as Win32Exception.
/// Read-only: the volume handle is opened with NativeMethods.GENERIC_READ and full sharing.
/// Not verified on a real volume yet (dev machine not elevated): see docs/NTFS-RISKS.md section 8.
/// </summary>
public static unsafe class UsnVolumeEnumerator
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>File reference of the root directory, as USN records name it in ParentFileReferenceNumber. No elevation needed.</summary>
    public static UInt128 GetRootReference(char driveLetter)
    {
        using SafeFileHandle h = NativeMethods.CreateFileW($"{driveLetter}:\\", 0, NativeMethods.FILE_SHARE_ALL, 0, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (h.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // FILE_ID_INFO: VolumeSerialNumber u64 @0, FileId FILE_ID_128 @8.
        byte* info = stackalloc byte[24];
        if (!NativeMethods.GetFileInformationByHandleEx(h, NativeMethods.FileIdInfo, info, 24))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return System.Buffers.Binary.BinaryPrimitives.ReadUInt128LittleEndian(new ReadOnlySpan<byte>(info + 8, 16));
    }

    public static UsnEnumerationStats Enumerate(char driveLetter, UsnRecordStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var sw = Stopwatch.StartNew();
        using SafeFileHandle volume = NativeMethods.CreateFileW($"\\\\.\\{driveLetter}:", NativeMethods.GENERIC_READ, NativeMethods.FILE_SHARE_READ_WRITE, 0, NativeMethods.OPEN_EXISTING, 0, 0);
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
                if (!NativeMethods.DeviceIoControl(volume, NativeMethods.FSCTL_ENUM_USN_DATA, input, 24, buffer, BufferSize, out uint returned, 0))
                {
                    int code = Marshal.GetLastPInvokeError();
                    if (code == NativeMethods.ERROR_HANDLE_EOF)
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
}
