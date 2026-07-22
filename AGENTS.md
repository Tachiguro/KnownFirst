# KnownFirst repository instructions

## Project

KnownFirst is a local-first vocabulary-learning application for Windows and
Android. It imports user-provided text, separates known from unknown
vocabulary, prepares selected words, and schedules recognition and spelling
practice. Use the name **KnownFirst** exclusively.

## Required reading order

Before changing anything, read these sources completely in this order:

1. `AGENTS.md`
2. `docs/PROJECT_STATE.md`
3. `docs/ROADMAP.md`
4. `CHANGELOG.md`
5. the relevant architecture documents
6. the relevant accepted ADRs in `docs/decisions/`
7. the newest applicable report in `docs/handoffs/`

The binding architecture documents are:

- `docs/KNOWNFIRST_ARCHITECTURE.md`
- `docs/MVP_WORKFLOW.md`
- `docs/WORD_ANALYSIS.md` for text analysis and coordinates
- `docs/DATABASE_CONTRACT.md` for persisted data and migrations
- `docs/UI_UX_ACCEPTANCE.md` for the current UI acceptance baseline

Historical prompts and test records are evidence, not current instructions.
When sources conflict, stop and resolve the contradiction in the authoritative
documents before implementation.

## Product and architecture principles

- Preserve the original imported text byte-for-character at the .NET string
  level and preserve every documented coordinate invariant.
- Keep document analysis, vocabulary identity, preparation, and learning as
  separate domain concerns.
- Frequency affects priority, never whether a vocabulary item exists.
- Permanently known vocabulary is retained as a minimal language-scoped marker
  and applies across documents.
- Keep personal learning data local. Optional Wikimedia lookup sends only the
  selected term and required language information after consent.
- Never fabricate dictionary data or silently convert a failed operation into
  success.
- Keep long-running workflows resumable and user-visible state transactional.
- Reuse existing entities and services before introducing another
  representation.
- Do not expose DEBUG or diagnostic-only features in Release.

## Repository, branch, and worktree rules

- Never implement directly on `master`.
- Use one purpose-specific branch and an isolated Git worktree for each work
  package.
- Inspect branch, HEAD, status, diff, untracked files, and registered worktrees
  before editing.
- Treat every other worktree and every pre-existing local change as protected.
- Do not use `git clean`, destructive reset, stash, rebase, amend, history
  rewriting, or force-push unless an explicit recovery task authorizes it.
- Never delete a branch or worktree merely because Git reports it as merged.
  Confirm that its worktree is clean and that no uncommitted work exists.
- Use `feature/<topic>`, `fix/<topic>`, `hotfix/<topic>`, and
  `release/<topic>` branch names.
- Do not commit, push, merge, tag, or create a pull request unless the active
  task explicitly authorizes that action.

## Safety and privacy

- Automated tests must use temporary SQLite databases, fake clocks, offline
  fixtures, and fake HTTP handlers.
- Never open, copy, migrate, reset, delete, or test against a real user
  database.
- Never use live Wikimedia requests in automated tests.
- Do not commit secrets, signing material, private logs, real user content,
  databases, screenshots containing private data, or generated build
  artifacts.
- Do not use ADB, devices, emulators, GUI automation, Release builds, Android
  builds, APK/AAB creation, or store uploads unless explicitly requested.
- A successful build or automated test is not evidence of physical-device or
  visual validation.

## Testing and build requirements

- Add or identify a regression test before fixing behavior.
- Run focused tests while developing and the complete affected automated suite
  before handoff.
- For normal application changes, run the Windows Debug build after tests.
- Run Android or Release builds only when the task and affected scope require
  them.
- Documentation-only changes require link validation, content consistency
  checks, scope review, and `git diff --check`; an application build may be
  recorded as not applicable.
- Keep every published commit independently understandable and green.

## Documentation responsibilities

- Git history records the concrete change.
- `CHANGELOG.md` records user-visible effects.
- ADRs record why a durable decision was made.
- `docs/PROJECT_STATE.md` records the verified current whole-project state.
- Release notes record one published version and its evidence.
- Handoffs transfer technical context between sprints and agents.
- `docs/ROADMAP.md` records future order, not implementation claims.
- `docs/DATABASE_CONTRACT.md` records every persisted-schema and
  compatibility change.

Update documentation in the same work package as the behavior it describes.
Do not turn plans into claims of implemented functionality.

## Commits and pull requests

- Keep code, comments, logs, tests, documentation, commit messages, and pull
  requests in English.
- Make small, logical commits and stage files explicitly; do not use
  `git add .`.
- Use clear conventional commit subjects such as `feat:`, `fix:`,
  `test:`, `docs:`, and `build:`.
- Do not combine unrelated product, generated, diagnostic, or release
  artifacts with a work package.
- Push without force and open a pull request against the intended integration
  branch. Never merge unless explicitly authorized.
- Complete the repository pull-request checklist and identify every unverified
  manual or platform-specific claim.

## Definition of Done

A mergeable change is complete only when:

- implementation is complete;
- automated tests exist for affected behavior;
- all affected tests pass;
- relevant builds pass, or a documentation-only exception is recorded;
- user impact is documented in `CHANGELOG.md`;
- durable architecture decisions are documented in ADRs;
- database changes are documented in `docs/DATABASE_CONTRACT.md`;
- `docs/PROJECT_STATE.md` is updated after a milestone;
- known limitations are documented;
- commit messages are understandable;
- no temporary files or generated artifacts are committed;
- the branch is pushed; and
- a pull request is created.

If the active task does not authorize publication, report the push and pull
request items as intentionally pending rather than exceeding the task.
