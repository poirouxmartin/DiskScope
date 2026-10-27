# Publishing checklist

Status on 2026-10-27. The public repository already exists: `github.com/poirouxmartin/DiskScope`, visibility public, `main` at `9c4ab12` (pushed 2026-10-22). The work done since then lives on the local branch `dev/poiroux/retours-laptop-pro` and is not pushed (`git ls-remote origin` shows only `main` at `9c4ab12`).

## Ready

| Item | Evidence |
|---|---|
| License | `LICENSE`: MIT, copyright 2026 Martin Poiroux |
| README | English; storage (Part 1) and CPU, memory, GPU, processes including the "By application" view (Part 2); requirements, build and run, measured performance, limits, doc index with both module reports |
| Screenshots | `docs/images/dashboard.png`, `cpu-memory.png`, `gpu.png`: no user, folder or machine name; only the GPU model and generic process names (Windows shell, a browser, DiskScope). No text or EXIF chunk in the PNG files. No "By application" or storage screenshot on purpose: they would show the process or folder names of the machine they were taken on |
| CI | `.github/workflows/ci.yml`: windows-latest, .NET SDK from `global.json`, Release build, tests, TRX artifact; `contents: read` only. On GitHub: one run, on `9c4ab12`, success. The same two commands run locally on the branch head on 2026-10-27: build 0 warnings, 0 errors; 270 tests passing |
| `.gitignore` | `bin/`, `obj/`, test results and IDE files are not tracked |
| No external runtime dependency | WPF and Win32 only; test projects use xunit |
| Privacy audit of the history | See below: every commit reachable from a branch is clean |

## Privacy audit of the history (2026-10-27)

Scope: every commit reachable from any ref plus every commit still in the local reflog, 33 commits in total (26 reachable). Each commit's author, committer, message and added lines were searched for: the employer's and its products' names, work e-mail addresses, any e-mail other than the owner's public one, the Windows user name and `C:\Users\<name>` paths, the machine name, private IP ranges, key or token formats (GitHub, OpenAI, AWS, private keys, Slack), `password|secret|apikey =` assignments, security product names, and the names of the owner's other projects. The 186 tracked files of the branch head were searched for the user, machine and employer names, including the spec `.docx` XML parts and the PNG metadata chunks.

Result:

- The 26 commits reachable from a branch: no personal or work data. Pattern hits are false positives (`reset` matching a product-name pattern, the audit description itself), except the line below.
- One line already public on `main` (`fa9fb14`, `docs/benchmarks/RESULTS.md` and `docs/BENCHMARK-PROTOCOL.md`) names the endpoint security product of the dev laptop and its mode. It names no company, person or machine. Both files are generalized on the branch (2026-10-26 and 2026-10-27); the old wording stays in the public history unless `main` is rewritten, which is not worth it for this line.
- 7 commits reachable only from the local reflog carry a work e-mail address as author: `779b3c3`, `facbf6a`, `48838f4`, `d28259a` (the original commits of 2026-10-26) and `569f1bd`, `e32cfc6`, `63545e0` (intermediate states of the rewrite that replaced them). They were never pushed. A push sends only commits reachable from the pushed branch, so they cannot leak through a normal push. They disappear when the reflog expires, or at once with `git reflog expire --expire=now --all` then `git gc --prune=now` (destructive for local undo history; not run).
- The benchmark CLI prints the machine name in its header (`Machine:`). That header is never committed; keep it that way when pasting results into `RESULTS.md`.

## Owner's steps to publish

1. Push the branch: `git push -u origin dev/poiroux/retours-laptop-pro`.
2. Open a pull request into `main` on GitHub; CI runs on it. Check it is green (the GPU test accepts machines without a GPU, which GitHub runners are, but the new commits have not run there yet).
3. Merge into `main`; CI runs again on the merge.
4. Repository settings: description, topics, branch protection on `main`, whether Actions run on pull requests from forks.
5. Optional: expire the local reflog (above), a release (tag plus a zipped `dotnet publish` output; nothing is signed and there is no installer, the README says so), issue templates, `CONTRIBUTING.md`, a security contact.

Any change of license or copyright holder is also the owner's call. The agents working on this branch commit locally only.
