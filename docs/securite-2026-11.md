# Security review, November 2026

Date: 2026-11-14. Branch: `dev/poiroux/securite-1411` (local, not pushed). Base: `main` at `9c4ab12`, identical to `origin/main`.

## 1. Repository status

The repository is already public on GitHub (`poirouxmartin/DiskScope`, MIT license, last push 2026-10-22, CI green on `9c4ab12`). This review therefore checks what is already published and prepares the next push; it does not gate a first publication.

## 2. NuGet dependencies

- `dotnet list package --vulnerable --include-transitive`: no vulnerable package in any of the 16 projects.
- `dotnet list package --outdated`: only test tooling in `DiskScope.Tests` has newer major versions (`Microsoft.NET.Test.Sdk` 17.14.1 -> 18.x, `xunit.runner.visualstudio` 3.1.4 -> 4.x, `coverlet.collector` 6.0.4 -> 10.x). None has a known vulnerability; the major upgrades are left to a separate change so this review does not mix security and tooling migrations.
- Added: NuGet audit on every restore for direct and transitive packages (`NuGetAuditMode=all`, `NuGetAuditLevel=low` in `Directory.Build.props`). Warnings are already errors, so a known vulnerability now fails the build.
- Added: `.github/dependabot.yml` (weekly NuGet and GitHub Actions updates) and a weekly scheduled CI run, so a new advisory is caught without a new commit.

## 3. History scan (personal data, employer data, secrets)

All 15 commits on all refs, every patch, plus binary files:

| Check | Result |
|---|---|
| Secrets (keys, tokens, passwords, private keys, cloud key patterns, connection strings) | none |
| Employer name or employer data | none |
| Local user paths, machine names, IP addresses | none |
| Author and committer identity | one identity, the owner's personal account and e-mail (normal for a public repo; switch to the GitHub noreply address for future commits if the e-mail should stay private) |
| Deleted files in history | one source file (`NtfsScanEngine.cs`), no sensitive content |
| `docs/spec/*.docx` metadata | creator `python-docx`, no author, company or manager |
| `docs/images/*.png` screenshots | hardware model, generic process names and PIDs only; no user name, path or machine name |

No finding requires a history rewrite.

## 4. Code review findings

- **Fixed: CSV formula injection.** `CsvTable` (process, startup/services and monitoring exports) wrote third-party text such as registry Run values and command lines as is. A value starting with `=`, `+`, `-`, `@`, tab or CR could run as a formula when the CSV is opened in a spreadsheet. Such fields are now prefixed with `'` and quoted; invariant-culture numbers (`-12.5`) are unchanged. 8 test cases added.
- **Not affected:** the storage CSV (`CsvExporter`) always starts a path with the scan root, so the first character is never a formula trigger. The HTML report encodes all text with `WebUtility.HtmlEncode`.
- **Read-only confirmed:** no network API, no file deletion or move, no registry write. The only process start is `explorer.exe /select` on user request; the only file writes are exports to a path chosen in a save dialog. History is in memory only.

## 5. Files added or changed

- `SECURITY.md` (new): supported versions, private reporting, scope.
- `CONTRIBUTING.md` (new): build and test, ground rules, pull requests.
- `.github/dependabot.yml` (new).
- `.github/workflows/ci.yml`: manual trigger and weekly schedule added. Build and tests in Release were already there.
- `Directory.Build.props`: NuGet audit.
- `src/DiskScope.Reports/CsvTable.cs` and `tests/DiskScope.Tests/Maintenance/InventoryTests.cs`: CSV injection fix and tests.

## 6. Verification

- `dotnet build DiskScope.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test DiskScope.slnx -c Release`: 217 passed, 0 failed, 0 skipped (209 before this review, plus 8 new cases).
- The application was not launched for this review.

## 7. Open source publication checklist

Already done:

- [x] MIT `LICENSE`, English `README.md` with build, test and limits
- [x] CI on GitHub Actions (build and tests in Release, `windows-latest`, read-only token)
- [x] No secret, employer data or personal path in the history
- [x] Public repository with description and license detected by GitHub

Done on this branch, published after the next push:

- [x] `SECURITY.md`, `CONTRIBUTING.md`
- [x] Dependabot, NuGet audit, weekly CI run
- [x] CSV formula injection fixed

To do by the owner on GitHub (needs approval, not done here):

- [ ] Review and push `dev/poiroux/securite-1411`, then merge into `main` through a pull request
- [ ] Enable private vulnerability reporting (Settings > Code security; currently disabled), otherwise the procedure in `SECURITY.md` does not work
- [ ] Enable Dependabot alerts and security updates, and secret scanning with push protection
- [ ] Protect `main`: require the CI check and a pull request, block force pushes
- [ ] Add repository topics (for example `windows`, `wpf`, `disk-usage`, `system-monitor`)
- [ ] Optional: commit with the GitHub noreply e-mail from now on
- [ ] Optional: issue and pull request templates, a first tagged release with a signed or checksummed build

Follow-ups (not security):

- [ ] Upgrade the test tooling majors (`Microsoft.NET.Test.Sdk` 18, `xunit.runner.visualstudio` 4, `coverlet.collector` 10) in a separate change
- [ ] Update the test count in `ROADMAP.md` (209 -> 217) at the next roadmap entry
