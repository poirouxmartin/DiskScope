using System.Runtime.InteropServices;

namespace DiskScope.Monitoring.Gpu;

/// <summary>
/// DXGI adapter enumeration through raw COM vtable calls (no interop assembly):
/// IDXGIFactory1::EnumAdapters1 is vtable slot 12 (IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5),
/// IDXGIAdapter1::GetDesc1 is slot 10 (IUnknown 3 + IDXGIObject 4 + IDXGIAdapter 3), Release is slot 2.
/// </summary>
internal static unsafe partial class NativeMethods
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
