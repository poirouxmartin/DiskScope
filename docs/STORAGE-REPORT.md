# Part 1 (storage module): final report

Status on 2026-10-26. Details live in ROADMAP.md, docs/benchmarks/RESULTS.md and docs/NTFS-RISKS.md; this page is the summary.

## Verdict

The storage module works without administrator rights and is exact where it can be checked. Two items stay open because the environment blocks them: the NTFS (USN) engine on a real volume, and reference benchmarks on a quiet machine.

## What is delivered

- Standard scan engine (`GetFileInformationByHandleEx`, one handle per directory, no administrator rights), cancellable, with partial results during the scan and every incomplete subtree flagged (`NodeStatus`), never counted as zero.
- `NodeIndex`: struct-of-arrays, no per-file object or path string, parent id < child id, O(n) aggregation; logical and allocated sizes kept apart.
- App: lazy tree sorted by size, logical/allocated switch without rescan, top 1000 largest files, search and filters, extension statistics, squarified treemap synchronized with the tree, CSV export.
- Benchmark CLI (`DiskScope.Benchmarks`): generated datasets, timed scans, `compare` against an independent .NET walk, `inspect` (unreadable directories, one node's details), `usn-probe` for the NTFS prototype.
- NTFS prototype, parts that need no elevation: USN record parser and tree rebuild, tested on synthetic records.

## Evidence

| Check | Result |
|---|---|
| Tests | 255 passing (`dotnet test DiskScope.slnx`), storage and scanner included |
| Exactness | `compare` equal to the byte on stable subtrees of a full system volume (418 144 files, 113.7 GB, 198 denied directories on both sides) |
| Full volume, not elevated | 3.59 M nodes in 588-601 s on a busy laptop; 225-230 denied directories reported, not counted |
| Live tree | a directory deleted during the scan is counted as vanished; the scan and `compare` both finish |
| Memory | 491-501 MB allocated per million nodes on real trees, 240 MB on the synthetic set; peak working set 902 MB for 3.59 M nodes |

The 2026-10-26 measurements ran on a busy machine (20 logical processors often at 50-100 %, endpoint security filtering every file open): run-to-run spread reached 1.8x. They are orders of magnitude, not a reference.

## Open items

1. NTFS engine: run `usn-probe` in an elevated console, then take the sizes decision (docs/NTFS-RISKS.md sections 4 and 8). Expected gain, not measured: one stream over the master file table instead of one handle per directory.
2. Reference benchmarks on a quiet machine (docs/BENCHMARK-PROTOCOL.md).

## Known limits found on real use

Open:

- No persistent index or incremental rescan: every question costs a full scan (about 10 minutes on a full volume without elevation). Needs a design decision (storage format, invalidation; the NTFS journal is the natural source of changes and needs elevation).
- Byte sizes use binary multiples labelled KB/MB/GB, as Windows Explorer and Task Manager do. Switching to KiB or to decimal units is a product decision.

Fixed on 2026-10-26, without elevation:

- Last change in the subtree: `NodeIndex.GetSubtreeLastWriteUtc`, the latest known date of the node and everything below it (a directory's own date moves only when its direct entries change). Shown in the tree ("last change" column) and exported (`SubtreeLastWriteUtc`). Cost: 8 bytes per node, 259 bytes per node in total on the synthetic million (was 251); build time unchanged.
- Lookup by path (`NodeIndexQueries.FindByPath`) and a CLI command, `inspect <path> [--node <path>]`, that lists every unreadable directory with its error and describes one node.
- Unknown sizes are no longer zeros by accident: `GetKnownTotalSize` returns null when the node's own listing failed or was not done. The tree shows `n/a` and the CSV leaves the total columns empty for such directories; a directory with an incomplete descendant still shows its total, flagged as a lower bound by its status.
- The tree keeps expanded and selected folders across partial refreshes, the final result and the logical/allocated switch (node ids are stable within one scan).
