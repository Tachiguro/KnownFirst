# KnownFirst project state

**Status date:** 2026-07-24
**State source:** `master` (`28f8a74637b2b81d55ed39411922d2ac1decda75`)
**Next product milestone:** Sense-level learning data-model decision package

This document is the authoritative snapshot of verified current state. Update it when a milestone is completed or when a release, schema, supported platform, or confirmed limitation changes. Plans belong in [ROADMAP.md](ROADMAP.md).

## Stable release

| Field | Verified value |
| --- | --- |
| Project | KnownFirst |
| Source Version | `1.0.0-beta.9` (build 9, merged PR #15 `28f8a74`) |
| Distributed Stable Version | `1.0.0-beta.8` (code 8) |
| Package ID | `com.tachiguro.knownfirst` |
| Release commit (Beta 8) | `29aff385f2c3cabca49b70bd011bf4c09808df6d` |
| Master merge commit (Beta 8) | `956e71895cf141805c8c24f7d32691075d439730` |
| Annotated release tag | `v1.0.0-beta.8` |
| Distribution | Google Play Internal Testing |

## Supported platforms

- **Android:** released through Google Play Internal Testing; minimum Android version is API 24 (Android 7.0).
- **Windows:** primary local development and automated/manual verification platform.
- **iOS:** deliberately removed from the project and not supported.
- **Mac Catalyst:** deliberately removed from the project and not supported.

## Production capabilities

The current product implements:

- English and German UI localization with persisted System, Light, and Dark appearance modes;
- exact text import with deterministic Unicode-aware sentence and vocabulary analysis;
- resumable Known/Unknown vocabulary review with persisted decisions and Undo;
- language-scoped vocabulary identity and global minimal known-word markers;
- frequency-prioritized automatic or manual preparation;
- explicit online-lookup consent, read-only Wiktionary lookup with automatic fallback to Wikipedia definitions, and a local SQLite lexical cache;
- source attribution, alternative-meaning selection, manual correction, and context snapshots;
- recognition and spelling card directions with independent deterministic schedules;
- resumable learning sessions and permanent-known cleanup;
- transactional local persistence, startup maintenance, and bounded structured diagnostics;
- responsive Windows and Android layouts with localized workflow gating.

## Confirmed verification

### Automated

Automated test suite (606 tests) passed clean against PR #15 head before master merge. Tests cover Core policies, text analysis, temporary-SQLite persistence and migration, review/preparation/learning workflows, localization, diagnostics, build identity, UI contracts, Wikipedia JSON API client, Wikipedia lookup provider, and offline Wiktionary fixtures. Automated tests do not use live Wikimedia requests.

### Release evidence

The verified release handoff records that a signed Beta 8 AAB was built from `29aff385f2c3cabca49b70bd011bf4c09808df6d` and accepted in Google Play Internal Testing. Post-merge Beta 9 build and AAB output package awaits explicit post-merge execution.

## Database status

- Storage is local SQLite in the application data directory (`knownfirst.db3`).
- Current `PRAGMA user_version` is **7**.
- The schema creates 17 application tables for documents, vocabulary, occurrences, review, lexical cache, preparation, meanings, context snapshots, learning cards, reviews, and resumable sessions.
- Initialization is forward-oriented and preserves existing rows while adding supported tables or columns.
- Initialization reads `PRAGMA user_version` first and rejects a version greater than 7 before modifying tables or cache.
- Complete persisted-data rules are in [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md).
- Data Safety v1 internal contracts and schemas are defined, but backup/restore runtime features are paused.

## Known limitations

- Data Safety v1 is not complete. Internal formats exist, but backup, export, restore, and UI are not implemented.
- Cloud synchronization, accounts, analytics, advertising, and payments are not implemented.
- Offline dictionary packages and FSRS scheduling are deferred.
- Online lookup requires explicit consent and network access on cache misses.
- Apple platform targets were deliberately removed.
- Visual acceptance remains manual.

## Active development

The stable master baseline is `28f8a74637b2b81d55ed39411922d2ac1decda75` (PR #15 merged).

- Version identity and versioning governance policy for Beta 9 are merged.
- Wikipedia fallback orchestration is merged.
- Single canonical working directory (`C:\Dev\KnownFirst`) is established in [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md).
- Task-based documentation routing package (`docs/task-based-documentation-routing`) is active.

## Immediate action

- Review and merge `docs/task-based-documentation-routing` pull request.

## Next milestones (Future Work)

1. Post-merge Beta 9 build and AAB release package.
2. Sense-level learning data-model decision package.
3. Resume Backup/Restore Phase 3.
