# Benchmark results

Each row is one measured scan series produced by the procedure in `docs/BENCHMARK-PROTOCOL.md`; rows without that provenance are not evidence.

| Date | Commit | Dataset | Engine | Cache | Median | Min | Nodes/s | Peak WS (MB) | Alloc (MB / 1M nodes) | Notes |
|------|--------|---------|--------|-------|--------|-----|---------|--------------|-----------------------|-------|
| 2026-10-19 | initial | synthetic 1M nodes (in memory) | n/a | n/a | Add 128 ms, Build 72 ms | - | - | 205 | 240 | Single run. 251 managed bytes/node. |
| 2026-10-19 | search step | synthetic 1M nodes (in memory) | n/a | n/a | Search 31.5 ms, ext. stats 63.4 ms | - | - | 207 | 240 | Best of 3. Name search "n9": 111 111 matches. Synthetic names have no extension (1 bucket). |
| 2026-10-19 | initial | `.nuget\packages` (12 710 nodes) | Standard | warm | 265-627 ms | 254 ms | 20k-48k | 39 | 566 | 3 rounds x 3 runs; round medians vary 2.4x. .NET EnumerateFileSystemEntries: 457-540 ms. `compare`: equal (8 553 files, 2 131 449 582 bytes). |
| 2026-10-19 | initial | `.vscode` (7 461 nodes) | Standard | warm | 112-243 ms | 107 ms | 31k-67k | - | - | .NET baseline 235-3 382 ms in the same session. |
| 2026-10-19 | initial | user profile (~2M nodes, partial) | Standard | cold | stopped at 905 s | - | ~2 200 | - | - | Not completed. Cold-cache dominated (warmup on `.nuget` cold: 7.8 s vs 0.26 s warm). |
| 2026-10-26 | 9c4ab12 | synthetic 1M nodes (in memory) | n/a | n/a | Add 122.5 ms, Build 40.1 ms | - | - | 209 | 240 | Single run. Search 22.5 ms, ext. stats 68.5 ms. Same figures as 2026-10-19. |
| 2026-10-26 | subtree-date step | synthetic 1M nodes (in memory) | n/a | n/a | Add 86.1 ms, Build 39.7 ms | - | - | 215 | 247 | Single run, Release. 259.2 managed bytes/node (+8: subtree last-write column). Search 16.4 ms, ext. stats 55.6 ms. |
| 2026-10-26 | 9c4ab12 | D2 user profile (2.31M nodes, 1.96M files, 228 GB) | Standard | warm | 141.0 s | 87.5 s | 16 404 | 655 | 501 | 1 warmup + 3 runs (116, 159, 88, 141 s): 1.8x spread. CPU time 80-108 s per run. AC power. 3-6 denied directories. Busy machine (see caveat below). `compare` after the fix in dbf025b: files -0.027 %, bytes -0.064 % (live tree, passes minutes apart). |
| 2026-10-26 | dbf025b | D3 full volume C: (3.59M nodes, 2.95M files) | Standard | warm | 600.9 s | 587.4 s | 5 981 | 902 | 491 | 2 runs, no warmup (the compare pass just before warmed the cache). Not elevated: 225-230 denied directories. CPU time 282 s per run. First progress 2.2-2.9 s. `compare C:\`: files -1.48 %, 1 directory vanished; on stable subtrees (Program Files, ProgramData, Windows: 418 144 files, 113.7 GB, 198 denied directories) the result is equal to the byte. |

## Monitoring cost (`monitor --seconds 60`, Release, no UI)

| Date | Dataset | Collector mean / max (ms) | Process refresh median / max (ms) | Self CPU (% machine) | Peak WS (MB) | Alloc (MB/s) | Notes |
|------|---------|---------------------------|-----------------------------------|----------------------|--------------|--------------|-------|
| 2026-10-21 | dev laptop, ~545 processes, 1 iGPU | CPU 1.22 / 14.3, Memory 0.65 / 16.3, Volumes 0.49 / 2.9, GPU 3.25 / 28.4, Self 0.07 / 2.9 | 72.0 / 145.7 (29 runs, every 2 s) | 0.25 | 80 | 1.09 | Single 61 s run. 0 failures, 0 missed ticks. Not measured yet: the WPF app itself (its working set went from about 210 to 290 MB in the first minutes of an interactive session; to be measured over 10 min before drawing a conclusion). |
| 2026-10-26 | same laptop, ~590 processes, machine CPU often 50-100 % | CPU 27.1 / 301, Memory 0.56 / 12.4, Volumes 0.50 / 3.3, Disk 0.44 / 9.9, GPU 2.64 / 37.0, Self 0.03 / 2.8 | 129.0 / 3 929 (127 runs) | 0.27 | 82 | 0.79 | 302 s run. 0 failures, 0 missed ticks. The CPU collector cost is wall time: a standalone loop of its two calls (GetSystemTimes, NtQuerySystemInformation) measured 3-4 ms median and ~200 ms p99 on the same machine, so the figure is CPU starvation, not collector work. A 62 s rerun under heavier load: CPU 110 / 487 ms, process refresh median 741 ms. |

WPF app, 2026-10-26, same laptop, 10 min sampled every 10 s while tabs were opened through UI Automation: working set 209 MB at 10 s, peak 345 MB at 7 min, 325 MB at 10 min; private bytes 137 to 241 MB; handles 678-702 and threads 41-49, both flat; always responding. No growth trend after minute 7; the rise follows the tabs being opened (process grid). A run without interaction is still needed before calling it leak-free.

Environment caveat for all rows above: dev laptop (Samsung PM9A1 NVMe, 20 logical CPUs), endpoint security software inspecting file opens, other heavy processes running. Run-to-run variance exceeded the A/B differences measured; FileIdBothDirectoryInfo vs FileFullDirectoryInfo could not be ranked. A quiet machine is required before any comparative claim.
