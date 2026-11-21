# Security policy

## Supported versions

DiskScope has no tagged release yet. Only the latest commit on `main` is supported; fixes are not backported.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability** (private vulnerability reporting). Include:

- the DiskScope commit you tested,
- the Windows version and whether DiskScope ran elevated,
- the steps to reproduce and the impact you observed.

You should get a first answer within 14 days. Once a fix is on `main`, the advisory is published with credit to the reporter unless you ask otherwise.

## Scope

DiskScope is a local, read-only analyzer. It does not open network connections and does not modify files, services, the registry or scheduled tasks; the only files it writes are the exports you save through a file dialog. Relevant reports include, for example:

- any code path that writes, deletes or changes system state without an explicit user action,
- crashes or memory corruption triggered by crafted file system content (names, reparse points, NTFS metadata),
- exports (CSV, JSON, HTML) that can inject script or formulas when opened,
- vulnerable NuGet dependencies.

Running DiskScope elevated gives it read access to raw NTFS metadata; problems that require an attacker who is already administrator are out of scope.

## Dependencies

NuGet audit runs on every restore (`NuGetAuditMode=all`, see `Directory.Build.props`) and warnings are errors, so a known vulnerable package, direct or transitive, fails the build. Dependabot proposes NuGet and GitHub Actions updates weekly.
