namespace DiskScope.Monitoring.Gpu;

/// <summary>Adapter identity and capacities from DXGI (IDXGIAdapter1::GetDesc1).</summary>
public sealed record GpuAdapterInfo(
    string Name,
    string Luid,
    uint VendorId,
    long DedicatedVideoMemory,
    long SharedSystemMemory,
    bool IsSoftware);
