# KnownFirst documentation index

This is the canonical map of project documentation. New agents read
`AGENTS.md`, this index, `CURRENT_WORK.md`, `PROJECT_STATE.md`, `ROADMAP.md`,
`CHANGELOG.md`, and then only the relevant architecture, plan, decision, test,
release, or handoff documents.

## Canonical entry points

- [Current work and handoff](CURRENT_WORK.md) — operational state and the next exact action.
- [Project state](PROJECT_STATE.md) — verified stable product and repository facts.
- [Roadmap](ROADMAP.md) — prioritized future work; planned items are not claims of implementation.
- [Changelog](../CHANGELOG.md) — completed user-visible changes and release notes.
- [Debug artifact policy](development/DEBUG_ARTIFACT_POLICY.md) — disposable diagnostic storage rules.

## Architecture and domain contracts

- [Architecture](KNOWNFIRST_ARCHITECTURE.md)
- [MVP workflow](MVP_WORKFLOW.md)
- [Word analysis](WORD_ANALYSIS.md)
- [Database contract](DATABASE_CONTRACT.md)
- [UI/UX acceptance](UI_UX_ACCEPTANCE.md)
- [Database audit](architecture/database-audit.md)
- [Backup format v1](architecture/backup-format-v1.md)
- [Wikipedia JSON API client](architecture/wikipedia-json-client.md)
- [Wikipedia Lookup Provider](architecture/wikipedia-lookup-provider.md)

## Feature and delivery material

- [Word preparation and online lookup](WORD_ANALYSIS.md)
- [Backup and restore plan](plans/backup-restore-v1-implementation-plan.md)
- [Structured vocabulary import and sense-level learning](plans/structured-vocabulary-import-and-sense-learning.md)
- [Learning and normalization requirements](REQUIREMENTS_DELTA_LEARNING_AND_NORMALIZATION.md)
- [GUI test matrix](GUI_TEST_MATRIX.md)
- [Windows GUI test plan](KnownFirst_Windows_GUI_Testplan.md)
- [Beta testing](BETA_TESTING.md)

## Decisions, releases, and handoffs

- [Accepted decisions](decisions/README.md)
- [Beta 8 release notes](releases/1.0.0-beta.8.md)
- [Beta 8 release handoff](handoffs/2026-07-22-beta-8-release.md)
- [Single-worktree consolidation handoff](handoffs/2026-07-22-single-worktree-consolidation.md)
- [Remove Apple targets handoff](handoffs/2026-07-22-remove-apple-targets.md)
- [Wikipedia fallback orchestration handoff](handoffs/2026-07-23-wikipedia-fallback-orchestration.md)

## Maintenance and history

- [Documentation audit](maintenance/documentation-audit.md)
- [Branch/worktree inventory](maintenance/branch-and-worktree-inventory.md) — historical snapshot, not current operational state.
- [Agent workflow](AGENT_WORKFLOW.md)
- [Archived vertical-slice prompt](archive/TEXT_REVIEW_VERTICAL_SLICE_PROMPT.md) — historical reference only.
- [Original implementation prompt](CODEX_IMPLEMENTATION_PROMPT.md) — historical reference; it cannot override this index or `AGENTS.md`.

Historical documents remain available for traceability. They are not current
instructions unless explicitly linked from `CURRENT_WORK.md` as relevant
context. `PROJECT_STATE.md` owns stable facts, `ROADMAP.md` owns future order,
and `CURRENT_WORK.md` owns the active handoff.
