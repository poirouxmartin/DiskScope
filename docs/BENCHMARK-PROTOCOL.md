# Benchmark protocol

How DiskScope scan performance is measured. Engines and orchestration: see `docs/ARCHITECTURE.md`.
Results are recorded in `docs/benchmarks/RESULTS.md`.

Rule: no performance claim without a row in `docs/benchmarks/RESULTS.md` produced by this protocol.

## Metrics

- Total time per run (median and min over measured runs).
- Time to first progress (scan start latency, from the first `OnProgress` callback).
- Nodes/s (node count of the final index divided by total time).
- Peak working set (MB).
- Managed allocated bytes (per run, normalized to MB per 1M nodes in RESULTS).
- GC counts (gen0, gen1, gen2 deltas per run).
- CPU time (process CPU time delta per run).
- UI responsiveness: dispatcher latency probe. Not implemented yet. No UI responsiveness number may be reported until it exists.

Check the printed header of `scan` for which of these it already emits. Anything not printed is measured by hand and must be labelled as such in Notes.

## Datasets

- D1, synthetic: `generate C:\tmp\ds-bench --files 200000 --seed 1`. Deterministic (1110 directories, 8 GiB logical cap). `generate` refuses a non-empty target, so the directory must not exist or be empty.
- D2, user profile: the profile directory of the benchmark user. Size varies over time. Record the exact path and the `compare` counts with each run.
- D3, full volume: `C:\`. The NTFS engine needs elevation. The standard engine reports denied directories; record their count with the run.

## Procedure

1. Release build. Run from the repo root:
   `dotnet run -c Release --project src/DiskScope.Benchmarks -- scan <path>`
2. Machine: AC power, heavy applications closed. Record the power state in Notes.
3. Correctness first: run `compare <path>` on the dataset before any timing. Exit code 1 (mismatch) blocks timing for that dataset.
4. Defaults are 1 warmup and 5 measured runs (`--warmup 1 --runs 5`). Warmups are tagged in the output and excluded from median and min.
5. Report the median and the min, plus the printed header line, in the RESULTS row.
6. Any non-zero exit code invalidates the run. Exit codes: 1 scan not completed, compare mismatch, or I/O failure; 2 usage error; 3 refused by precondition.

## Cache state

- Warm: repeated runs back to back. This is the default measurement. Label rows `warm`.
- Cold: needs a reboot, or a standby-list purge (RAMMap "Empty Standby List", or `NtSetSystemInformation` with `SystemMemoryListInformation`, both administrator). Label rows `cold` and name the method used.
- Never mix warm and cold runs in the same median.

## Defender

Real-time scanning slows the first touch of newly created files. D1 is generated right before timing, so its first runs are skewed. Record whether Defender real-time protection was on, and whether D1 was touched between generation and the first measured run. Do not drop those runs silently; note them.

## Recording

Each RESULTS row carries: date, short commit hash, dataset (D1/D2/D3 with path), engine, cache state, median, min, nodes/s, peak working set, allocation per 1M nodes, and notes (power state, Defender, denied directories, warmup or cold method).
