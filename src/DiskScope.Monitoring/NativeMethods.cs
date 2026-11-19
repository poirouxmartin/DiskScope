using System.Runtime.InteropServices;

namespace DiskScope.Monitoring;

internal static unsafe partial class NativeMethods
{
    internal const uint PDH_FMT_DOUBLE = 0x00000200;
    internal const uint PDH_FMT_NOCAP100 = 0x00008000;
    internal const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    internal const uint PDH_CSTATUS_VALID_DATA = 0;
    internal const uint PDH_CSTATUS_NEW_DATA = 1;

    // PDH_FMT_COUNTERVALUE_ITEM_W on x64: LPWSTR szName @0, DWORD CStatus @8, union (double) @16; 24 bytes.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Item
    {
        public char* Name;
        public uint CStatus;
        public double Value;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    internal static partial int PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    internal static partial int PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, Item* items);

    [LibraryImport("pdh.dll")]
    internal static partial int PdhCloseQuery(nint query);
}
