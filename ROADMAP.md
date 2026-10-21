# DiskScope roadmap

Status as of 2026-10-19. Part 1 is in progress. Part 2 starts only after Part 1 is complete.

## Part 1: Storage module (in progress)

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
