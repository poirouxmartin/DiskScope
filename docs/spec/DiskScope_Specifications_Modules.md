> Extracted from DiskScope_Specifications_Modules.docx (same folder, the .docx is the source of truth). Received 2026-10-19.

# DiskScope

Specifications for the Windows monitoring, diagnostics and maintenance modules

## Technical base: C# · .NET 10 · WPF · MVVM

This document gathers the proposed specifications for the modules to be developed after the Storage module, which has already been assigned to the development agent. It defines a common architecture, the features, the data sources, the risks and a progressive roadmap.

# 1. Product vision

DiskScope should become a Windows control center that explains what consumes resources, identifies slowdowns and performs careful, reversible maintenance. The goal is not to artificially display minimal usage, but to provide reliable measurements and verifiable diagnostics.

- Native Windows application, read-only for measurement functions.

- Maintenance actions kept separate, explicitly confirmed and reversible whenever possible.

- Local history, explainable alerts and correlation between resources.

- No automatic deletion, service disabling or system modification without explicit confirmation.

- The Storage module is already under development; the other modules must integrate without being tightly coupled to the core.

# 2. Scope and priorities

| Module | Priority | Indicative complexity | Main goal |
|---|---|---|---|
| Monitoring CPU/RAM | Very high | Medium | Global and per-process measurements, trends |
| Process Explorer | Very high | Medium | Process tree and resources consumed |
| History & alerts | High | Medium | Find the possible causes of a slowdown |
| GPU Monitor | High | High | GPU engines, VRAM and available sensors |
| Dashboard | High | Low to medium | Real-time summary view |
| Hardware health | Medium | High | Temperatures, frequencies, fans if exposed |
| Startup & services | Medium | Medium | Inventory and reversible actions |
| Reports & export | Medium | Low | Readable, exportable reports |
| ETW diagnostics | Low at first | Very high | In-depth analysis of stalls and latencies |

# 3. Common architecture

Keep a layered architecture, with independent collectors and stable contracts.

```
DiskScope.App                 WPF / MVVM / navigation / views
DiskScope.Core                models, contracts, diagnostic rules
DiskScope.Monitoring          collector orchestration and scheduling
DiskScope.Monitoring.CpuRam   CPU and memory metrics
DiskScope.Monitoring.Gpu      GPU counters and hardware adapters
DiskScope.Processes           process inventory and tree
DiskScope.History             local persistence and retention
DiskScope.Diagnostics         alerts, correlations and explainable rules
DiskScope.Hardware            sensors and hardware health
DiskScope.Maintenance         startup, tasks and services
DiskScope.Reports             exports and reports
DiskScope.Tests               unit and integration tests
DiskScope.Benchmarks          reproducible measurements
```

The names are proposals: if the existing solution already has a naming convention or a Core project, reuse it rather than creating duplicates.

## Common contracts

- IMetricCollector: collects the metrics of one category, with a configurable interval, cancellation and availability state.

- IMetricStore: persistence and querying of timestamped measurements.

- IAlertEngine: rule evaluation, duration of threshold breach, severity, evidence and acknowledgment.

- IDiagnosticRule: deterministic rule that provides a finding, the observed data, the confidence level and the limits.

- IHardwareTelemetryProvider: optional adapter for hardware-specific sensors.

- Collectors publish in batches; they do not manipulate WPF controls directly.

## Non-negotiable principles

- No claim of success without actually running the tests or benchmarks.

- No strong dependency between the UI, the collectors and the storage.

- Handle unavailable metrics: do not display zero when a value is unknown.

- Each metric must specify its unit, its source, its timestamp and, where needed, its quality status.

- Collection must have a measured cost and a configurable frequency.

# 4. CPU & RAM Monitoring module

## Goal

Measure load and memory evolution, with enough history to spot memory pressure or abnormal consumption.

## CPU

- Global and per-logical-processor utilization, computed from counter differences over time.

- User/kernel CPU time when available.

- Per-process CPU consumption, from processor time deltas and elapsed time.

- CPU frequency only if a reliable source exposes it; distinguish nominal, reported and effective frequency.

## RAM

- Total, available and used physical memory.

- Committed memory (commit) and commit limit.

- Cache, paged and non-paged pool when the corresponding counters are available.

- Per-process memory: working set, private working set and private/committed memory depending on the APIs used.

- Time trend of a process; qualify a leak only as a hypothesis until it is confirmed.

## Technical sources

| Need | API / source | Notes |
|---|---|---|
| Global CPU | GetSystemTimes | Computed as the difference between two samples |
| Process CPU | GetProcessTimes | Normalize by the number of logical processors if a system-wide percentage is displayed |
| Global RAM | GlobalMemoryStatusEx | Global physical and virtual memory state |
| Process RAM | GetProcessMemoryInfo | Working set; complete with suitable counters for private memory |
| Additional counters | PDH / Performance Counters | Use at a reasonable frequency and test the localized counter names |

## Refresh and retention

- Starting point: lightweight collection every 1 second, configurable.

- Indicative retention: 1 second for 10 minutes, 5 seconds for 1 hour, 1 minute for 24 hours.

- Retention rules must be configurable; aggregate old points rather than keeping every raw sample indefinitely.

## Initial alerts

- CPU above 90% for 30 seconds, with a configurable threshold and a debounce delay.

- Very low available memory or commit close to its limit; do not rely only on the percentage of RAM in use.

- Sustained growth of a process's memory over a time window, presented as a suspicion to verify.

## Acceptance criteria

- Counters are computed with the documented units and monotonic/consistent timestamps.

- A terminated or inaccessible process does not crash the collection.

- The load added by monitoring is measured and stays low on the reference machine.

# 5. GPU Monitor module

Provide an accurate view of GPU engines and video memory, explicitly flagging measurements that are unavailable depending on the driver.

## Target metrics

- Utilization per engine: 3D, Compute, Copy, Encode, Decode and other exposed engines.

- Dedicated and shared GPU memory; distinguish budget, usage and physical capacity when the APIs provide these fields.

- Temperature, frequency, power and fan speed only if a reliable source exposes them.

- Per-adapter data to handle multi-GPU systems.

## Sources and design

- Explore the Windows GPU counters and the appropriate DXGI/D3DKMT interfaces.

- IDXGIAdapter3::QueryVideoMemoryInfo can provide video memory information tied to the process and the segment; do not present it as a universal measurement of all VRAM.

- Create a provider abstraction to add separate vendor integrations if needed.

- Do not depend on a single vendor and do not assume that temperatures or power readings are always available.

## Risks

- The available counters vary by Windows version, driver and hardware.

- A global GPU utilization figure can hide a saturated engine; show the engines separately.

- Shared memory is not equivalent to dedicated VRAM.

## Acceptance criteria

- An unavailable adapter does not prevent the rest of the monitoring from working.

- Metrics have a source, a unit and an explicit unavailability status.

- The data is compared with Task Manager or a reference tool, documenting the differences in definition.

# 6. Process Explorer module

Provide a hierarchical view of processes, their resources and the information useful for diagnostics.

## Features

- Parent/child tree built with PID, PPID and creation time; handle PID reuse.

- CPU, working set, private memory, I/O when available, and linkage to GPU metrics if the source allows reliable attribution.

- Image name, executable path, command line depending on permissions, user and start time.

- Thread and handle counts if collection is reliable and its cost is acceptable.

- Digital signature and publisher as complementary information, loaded on demand.

- Search, filters, sorting and display of terminated processes in the history if enabled.

## Security and accuracy

- Do not kill processes or change their priority in the first version.

- Do not assume that command line or module information is accessible for all processes.

- Identify a process by PID and creation time, not by PID alone.

- Do not promise per-process GPU attribution if the source does not provide it.

## Acceptance criteria

- Processes that appear or disappear during a refresh are handled without a fatal error.

- Columns clearly indicate their units and calculation method.

- Costly operations (signature, modules, details) are deferred or triggered on demand.

# 7. Dashboard module

Present the PC's state at a glance and provide access to the detailed modules.

- Cards for CPU, RAM, GPU/VRAM, storage, network and available sensors.

- Current value, short trend, recent maximum and availability state.

- Each card opens the corresponding detailed module.

- Lightweight metrics: indicative interval of 1 second; costlier sensors: 2 to 5 seconds depending on the provider.

- Avoid copying data: the dashboard consumes the common model and the latest published measurements.

- Show data gaps; do not invent a temperature or a missing metric.

# 8. History & Alerts module

## Goal

Keep time series locally and make it possible to find out what happened before or during a slowdown.

## Features

- History browsable by time range, resource and process.

- Configurable rules with threshold, minimum duration, recovery period and debounce delay.

- Alert event with timestamp, source measurements, severity, explanation and acknowledgment status.

- Aggregation of old measurements and configurable retention.

- SQLite is a suitable option for local storage; define a batched write strategy and a schema migration strategy.

## Requirements

- Distinguish a factual alert (measured threshold) from a diagnostic hypothesis.

- Avoid repeated alerts for the same episode.

- The user can disable a rule or change its threshold.

- The application keeps working if the history database is temporarily unavailable; report the possible loss of measurements.

# 9. Hardware health module

Group the available sensors without claiming that all hardware exposes the same information.

- CPU/GPU/SSD temperatures, frequencies, power, voltages and fans only when provided by an identifiable source.

- WMI can complement some data; prefer a provider interface for hardware-specific sensors.

- Evaluate the use of a compatible library, for example LibreHardwareMonitor, after checking its license, its maintenance and its compatibility.

- Distinguish manufacturer threshold, configurable threshold and simple generic warning.

- Do not trigger any automatic hardware action; do not modify fan curves or voltages in the initial scope.

## Acceptance criteria

- Each sensor indicates its name, unit, source and validity state.

- Impossible or frozen values are flagged rather than presented as reliable.

- Thresholds are adapted to the component when documented; otherwise the UI states that it is a generic threshold.

# 10. Startup & Services module

Help examine the items launched automatically, without disabling components at random.

## Sources to inventory

- Startup applications and the Run/RunOnce registry keys.

- User and system startup folders.

- Scheduled tasks.

- Windows services: startup type, state, account and dependencies.

## Features and safeguards

- Show the publisher, the path, the signature when available and the source of the entry.

- Do not display an invented impact score; use observed measurements or state that the impact is unknown.

- Any change requires confirmation and must be reversible when possible.

- Before modifying a service or a task, show the dependencies and the known risk.

- The first version can be read-only, with inventory export.

# 11. ETW Diagnostics module

Expert module to be developed after the collectors and the history have stabilized.

- Start explicit, time-bounded trace sessions.

- Explore CPU, disk I/O, scheduling, DPC/ISR and graphics events depending on the providers enabled.

- Provide a timeline correlated with the base metrics.

- Handle permissions, collection cost, trace size and their deletion/archiving.

- Avoid prematurely implementing a full clone of Windows Performance Analyzer; start with a specific diagnostic scenario.

- Do not declare a root cause if the events only suffice to suggest a correlation.

# 12. Reports & Export module

- CSV export of measurements, processes and inventories.

- Structured JSON export for diagnostics and automation.

- Local HTML report with summary, time range, charts, alerts and collection limits.

- PDF in a later phase if the need is confirmed.

- Reports must state the date, the application version, the data sources and the unavailable metrics.

- Do not include full command lines or other potentially sensitive data by default in a shared report.

# 13. Diagnostic and correlation engine

The diagnostic engine turns metrics into explainable findings. Each finding must keep its evidence and its limits.

| Level | Meaning | Example |
|---|---|---|
| Information | Observed state with no established problem | A process is currently using 35% of the CPU |
| To watch | Unusual trend or sustained threshold | Private memory growing for several minutes |
| Alert | Measured, persistent condition | Available memory below a configured threshold |
| Hypothesis | Possible explanation, to be confirmed | The slowdown coincides with high disk activity |

- Each rule must document the formula, the time window, the thresholds and the known false positives.

- A temporal correlation is not proof of causality.

- Show the data that triggered the diagnosis and suggest concrete checks.

- Any corrective action must remain separate from the diagnosis and require confirmation.

# 14. Benchmark and quality

## Test scenarios

- PC idle for 10 minutes.

- Sustained CPU load and single-core load.

- Progressive memory allocation and processes terminated during collection.

- GPU used for 3D, compute and video memory, when the test hardware allows it.

- Heavy disk activity with low throughput or high latency.

- High process count, access denied errors and unavailable counters.

## Measurements to publish

- DiskScope's own CPU and memory consumption.

- Latency and duration of each collection.

- Rate of missed samples and errors per provider.

- Cost of the history in disk space and effect of retention.

- Comparison with the reference Windows tools, documenting the differences in definition.

Numeric performance targets must be set after measuring a baseline version, and not announced as achieved.

# 15. Recommended roadmap

- Step A: inspect the existing solution and its architecture; do not create duplicate projects.

- Step B: define the shared contracts for metrics, states, errors and cancellation.

- Step C: develop CPU/RAM and Process Explorer with tests.

- Step D: add the dashboard and the local history.

- Step E: develop GPU with a hardware compatibility matrix.

- Step F: add hardware sensors and the startup/services inventory.

- Step G: develop the correlation engine, then a first ETW scenario.

- Step H: finalize the exports, the documentation and the regression tests.

# 16. Common instructions for Claude Code

To be used as persistent instructions in the repository, or to be repeated at the start of each module. Adapt the project names to the existing solution before any modification.

```
Act as a senior C#/.NET/WPF engineer specialized in Windows APIs and performance diagnostics.
Before coding:
1. Inspect the solution and read the existing conventions.
2. Identify the projects already created, in particular the Storage module in progress.
3. Propose a plan limited to the requested module.
4. Verify the APIs in the official Microsoft documentation.
During development:
- Respect the existing architecture and avoid duplication.
- Separate the collectors, the data model and the MVVM interface.
- Handle cancellation, errors, permissions and unavailable metrics.
- Do not present an unknown value as zero.
- Do not run any destructive action or system modification without an explicit request.
- Add tests for the calculations and the edge cases.
- Build and run the tests after each step.
- Do not claim that a test or benchmark passed if it was not run.
- Measure performance instead of assuming it.
- End each step with a summary of the changes, test results and remaining limits.
Start by inspecting the repository and propose the next steps without rewriting the modules already in progress.
```

# 17. Starting technical references

- Microsoft Learn - GetSystemTimes - https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes

- Microsoft Learn - GlobalMemoryStatusEx - https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex

- Microsoft Learn - GetProcessMemoryInfo - https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo

- Microsoft Learn - Performance Counters - https://learn.microsoft.com/en-us/windows/win32/perfctrs/about-performance-counters

- Microsoft Learn - DXGI_QUERY_VIDEO_MEMORY_INFO - https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/ns-dxgi1_4-dxgi_query_video_memory_info

- Microsoft Learn - WPF - https://learn.microsoft.com/en-us/dotnet/desktop/wpf/

- Microsoft Learn - Event Tracing for Windows - https://learn.microsoft.com/en-us/windows/win32/etw/event-tracing-portal

Note: This document is an initial design specification, not a certification of the APIs or of performance. The precise APIs, permissions, compatibility limits and dependencies must be verified during implementation.

