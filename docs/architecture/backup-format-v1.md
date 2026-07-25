# KnownFirst backup format v1

**Status:** Implemented and shipped in Beta 10 (PR #18) as a **portable recovery** feature — narrower in scope than the original v1 proposal. This document is the current binding contract for what is actually implemented. Any broader restore behavior (merge, overwrite, delete-then-insert `ReplaceAll` into a populated installation) is **not implemented** and is described only in the clearly marked "Future work" section at the end.
**File extension:** `.kfarchive`
**Container:** ZIP with exactly two root entries

This document defines the logical, versioned local backup/recovery format for KnownFirst. It does not make the SQLite file a public format. The source findings and background risks are in [database-audit.md](database-audit.md).

## Goals and non-goals

Version 1, as shipped, is designed to:

- preserve personal imported text, vocabulary identity and decisions, prepared content, contexts, schedules, history, and *completed* resumable workflows;
- be independent of current SQLite table names, integer primary keys, declared SQL types, and schema-addition mechanics;
- detect corruption before any mutation;
- reject unsupported or malicious input within fixed resource limits;
- validate an archive and preview its contents without changing state;
- import strictly additively into an empty installation, refusing any populated target; and
- remain compatible with Android trimming and AOT by using only explicit source-generated `System.Text.Json` metadata (`BackupJsonSerializerContext`).

Version 1, as shipped, does not provide:

- encryption, password protection, signatures, a MAC, or proof of origin;
- cloud synchronization, merging, conflict resolution, or cross-archive stable object identity;
- import into a non-empty installation, in any form (no merge, no overwrite, no `ReplaceAll`);
- an automatic safety backup before any mutation (moot today because import only ever inserts into a verified-empty database; required before any future populated-target restore);
- free-space estimation before mutation;
- a physical SQLite copy;
- transfer of device preferences or online-lookup consent;
- lexical cache transfer;
- diagnostic-log transfer; or
- transfer of any *active/incomplete* review, preparation, or learning workflow (only completed workflows are included — see the Included/Excluded table).

A backup contains original documents and learning history and must be treated as sensitive personal data. A checksum detects accidental corruption, not malicious replacement: an attacker able to change the payload can also change its manifest. Every archive is therefore untrusted input even when its checksum matches. The exported-data privacy notice (unencrypted, contains personal data) is shown to the user in Settings before export.

## Archive layout

The ZIP central directory and decompressed stream must contain exactly these case-sensitive names at the archive root:

```text
manifest.json
data.json
```

No directory entry, duplicate entry, case variant, alternate separator, absolute path, drive prefix, `.` or `..` segment, symbolic-link interpretation, extra field containing application data, or nested archive is accepted. The reader never extracts an entry by path; it opens the two matched entry streams directly. Writers use Deflate compression. Readers accept only Store or Deflate and reject encrypted or unsupported methods.

The exact two-entry rule is part of format v1. A future format that needs more files must increment `formatVersion`.

## Encoding and scalar rules

- Both files are UTF-8 without a byte-order mark.
- JSON property names are case-sensitive.
- Duplicate JSON properties are invalid, including duplicates that differ only in case from a defined property.
- Required properties must be present and non-null.
- Unknown properties in the v1 core model are invalid. Extension data belongs only under the defined `extensions` object.
- Timestamps are RFC 3339/ISO 8601 UTC strings in round-trip form and end in `Z`, for example `2030-01-02T03:04:05.0000000Z`.
- Enum values are lowercase kebab-case strings, never integers.
- Counts and coordinates are non-negative JSON integers and must fit a signed 32-bit value unless a field explicitly states `int64`.
- Text is preserved as JSON string content. Deserialization must reproduce the same .NET string; no trimming, line-ending normalization, Unicode rewriting, or spelling correction is allowed.
- `null` is used only where the v1 model declares an optional value. Empty string is not a substitute for null in the external format.

## `manifest.json`

The manifest is a small, strict envelope. Its required fields are:

| Field | Type and rule |
| --- | --- |
| `formatVersion` | Integer. Exactly `1` for this contract. |
| `sourceAppVersion` | Non-empty application version string. Informational; it does not select the importer. |
| `sourceDatabaseSchemaVersion` | Non-negative integer captured from the source database. Informational after export; compatibility is governed by the archive format. |
| `createdAtUtc` | UTC timestamp at which the logical snapshot was completed. |
| `sourcePlatform` | `windows` or `android` in v1. |
| `recordCounts` | Object containing every fixed count key listed below. |
| `dataChecksum` | `sha256:` followed by 64 lowercase hexadecimal characters for the exact uncompressed bytes of `data.json`. |
| `optionalFeatures` | Sorted array of unique feature identifiers. Empty for the core v1 writer. |

The manifest may additionally contain `requiredFeatures`, a sorted unique array. It is empty for core v1. A reader rejects an unknown required feature. It may ignore an unknown optional feature only when all associated data is isolated under `data.json.extensions` with the same feature identifier; an optional feature cannot alter interpretation of a core field.

The fixed `recordCounts` keys are:

```text
sourceMaterials
sentenceRanges
vocabularyItems
encounteredForms
occurrences
preparedItems
contextSnapshots
legacyReviewSummaries
vocabularyReviewWorkflows
vocabularyReviewItems
preparationWorkflows
preparationItems
learningCards
learningReviews
learningWorkflows
learningQueueItems
```

Each count must exactly match the decoded core payload. Missing, negative, additional, or mismatched count keys are validation failures.

Illustrative structure only—the checksum shown is not for this example:

```json
{
  "formatVersion": 1,
  "sourceAppVersion": "1.0.0-beta.10",
  "sourceDatabaseSchemaVersion": 7,
  "createdAtUtc": "2030-01-02T03:04:05.0000000Z",
  "sourcePlatform": "windows",
  "recordCounts": {
    "sourceMaterials": 0,
    "sentenceRanges": 0,
    "vocabularyItems": 0,
    "encounteredForms": 0,
    "occurrences": 0,
    "preparedItems": 0,
    "contextSnapshots": 0,
    "legacyReviewSummaries": 0,
    "vocabularyReviewWorkflows": 0,
    "vocabularyReviewItems": 0,
    "preparationWorkflows": 0,
    "preparationItems": 0,
    "learningCards": 0,
    "learningReviews": 0,
    "learningWorkflows": 0,
    "learningQueueItems": 0
  },
  "dataChecksum": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
  "optionalFeatures": [],
  "requiredFeatures": []
}
```

## `data.json`

The top-level object is:

```json
{
  "sourceMaterials": [],
  "vocabulary": [],
  "preparedLearning": [],
  "learning": {
    "cards": [],
    "reviewEvents": []
  },
  "workflows": {
    "vocabularyReviews": [],
    "preparationBatches": [],
    "learningSessions": []
  },
  "extensions": {}
}
```

This layout expresses product concepts rather than tables. Collections may be empty but cannot be null.

### Archive-local IDs

Every referenced object has a non-empty opaque string ID unique within its object kind. IDs are assigned during export and have meaning only inside that archive. They do not contain or promise preservation of a SQLite primary key, file path, device identifier, account identifier, or stable cross-archive ID.

At minimum, source materials, sentence ranges, vocabulary items, prepared items, cards, and the three workflow types receive IDs. Nested workflow items and learning queue items also receive IDs because they carry resumable state. Import builds maps from each archive ID to a newly allocated database integer ID. All references must resolve exactly once before any row is inserted.

Vocabulary semantic uniqueness remains `(language, identityKey)`. A duplicate semantic identity is invalid even when its archive IDs differ.

### Source material, vocabulary, prepared learning, cards and review events

These sections retain their originally proposed structure and enum vocabulary; the shipped implementation matches this description. See prior revisions of this document, or the `Models/BackupModels.cs` DTOs and `Services/DataSafety/BackupJsonSerializerContext.cs` registration, for the exact field-level shape. Key invariants that remain binding:

- The content hash is lowercase SHA-256 of the UTF-8 bytes of `originalText` and must match. Sentence and occurrence offsets are .NET UTF-16 indices, and every range must resolve to the exact original substring.
- Core v1 vocabulary enum strings: `knowledgeState` (`unreviewed`, `known`, `unknown-backlog`, `prepared`, `learning`, `mastered`, `ignored`), `tokenKind` (`word`, `acronym`, `abbreviation`, `technical-term`), `preparationState` (`unprepared`, `preparing`, `prepared`, `preparation-failed`), `interactionMode` (`reading`, `typing`).
- There is at most one learning card per `(vocabularyId, direction)`. Ratings are `again`, `hard`, `good`, or `easy`.
- `legacyAnswerText` preserves a legacy value that cannot be reproduced from structured fields; at least one useful answer field is required for a confirmed prepared item.

### Resumable workflows — shipped scope is narrower than originally proposed

The originally proposed format described preserving **active and retained** review, preparation, and learning workflows. **As shipped in Beta 10, only completed workflows are included in the portable export; active/incomplete workflows are deliberately excluded.** An archive containing a workflow with `Active` status is rejected on import with `active-workflow-unsupported`. This avoids exporting or importing half-finished, Undo-dependent state across installations.

- `workflows.vocabularyReviews`, `workflows.preparationBatches`, and `workflows.learningSessions` therefore only ever contain sessions whose status is a completed/terminal state in a shipped portable archive.
- Queue order remains unique within a learning session; a repeated `cardId` is allowed only for one marked-Again repeat, consistent with the in-app scheduling contract.

## Included and excluded data (as shipped)

| Included in shipped v1 portable export | Excluded from shipped v1 portable export |
| --- | --- |
| Original retained documents and exact metadata | Physical `knownfirst.db3`, WAL, SHM, or SQLite sequence state |
| Sentence coordinates and every retained occurrence | Lexical cache entries and provider-cache JSON |
| Vocabulary identity, knowledge/preparation state, forms, and automatic-learning state | UI language, theme, preparation limit, card-direction preference, and learning-mode preference |
| Legacy review aggregates | Online lookup consent; import never grants consent |
| Confirmed meanings, aliases, notes, attribution, and context snapshots | Application and lexical diagnostic logs |
| Card schedules and review history | DEBUG timing, artificial clock, navigation history, transient UI state |
| **Completed** review, preparation, and learning workflows | **Active/incomplete** review, preparation, and learning workflows (rejected on import, `active-workflow-unsupported`) |
| — | Secrets, signing material, package/build identity, binaries, APK/AAB files |
| — | Cloud/account/sync data, because none exists |

On a successful import, all archive data is inserted into the target's empty tables; nothing is deleted, overwritten, or merged, and the local lexical cache is untouched (it is already necessarily empty on a fresh installation targeted by this feature). Excluded Preferences and log files are left unchanged.

## Resource and parser limits

The v1 reader applies all of these hard limits before committing any data:

| Limit | v1 maximum |
| --- | ---: |
| Archive file size | 128 MiB |
| `manifest.json` uncompressed | 256 KiB |
| `data.json` uncompressed | 256 MiB |
| Total uncompressed entry bytes | 256 MiB plus manifest |
| Compression ratio | 100:1 per entry and overall once an entry exceeds 1 MiB |
| ZIP entries | Exactly 2 |
| JSON nesting depth | 64 |
| One document or context string | 16 MiB UTF-8 |
| Any other string | 1 MiB UTF-8 |
| Source materials | 10,000 |
| Vocabulary items | 250,000 |
| Occurrences | 1,000,000 |
| Sum of all other counted records | 1,000,000 |

The first exceeded limit terminates validation with a stable error code. ZIP header sizes and manifest counts are hints, not trusted facts: the reader counts actual decompressed bytes and actual decoded records. It streams the checksum and validation input into an app-private bounded temporary file rather than trusting a single large allocation.

These values are security limits, not product-size promises. A future increase can remain a reader capability change if it does not alter the logical format.

## Checksum and deterministic writing

The writer serializes `data.json` first using UTF-8 without BOM, computes SHA-256 over those exact uncompressed bytes, and only then serializes the manifest. `dataChecksum` uses lowercase hexadecimal. The reader:

1. checks archive layout and byte limits;
2. parses and validates the bounded manifest;
3. streams `data.json` while enforcing its byte and compression-ratio limits;
4. computes SHA-256 over that exact stream;
5. compares the full checksum in constant time; and
6. only after a match, parses and validates the domain graph.

No JSON reserialization or canonicalization participates in checksum verification.

Export itself is failure-safe: write to a new temporary file, close and reopen it through the same strict validator, then atomically move/replace it at the chosen destination where the platform permits. An existing destination is never overwritten without explicit user authorization through the native Save picker.

## Compatibility behavior

- A v1 reader accepts only `formatVersion = 1`. Zero, negative, malformed, or higher versions are rejected before opening an import transaction.
- `sourceAppVersion` and `sourceDatabaseSchemaVersion` are diagnostic context, not reasons to interpret table layouts.
- An unknown required feature is rejected. An unknown optional feature is ignored only under the isolated extension rule.
- An unknown enum string, missing required field, duplicate ID, missing reference, duplicate semantic key, invalid range, impossible count, invalid timestamp, non-finite numeric value, or inconsistent workflow is rejected.
- The writer refuses to export a database whose schema is newer than the running application or whose graph fails current invariants.

## Validation and preview

Validation is read-only and completes before any mutation. Its order is:

1. archive length and central-directory layout;
2. exact entry names, count, methods, declared lengths, and ratio bounds;
3. strict manifest JSON and supported version/features;
4. streamed `data.json` byte limit and checksum;
5. strict source-generated JSON decoding with duplicate-property detection;
6. string, date, enum, numeric, and record-count limits;
7. ID uniqueness and complete relationship resolution;
8. document, sentence, occurrence, and context coordinate invariants;
9. vocabulary semantic uniqueness, form/count, card, schedule, and workflow invariants (including the active-workflow rejection above); and
10. an immutable preview summary.

Validation errors identify a stable code and safe path such as `vocabulary[3].language`, not private text.

## Current binding Beta 10 recovery contract (import)

This is the only import behavior currently implemented and shipped. It is intentionally **not** the general-purpose `ReplaceAll` design originally proposed for v1 (see "Future work" below).

1. The target installation is checked for any durable user data across all in-scope tables. If any row exists in any of them, import is refused with `target-not-empty` and **no mutation occurs**.
2. If the target is confirmed empty, the archive is revalidated end-to-end (steps above) inside the same database transaction gate.
3. New integer IDs are allocated and archive data is inserted only — source materials/vocabulary first, then detail, prepared content, cards/history, and workflow state, in dependency order.
4. The insert runs inside one SQLite transaction. Any exception, cancellation, or post-insert invariant mismatch causes a full rollback; only after a successful commit does the operation report success.
5. Preferences, online-lookup consent, lexical cache, and diagnostic logs are left untouched (and are already empty/absent, since the target is a fresh installation).
6. There is no delete step, no merge step, no overwrite step, and no safety-backup step, because the operation never touches an installation that already has data.

## Error contract

Stable machine-readable codes, localized at the UI boundary, currently thrown by the implementation:

```text
archive-layout-invalid
archive-too-large
archive-compression-limit
unsupported-format
unsupported-required-feature
manifest-invalid
checksum-mismatch
data-json-invalid
unknown-enum
duplicate-id
missing-reference
invariant-violation
record-count-mismatch
restore-failed
target-not-empty
active-workflow-unsupported
operation-cancelled
io-failure
```

Two additional codes (`insufficient-space`, `safety-backup-failed`) are declared in the codebase but are **not currently thrown by any code path**, because free-space estimation and automatic safety backups are not implemented (see "Future work"). They are reserved identifiers for that future design, not evidence that the behavior exists today.

Errors never include original document text, meanings, aliases, or context.

## AOT and trimming contract

All manifest, data, nested DTO, enum, collection, preview, and error payload types are registered in `Services/DataSafety/BackupJsonSerializerContext.cs`. Every production serialize/deserialize call passes its generated `JsonTypeInfo` explicitly; no reflection-based fallback resolver is installed, and the test project's `JsonSerializerIsReflectionEnabledByDefault=false` constraint is enforced by dedicated tests (`KnownFirst.Tests/BackupJsonContractTests.cs`).

## Future work (non-binding — requires a separate approved plan)

The following were part of the original v1 proposal and remain reasonable future directions, but are **not implemented today** and must not be described as current behavior anywhere else in the documentation set:

- **Merge or overwrite import into a populated installation.** Any such feature is a distinct, separately planned capability, not an extension of the current empty-install-only contract.
- **`ReplaceAll` restore** (delete all in-scope rows, then insert archive data, in one transaction) against a database that already has data.
- **Automatic safety backup** created before any destructive mutation, validated with the normal reader before proceeding.
- **Free-space estimation** before mutation (bounded input size, one safety backup, SQLite journal growth, plus margin), stopping before mutation if space cannot be established or is insufficient.
- **Active/incomplete workflow transfer** across installations (currently deliberately excluded and rejected on import).
- **Preference and consent transfer**, which would require either moving those settings into a transactional store or a durable recovery journal with tested compensation.

Any decision to build these must update this document, be recorded in an ADR, and update [DATABASE_CONTRACT.md](../DATABASE_CONTRACT.md) and [PROJECT_STATE.md](../PROJECT_STATE.md) once implemented and verified — consistent with [ROADMAP.md](../ROADMAP.md)'s "Future recovery evolution" milestone, which is explicitly deferred until a dedicated data-model and safety plan is approved.
