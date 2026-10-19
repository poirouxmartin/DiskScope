# DiskScope architecture

## Project graph

```
Core <- Scanner <- Scanner.Standard <- App, Benchmarks
                 <- Scanner.Ntfs     <- App
Tests references every project except App.
```

Core has no dependencies. Scanner holds the engine contract and the orchestrator. The two engines
are separate assemblies so the NTFS-specific code (volume handles, file reference numbers) stays
out of the standard path.

## NodeIndex

Nodes are stored struct-of-arrays: one array per column (parent, name offset and length, kind,
attributes, logical and allocated size, last write ticks, file reference, status). There is no
per-node object, so a million nodes cost a few arrays rather than a million allocations.

Invariant: parent ids are always smaller than child ids. `NodeIndexBuilder` enforces it by
appending only and requiring an existing directory as parent. Aggregation depends on it.

Names are copied into one char pool. Lookups return spans, and `GetNameString` allocates only on
request. Paths are never stored: `GetFullPath` walks the parent chain and joins the names.

Children use CSR (compressed sparse row): one start array of Count+1 entries and one flat array of
child ids, filled by counting sort, so children of each parent are in ascending id order.

## Aggregation

Totals and counts are computed in a single reverse pass over ids (O(n)). Each node is folded into
its parent once all its descendants have been folded into it. The loop is iterative, so a deep
tree cannot overflow the stack.

## Counting rules v1

Sizes come from the file system as reported per directory entry. Directories own no size (0); their totals are the sum of descendants. Links (junctions, symlinks) are not traversed. Hard links are counted once per occurrence (per path), as Explorer does; deduplication by file reference is planned for the NTFS engine. Logical and allocated sizes are never mixed.

NTFS file reference is stored in its own column. It is not the internal id: ids are dense
positions in this index, file references are file system identities.

## Status semantics

- `AccessDenied`, `IoError`, `Vanished`, `NotScanned` describe the node itself: its own contents
  are unknown or incomplete.
- `SubtreeIncomplete` is computed during index build. It is set on every node whose own status or
  any descendant status is non-None, so the UI can flag ancestors of a damaged subtree.

## Engine contract

- `CheckAvailability` is cheap and side-effect free; it reports whether the engine can scan the request.
- `ScanAsync` does not throw on cancellation. It returns a `ScanResult` with `Outcome = Cancelled`
  and a partial index. Unvisited directories carry `NodeStatus.NotScanned`.
- Only argument errors throw.
- Progress goes to `IScanObserver.OnProgress`, from the scan thread, at most once per
  `ProgressInterval` plus once at the end.

## Orchestration

`ScanOrchestrator` takes engines in preference order. It selects the first engine whose
`CheckAvailability` is true. The NTFS engine is preferred when available; the standard engine is
the fallback. If none is available, the orchestrator throws with every engine's reason.

Partial-tree publication during scan (batches readable by the UI) is deferred: it needs a reader-safe snapshot of the builder; v1 publishes progress counters only.
