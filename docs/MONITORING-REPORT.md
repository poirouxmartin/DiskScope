# Part 2 (CPU, memory, GPU, processes): report

Status on 2026-10-27. Details live in docs/MONITORING.md, docs/GPU-COMPATIBILITY.md, docs/benchmarks/RESULTS.md and ROADMAP.md; this page is the summary.

## Verdict

The CPU, memory, GPU and process views work without administrator rights on the two machines tried, and every value that cannot be read shows `n/a`. What is still open needs a decision (external dependency, kernel driver, persistence) or elevation.

## What is delivered

- CPU: global and per-processor usage, user/kernel split. Memory: physical memory, commit charge and limit, cache, kernel pools. Each counter states its definition and Windows source.
- Processes: parent/child tree with PID reuse detected, CPU % of the machine, private bytes, working sets, threads, handles, I/O, start time, path; sustained memory growth flagged as a suspicion.
- By application (new on 2026-10-27): processes grouped by image name, with 5 minutes of history (CPU now, average and maximum, private bytes trend) and what closing each application would return (private RAM and commit). Windows components, processes that cannot be inspected and DiskScope itself get no estimate. DiskScope never ends a process.
- GPU: usage per engine type, dedicated and shared memory per adapter, processes using a GPU engine.
- Short history: in-memory tiers (1 s for 10 min, 5 s for 1 h, 1 min for 24 h), trend and average on every dashboard tile, alerts with evidence, coincidence hypotheses.
- Reports: history and process CSV, JSON and HTML, with no user name, machine name or command line.

## Evidence

| Check | Result |
|---|---|
| Tests | 270 passing (`dotnet test DiskScope.slnx`) |
| Monitoring cost, no UI | 0.25-0.27 % of the machine's CPU, 0 failures and 0 missed ticks over 60 s and 302 s runs (docs/benchmarks/RESULTS.md) |
| App memory | working set 209 MB at start, 325 MB after 10 min with every tab opened, handles and threads flat |
| By application, running app on the work laptop | 30 user applications, about 5.1 GB of private RAM and 11.9 GB of commit; Windows, not inspectable and DiskScope groups held 2.0 GB of commit, not counted. Service hosts grouped as one row (about 100 processes, more than 90 not openable without elevation). Trends read through UI Automation after about 4 min of run time |
| GPU | one integrated adapter; engines and memory read through PDH and DXGI (docs/GPU-COMPATIBILITY.md) |

The 2026-10-26 and 2026-10-27 runs were on a busy work laptop (20 logical processors often at 50-100 %). Collector wall times there mostly measure waiting for a CPU, not collector work (RESULTS.md).

## What "can be freed" means here

An application's private working set is the RAM its exit returns, and its private bytes the commit. Shared pages (DLLs, mapped files) and the file cache stay, so they are not counted: the real gain can be a little higher. DiskScope does not say a process is safe to end. It refuses an estimate for anything it cannot inspect or that runs from the Windows directory, and reminds the user to save open work.

## Open items

1. Processes: command line, user and signature on demand; exited processes in the history; telling a windowed application from a background helper; GPU memory per application in the "By application" view.
2. Two programs with the same image name form one group (for example two separate `python.exe` installs).
3. Hardware sensors (GPU temperature, clocks, fans): no vendor-neutral source validated; the usual library loads a kernel driver. Needs the owner's decision.
4. History persistence (SQLite would be the first external dependency) and a first ETW scenario (needs elevation). Needs decisions.
5. Side-by-side comparison with Task Manager, and a 10-minute app memory run without interaction.
