# Publishing checklist

Status on 2026-10-26. The public repository already exists: `github.com/poirouxmartin/DiskScope`, visibility public, `main` at `9c4ab12` (pushed 2026-10-22). The work done since then lives on the local branch `dev/poiroux/retours-laptop-pro` and is not pushed.

## Ready

| Item | Evidence |
|---|---|
| License | `LICENSE`: MIT, copyright 2026 Martin Poiroux |
| README | English, features, requirements, build and run, measured performance, limits, doc index |
| CI | `.github/workflows/ci.yml`: windows-latest, .NET SDK from `global.json`, Release build, tests, TRX artifact; `contents: read` only. One run on GitHub, on `9c4ab12`: success |
| Tests | 255 passing locally (`dotnet test DiskScope.slnx`), Debug |
| `.gitignore` | `bin/`, `obj/`, test results and IDE files are not tracked |
| No external runtime dependency | WPF and Win32 only; test projects use xunit |
| Privacy audit of the history | See below: every commit reachable from a branch is clean |

## Privacy audit of the history (2026-10-26)

Scope: every commit reachable from any ref (`main`, `dev/poiroux/retours-laptop-pro`, `origin/main`) plus every commit still in the local reflog, 30 commits in total. Each commit's author, committer, message and added lines were searched for: the employer's name, work e-mail addresses, any e-mail other than the owner's public one, Windows user names and `C:\Users\<name>` paths, the machine name, private IP ranges, key or token formats (GitHub, OpenAI, AWS, private keys, Slack) and `password|secret|token|apikey =` assignments. The spec `.docx` (XML parts) and the screenshots in `docs/images` were checked separately.

Result:

- The 23 commits reachable from a branch: no match. The only pattern hits are false positives (`CancellationToken`, `DateTimeKind.Local`, the SDK version `10.0.401`).
- 7 commits reachable only from the local reflog carry a work e-mail address as author: `779b3c3`, `facbf6a`, `48838f4`, `d28259a` (the original commits of 2026-10-26) and `569f1bd`, `e32cfc6`, `63545e0` (intermediate states of the rewrite that replaced them). They were never pushed: `git ls-remote origin` shows only `main` at `9c4ab12`. A push sends only commits reachable from the pushed branch, so they cannot leak through a normal push. They disappear when the reflog expires, or at once with `git reflog expire --expire=now --all` then `git gc --prune=now` (destructive for local undo history; not run).
- One line to consider, already public on `main` (`fa9fb14`, `docs/benchmarks/RESULTS.md`): the benchmark environment caveat names the endpoint security product and its mode on the dev laptop. It names no company, person or machine. It was generalized in the working tree on 2026-10-26; the old wording stays in the public history unless `main` is rewritten, which is not worth it for this line.
- The benchmark CLI prints the machine name in its header (`Machine:`). That header is never committed; keep it that way when pasting results into `RESULTS.md`.

## Remaining before announcing the project

1. Push `dev/poiroux/retours-laptop-pro` and merge it into `main` (owner's go; see below). CI then runs on the merge.
2. Check the CI run on the merged `main` is green; the GPU test accepts machines without a GPU, which GitHub runners are, but this has not run there yet.
3. Optional: a release (tag plus a zipped `dotnet publish` output). There is no installer and nothing is signed; the README says so.
4. Optional: issue templates, `CONTRIBUTING.md`, a security contact. Not needed for a first publication.

## Only the owner can do

- Push the branch and merge it into `main`; the agents working on this branch commit locally only.
- Decide whether to expire the local reflog (see above).
- Repository settings on GitHub: description, topics, branch protection on `main`, whether Actions run on pull requests from forks.
- Any change of license or copyright holder.
