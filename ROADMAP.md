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

### Remaining, in order

1. Manual check of treemap mouse interaction (click select, double-click zoom, right-click zoom out) on an unlocked session.
2. Partial-tree publication during scan (reader-safe snapshot of the builder).
3. Vanished-during-scan test via an injection point in the engine.
4. NTFS engine prototype (needs an elevated session, see docs/NTFS-RISKS.md).
5. Reference benchmarks on a quiet machine (docs/BENCHMARK-PROTOCOL.md).

Open decision: hard links counted once per path (current behavior) or once per file reference.

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

### Integration notes (decide at step A)

- DiskScope.Core currently holds the storage index.
- The spec's Core holds shared contracts: IMetricCollector, IMetricStore, IAlertEngine, IDiagnosticRule, IHardwareTelemetryProvider.
- Option 1: move the storage types to DiskScope.Storage and keep Core for shared contracts.
- Option 2: add DiskScope.Monitoring.Abstractions for the contracts and leave Core as is.
- Decide before step B, since step B creates the contracts.
- The App shell must gain navigation between modules.
