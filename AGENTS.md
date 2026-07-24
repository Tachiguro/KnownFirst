# KnownFirst repository instructions

KnownFirst is a local-first vocabulary-learning application for Windows and Android. Use the name **KnownFirst** exclusively.

## Core repository rules

- **Single worktree:** `C:\Dev\KnownFirst` is the only normal working folder (see [ADR-0007](docs/decisions/ADR-0007-single-canonical-working-directory.md)). Do not create additional worktrees or repository copies without explicit user approval.
- **Single writing agent:** Only one writing agent may operate at a time in the repository.
- **No direct master commits:** Never implement directly on `master`. Work on task-appropriate branches using narrow prefixes (`feature/`, `fix/`, `docs/`, `build/`, `test/`, `chore/`).
- **Prohibited destructive operations:** Do not use `git clean`, destructive reset, stash, rebase, amend, history rewriting, or force-push unless an explicit recovery task authorizes it.
- **Branch and worktree safety:** Inspect branch, HEAD, status, diff, untracked files, and registered worktrees before editing. Treat all pre-existing local work as protected. Never delete a branch or worktree without verifying it is clean.
- **Explicit authorization required:** Do not commit, push, create a pull request, merge, tag, run ADB/device commands, build APK/AAB packages, or perform release deployment unless explicitly authorized. Auto-merge is strictly prohibited.

## Task-based documentation routing

Do not reconstruct the full repository context for routine tasks. Follow task-based routing:

1. Always read `AGENTS.md` before making edits.
2. Read `docs/CURRENT_WORK.md` for active operational task context and the next exact action.
3. Consult `docs/INDEX.md` to locate only the specific architecture, contract, or workflow files needed for the active task.
4. Single authoritative owners: `docs/PROJECT_STATE.md` owns stable verified facts; `docs/ROADMAP.md` owns planned sequence; `docs/CURRENT_WORK.md` owns active task state.
5. Historical prompts, handoffs, audits, and old test plans are non-routine evidence and must not be read unless specifically required by the task.

## Delivery, workflow, and testing policies

- Detailed operational delivery, testing, validation, documentation, review, commit, and PR rules live in [docs/AGENT_WORKFLOW.md](docs/AGENT_WORKFLOW.md).
- Build, packaging, signing, AAB retention, and release instructions live in [docs/BUILD_AND_RELEASE.md](docs/BUILD_AND_RELEASE.md) and are read only when explicitly requested.
- Architecture, data contracts, and UI guidelines live in [docs/KNOWNFIRST_ARCHITECTURE.md](docs/KNOWNFIRST_ARCHITECTURE.md), [docs/DATABASE_CONTRACT.md](docs/DATABASE_CONTRACT.md), and [docs/UI_UX_ACCEPTANCE.md](docs/UI_UX_ACCEPTANCE.md).

## Safety and privacy rules

- Automated tests must use isolated temporary SQLite databases, fake clocks, offline fixtures, and mock HTTP handlers.
- Never open, copy, migrate, reset, delete, or test against a real user database.
- Never make live Wikimedia network requests in automated tests.
- Never commit secrets, signing material, private logs, real user content, databases, or generated build artifacts.
- Physical Android device testing, ADB, Logcat, and packaging are never routine and require explicit user authorization.
- AOT, trimming, and source-generated JSON serialization remain binding architecture constraints.
- All code, comments, tests, logs, documentation, commits, and PR descriptions must remain in English.
