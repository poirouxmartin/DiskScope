# DiskScope: agent instructions

Read ROADMAP.md first. The Part 2 spec lives in docs/spec/ (DiskScope_Specifications_Modules.md is an extracted copy; the .docx next to it is the source of truth).

## Build and test

- Build: `dotnet build DiskScope.slnx`
- Test: `dotnet test DiskScope.slnx`
- Use PowerShell or quote paths. Git Bash mangles unquoted C:\ paths.
- Build output is localized (French). To find failures, grep for `erreur|error`.

## Working rules

- Inspect the existing code and conventions before coding. Avoid duplicate projects.
- Plan limited to the requested module. Do not rewrite modules already in progress.
- Verify every API against official Microsoft docs before use.
- Separate collectors, the data model and the MVVM UI. Collectors publish batches and never touch WPF controls.
- Handle cancellation, errors, permissions and unavailable metrics.
- Never show an unknown value as zero.
- Read-only by default. No destructive or system-modifying action without an explicit request.
- Add tests for calculations and edge cases. Build and run tests after each step.
- No success claim without execution. Report the real pass/fail count.
- Measure performance, do not assume it. Protocol: docs/BENCHMARK-PROTOCOL.md. Results: docs/benchmarks/RESULTS.md.
- End each step with a summary of changes, test results and remaining limits.

## Code conventions

- English for code, comments, commits and docs.
- NodeIndex invariant: parent id < child id.
- No per-file object or path string in the index.
- Logical and allocated sizes are never mixed.
- Partial results are always flagged (NodeStatus).

## Branches and deployment

- Work directly on `main`: no feature branch, no worktree. Martin is the only user of this app.
- After green tests: `git push origin main` (never force), then reinstall.
- Reinstall: `dotnet publish src/DiskScope.App/DiskScope.App.csproj -c Release -o <tmp dir>`, back up then robocopy into
  `%LOCALAPPDATA%\Programs\DiskScope`. Check `DiskScope.App.dll` ProductVersion ends with the pushed commit hash.
- `global.json` pins SDK 10.0.401, which is not installed: run `dotnet` from `C:\tmp\sdk11-run` (SDK 11 override).

## Commits

- No Co-Authored-By trailer.
- Write commit message files as UTF-8 without BOM (PowerShell 5.1 Set-Content adds a BOM).
