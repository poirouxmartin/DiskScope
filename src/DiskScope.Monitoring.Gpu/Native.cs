using System.Runtime.InteropServices;

namespace DiskScope.Monitoring.Gpu;

/// <summary>Adapter identity and capacities from DXGI (IDXGIAdapter1::GetDesc1).</summary>
public sealed record GpuAdapterInfo(
    string Name,
    string Luid,
    uint VendorId,
    long DedicatedVideoMemory,
    long DedicatedSystemMemory,
    long SharedSystemMemory,
    bool IsSoftware);

/// <summary>
/// DXGI adapter enumeration through raw COM vtable calls (no interop assembly):
/// IDXGIFactory1::EnumAdapters1 is vtable slot 12 (IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5),
/// IDXGIAdapter1::GetDesc1 is slot 10 (IUnknown 3 + IDXGIObject 4 + IDXGIAdapter 3), Release is slot 2.
/// </summary>
internal static unsafe partial class Dxgi
{
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out nint factory);

    public static IReadOnlyList<GpuAdapterInfo> EnumerateAdapters()
    {
        var result = new List<GpuAdapterInfo>();
        if (CreateDXGIFactory1(IID_IDXGIFactory1, out nint factory) < 0 || factory == 0)
        {
            return result;
        }

        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(void***)factory)[12];
            for (uint i = 0; ; i++)
            {
                nint adapter;
                int hr = enumAdapters1(factory, i, &adapter);
                if (hr == DXGI_ERROR_NOT_FOUND || hr < 0)
                {
                    break;
                }

                try
                {
                    var getDesc1 = (delegate* unmanaged[Stdcall]<nint, DXGI_ADAPTER_DESC1*, int>)(*(void***)adapter)[10];
                    DXGI_ADAPTER_DESC1 d;
                    if (getDesc1(adapter, &d) >= 0)
                    {
                        string name = new string(d.Description).TrimEnd('\0');
                        result.Add(new GpuAdapterInfo(
                            name,
                            GpuCounterMath.FormatLuid(d.LuidLowPart, d.LuidHighPart),
                            d.VendorId,
                            (long)d.DedicatedVideoMemory,
                            (long)d.DedicatedSystemMemory,
                            (long)d.SharedSystemMemory,
                            (d.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0));
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        return result;
    }

    private static void Release(nint unknown) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)unknown)[2])(unknown);
}

/// <summary>
/// Minimal PDH query with English counter paths (PdhAddEnglishCounterW), so counter names do not depend on the
/// Windows display language. Wildcard instances are read with PdhGetFormattedCounterArrayW.
/// </summary>
internal sealed unsafe partial class PdhQuery : IDisposable
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
