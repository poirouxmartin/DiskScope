# Monitoring: metrics, sources and limits

Every metric carries an id, a unit, a source and, when computed, a definition (`MetricDescriptor`). A value that cannot be read is `Unavailable` (`MetricValue.Value == null`) and is displayed `n/a`, never `0`.

## Pipeline

```
collectors (IMetricCollector)  ->  MonitoringScheduler  ->  IMetricStore (history)
   CPU, Memory, Volumes,             one loop per collector,   InMemoryMetricStore
   GPU, DiskScope (self)             isolates exceptions,      ->  AlertEngine (rules)
                                     measures each collection  ->  latest values
                                                                    ^ pulled once per second by the UI
```

- A collector never touches WPF. The UI pulls the latest values and the recent history once per second (`MonitoringViewModel.Refresh`), so the UI cost does not depend on the number of batches.
- A collector that throws publishes a batch where all its metrics are `Unavailable`, plus the error. The others keep running.
- A failing history store does not stop monitoring: the failure is counted (`StoreFailures`) and shown in the status bar.
- Timestamps come from `MonitoringClock`: the wall clock is read once, then time advances with `Stopwatch`, so batches never go back in time when the system clock changes.

## CPU (`DiskScope.Monitoring.CpuRam`)

| Id | Definition | Source |
|----|------------|--------|
| `cpu.total` | (kernel + user - idle) / (kernel + user), between two collections | GetSystemTimes |
| `cpu.user` | user / (kernel + user) | GetSystemTimes |
| `cpu.kernel` | (kernel - idle) / (kernel + user) | GetSystemTimes |
| `cpu.core.N` | same as `cpu.total`, per logical processor | NtQuerySystemInformation(SystemProcessorPerformanceInformation) |

- GetSystemTimes kernel time includes idle time (documented). The per-processor source behaves the same way: checked on the dev machine, every per-processor value is computable (`CpuRamTests`).
- NtQuerySystemInformation is resolved at run time, as its documentation requires. If it disappears, per-processor values are not published and the collector says so.
- The first collection has no previous sample: all CPU values are `Unavailable` until the second one.
- A delta that goes backwards or where idle grows faster than kernel time is reported `Unavailable`, not clamped.
- Limits: above 64 logical processors, both sources cover only one processor group. CPU frequency is not published: Windows exposes no reliable effective frequency without a driver.

## Memory

| Id | Definition | Source |
|----|------------|--------|
| `mem.physical.total`, `.available`, `.used`, `.load` | used = total - available; available = standby + free + zero pages | GlobalMemoryStatusEx |
| `mem.commit.used`, `.limit`, `.peak`, `.percent` | pages x page size; the limit is soft if the page file can grow | GetPerformanceInfo |
| `mem.cache`, `mem.pool.paged`, `mem.pool.nonpaged` | standby list + system working set; kernel pools | GetPerformanceInfo |
| `sys.processes`, `sys.threads`, `sys.handles` | counts | GetPerformanceInfo |

The two sources fail independently: one failing call only makes its own metrics `Unavailable`.

## Processes (`DiskScope.Processes`)

- Enumeration: Toolhelp32 snapshot (PID, parent PID, threads, image name; no handle needed).
- Details: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` then GetProcessTimes, GetProcessMemoryInfo, GetProcessHandleCount, GetProcessIoCounters, QueryFullProcessImageName. A process that refuses the open is listed with those fields `n/a`. Not elevated, about a third of the processes refuse on the dev machine (system services, protected processes).
- Identity: PID + creation time (`ProcessKey`). A reused PID never inherits the previous process's CPU time, and a parent is accepted only if it was created before its child.
- CPU % = delta of user + kernel time / elapsed time / logical processors: percent of the whole machine, as in Task Manager.
- Private bytes = commit charge (PrivateUsage). Private working set needs PROCESS_MEMORY_COUNTERS_EX2 (Windows 10/11 22H2 with the September 2023 update); older builds show `n/a`.
- Memory growth: `MemoryGrowthDetector` flags private bytes that grew for at least 5 min of a 10 min window, by at least max(100 MB, 20 %), with 80 % non-decreasing steps and growth in both halves of the span. Shown as "sustained growth, to verify (not a proven leak)". Known false positives: caches filling up, large documents loading.
- Not implemented yet: command line, user, signature (on demand), exited processes in history.

## GPU (`DiskScope.Monitoring.Gpu`)

See `docs/GPU-COMPATIBILITY.md`.

## Volumes

`disk.X.free`, `disk.X.total`, `disk.X.used` for the fixed volumes present at start-up (DriveInfo / GetDiskFreeSpaceEx). "Free" is the space available to the current user.

## History (`DiskScope.History`)

- In memory, per metric and tier: 1 s for 10 min, 5 s for 1 h, 1 min for 24 h (configurable `RetentionTier`).
- Every value goes to every tier on arrival; each bucket keeps min, average, max and sample count.
- A query uses the finest tier that still holds the start of the range, or that never dropped anything.
- Unavailable values are not stored: a period without data is a gap, drawn as a gap.
- Not persisted: history is lost when DiskScope exits. Persistence (SQLite, with batched writes and schema migrations, as the spec suggests) is a later step; it would add the first external dependency and needs a decision.

## Alerts (`DiskScope.Diagnostics`)

| Rule | Condition | Min duration | Recovery | Cooldown |
|------|-----------|--------------|----------|----------|
| Sustained high CPU | `cpu.total` > 90 % | 30 s | 10 s | 1 min |
| Very low available memory | `mem.physical.available` < 512 MB | 10 s | 10 s | 1 min |
| Commit charge near its limit | `mem.commit.percent` > 90 % | 10 s | 10 s | 1 min |

- One episode raises one event. It ends when the condition has been false for the recovery time. If the condition returns within the cooldown, the same event is reopened: a flapping metric gives one episode, not a stream of alerts.
- Unavailable values neither start nor end an episode.
- Each event keeps its evidence (first value over the duration, worst value) and an explanation that says it is a measured fact, not a diagnosis.
- Thresholds and enabling are editable in the History & alerts tab; byte thresholds are edited in MB.
- Low memory uses an absolute amount, not a percentage of RAM: a high "in use" percentage is normal with a large cache.

## Comparison with Windows tools

Not done yet in a controlled way. Expected differences by definition:

- Task Manager "CPU" uses the same idle/kernel/user arithmetic; it may differ slightly in sampling instants. On Windows 11 it can also show "processor utility" (frequency-scaled), which DiskScope does not compute.
- Task Manager "In use" excludes the compressed store from some displays; DiskScope's "in use" is total - available.
- GPU: see the GPU document.
