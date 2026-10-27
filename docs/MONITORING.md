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
- Cost of each collector (`CollectorStats`): wall-clock duration (last, max, total) and the CPU time of the collecting thread (`ThreadCpuClock`, GetThreadTimes). On a saturated machine the wall-clock figure mostly measures waiting for a processor; the CPU figure is the work itself. Windows counts thread time in clock ticks (about 15.6 ms), so only the mean over many collections is meaningful. One unreadable sample makes the CPU total unknown (`n/a`), never a low number.

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

### By application (`ApplicationConsumerTracker`)

The "By application" tab groups processes by image name (case-insensitive) at each refresh, keeps 5 minutes of history per group (CPU % sum and private bytes sum), and answers "what would closing it return".

- Sums cover the processes that could be opened; a group with an unreadable member shows how many were `n/a`, and its sums are lower bounds. A sum no member reported stays `n/a`, never 0.
- CPU average and maximum, and the private bytes trend, use `MetricTrend` over the window (no trend before half the window is covered, shown as "collecting").
- Kind, most cautious wins: DiskScope itself; any member running from the Windows directory is a Windows component ("do not end it"); any member that refused the open, or whose path is unknown, makes the group "not inspectable" (no advice: protected, elevated or security processes look like this); otherwise an application.
- Estimate for an application: its private working set (physical memory returned on exit) and its private bytes (commit returned on exit). Shared pages (DLLs, mapped files) and the file cache stay, so they are not counted. The total in the summary covers applications only.
- DiskScope never ends a process. The advice never says "safe": closing an application can lose unsaved work, and only the user can judge that.
- Limits: two programs with the same image name (for example two `python.exe` installs) form one group. Whether a process has a window, or is a background helper restarted by its parent, is not known yet. GPU memory per application is not included.

## GPU (`DiskScope.Monitoring.Gpu`)

See `docs/GPU-COMPATIBILITY.md`.

## Disk activity

`disk.active` (100 - % Idle Time), `disk.read`, `disk.write` (bytes/s), `disk.latency` (Avg. Disk sec/Transfer, in ms), `disk.queue` (instantaneous), all physical disks together, from the PhysicalDisk(_Total) counters with English names. The first collection has no rate data. Latency is `n/a` when no transfer happened in the interval (the counter then reads 0, which would mean "instant").

## Volumes

`disk.X.free`, `disk.X.total`, `disk.X.used` for the fixed volumes present at start-up (DriveInfo / GetDiskFreeSpaceEx). "Free" is the space available to the current user.

## History (`DiskScope.History`)

- In memory, per metric and tier: 1 s for 10 min, 5 s for 1 h, 1 min for 24 h (configurable `RetentionTier`).
- Every value goes to every tier on arrival; each bucket keeps min, average, max and sample count.
- A query uses the finest tier that still holds the start of the range, or that never dropped anything.
- Unavailable values are not stored: a period without data is a gap, drawn as a gap.
- Tiles summarize their window with `MetricTrend`: average weighted by sample count, min and max from the stored extremes, and the change over the window from a weighted least-squares line on the real timestamps (gaps are skipped, not read as zeros). The change is shown only with at least 3 points covering half the window; before that the tile says how much history it needs. Percent metrics change in percentage points (`pts`). A trend describes the window; it is not a forecast.
- Not persisted: history is lost when DiskScope exits. Persistence (SQLite, with batched writes and schema migrations, as the spec suggests) is a later step; it would add the first external dependency and needs a decision.

## Alerts (`DiskScope.Diagnostics`)

| Rule | Condition | Min duration | Recovery | Cooldown |
|------|-----------|--------------|----------|----------|
| Sustained high CPU | `cpu.total` > 90 % | 30 s | 10 s | 1 min |
| Very low available memory | `mem.physical.available` < 512 MB | 10 s | 10 s | 1 min |
| Commit charge near its limit | `mem.commit.percent` > 90 % | 10 s | 10 s | 1 min |
| High disk latency | `disk.latency` > 50 ms | 30 s | 10 s | 1 min |

- One episode raises one event. It ends when the condition has been false for the recovery time. If the condition returns within the cooldown, the same event is reopened: a flapping metric gives one episode, not a stream of alerts.
- Unavailable values neither start nor end an episode.
- Each event keeps its evidence (first value over the duration, worst value) and an explanation that says it is a measured fact, not a diagnosis.
- Thresholds and enabling are editable in the History & alerts tab; byte thresholds are edited in MB.
- Low memory uses an absolute amount, not a percentage of RAM: a high "in use" percentage is normal with a large cache.

## Hypotheses (`IDiagnosticRule`, `CoincidenceRule`)

Level `Hypothesis`, with a stated confidence, the observed data, the checks to run and the limits. Re-evaluated every 5 s.

| Rule | Formula | Confidence |
|------|---------|------------|
| High CPU coincided with heavy disk activity | during a "Sustained high CPU" episode, average `disk.active` >= 60 % | Low (coincidence only) |
| Low memory coincided with heavy disk activity: paging is likely | during a "Very low available memory" episode, average `disk.active` >= 50 % | Medium (known paging mechanism) |

Nothing is concluded when the history covers less than half of the episode. Every finding says that a coincidence in time is not proof of causality.

## Start-up and services (`DiskScope.Maintenance`)

Read-only. Run and RunOnce keys (current user, machine, 32-bit view), user and common Startup folders, scheduled tasks with a logon or boot trigger (Task Scheduler COM, late bound), and Win32 services (SCM opened with SC_MANAGER_ENUMERATE_SERVICE only; configuration from the registry). Publisher is the version-resource CompanyName, not a verified signature. "Enabled" comes from the undocumented StartupApproved values (bit 0 of the first byte set = disabled, bytes 4-11 = FILETIME of the disabling; checked on the dev machine). No impact score: start-up impact has not been measured. Sources that cannot be read are listed, and the status says the inventory is partial.

## Reports (`DiskScope.Reports`)

From the History & alerts tab, over the selected range: history CSV (every metric, one row per stored point), JSON report and self-contained HTML report (summary, inline SVG charts with gaps, alerts, hypotheses, rules, collectors, unavailable metrics, limits). From the Processes tab: CSV of the visible rows. Reports never include the machine name, user names or command lines.

## Comparison with Windows tools

Not done yet in a controlled way. Expected differences by definition:

- Task Manager "CPU" uses the same idle/kernel/user arithmetic; it may differ slightly in sampling instants. On Windows 11 it can also show "processor utility" (frequency-scaled), which DiskScope does not compute.
- Task Manager "In use" excludes the compressed store from some displays; DiskScope's "in use" is total - available.
- GPU: see the GPU document.
