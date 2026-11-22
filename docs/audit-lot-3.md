# Code audit 2026-11-16: lot 3 (start-up inventory, B8-B11)

Branch `fix/startup-inventory`, from `main` at 61cbc52 (main had moved past 0b6f6fb with the security merge).
Local commits only, not pushed.

The fix itself was first written on 2026-11-19 on `dev/poiroux/audit-lots-1911` (commit 3e2fd98, based on 0b6f6fb and
mixed with lot 4). This branch carries lot 3 alone on top of the current `main`, test first, so it can be merged on its
own. The source changes are the same as 3e2fd98; the test table is extended.

## Toolchain

`global.json` pins SDK 10.0.401, which is not installed on this machine. Build and tests were run with SDK 11.0.100
from outside the repo (so `global.json` is not picked up), targeting net10.0. CI uses the pinned SDK and has not run on
this branch.

## Test first

The `CommandLine.ExtractExecutable` case table (quotes, spaces, arguments, environment variables, `cmd /c`, extensions)
was added on top of the unchanged code and run before the fix:

- Before: 7 failed, 249 passed (256). The 7 failures are exactly the B8/B9 cases: doubled quotes, `cmd /c` and
  `cmd.exe /q /c`, `CMD /K` with a variable, `.msc`, `.dll`, and `rundll32 C:\Program Files\x.exe`.
- After: 268/268 passed, 0 warnings (TreatWarningsAsErrors). With `--filter "Category!=Machine"`: 266/266.

The other new tests (`CommandLine.Join`, `CommandText`, `Locate`, `WalkFolder`) target functions introduced by the fix,
so they could not compile against the old code; they cover B10 and B11.

## Fixes

| Point | Fix | Files | Tests |
|---|---|---|---|
| B8 | Doubled quotes (`""C:\a b\x.exe"" -s`) are tolerated; task actions no longer quote an already quoted path (`CommandLine.Join`) | `CommandLine.cs`, `StartupInventory.cs` | `ExtractExecutable`, `CommandLine_Join` |
| B9 | `cmd /c` and `/k` (after other switches) give the launched program; bare program names (`cmd`, `rundll32`) are taken as-is; `.dll/.msc/.cpl` known | `CommandLine.cs` | `ExtractExecutable` (23 cases) |
| B10 | Existence is checked only for fully qualified paths. Bare names are searched in System32, Windows and PATH, otherwise unknown. Relative, drive-relative (`C:x.exe`), rooted-without-drive (`\x.exe`) and unexpanded (`%NOPE%\x.exe`) paths are unknown, never "absent". Publisher read from the resolved path | `StartupInventory.Locate` | `Locate_*` (4) |
| B11 | Task walk: access denied, binder and cast failures are reported per task and per folder; a failed task listing no longer skips subfolders; a refused `Connect()` is an error line, not a failed read | `StartupInventory.WalkFolder` (internal seam, fakes passed as `dynamic`) | `WalkFolder_*` (3) |
| Report §8 | Non-string Run values (REG_BINARY...) reported as such, never `System.Byte[]`; Run commands and service `ImagePath` read raw (`DoNotExpandEnvironmentNames`) | `StartupInventory.CommandText`, `ServiceInventory.cs` | `CommandText_OnlyStringValuesAreCommands` |

Machine-dependent inventory tests (`Services_OnThisMachine_*`, `Startup_OnThisMachine_*`) are tagged
`[Trait("Category", "Machine")]`.

## Remaining limits

- Unquoted paths with spaces and no known extension before a comma (`C:\Program Files\x\plugin.dll,Entry`) still cut
  at the first space; Windows itself is ambiguous there.
- The app was not launched. The real Task Scheduler and registry paths are covered only by the machine test, which
  asserts no crash and explicit error lines, not content.
- `Monitoring/GpuTests.Collector_OnThisMachine_IsHonestAboutAvailability` also depends on the machine and is not
  tagged (outside lot 3).
- `dev/poiroux/audit-lots-1911` and `feature/ux-refresh` still carry 3e2fd98. Once this branch is merged, rebasing
  them will drop that commit as already applied, except for the extra test cases, which may need a trivial merge in
  `InventoryTests.cs`.
