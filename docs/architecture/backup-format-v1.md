# KnownFirst backup format v1

**Status:** Proposed Data Safety v1 contract; implementation has not begun
**File extension:** `.knownfirst-backup`
**Container:** ZIP with exactly two root entries

This document defines a logical, versioned local backup format for KnownFirst.
It does not make the SQLite file a public format and does not claim that the
application can currently create or restore a backup. The source findings and
current risks are in [database-audit.md](database-audit.md).

## Goals and non-goals

Version 1 is designed to:

- preserve personal imported text, vocabulary identity and decisions,
  prepared content, contexts, schedules, history, and resumable workflows;
- be independent of current SQLite table names, integer primary keys, declared
  SQL types, and schema-addition mechanics;
- detect corruption before any mutation;
- reject unsupported or malicious input within fixed resource limits;
- preview the effect of a restore without changing state;
- replace all data in the documented backup scope atomically; and
- remain compatible with Android trimming and AOT by using only explicit
  source-generated `System.Text.Json` metadata.

Version 1 does not provide:

- encryption, password protection, signatures, a MAC, or proof of origin;
- cloud synchronization, merging, conflict resolution, or cross-archive stable
  object identity;
- a physical SQLite copy;
- transfer of device preferences or online-lookup consent;
- lexical cache transfer; or
- diagnostic-log transfer.

A backup contains original documents and learning history and must be treated
as sensitive personal data. A checksum detects accidental corruption, not
malicious replacement: an attacker able to change the payload can also change
its manifest. Every archive is therefore untrusted input even when its checksum
matches.

## Archive layout

The ZIP central directory and decompressed stream must contain exactly these
case-sensitive names at the archive root:

```text
manifest.json
data.json
```

No directory entry, duplicate entry, case variant, alternate separator,
absolute path, drive prefix, `.` or `..` segment, symbolic-link interpretation,
extra field containing application data, or nested archive is accepted. The
reader never extracts an entry by path; it opens the two matched entry streams
directly. Writers use Deflate compression. Readers accept only Store or Deflate
and reject encrypted or unsupported methods.

The exact two-entry rule is part of format v1. A future format that needs more
files must increment `formatVersion`.

## Encoding and scalar rules

- Both files are UTF-8 without a byte-order mark.
- JSON property names are case-sensitive.
- Duplicate JSON properties are invalid, including duplicates that differ only
  in case from a defined property.
- Required properties must be present and non-null.
- Unknown properties in the v1 core model are invalid. Extension data belongs
  only under the defined `extensions` object.
- Timestamps are RFC 3339/ISO 8601 UTC strings in round-trip form and end in
  `Z`, for example `2030-01-02T03:04:05.0000000Z`.
- Enum values are lowercase kebab-case strings, never integers.
- Counts and coordinates are non-negative JSON integers and must fit a signed
  32-bit value unless a field explicitly states `int64`.
- Text is preserved as JSON string content. Deserialization must reproduce the
  same .NET string; no trimming, line-ending normalization, Unicode rewriting,
  or spelling correction is allowed.
- `null` is used only where the v1 model declares an optional value. Empty
  string is not a substitute for null in the external format.

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

The manifest may additionally contain `requiredFeatures`, a sorted unique
array. It is empty for core v1. A reader rejects an unknown required feature.
It may ignore an unknown optional feature only when all associated data is
isolated under `data.json.extensions` with the same feature identifier; an
optional feature cannot alter interpretation of a core field.

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

Each count must exactly match the decoded core payload. Missing, negative,
additional, or mismatched count keys are validation failures.

Illustrative structure only—the checksum shown is not for this example:

```json
{
  "formatVersion": 1,
  "sourceAppVersion": "1.0.0-beta.8",
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

This layout expresses product concepts rather than tables. Collections may be
empty but cannot be null.

### Archive-local IDs

Every referenced object has a non-empty opaque string ID unique within its
object kind. IDs are assigned during export and have meaning only inside that
archive. They do not contain or promise preservation of a SQLite primary key,
file path, device identifier, account identifier, or stable cross-archive ID.

At minimum, source materials, sentence ranges, vocabulary items, prepared
items, cards, and the three workflow types receive IDs. Nested workflow items
and learning queue items also receive IDs because they carry resumable state.
Restore builds maps from each archive ID to a newly allocated database integer
ID. All references must resolve exactly once before the first delete.

Vocabulary semantic uniqueness remains `(language, identityKey)`. A duplicate
semantic identity is invalid even when its archive IDs differ.

### Source material

Each `sourceMaterials` item contains:

- `id`, `title`, `textLanguage`, and `explanationLanguage`;
- `lookupMode` and optional `targetLanguage`;
- `originalText` and `contentSha256`;
- `importedAtUtc` and `storedWordCount`;
- `sentences`, each with `id`, `order`, `start`, and `length`; and
- `occurrences`, each with `vocabularyId`, `sentenceId`, absolute `start`,
  `length`, exact `surfaceForm`, `order`, `technicalFamily`, optional
  `technicalInstanceYear`, optional `technicalInstanceIdentifier`, and optional
  `technicalVariant`.

The content hash is lowercase SHA-256 of the UTF-8 bytes of `originalText` and
must match. Sentence and occurrence offsets are .NET UTF-16 indices. Every
sentence and occurrence range must lie inside the original string; an
occurrence must lie inside its referenced sentence; and its substring must
equal `surfaceForm` ordinally. Sentence order is unique per source material.

### Vocabulary

Each `vocabulary` item contains:

- `id`, `language`, `canonicalTerm`, `identityKey`, and `tokenKind`;
- `knowledgeState` and `preparationState`;
- stored `totalOccurrenceCount` and `documentCount`;
- `createdAtUtc` and `updatedAtUtc`;
- `encounteredForms`, each with exact `surfaceForm` and `occurrenceCount`;
- `automaticLearning` with `interactionMode`, consecutive recall successes,
  consecutive typing successes/failures, and the mastery-extension flag; and
- `legacyReviewSummaries`, each with its four counters and optional
  `lastReviewedAtUtc`.

The stored counts must equal the accepted detail under current lifecycle rules.
Outside an active review/Undo dependency, Known and ignored minimal markers
normally have no occurrences, forms, prepared items, cards, or personal review
history. A backup taken during active review may legitimately retain temporary
forms/occurrences and previous-state data needed for Undo; those rows must be
preserved and are valid only while the matching active workflow references
them. Any legacy review summary is also preserved rather than silently
dropped. The format preserves `mastered` separately from `known` and does not
infer permanent knowledge.

Core v1 vocabulary enum strings are:

- `knowledgeState`: `unreviewed`, `known`, `unknown-backlog`, `prepared`,
  `learning`, `mastered`, `ignored`;
- `tokenKind`: `word`, `acronym`, `abbreviation`, `technical-term`;
- `preparationState`: `unprepared`, `preparing`, `prepared`,
  `preparation-failed`; and
- `interactionMode`: `reading`, `typing`.

### Prepared learning

Each `preparedLearning` item contains:

- `id` and `vocabularyId`;
- source/explanation languages, display term, optional encountered form, optional
  grammatical relationship, and token kind;
- optional provider meaning identifier, acronym expansion, translation,
  definition, dictionary example, additional note, and `legacyAnswerText`;
- accepted aliases as an array of strings;
- `confirmedByUser`;
- source metadata with provider name, source project, page title, optional
  revision ID, and attribution;
- `createdAtUtc`, `updatedAtUtc`, and `preparedAtUtc`; and
- `contexts`, each with `sourceMaterialId`, source title, exact text,
  `targetStart`, `targetLength`, normalized comparison fingerprint, and
  `createdAtUtc`.

`legacyAnswerText` preserves a legacy `TranslationOrDefinition` value that
cannot be reproduced from the structured fields. It is not displayed in
preference to valid structured content. At least one useful answer field is
required for a confirmed item. Context fingerprints are unique per prepared
item, a target range lies inside its exact context, and the target text remains
compatible with the linked vocabulary/encountered form.

### Cards and review events

Each `learning.cards` item contains:

- `id`, `vocabularyId`, and `preparedItemId`;
- `direction` (`term-to-meaning` or `meaning-to-term`);
- `state` (`new`, `learning`, `review`, `relearning`, `suspended`, or
  `retired`);
- `dueAtUtc`, interval days, ease factor, successful-review count, and lapse
  count;
- optional `lastReviewedAtUtc` and optional `lastRating`; and
- `createdAtUtc` and `updatedAtUtc`.

There is at most one card per `(vocabularyId, direction)`. Ratings are `again`,
`hard`, `good`, or `easy`.

Each `learning.reviewEvents` item contains `cardId`, `learningSessionId`,
rating, typed/correct flags, `reviewedAtUtc`, the resulting `dueAtUtc`, interval
days, and ease factor. Review events and current card schedules are both
authoritative historical state; restore does not replay events to recalculate a
schedule.

### Resumable workflows

`workflows.vocabularyReviews` preserves document review and Undo:

- session ID, `sourceMaterialId`, status, all summary counts,
  `decisionSequence`, start time, and optional completion time;
- ordered items with ID, `vocabularyId`, status, previous knowledge state,
  previous aggregate counts/time, decision sequence, created-for-session flag,
  and optional decision time.

`workflows.preparationBatches` preserves batch continuity:

- session ID, status, method, total/completed counts, start/update time, and
  optional completion time;
- ordered items with ID, `vocabularyId`, status, selected meaning index, last
  error code, attempt count, update time, and optional structured
  `lookupDraft`.

A lookup draft contains the semantic `LexicalResult` fields required to resume:
lookup status/mode/languages/terms, token kind, optional acronym and form
relationship, candidate meanings and labels, provider/source attribution,
lookup time, redirect depth, and explicit form relations (`kind`, base lemma,
relationship). It excludes cache identity, cache-hit state, provider request
diagnostics, and exception diagnostics. Its enum strings are validated just as
strictly as core fields.

`workflows.learningSessions` preserves the exact queue:

- session ID, status, total/completed and rating counts, start/update time, and
  optional completion time;
- queue items with ID, `cardId`, queue order, due/repeat flags,
  answer-revealed/spelling-checked/spelling-correct/completed flags, optional
  rating, and optional completion time.

Summary counts must agree with item/event detail. At most one session of each
workflow kind may be active. Queue order is unique within a learning session;
repeated `cardId` is allowed only for one marked Again repeat.

## Included and excluded data

| Included in core v1 | Excluded from core v1 |
| --- | --- |
| Original retained documents and exact metadata | Physical `knownfirst.db3`, WAL, SHM, or SQLite sequence state |
| Sentence coordinates and every retained occurrence | Lexical cache entries and provider-cache JSON |
| Vocabulary identity, knowledge/preparation state, forms, and automatic-learning state | UI language, theme, preparation limit, card-direction preference, and learning-mode preference |
| Legacy review aggregates | Online lookup consent; restore never grants consent |
| Confirmed meanings, aliases, notes, attribution, and context snapshots | Application and lexical diagnostic logs |
| Card schedules and review history | DEBUG timing, artificial clock, navigation history, transient UI state |
| Active and retained review, preparation, and learning workflows | Secrets, signing material, package/build identity, binaries, APK/AAB files |
| Resumable preparation lookup draft without diagnostics | Cloud/account/sync data, because none exists |

On successful `ReplaceAll`, all included data is replaced and the local lexical
cache is cleared in the same SQLite transaction. Excluded Preferences and log
files are left unchanged. Clearing cache does not remove confirmed prepared
content or personal history. A cache miss after restore follows the existing
consent and network rules of the target device.

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

The first exceeded limit terminates validation with a stable error code. ZIP
header sizes and manifest counts are hints, not trusted facts: the reader
counts actual decompressed bytes and actual decoded records. It streams the
checksum and validation input into an app-private bounded temporary file rather
than trusting a single large allocation. Temporary files are removed on normal
completion and best-effort on failure; they are never placed beside or over a
user-selected source archive.

These values are security limits, not product-size promises. Implementation
must centralize them, test every boundary, and present a clear size-limit error.
A future increase can remain a reader capability change if it does not alter
the logical format.

## Checksum and deterministic writing

The writer serializes `data.json` first using UTF-8 without BOM, computes
SHA-256 over those exact uncompressed bytes, and only then serializes the
manifest. `dataChecksum` uses lowercase hexadecimal. The reader:

1. checks archive layout and byte limits;
2. parses and validates the bounded manifest;
3. streams `data.json` while enforcing its byte and compression-ratio limits;
4. computes SHA-256 over that exact stream;
5. compares the full checksum in constant time; and
6. only after a match, parses and validates the domain graph.

No JSON reserialization or canonicalization participates in checksum
verification. Writers use a stable property and collection order where
possible, but archive equality is not a compatibility guarantee.

Backup creation itself is failure-safe: write to a new temporary file, close
and reopen it through the same strict validator, then atomically move/replace it
at the chosen destination where the platform permits. An existing destination
is never overwritten without explicit authorization.

## Compatibility behavior

- A v1 reader accepts only `formatVersion = 1`.
- Zero, negative, malformed, or higher versions are rejected before opening a
  restore transaction. There is no best-effort import of a future format.
- `sourceAppVersion` and `sourceDatabaseSchemaVersion` are diagnostic context,
  not reasons to interpret table layouts. A valid format-1 archive remains
  importable after the internal database schema changes, through an explicit
  mapper.
- An unknown required feature is rejected. An unknown optional feature is
  ignored only under the isolated extension rule.
- An unknown enum string, missing required field, duplicate ID, missing
  reference, duplicate semantic key, invalid range, impossible count, invalid
  timestamp, non-finite numeric value, or inconsistent workflow is rejected.
- Deprecated values require an explicit format-level migration function and
  tests. They are never silently mapped to an enum default.
- The writer refuses to export a database whose schema is newer than the
  running application or whose graph fails current invariants.

## Validation and preview

Validation is read-only and completes before preview is marked valid. Its order
is:

1. archive length and central-directory layout;
2. exact entry names, count, methods, declared lengths, and ratio bounds;
3. strict manifest JSON and supported version/features;
4. streamed `data.json` byte limit and checksum;
5. strict source-generated JSON decoding with duplicate-property detection;
6. string, date, enum, numeric, and record-count limits;
7. ID uniqueness and complete relationship resolution;
8. document, sentence, occurrence, and context coordinate invariants;
9. vocabulary semantic uniqueness, form/count, card, schedule, and workflow
   invariants; and
10. an immutable preview summary.

The preview reports source version/schema/platform/date, all record counts,
supported optional features, checksum status, warnings that do not alter data,
and the exact ReplaceAll scope. It does not persist imported content, create a
safety backup, clear cache, or change Preferences. Validation errors identify a
stable code and safe path such as `vocabulary[3].language`, not private text.

## `ReplaceAll` restore contract

`ReplaceAll` is the only v1 conflict policy. It does not merge with existing
personal learning data and does not preserve target-side rows inside the
included scope.

The required sequence is:

1. Revalidate the archive and materialize an immutable import model. Do not
   reuse an old preview as authority.
2. Estimate free space for the bounded input, one logical safety backup,
   SQLite journal growth, and a fixed 32 MiB margin. If available space cannot
   be established or is insufficient, stop before mutation.
3. Under the database gate, create a logical `.knownfirst-backup` safety backup
   of current in-scope data in an app-private recovery location. Validate that
   archive with the normal reader before proceeding.
4. Begin one SQLite transaction. Do not call `ResetAsync` and do not replace a
   live database file.
5. Delete all in-scope child rows before parents and clear `LexicalCache` inside
   the same transaction.
6. Allocate new integer IDs and insert source materials/vocabulary first, then
   detail, prepared content, cards/history, and workflow state in dependency
   order.
7. Re-run relationship, coordinate, uniqueness, aggregate, and workflow checks
   against the transaction's new rows.
8. Commit once. Only after the commit may the operation report success.
9. Leave Preferences and logs untouched. Retain the safety backup until the
   caller explicitly acknowledges successful restore; apply a documented
   bounded retention policy later.

Any exception, cancellation, validation failure, or post-insert mismatch before
commit escapes the transaction delegate and causes SQLite rollback. A process
termination during the transaction relies on SQLite journal recovery; the
validated safety backup remains defense in depth. If the commit result is
uncertain, the next startup must not guess success: it should run an integrity
check and offer recovery from the safety backup.

Because v1 excludes Preferences, there is no cross-store partial commit. Future
preference restore requires either moving those settings into a transactional
store or introducing a durable recovery journal and tested compensation; it
must not quietly extend v1 ReplaceAll semantics.

## Error contract

Implementation should expose stable machine-readable codes, localized at the
UI boundary. At minimum:

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
insufficient-space
safety-backup-failed
restore-failed
operation-cancelled
io-failure
```

Errors never include original document text, meanings, aliases, or context.
Cancellation is honored during file work and validation. Once the short final
database mutation begins, cancellation is deferred until the transaction has
committed or rolled back so it cannot create a partial result.

## AOT and trimming contract

All manifest, data, nested DTO, enum, collection, preview, and error payload
types must be registered in a dedicated `BackupJsonSerializerContext`. Every
production serialize/deserialize call passes its generated `JsonTypeInfo`
explicitly. The implementation must not:

- call an overload that relies on runtime type discovery;
- install a reflection fallback resolver;
- loosen the test project's
  `JsonSerializerIsReflectionEnabledByDefault=false`; or
- reuse internal lexical JSON as the public backup representation.

Low-level `Utf8JsonReader` scanning for duplicate properties, depth, and byte
limits is allowed because it does not use reflection. The decoded domain DTOs
still use source-generated metadata. A reflection-disabled round trip of the
maximum type graph is a release gate; Android Release/AOT validation is a later
implementation activity and is not evidence from this documentation task.

## Acceptance criteria for the format

The format contract is ready for implementation only when reviewers accept:

- the exact two-entry layout and v1 manifest fields;
- logical data structure and archive-local ID policy;
- the inclusion of personal SQLite state and exclusion of Preferences, consent,
  cache, and logs;
- strict unknown-version, feature, enum, and relationship behavior;
- byte, ratio, depth, string, and record-count limits;
- checksum-without-authenticity limitation;
- pre-validation, preview, safety backup, and single-transaction ReplaceAll;
  and
- the source-generated JSON requirement.

Any durable change to these decisions before implementation must update this
document and, once accepted as architecture, be recorded in an ADR and the
[database contract](../DATABASE_CONTRACT.md).
