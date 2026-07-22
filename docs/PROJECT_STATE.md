# KnownFirst project state

**Status date:** 2026-07-22
**State source:** `origin/master` after pull request 1
**Next product milestone:** Data Safety v1

This document is the authoritative snapshot of verified current state. Update
it when a milestone is completed or when a release, schema, supported platform,
or confirmed limitation changes. Plans belong in [ROADMAP.md](ROADMAP.md).

## Stable release

| Field | Verified value |
| --- | --- |
| Project | KnownFirst |
| Version | `1.0.0-beta.8` |
| Version code | `8` |
| Package ID | `com.tachiguro.knownfirst` |
| Release commit | `29aff385f2c3cabca49b70bd011bf4c09808df6d` |
| Master merge commit | `956e71895cf141805c8c24f7d32691075d439730` |
| Annotated release tag | `v1.0.0-beta.8` |
| Tag target | `29aff385f2c3cabca49b70bd011bf4c09808df6d` |
| Distribution | Google Play Internal Testing |

The release commit and merge commit have the same Git tree
(`64c97c4768f240099e25bffcca2e570310b2fdcc`). The tag intentionally identifies
the exact source commit used for the tested AAB rather than the later merge
commit.

## Supported platforms

- **Android:** released through Google Play Internal Testing; minimum Android
  version is API 24.
- **Windows:** primary local development and automated/manual verification
  platform.

The project file also evaluates iOS and Mac Catalyst targets, but those targets
are not currently claimed as supported or release-validated platforms.

## Production capabilities

The current product implements:

- English and German UI localization with persisted System, Light, and Dark
  appearance modes;
- exact text import with deterministic Unicode-aware sentence and vocabulary
  analysis;
- resumable Known/Unknown vocabulary review with persisted decisions and Undo;
- language-scoped vocabulary identity and global minimal known-word markers;
- frequency-prioritized automatic or manual preparation;
- explicit online-lookup consent, read-only Wiktionary lookup, and a local
  SQLite lexical cache;
- source attribution, alternative-meaning selection, manual correction, and
  context snapshots;
- recognition and spelling card directions with independent deterministic
  schedules;
- resumable learning sessions and permanent-known cleanup;
- transactional local persistence, startup maintenance, and bounded structured
  diagnostics;
- responsive Windows and Android layouts with localized workflow gating.

## Confirmed verification

### Automated

On 2026-07-22, the following command completed against master merge commit
`956e71895cf141805c8c24f7d32691075d439730`:

```powershell
dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj -c Debug --nologo
```

Result: **389 passed, 0 failed, 0 skipped**. The run used the `net10.0` test
project and did not build an Android APK or AAB. Existing nullable and MSTest
analyzer warnings remain; they did not fail the run.

Automated coverage includes Core policies, text analysis, temporary-SQLite
persistence and migration, review/preparation/learning workflows, localization,
diagnostics, build identity, UI contracts, and offline Wiktionary fixtures.
Automated tests do not use live Wikimedia requests.

### Release evidence

The verified release handoff records that:

- a signed Beta 8 AAB was built from `29aff385f2c3cabca49b70bd011bf4c09808df6d`;
- the AAB was accepted in Google Play Internal Testing;
- a physical Android run covered startup, automatic online lookup, learning,
  reset, restart, and workflow continuation; and
- the two Android Release/AOT crashes fixed by `f1f1891` and `29aff385` did
  not recur in the accepted Beta 8 flow.

The repository does not contain the device model, Android version, AAB
checksum, screenshots, or a completed full GUI matrix. Do not infer those
details.

## Database status

- Storage is local SQLite in the application data directory.
- Current `PRAGMA user_version` is **7**.
- The schema creates 17 application tables for documents, vocabulary,
  occurrences, review, lexical cache, preparation, meanings, context
  snapshots, learning cards, reviews, and resumable sessions.
- Initialization is forward-oriented and preserves existing rows while adding
  supported tables or columns.
- One automated migration regression starts with an older `Words` table,
  verifies the existing row is preserved, and verifies defaults for newer
  learning fields.
- Legacy lexical-cache keys outside the current `v2|` key format are
  invalidated; this affects reference cache data, not personal learning state.
- Beta 8 introduced no schema change relative to Beta 7.

The complete persisted-data rules are in
[DATABASE_CONTRACT.md](DATABASE_CONTRACT.md). No versioned backup, restore, or
export format exists yet.

## Known limitations

- Data Safety v1, versioned backup, restore, and a backup/restore UI are not
  implemented.
- Cloud synchronization, accounts, analytics, advertising, and payments are
  not implemented.
- Offline dictionary packages and FSRS are deferred.
- Online lookup needs explicit consent and network access on cache misses.
- iOS and Mac Catalyst are not release-validated.
- The direct-install Android test-package script contains legacy Beta 6
  filenames and installation metadata and is not a valid Beta 8 release path.
- Complete visual acceptance remains manual; the checked-in matrices are test
  definitions, not completed evidence.

## Last completed sprint

**Beta 8 Android Release/AOT stabilization**

- `f1f1891eaf42ad9e7610afc2e9f796771fed27e7` replaced reflection-dependent
  JSON persistence paths with source-generated metadata and hardened provider
  failure handling.
- `29aff385f2c3cabca49b70bd011bf4c09808df6d` removed the AOT-unsafe parser
  selector path and added parser regression coverage.
- Pull request 1 merged the release source into `master` as `956e718`.

See the [release handoff](handoffs/2026-07-22-beta-8-release.md).

## Active development

- `feature/project-governance-and-docs`: active documentation-governance
  work; no product behavior changes.
- `hotfix/beta-8-online-lookup-crash`: branch tip is merged, but its attached
  worktree contains protected uncommitted parser/test work and must not be
  cleaned or removed.
- `hotfix/beta-8-parser-aot-fix-clean`: clean release-source worktree retained
  pending later owner-approved cleanup.

See the dated
[branch and worktree inventory](maintenance/branch-and-worktree-inventory.md)
for the complete snapshot.

## Next milestone

Data Safety v1 is next. It must begin with explicit requirements and a
database/migration audit before any backup format or restore behavior is
implemented. This documentation branch does not implement backup or restore.
