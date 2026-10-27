# DiskScope roadmap

Status as of 2026-10-26. Part 1 is complete except the items blocked by the environment (elevation, quiet machine); summary in docs/STORAGE-REPORT.md. Part 2 is in progress (summary in docs/MONITORING-REPORT.md): steps A to E and H done; F and G partly done, their remaining parts need decisions (dependencies, kernel driver, elevation).

Target: open-source publication on the owner's GitHub. LICENSE (MIT), English README, CI (GitHub Actions, windows-latest) and a privacy audit of the whole history are in place. The public repository exists (`main` at 9c4ab12, CI green there); later work is on a local branch, not pushed (needs the owner's go). Checklist: docs/PUBLISHING.md.

## Part 1: Storage module (done, except blocked items)

Initial spec: prompt of 2026-10-19.

### Done (commit fa9fb14)

- Solution scaffold.
- Core NodeIndex (struct-of-arrays, O(n) aggregation).
- Scan engine contract and orchestrator.
- Standard engine (GetFileInformationByHandleEx).
- Benchmarks harness.
- NTFS risk study.
- Benchmark protocol.
- 29 tests.

Then: size queries (children by size, bounded-heap largest files); virtualized lazy tree sorted by size,
logical/allocated switch without rescan, largest-files list (top 1000), context menu (copy path,
open in Explorer). Checked in the running app through UI Automation on `.nuget\packages`. 45 tests.
Known limit: tree number columns shift by one indent per depth level (fixed-width name column).

Then: search and filters (name, extensions, size range, modification date range, type) as a linear scan with a bounded top-1000 result, debounced and cancelled per keystroke; files with an unknown date are excluded from date filters and counted, never matched; extension statistics tab. Checked in the running app (`dll` on `.nuget\packages`: 3,541 matches). 70 tests.

Then: squarified treemap (Core layout, pure and tested; one WPF element drawing in OnRender, capped at 100 000 rectangles, areas under 9 px² dropped; click selects, double-click zooms into a directory, right-click zooms out) synchronized with the tree (tree selection to treemap, treemap selection reveals and selects the node in the tree); CSV export (RFC 4180, depth-first, no path string per file, unknown dates left empty). Rendering checked in the running app. NOT verified in the running app: mouse click selection in the treemap (posted clicks do not reach WPF and real clicks were refused while the session was locked); the view-model side is covered by tests. 96 tests.

Then (2026-10-21): treemap mouse interaction checked in the running app with real clicks on `C:\Program Files\dotnet` (click selects and reveals the node in the tree, double-click zooms into the directory, right-click zooms out). Partial results during the scan: `NodeIndexBuilder.Snapshot` copies the builder on the scan thread and flags pending directories NotScanned, so the root reads SubtreeIncomplete; `ScanOptions.PartialResultInterval` (app: 2 s, doubling after each snapshot, capped at 30 s, never shorter than 10x the last snapshot cost); `IScanObserver.OnPartialResult`. Checked in the running app on `C:\Windows` (summary shows PARTIAL RESULT during the scan). Known limit: each partial refresh rebuilds the tree, so expansion state is lost. Changes during the scan: a test seam in the standard engine deletes or renames a directory between listing and enumeration (Vanished, NotFound error, siblings still counted). 107 tests.

Then: NTFS prototype, parts that do not need elevation (USN record parser, tree rebuild with orphans, root reference, `usn-probe` benchmark command). Status and what is not verified: docs/NTFS-RISKS.md section 8. 123 tests.

Then (2026-10-26): limits found on real use and fixable without elevation: last change in the subtree (tree column, CSV column), lookup by path and `inspect` CLI command (denied directories listed), `GetKnownTotalSize` (unknown totals show `n/a`, never 0), tree expansion and selection kept across partial refreshes. Details: docs/STORAGE-REPORT.md. 255 tests.

### Remaining (blocked by the environment)

1. NTFS probe on a real volume, then the sizes decision (docs/NTFS-RISKS.md section 4): needs an elevated console.
2. Reference benchmarks on a quiet machine (docs/BENCHMARK-PROTOCOL.md).

Hard links: decided by default to keep "once per path" in the standard engine (it cannot see link counts without one handle per file). To revisit with the NTFS engine, which sees the file reference.

## Part 2: Monitoring, diagnostic and maintenance modules

Source: docs/spec/DiskScope_Specifications_Modules.md (the .docx next to it is the source of truth).

Priorities (spec section 2):

- Very high: CPU/RAM monitoring, Process Explorer.
- High: History and alerts, GPU monitor, Dashboard.
- Medium: Hardware health, startup and services, reports and export.
- Low at first: ETW diagnostics.

Spec roadmap steps (spec section 15):

- A. Inspect the existing solution and architecture; no duplicate projects.
- B. Shared contracts for metrics, states, errors and cancellation.
- C. CPU/RAM and Process Explorer, with tests.
- D. Dashboard and local history.
- E. GPU, with a hardware compatibility matrix.
- F. Hardware sensors and startup/services inventory.
- G. Correlation engine, then a first ETW scenario.
- H. Exports, documentation and regression tests.

### Step A decision (2026-10-21): DiskScope.Core holds the shared contracts

The storage index moved from `DiskScope.Core` to a new `DiskScope.Storage` project (namespace `DiskScope.Storage`); `DiskScope.Core` now holds only the contracts shared by every module (metrics, quality, availability, collector errors).

Why this option rather than a separate `DiskScope.Monitoring.Abstractions` next to an unchanged Core:

- The spec (section 1) asks that new modules integrate "without coupling them strongly to the core". With the index in Core, every monitoring module would reference storage types it never uses, or Core would end up meaning two things.
- Storage becomes one module among others, which is what it is. Its types do not leak into CPU, GPU or process code.
- The cost was one mechanical rename (45 files, namespaces only, no behavior change, 123 tests green before and after). The repository has no external consumers yet, so nothing else breaks. A later rename, once the repo is public, would cost more.
- No real trade-off was left, so the choice was made without asking.

Other rules kept from the spec:

- Collectors never touch WPF; they return batches and the scheduler publishes them.
- The App shell gains navigation between modules (Dashboard, Storage, CPU & memory, Processes, GPU).

### Done (2026-10-21)

- B. `DiskScope.Core`: `IMetricCollector`, `MetricDescriptor` (unit, source, definition), `MetricValue` (null when unavailable, NaN refused), `MetricBatch`, `CollectorAvailability`, `CollectorError`, `IMetricStore`, `IAlertEngine`, `ThresholdRule`, `FindingLevel`. `DiskScope.Monitoring`: scheduler (one loop per collector, failure isolation, cost and missed-tick counters, survives a failing store), monotonic clock.
- C. CPU (GetSystemTimes, per-processor NtQuerySystemInformation resolved at run time), memory (GlobalMemoryStatusEx, GetPerformanceInfo), process explorer (Toolhelp32 + PROCESS_QUERY_LIMITED_INFORMATION, PID + creation time identity, tree with PID-reuse check, CPU deltas, in-place UI update), self-cost collector.
- D. Dashboard (cards open their module), in-memory tiered history with gaps, alert engine (episodes, recovery, cooldown, evidence, editable thresholds, acknowledgement), memory growth suspicion per process.
- E. GPU via PDH English counters + DXGI by LUID, engines by type, dedicated vs shared memory, per-process attribution; compatibility matrix in `docs/GPU-COMPATIBILITY.md` (one machine so far).
- Checked in the running app (screenshots in `docs/images`): every tab renders live values; a DataGrid star column collapsed in the processes tab and was fixed. Monitoring cost measured (`docs/benchmarks/RESULTS.md`). 177 tests.

- F (partial). Read-only start-up and services inventory with CSV export (`DiskScope.Maintenance`); checked in the app on the dev machine (68 entries, 345 services, every source read). Hardware sensors: not started (see below).
- G (partial). Disk activity collector; `Finding`, `Confidence`, `IDiagnosticRule` in Core; two coincidence hypotheses (CPU or low memory with heavy disk activity) and a disk latency alert. ETW: not started (see below).
- H. History CSV, process CSV, JSON and HTML reports (`DiskScope.Reports`); HTML export checked end to end from the running app (real data, no user or machine name in the file). Docs updated. 209 tests.

### Done (2026-10-26)

- Tested on a second, busy machine (work laptop, not elevated): build, 214 tests, full-volume scan, monitoring for 5 minutes, app memory over 10 minutes. Fixes: `compare` survives a directory vanishing mid-walk; view models have UI Automation names.
- Metric tiles show average, maximum and a least-squares trend over their window (`MetricTrend`, Core); no trend before half the window is covered.
- Collector cost: CPU time of the collecting thread (GetThreadTimes) next to wall-clock time, which on a saturated machine mostly measures waiting.
- Privacy: commits authored with a work address were re-authored with the personal one before any push; tracked files, history patches, screenshots and the spec .docx metadata checked for work or personal data. 231 tests.

### Done (2026-10-27)

- "By application" view in the Processes tab (`ApplicationConsumerTracker`, Processes): processes grouped by image name, 5 minutes of history per group (CPU now, average, maximum; private bytes trend), and what closing each application would return (private working set and private bytes). Windows components, processes that cannot be inspected and DiskScope get no estimate; nothing is ever ended. Checked in the running app on the work laptop. Summary of Part 2: docs/MONITORING-REPORT.md. 270 tests.

### Remaining, in order

1. Hardware sensors (F): study `D3DKMTQueryAdapterInfo` with `KMTQAITYPE_ADAPTERPERFDATA` (GPU temperature, documented in the WDK) and WMI thermal zones (often absent or wrong on laptops). LibreHardwareMonitor only after a license (MPL 2.0), maintenance and driver check, as the spec asks; it loads a kernel driver for most sensors, which conflicts with the "no kernel driver" rule of Part 1: needs the owner's decision.
2. First ETW scenario (G): kernel providers need administrator rights (or the Performance Log Users group), so it cannot be exercised on the dev machine as is; the usual consumer library (Microsoft.Diagnostics.Tracing.TraceEvent, MIT) would be an external dependency: needs a decision. Candidate scenario: a bounded 30 s trace of disk I/O per process during a "High disk latency" alert.
3. History persistence (SQLite would be the first external dependency: needs a decision).
4. Process details on demand: command line, user, signature; exited processes in history.
5. Side-by-side comparison with Task Manager; 10-minute measurement of the WPF app's own memory.
6. Startup changes (disable/enable) only if requested, with confirmation, dependencies shown and a way back (spec section 10).
