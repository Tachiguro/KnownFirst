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
| 1 | Data Safety v1 | Planned | Define threat model, data boundaries, compatibility guarantees, failure behavior, and acceptance criteria without yet claiming backup implementation. |
| 2 | Database and migration audit | Planned | Inventory schema/version transitions, destructive operations, integrity checks, and upgrade fixtures; close contract gaps before backup work. |
| 3 | Versioned backup and restore | Planned | Implement a documented, versioned, integrity-checked local format with safe validation and transactional restore. |
| 4 | Backup/restore UI | Planned | Add understandable local user flows, confirmations, progress, error recovery, and accessibility around the verified service layer. |
| 5 | Learning events for statistics | Planned | Define and persist privacy-preserving learning events without changing scheduling truth or duplicating review history. |
| 6 | Statistics dashboard | Planned | Present useful local metrics derived from the accepted event contract. |
| 7 | Privacy-friendly bug reporting | Planned | Produce explicit user-reviewed diagnostic exports with redaction and no automatic upload. |
| 8 | Public-beta preparation | Planned | Complete release criteria, privacy review, platform validation, support material, and operational readiness. |
| 9 | Size and build-artifact optimization | Planned | Measure and reduce output size without weakening AOT, trimming, signing, or reproducibility. |

Data Safety v1 and the database audit are gates for versioned backup and
restore. The UI follows the service and compatibility contract; it must not
define the format implicitly.

## In progress

- Project governance and documentation structure on
  `feature/project-governance-and-docs`.

No backup or restore implementation is in progress.

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
