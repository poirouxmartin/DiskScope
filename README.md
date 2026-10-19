# DiskScope

Read-only disk space analyzer for Windows, built with WPF on .NET 10.

It scans a directory tree and shows where the space goes. Two scan engines sit behind one contract:
an NTFS engine (fast, reads the volume's metadata) and a standard engine (portable fallback that
walks the directory tree with the regular file APIs). The orchestrator picks the first engine that
reports itself available.

DiskScope never modifies, moves or deletes files.

## Layout

- `src/DiskScope.Core`: node index (struct-of-arrays), status flags, scan errors. No dependencies.
- `src/DiskScope.Scanner`: scan contract (`IScanEngine`, requests, progress, results) and the orchestrator.
- `src/DiskScope.Scanner.Standard`: fallback engine (step 2, not implemented yet).
- `src/DiskScope.Scanner.Ntfs`: NTFS engine (step 2, not implemented yet).
- `src/DiskScope.App`: WPF shell.
- `src/DiskScope.Benchmarks`: benchmark console (step 3, not implemented yet).
- `tests/DiskScope.Tests`: xUnit tests.

## Build and test

```
dotnet build DiskScope.slnx
dotnet test DiskScope.slnx
```

## Docs

Architecture, counting rules and engine contract: `docs/ARCHITECTURE.md`.
