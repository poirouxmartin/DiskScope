# GPU monitoring: sources and compatibility

## Sources

| Data | Source | Notes |
|------|--------|-------|
| Adapter name, vendor, LUID, dedicated video memory, shared system memory limit | DXGI `IDXGIAdapter1::GetDesc1` | Software adapters (Microsoft Basic Render Driver) are skipped. On integrated GPUs "dedicated" is a small carve-out. |
| Engine utilization per process and engine | PDH `\GPU Engine(*)\Utilization Percentage` | English counter names (`PdhAddEnglishCounterW`), so the Windows display language does not matter. Rate counter: no value on the first collection. |
| Dedicated and shared memory in use per adapter | PDH `\GPU Adapter Memory(*)\Dedicated Usage`, `Shared Usage` | Summed over physical adapters of a LUID. |

The counter instance names carry the adapter LUID (`luid_0xHIGH_0xLOW`); DXGI gives the same LUID, which is how both sources are joined.

## Definitions

- Engine usage: sum over processes for one engine (capped at 100 %), then the busiest engine of each type (there may be several Copy or Compute engines).
- Adapter usage: the busiest engine type. Engine types run in parallel and are never added together (3D 60 % + Copy 30 % is not 90 %). This is the Task Manager definition.
- Process usage (GPU tab list): the busiest engine used by the process.
- An engine type never seen on an adapter is `n/a`. Once seen, a sample with no instance of it means no process holds a context on it, reported as 0 %.
- Shared GPU memory is system RAM, not video memory, and is shown separately.

## Not provided

- Temperature, clock, power and fan speed: these counters do not expose them, and no vendor-neutral, documented user-mode source has been validated. Candidates to study: `D3DKMTQueryAdapterInfo` with `KMTQAITYPE_ADAPTERPERFDATA` (WDK, used by Task Manager for GPU temperature), vendor SDKs (NVML, ADLX, Intel IGCL) behind `IHardwareTelemetryProvider`.
- `IDXGIAdapter3::QueryVideoMemoryInfo`: it reports the budget and usage of the calling process only, not the whole adapter, so it is not shown as a VRAM figure.

## Compatibility matrix

| Machine | Windows | Adapter | Driver model | Engines seen | Memory counters | Verified on |
|---------|---------|---------|--------------|--------------|-----------------|-------------|
| Dev laptop | Windows 11 Pro 26100 | Intel Iris Xe Graphics (iGPU, vendor 0x8086) | WDDM 2.x | 3D, Copy, VideoDecode, VideoProcessing, other; VideoEncode and Compute not seen | Dedicated and shared | 2026-10-21, GPU tab and `GpuTests` |

To add a row: run DiskScope on the machine, open the GPU tab, note which engine rows read `n/a`, and run `dotnet test --filter GpuTests`.

Requirements: the GPU Engine counters need WDDM 2.0 or later (Windows 10 1709+). Without them the GPU collector reports itself unavailable with the reason, and the other collectors keep running.

## Comparison with Task Manager

Not done yet side by side. By definition the adapter and engine figures should match Task Manager's GPU tab, which reads the same counters with the same aggregation; differences are expected from sampling instants only.
