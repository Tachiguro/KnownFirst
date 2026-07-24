# KnownFirst repository instructions

KnownFirst is a local-first vocabulary-learning application for Windows and Android. Use the name **KnownFirst** exclusively.

## Core repository rules

- **Single worktree:** `C:\Dev\KnownFirst` is the only normal working folder (see [ADR-0007](docs/decisions/ADR-0007-single-canonical-working-directory.md)). Do not create additional worktrees or repository copies without explicit user approval.
- **Single writing agent:** Only one writing agent may operate at a time in the repository.
- **No direct master commits:** Never implement directly on `master`. Work on task-appropriate branches using narrow prefixes (`feature/`, `fix/`, `docs/`, `build/`, `test/`, `chore/`, `hotfix/`, `release/`).
- **Prohibited destructive operations:** Do not use `git clean`, destructive reset, stash, rebase, amend, history rewriting, or force-push unless an explicit recovery task authorizes it.
- **Branch and worktree safety:** Inspect branch, HEAD, status, diff, untracked files, and registered worktrees before editing. Treat all pre-existing local work as protected. Never delete a branch or worktree without verifying it is clean.
- **Explicit authorization required:** Do not commit, push, create a pull request, merge, tag, run ADB/device commands, build APK/AAB packages, or perform release deployment unless explicitly authorized. Auto-merge is strictly prohibited.

## Task-based documentation routing

Do not reconstruct the full repository context for routine tasks. Follow task-based routing:

1. `AGENTS.md` is always read before making repository edits.
2. [docs/AGENT_WORKFLOW.md](docs/AGENT_WORKFLOW.md) is read for every task that writes repository files.
3. [docs/CURRENT_WORK.md](docs/CURRENT_WORK.md) is read only when continuing the active package, reviewing the active branch, changing operational task state, or explicitly requested by the task.
4. Consult [docs/INDEX.md](docs/INDEX.md) to locate task-specific contracts and architecture specifications.
5. [docs/BUILD_AND_RELEASE.md](docs/BUILD_AND_RELEASE.md) is read only after an explicit build, configuration, packaging, signing, APK, AAB, release, artifact-reconstruction, or store request.
6. Single authoritative owners: `docs/PROJECT_STATE.md` owns stable verified facts; `docs/ROADMAP.md` owns planned sequence; `docs/CURRENT_WORK.md` owns active task state.
7. Historical prompts, handoffs, audits, and old test plans are non-routine evidence and must not be read unless specifically required by the task.

## Safety and privacy rules

- Automated tests must use isolated temporary SQLite databases, fake clocks, offline fixtures, and mock HTTP handlers.
- Never open, copy, migrate, reset, delete, or test against a real user database.
- Never make live Wikimedia network requests in automated tests.
- Never use `git add .` to stage files.
- No routine device or emulator use, GUI automation, ADB, Logcat, `pm clear`, app uninstall, or device-data reset.
- No APK or AAB creation without explicit authorization.
- Never commit secrets, signing material, private logs, real user content, databases, or generated build artifacts.
- AOT, trimming, and source-generated JSON serialization remain binding architecture constraints.
- All code, comments, tests, logs, documentation, commits, and PR descriptions must remain in English.
