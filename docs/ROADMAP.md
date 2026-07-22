# KnownFirst roadmap

**Prioritization date:** 2026-07-22

This roadmap records intended order. It does not claim that planned behavior
exists. Verified implementation state belongs in
[PROJECT_STATE.md](PROJECT_STATE.md).

## Status definitions

- **Planned:** accepted ordering, implementation not started.
- **In progress:** an isolated branch has active scoped work.
- **Completed:** merged and verified at the stated milestone.
- **Deferred:** intentionally outside the current sequence.

## Prioritized milestones

| Priority | Milestone | Status | Required outcome |
| ---: | --- | --- | --- |
| 1 | Documentation and handoff structure | In progress | Maintain canonical entry points and an auditable single-worktree handoff. |
| 2 | Remove Apple targets | Planned | Remove iOS and Mac Catalyst from project, platform code, build configuration, tests, and docs on a dedicated branch. |
| 3 | Windows/Android validation | Planned | Validate Windows Debug and Android Debug/Release after platform cleanup. |
| 4 | Wikipedia fallback | Planned | Add a consented fallback behind Wiktionary without changing provider boundaries prematurely. |
| 5 | Persistence and migration decision | Planned | Decide and document schema implications of the final provider model. |
| 6 | Re-check backup model | Planned | Verify the backup contract against the final provider and persistence model. |
| 7 | Backup/Restore continuation | Planned | Resume Phase 3 only after the preceding gates are complete. |
| 8 | Statistics | Planned | Define and present privacy-preserving learning metrics. |
| 9 | Privacy-friendly bug reporting | Planned | Produce explicit user-reviewed redacted diagnostic exports. |
| 10 | Public-beta preparation | Planned | Complete release, privacy, platform, support, and operational readiness. |

Data Safety v1 and the database audit are gates for versioned backup and
restore. The UI follows the service and compatibility contract; it must not
define the format implicitly.

## In progress

- Canonical documentation and handoff structure on
  `docs/current-work-and-index`.

No Wikipedia fallback or backup/restore implementation is in progress.

## Completed

- Stable Windows and Android application foundation.
- Exact text import and resumable Known/Unknown review.
- Automatic/manual vocabulary preparation with optional Wiktionary lookup.
- Recognition and spelling learning with deterministic scheduling.
- Local SQLite persistence and schema version 7.
- Beta 7 UI/UX hardening baseline.
- Beta 8 Android Release/AOT stabilization.
- Beta 8 merge to `master` and annotated source tag
  `v1.0.0-beta.8`.

## Deferred

- Full offline dictionary package pipeline.
- FSRS scheduling.
- PDF, EPUB, and website import.
- Speech, handwriting, and pronunciation features.
- Cloud synchronization and accounts.
- Analytics, advertising, payments, and automatic telemetry.

Deferred items require a future explicit milestone and must not be introduced
speculatively while executing the prioritized sequence.

Longer-term checks and releases include Linux feasibility or alternative-host
architecture validation, Microsoft Store publication, and public Google Play
publication.
