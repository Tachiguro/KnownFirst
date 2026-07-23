# KnownFirst repository instructions

## Project

KnownFirst is a local-first vocabulary-learning application for Windows and
Android. It imports user-provided text, separates known from unknown
vocabulary, prepares selected words, and schedules recognition and spelling
practice. Use the name **KnownFirst** exclusively.

## Required reading order

This task-based reading policy is an efficiency rule, not permission to ignore applicable contracts or safety constraints.

1. Always read the applicable `AGENTS.md` file before making changes.
2. Read `docs/CURRENT_WORK.md` for implementation work, project-state work, or when the current active package and repository constraints are relevant.
3. Read only the architecture, contract, code, test, localization, migration, release, or operational files directly relevant to the task.
4. Follow any more specific nested `AGENTS.md` file that applies to the edited path.
5. Read `docs/INDEX.md`, `docs/PROJECT_STATE.md`, `docs/ROADMAP.md`, historical handoffs, plans, and broad repository documentation only when:
   - the task explicitly concerns them;
   - a milestone or verified global project state changes;
   - a referenced contract must be checked;
   - the task is high-risk and requires broader context.
6. Do not reconstruct the entire repository state for routine isolated work.
7. Do not repeatedly reread unchanged files during the same work package unless new evidence requires it.

The binding architecture documents are:

- `docs/KNOWNFIRST_ARCHITECTURE.md`
- `docs/MVP_WORKFLOW.md`
- `docs/WORD_ANALYSIS.md` for text analysis and coordinates
- `docs/DATABASE_CONTRACT.md` for persisted data and migrations
- `docs/UI_UX_ACCEPTANCE.md` for the current UI acceptance baseline

Historical prompts and test records are evidence, not current instructions.
When sources conflict, stop and resolve the contradiction in the authoritative
documents before implementation.

## Lean delivery workflow

### Vertical delivery

- Use one coherent vertical feature slice per work package.
- Keep one writing agent and one physical worktree.
- Prefer one connected implementation run over separate repetitive analysis, implementation, testing, documentation, and cleanup runs.
- Do not create speculative infrastructure, abstractions, migrations, UI, documentation, or follow-up packages outside the accepted scope.
- Resolve routine implementation details using repository evidence instead of repeatedly asking for confirmation.
- Stop and report only when a genuine safety boundary, destructive operation, missing authorization, incompatible repository state, or material product decision prevents safe continuation.

### Acceptance and regression scope

- Before implementation, identify approximately five to eight decisive acceptance or regression test groups for a normal feature package.
- The number is a risk-based guideline, not a requirement to create artificial tests.
- Add more only when the feature risk or contract surface justifies them.
- Do not expand a small package into a repository-wide test initiative.

### Focused implementation loop

- During implementation, run only the focused tests required to develop and stabilize the affected behavior.
- Prefer the smallest relevant test project, test class, filter, or contract check.
- Do not repeatedly run the complete suite after every small edit.
- Do not repeat a successful test or build when the relevant inputs have not changed.
- If a change after validation can affect a result, rerun only the affected validation unless the final complete validation has not yet occurred.

### Final validation

- For normal code packages, run the complete relevant test suite exactly once against the final stable tree before opening or finalizing the pull request.
- Run each required platform/configuration build exactly once against the final stable tree.
- The standard KnownFirst final validation remains:
  - complete test suite;
  - Windows Debug build;
  - Android Debug build with -m:1;
  - Android Release build with -m:1;
  - AOT, trimming, and source-generation warnings reviewed.
- Run additional validation only when:
  - a required result failed;
  - relevant inputs changed afterward;
  - the task is high-risk;
  - a specific contract requires it.
- Documentation-only and governance-only packages do not require tests or builds unless they alter executable tooling, build configuration, generated artifacts, or another verifiable technical contract.
- Never use this lean policy to skip validation that is materially required for the changed code.

### Risk-based escalation

Additional audits, test passes, reviews, or specialized validation are justified for high-risk packages such as:

- database migrations;
- backup or restore;
- data-loss risk;
- security-sensitive behavior;
- privacy-sensitive behavior;
- concurrency or synchronization;
- release and signing;
- production deployment;
- difficult AOT or trimming failures;
- destructive repository or device operations;
- cross-version persistence compatibility.

Routine isolated changes must not automatically inherit the process burden of these high-risk categories.

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
- `C:\Dev\KnownFirst` is the only normal working folder.
- Do not create a worktree without explicit user approval; do not create
  repository copies.
- Only one writing agent may operate at a time.
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
- Do not use ADB, devices, emulators, GUI automation, APK/AAB creation, or store
  uploads unless explicitly requested.
- Normal builds and unit tests do not require a connected physical Android device.
- Do not perform routine installation, execution, ADB, logcat, `pm clear`, app
  uninstallation, or data resets on physical Android devices without explicit
  user authorization.
- Physical Android device testing is conducted only as a separate work package
  following a complete user-facing feature, before a new Beta release, for a
  device-specific bug, or upon explicit user request.
- A successful build or automated test is not evidence of physical-device or
  visual validation.

## Testing and build requirements

- Add or identify a regression test before fixing behavior.
- Use focused tests during implementation.
- Perform final complete validation exactly once on the stable final tree.
- Select relevant builds according to the affected targets and established KnownFirst requirements.
- Do not repeat successful unchanged validation without cause.
- Diagnose failed validation and rerun it after the fix.
- Documentation-only governance changes require `git diff --check` but no tests or builds.
- Device testing is never routine and still requires an explicit package or user request.
- APK, AAB, publish, signing, deployment, and store work remain separately authorized activities.
- AOT and trimming requirements remain fully binding.
- Keep every published commit independently understandable and green.

## Documentation responsibilities

For a normal routine implementation package, update only:

- `docs/CURRENT_WORK.md`;
- `CHANGELOG.md` when behavior visible to users changes;
- an existing architecture or contract document only when that contract actually changes.

- Do not create a new handoff, audit, plan, roadmap document, architecture document, or status document for a small routine package unless the package materially requires one.
- Update `docs/PROJECT_STATE.md` only when the verified global project state or product milestone changes.
- Update `docs/ROADMAP.md` only when milestone status, accepted prioritization, or sequencing materially changes.
- Create or substantially update a handoff for significant milestones, releases, migrations, backup/restore packages, security-sensitive packages, cross-session operational transfers, or other high-risk work.
- Keep documentation evidence-based and written after implementation is stable, preferably in one bundled pass.
- Do not document unverified claims as completed behavior.
- Do not duplicate the same status across many documents without a concrete maintenance reason.
- Governance-only edits to `AGENTS.md` do not require `docs/CURRENT_WORK.md` or `CHANGELOG.md` unless the user explicitly requests them.

Before every change, verify branch, HEAD, status, and registered worktrees. Store diagnostic artifacts only according to `docs/development/DEBUG_ARTIFACT_POLICY.md`.

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

## Review policy

- A normal work package receives one external review after implementation and final validation.
- At most one targeted correction pass is expected for ordinary review findings.
- The correction pass should address all actionable findings together.
- Do not restart a full repository audit after a narrow correction unless the correction changed the relevant architecture or risk surface.
- Additional reviews or audits require a concrete risk reason.
- Appropriate reasons include migrations, backup/restore, data-loss risk, security, privacy, concurrency, releases, difficult AOT/trimming issues, or unresolved material findings.
- Cosmetic wording, already verified unchanged behavior, and duplicated evidence do not justify repeated agent loops.
- No agent may merge or enable auto-merge; final merge remains a user decision.

## Definition of Done

For a normal code package, completion should require:

- accepted scope and relevant acceptance criteria satisfied;
- approximately five to eight decisive test groups identified where appropriate;
- focused tests passed during implementation;
- complete relevant tests run once against the final stable tree;
- required platform/configuration builds run once against the final stable tree;
- AOT, trimming, and source-generation results reviewed where applicable;
- only materially affected documentation updated;
- no unauthorized schema, migration, device, release, privacy, or scope changes;
- `git diff` checked;
- clean worktree and index after commit;
- explicit file staging;
- branch pushed normally;
- pull request opened and left unmerged;
- remaining risks and unverified claims stated accurately.

For documentation-only or governance-only packages, completion should require only:

- requested document changes completed;
- existing safety and technical contracts preserved;
- `git diff --check` passed;
- only intended files changed;
- clean worktree and index;
- explicit staging;
- normal push;
- pull request opened and left unmerged.

If the active task does not authorize publication, report the push and pull
request items as intentionally pending rather than exceeding the task.
