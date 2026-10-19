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

### Remaining, in order

1. Virtualized tree sorted by size, largest-files list, context menu (copy path, open in Explorer). (in progress)
2. Search and filters (extension, size, date, type); extension statistics.
3. Treemap (custom rendering, squarified layout) synchronized with tree selection.
4. CSV export.
5. Partial-tree publication during scan (reader-safe snapshot of the builder).
6. Vanished-during-scan test via an injection point in the engine.
7. NTFS engine prototype (needs an elevated session, see docs/NTFS-RISKS.md).
8. Reference benchmarks on a quiet machine (docs/BENCHMARK-PROTOCOL.md).

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
