# DiskScope

A read-only Windows control center: where your disk space goes, and what your CPU, memory and GPU are doing. Native WPF on .NET 10, no external runtime dependency.

DiskScope measures; it does not "optimize" behind your back. It never deletes, moves or modifies files, never stops a process, never changes a service. When a value cannot be read, it says `n/a` instead of showing `0`.

![Dashboard](docs/images/dashboard.png)

## Features

### Storage

- Scan any folder or drive with a standard engine built on `GetFileInformationByHandleEx` (one handle per directory, sizes from directory entries). Handles access denied, long paths, junctions and symlinks (never followed, so no cycles), files deleted during the scan, cancellation.
- Results are shown while the scan runs, flagged as partial until it ends. An inaccessible folder is flagged, never shown as empty.
- Size tree sorted by size, logical or allocated size (never mixed), file counts, percentages.
- Squarified treemap drawn in one element (no control per file), synchronized with the tree: click to select, double-click to zoom in, right-click to zoom out.
- Search and filters (name, extensions, size, modification date, type), largest files, size per extension.
- CSV export (RFC 4180). Copy path and open in Explorer from the context menu.
- Index sized for millions of entries: struct-of-arrays, no object or path string per file (about 250 bytes per node, measured).

### Monitoring

- **Dashboard**: CPU, memory in use, commit charge, GPU, free space per volume, active alerts, memory growth suspicions, DiskScope's own CPU. Each card opens its module.
- **CPU & memory**: global and per-logical-processor usage, user/kernel split, physical memory, commit charge and limit, cache, kernel pools, process/thread/handle counts. Every counter lists its definition and Windows source.
- **Processes**: parent/child tree (PID reuse detected with creation times), CPU % of the machine, private bytes, working set, private working set, threads, handles, I/O, start time, path. Sustained private memory growth is flagged as a suspicion to verify, not as a leak.
- **GPU**: per adapter, usage per engine type (3D, copy, video decode/encode/processing, compute), dedicated and shared memory, processes using the GPU. Engines are not added together; shared memory is not presented as VRAM.
- **History & alerts**: in-memory history (1 s for 10 min, 5 s for 1 h, 1 min for 24 h), explainable alerts with evidence, editable thresholds, acknowledgement.
- The cost of monitoring is itself measured and displayed (collection time per collector, DiskScope CPU and memory).

| CPU & memory | GPU |
|---|---|
| ![CPU and memory](docs/images/cpu-memory.png) | ![GPU](docs/images/gpu.png) |

## Requirements

- Windows 10 or 11, x64. GPU monitoring needs WDDM 2.0 or later (Windows 10 1709+).
- To build: the .NET 10 SDK (the version pinned in `global.json`, or a later feature band).
- No administrator rights needed. Without them, some system processes show `n/a` for their details, and the NTFS fast path (not finished, see below) is unavailable.

## Build and run

```
git clone https://github.com/poirouxmartin/DiskScope.git
cd DiskScope
dotnet build DiskScope.slnx -c Release
dotnet run --project src/DiskScope.App -c Release
```

Tests:

```
dotnet test DiskScope.slnx
```

There is no installer or signed release yet.

## Measured performance

Numbers come from `src/DiskScope.Benchmarks` with the procedure in `docs/BENCHMARK-PROTOCOL.md`, recorded in `docs/benchmarks/RESULTS.md`. Highlights, from a single noisy dev laptop (so indicative only):

- Index: 1 million synthetic nodes built in about 200 ms, 251 bytes per node; search over them in 31 ms.
- Monitoring without UI: 0.25 % of the machine's CPU; collectors take 0.1 to 3.3 ms on average; listing about 545 processes takes 72 ms (median).

```
dotnet run -c Release --project src/DiskScope.Benchmarks -- monitor --seconds 60
dotnet run -c Release --project src/DiskScope.Benchmarks -- synthetic
dotnet run -c Release --project src/DiskScope.Benchmarks -- scan <folder>
```

## Status and limits

Work in progress. Known gaps:

- NTFS fast engine: the USN record parser and tree rebuild are written and tested, but enumeration of a real volume needs an elevated console and has not been run yet; sizes still come from the standard engine (`docs/NTFS-RISKS.md`).
- History is kept in memory only and lost on exit.
- No CPU frequency, no GPU temperature, clocks, power or fans, no hardware sensors, no network monitoring yet: no reliable vendor-neutral source has been validated.
- Not compared side by side with Task Manager yet; expected differences are documented in `docs/MONITORING.md`.

Roadmap: `ROADMAP.md`.

## Documentation

- `docs/ARCHITECTURE.md`: modules, storage index, counting rules, engine contract.
- `docs/MONITORING.md`: every metric, its formula, source and limits; history; alert rules.
- `docs/GPU-COMPATIBILITY.md`: GPU sources, definitions and tested hardware.
- `docs/NTFS-RISKS.md`: NTFS engine study and status.
- `docs/BENCHMARK-PROTOCOL.md`, `docs/benchmarks/RESULTS.md`: how performance is measured, and the results.
- `docs/spec/`: the original specification (French).

## License

MIT, see `LICENSE`.
