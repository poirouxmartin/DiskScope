# Code audit 2026-11-16: lots 3 and 4

Branch `dev/poiroux/audit-lots-1911`, from `main` (0b6f6fb). Not pushed.
Toolchain: `global.json` pins SDK 10.0.401, which is not installed on this machine. Build and tests were run with
SDK 11.0.100 (invoked from outside the repo so `global.json` is not picked up), targeting net10.0 on runtime 10.0.15.
CI still uses the pinned SDK and has not run on this branch.
Result: build with no warning (TreatWarningsAsErrors), 257/257 tests pass (baseline 230, +27).

## Lot 3: start-up inventory (B8-B11), commit 3e2fd98

| Fix | Files | Tests |
|---|---|---|
| `CommandLine`: doubled quotes (`""C:\a b\x.exe""`), bare program names (`cmd`, `rundll32`), `cmd /c` and `/k` give the launched program, `.dll/.msc/.cpl` known | `Maintenance/CommandLine.cs` | `ExtractExecutable` theory (+7 cases) |
| Task actions no longer double-quote an already quoted path (`CommandLine.Join`) | `CommandLine.cs`, `StartupInventory.cs` | `CommandLine_Join`, `WalkFolder_UnreadableTask_*` |
| Existence: fully qualified paths only; bare names searched in System32, Windows and PATH, otherwise unknown (never "absent" from our own working directory). Publisher read from the resolved path | `StartupInventory.Locate` | `Locate_*` (3) |
| Non-string Run values reported as `(Binary value, not a command line)` instead of `System.Byte[]` | `StartupInventory.CommandText` | `CommandText_OnlyStringValuesAreCommands` |
| Run commands and service `ImagePath` read raw (`DoNotExpandEnvironmentNames`) | `StartupInventory.cs`, `ServiceInventory.cs` | none (registry) |
| Task walk: access denied, binder and cast failures are reported per task and per folder; a failed task listing no longer skips subfolders; `Connect()` refused is an error line | `StartupInventory.WalkFolder` (internal seam, fakes passed as `dynamic`) | `WalkFolder_*` (3) |
| Machine-dependent tests tagged `[Trait("Category", "Machine")]` | `InventoryTests.cs` | n/a |

## Lot 4: domain deduplication (partial), commits 7849ad5 and 10e734a

Done:
- `Core.MetricIds` (26 fixed ids + `cpu.core.` prefix). Collector constants alias it; Diagnostics rules use it. No id value changed.
- Dead code removed: App -> Scanner.Ntfs reference, `IShellActions.AskSaveCsvPath`, `MonitoringViewModel.OnPropertyChanged`,
  `MonitoringScheduler.StoreFailed` (no subscriber; `StoreFailures` still counted), `VolumeSpaceCollector.Letters`,
  `GpuAdapterInfo.DedicatedSystemMemory`, `GrowthSuspicion.RisingStepShare`, unused Benchmarks exit codes.
  `ERROR_INVALID_PARAMETER` replaces the literal 87. `ARCHITECTURE.md` lists Scanner.Standard and Scanner.Ntfs.
- `Storage.TopKBySize`: one min-heap for largest files and search (same ordering and ties). Tests: `TopKBySizeTests` (2).
- `Core.CpuDelta.Percent` shared by `ProcessCpuTracker` and `SelfCollector` (same guards and 100 % cap as before).
  System CPU (`CpuUsageCalculator`) keeps its own idle/kernel/user formula. Tests: `CpuDeltaTests` (6).
- `CollectorResult.AllUnavailable` replaces four hand-built batches (scheduler, disk activity, GPU x2). Test: `CollectorResultTests`.
- `GpuCounterMath.NormalizeLuid`: one function for both call sites.

Not done in lot 4:
- Single `CsvWriter` for `Storage.CsvExporter` and `Reports.CsvTable`: formula neutralization (c1fc187) lives on the
  unmerged `dev/poiroux/securite-1411` branch and edits `CsvTable.cs`. Unifying now would conflict with it; best done
  after that branch is merged, with golden CSV tests.
- One `NativeMethods` class per native project (Gpu `Native.cs`, inline `PdhQuery`): not touched. In Processes the two
  `GetProcessTimes`/`K32GetProcessMemoryInfo` declarations are overloads (`SafeProcessHandle` vs `nint`), not duplicates.

## Remaining limits

- The app was not launched. Start-up inventory changes are checked by unit tests with fakes; the real Task Scheduler and
  registry paths only by the machine test (which asserts no crash, not content).
- Lot 5 (App layer refactor) is postponed as requested; lots 6 and 7 are untouched.
- This branch and `securite-1411` both edit `tests/DiskScope.Tests/Maintenance/InventoryTests.cs`: merging both will
  need a small conflict resolution.
