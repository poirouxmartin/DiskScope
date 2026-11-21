# Contributing to DiskScope

Thanks for your interest. Issues and pull requests are welcome.

## Before you start

- Read [ROADMAP.md](ROADMAP.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
- For a new feature or a large change, open an issue first so the approach can be agreed on.
- Security problems go through private reporting, not issues: see [SECURITY.md](SECURITY.md).

## Build and test

Windows 10 or 11 and the .NET SDK pinned in `global.json` are required.

```
dotnet build DiskScope.slnx -c Release
dotnet test DiskScope.slnx -c Release
```

Warnings are errors (`Directory.Build.props`), including NuGet audit warnings. CI runs the same two commands on `windows-latest` for every pull request.

## Ground rules

- **Read-only.** DiskScope never deletes, moves or changes anything on the user's machine. A change that writes outside a user-chosen export file needs an issue and an explicit discussion first.
- **Never show an unknown value as zero.** Unavailable metrics read "n/a" and are written empty in exports. Partial results are flagged (`NodeStatus`).
- **Layers stay separate.** Collectors publish batches and never touch WPF controls; the data model has no UI dependency; views follow MVVM.
- **Index invariants.** In `NodeIndex`, a parent id is always lower than its children's ids, and there is no per-file object or path string. Logical and allocated sizes are never mixed.
- **Cancellation and errors.** Long operations accept a `CancellationToken`; access denied and missing metrics are handled, not swallowed.
- **Verify Windows APIs** against the official Microsoft documentation, and cite the API in the code when the behavior is not obvious.
- **Measure performance.** Claims about speed or overhead follow [docs/BENCHMARK-PROTOCOL.md](docs/BENCHMARK-PROTOCOL.md).

## Pull requests

- Keep each pull request focused on one change and add tests for calculations and edge cases.
- Code, comments, commit messages and docs are in English.
- Commit messages: short imperative subject line (for example `Add GPU engine filter`), details in the body if needed.
- Describe what changed, why, and how you tested it (test count, manual checks).

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
