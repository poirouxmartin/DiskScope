using System.Runtime.InteropServices;

namespace DiskScope.Monitoring;

/// <summary>
/// Minimal PDH query with English counter paths (PdhAddEnglishCounterW), so counter names do not depend on the
/// Windows display language. Wildcard instances are read with PdhGetFormattedCounterArrayW.
/// </summary>
public sealed unsafe partial class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    private const uint PDH_CSTATUS_VALID_DATA = 0;
    private const uint PDH_CSTATUS_NEW_DATA = 1;

    private nint _query;

    // PDH_FMT_COUNTERVALUE_ITEM_W on x64: LPWSTR szName @0, DWORD CStatus @8, union (double) @16; 24 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct Item
    {
        public char* Name;
        public uint CStatus;
        public double Value;
    }

    public PdhQuery()
    {
        int status = PdhOpenQueryW(null, 0, out _query);
        if (status != 0)
        {
            throw new InvalidOperationException($"PdhOpenQuery failed, 0x{status:X8}");
        }
    }

    /// <summary>Adds an English counter path; returns 0 when the counter set does not exist on this machine.</summary>
    public nint TryAdd(string englishPath, out int status)
    {
        status = PdhAddEnglishCounterW(_query, englishPath, 0, out nint counter);
        return status == 0 ? counter : 0;
    }

    public int Collect() => PdhCollectQueryData(_query);

    /// <summary>Instances with valid data. Rate counters return nothing valid until the second collection.</summary>
    public List<(string Instance, double Value)> Read(nint counter, bool noCap = false)
    {
        var list = new List<(string, double)>();
        uint size = 0, count = 0;
        uint format = PDH_FMT_DOUBLE | (noCap ? PDH_FMT_NOCAP100 : 0);
        int status = PdhGetFormattedCounterArrayW(counter, format, ref size, out count, null);
        if (status != PDH_MORE_DATA || size == 0)
        {
            return list;
        }

        byte[] buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            status = PdhGetFormattedCounterArrayW(counter, format, ref size, out count, (Item*)p);
            if (status != 0)
            {
                return list;
            }

            var items = (Item*)p;
            for (int i = 0; i < count; i++)
            {
                if (items[i].CStatus is PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA && items[i].Name != null)
                {
                    list.Add((new string(items[i].Name), items[i].Value));
                }
            }
        }

        return list;
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            _ = PdhCloseQuery(_query);
            _query = 0;
        }
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    private static partial int PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, Item* items);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCloseQuery(nint query);
}
