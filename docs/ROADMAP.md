# KnownFirst roadmap

**Prioritization date:** 2026-07-24

This roadmap records intended order. It does not claim that planned behavior exists. Verified implementation state belongs in [PROJECT_STATE.md](PROJECT_STATE.md).

## Status definitions

- **Planned:** accepted ordering, implementation not started.
- **In progress:** active scoped work on a task branch under review.
- **Completed:** merged and verified at the stated milestone.
- **Deferred:** intentionally outside the current sequence.

## Prioritized milestones

| Priority | Milestone | Status | Required outcome |
| ---: | --- | --- | --- |
| 1 | Documentation and handoff structure | Completed | Maintain canonical entry points and single-worktree handoff. |
| 2 | Remove Apple targets | Completed | Remove iOS and Mac Catalyst from project, code, build configuration, tests, and docs. |
| 3 | Windows/Android validation | Completed | Validate Windows Debug and Android Debug/Release after platform cleanup. |
| 4 | Wikipedia fallback | Completed | Add consented fallback behind Wiktionary without changing provider boundaries prematurely. |
| 5 | Versioning policy & Beta 9 identity | Completed | Establish binding versioning rules, build identity formatting, and Beta 9 product version. |
| 6 | Task-based documentation routing | In progress | Restructure documentation so agents read only task-relevant specifications (PR #16). |
| 7 | Persistence and migration decision | Planned | Decide and document schema implications of the final provider model. |
| 8 | Re-check backup model | Planned | Verify the backup contract against the final provider and persistence model. |
| 9 | Backup/Restore continuation | Planned | Resume Phase 3 only after the preceding gates are complete. |
| 10 | Statistics | Planned | Define and present privacy-preserving learning metrics. |
| 11 | Privacy-friendly bug reporting | Planned | Produce explicit user-reviewed redacted diagnostic exports. |
| 12 | Public-beta preparation | Planned | Complete release, privacy, platform, support, and operational readiness. |
| 13 | [Structured vocabulary architecture](plans/structured-vocabulary-import-and-sense-learning.md) | Completed | Architecture plan from PR #10 is merged. |
| 14 | Structured vocabulary implementation | Planned | Multi-phase execution of list/PDF import, sense-level knowledge, sync domain, and Linux host feasibility. |

Data Safety v1 and the database audit are gates for versioned backup and restore. The UI follows the service and compatibility contract; it must not define the format implicitly.

## In progress

**Task-based documentation routing**
- Rationalize Markdown documentation, streamline AGENTS.md to universal rules, rebuild docs/INDEX.md as a task router, create docs/BUILD_AND_RELEASE.md, and resolve worktree ADR-0006/ADR-0007 (Pull Request #16 open).

## Completed

- Versioning policy and Beta 9 identity merged via PR #15 (`28f8a74`).
- Wikipedia fallback user-readiness merged via PR #14 (`766f3c1`).
- Wikipedia fallback orchestration merged via PR #11 (`d33cd80`).
- Stable Windows and Android application foundation.
- Exact text import and resumable Known/Unknown review.
- Automatic/manual vocabulary preparation with optional Wiktionary and Wikipedia fallback.
- Recognition and spelling learning with deterministic scheduling.
- Local SQLite persistence and schema version 7.
- Beta 8 Android Release/AOT stabilization and release tag `v1.0.0-beta.8`.

## Deferred

- Full offline dictionary package pipeline.
- FSRS scheduling.
- PDF, EPUB, and website import.
- Speech, handwriting, and pronunciation features.
- Cloud synchronization and accounts.
- Analytics, advertising, payments, and automatic telemetry.

Deferred items require a future explicit milestone and must not be introduced speculatively while executing the prioritized sequence.
