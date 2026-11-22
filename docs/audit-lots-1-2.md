# Code audit 2026-11-16: lots 1 and 2

Branch `dev/poiroux/audit-lots-1-2`, from `main` (9c4ab12). Merged into `main` and pushed on 2026-11-16 (CI green).
Baseline on main at the time: 209 tests (the 217 of the audit include 8 tests of the `securite-1411` branch, merged later).
Result: Release build with no warning (TreatWarningsAsErrors), 230/230 tests pass (+21).

## Lot 1: safety nets (B1-B7)

| Fix | Files | Tests |
|---|---|---|
| Global handlers: UI thread, unobserved tasks, background threads. Logged to `%LOCALAPPDATA%\DiskScope\errors.log`, one message box at a time, the app keeps running | `App.xaml.cs`, `Services/ErrorLog.cs` (new) | `ErrorLog_Write_*`, `ErrorLog_UnwritableDirectory_*` |
| `AsyncRelayCommand`: no more async void lambdas for scan and export; any failure becomes a status | `ViewModels/AsyncRelayCommand.cs` (new), `StorageViewModel.cs` | `AsyncRelayCommand_*` (2), `StorageExport_NonIoFailure_SetsStatusInsteadOfCrashing` |
| Clipboard busy and Explorer launch failures become a status | `StorageViewModel.cs` | `CopyPath_ClipboardBusy_SetsStatus` |
| Window close always completes even if disposal throws (logged) | `MainWindow.xaml.cs` | none (window code) |
| `Application.Current` null-guarded in scan callbacks | `StorageViewModel.cs` | none (needs a closing WPF app) |
| Scheduler: a throwing subscriber is isolated and counted (`SubscriberFailures`); the collector loop goes on | `Monitoring/MonitoringScheduler.cs` | `ThrowingSubscriber_*` (2) |
| Process refresh loop survives any read failure and shows it | `ProcessesViewModel.cs` (`TryRead`) | `ProcessesTryRead_*` (2) |
| Start-up inventory read failure shows a status instead of "Reading..." forever | `StartupViewModel.cs` | `StartupRefresh_ReaderThrows_StatusSaysFailed` |
| Export catches widened to every non-cancellation exception (storage, monitoring, processes, start-up) | the four view models | covered by the storage export test |

## Lot 2: unknown is not zero (B12, B14, B15 and related)

| Fix | Files | Tests |
|---|---|---|
| `MetricQuality.Unavailable = 0`: `default(MetricValue)` is unavailable. The enum is never serialized as an integer (reports use `JsonStringEnumConverter` and do not carry `MetricValue`) | `Core/Metrics.cs` | `DefaultMetricValue_IsNotAvailable` |
| `MetricFormatter` prints `n/a` for NaN/infinity (HTML average over zero samples, history summary) | `Core/MetricFormatter.cs` | `MetricFormatter_NonFinite_IsUnknown`, `HtmlReport_ZeroSamples_*` |
| History CSV: empty cell for a non-finite value; HTML chart: gap | `Reports/ReportWriters.cs` | `HistoryCsv_NaNAverage_*`, `Svg_NaNPoint_*` |
| Per-core CPU bar hidden when unknown | `MetricTileViewModel.cs`, `Views/CpuMemoryView.xaml` | `CoreBar_Unknown_IsHidden` |
| Sparkline: non-finite points dropped; no exception when `End` is default | `Controls/SparklineControl.cs` | `Sparkline_TryGetStart_*`, `Sparkline_FinitePoints_*` |
| Incomplete directory sizes shown as lower bounds (`≥ 1.2 GB`), `n/a` when nothing could be read; percent of an empty parent is `n/a` | `Formatting/ByteFormatter.cs`, `NodeViewModel.cs`, `Controls/TreemapControl.cs` | `ByteFormatter_FormatNodeSize`, `NodeViewModel_IncompleteDirectory_*` |
| GPU memory detail no longer shows "of 0 B" when the total is unknown | `MonitoringViewModel.cs` | none (needs a GPU adapter fake) |

The unknown marker stays `n/a` (`MetricFormatter.Unknown`), already used across the app, rather than a new "—".

## Not done / remaining

- The app was not launched: the dialog, the hidden core bar and the `≥` columns are checked by tests and build only. A manual pass on each tab is still needed.
- Not tested: treemap tooltip, GPU detail, window close, null `Application.Current` (WPF runtime needed).
- `SubscriberFailures` is counted and shown in the monitoring status bar (`MonitoringViewModel.StatusText`).
- Lot 2 leaves alone: largest-files rows (files have an exact size), `DiskActivityCollector` all-`Derived` quality, storage index CSV export (not reviewed for this rule).
- Lots 3 to 7 of the audit (start-up inventory, deduplication, App refactor, UI performance, background monitoring) are untouched. The `securite-1411` commits are not on this branch: merging both will need a rebase (that branch's files are not touched here). Update: it was rebased and merged into `main` afterwards.
