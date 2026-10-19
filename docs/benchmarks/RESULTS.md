# Benchmark results

Each row is one measured scan series produced by the procedure in `docs/BENCHMARK-PROTOCOL.md`; rows without that provenance are not evidence.

| Date | Commit | Dataset | Engine | Cache | Median | Min | Nodes/s | Peak WS (MB) | Alloc (MB / 1M nodes) | Notes |
|------|--------|---------|--------|-------|--------|-----|---------|--------------|-----------------------|-------|
| 2026-10-19 | initial | synthetic 1M nodes (in memory) | n/a | n/a | Add 128 ms, Build 72 ms | - | - | 205 | 240 | Single run. 251 managed bytes/node. |
| 2026-10-19 | initial | `.nuget\packages` (12 710 nodes) | Standard | warm | 265-627 ms | 254 ms | 20k-48k | 39 | 566 | 3 rounds x 3 runs; round medians vary 2.4x. .NET EnumerateFileSystemEntries: 457-540 ms. `compare`: equal (8 553 files, 2 131 449 582 bytes). |
| 2026-10-19 | initial | `.vscode` (7 461 nodes) | Standard | warm | 112-243 ms | 107 ms | 31k-67k | - | - | .NET baseline 235-3 382 ms in the same session. |
| 2026-10-19 | initial | user profile (~2M nodes, partial) | Standard | cold | stopped at 905 s | - | ~2 200 | - | - | Not completed. Cold-cache dominated (warmup on `.nuget` cold: 7.8 s vs 0.26 s warm). |

Environment caveat for all rows above: dev laptop (Samsung PM9A1 NVMe, 20 logical CPUs), Defender in EDR block mode with MsSense running, other Claude sessions active. Run-to-run variance exceeded the A/B differences measured; FileIdBothDirectoryInfo vs FileFullDirectoryInfo could not be ranked. A quiet machine is required before any comparative claim.
