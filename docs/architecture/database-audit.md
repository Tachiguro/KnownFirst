# KnownFirst database and persistence audit

**Status:** Data Safety v1 analysis; no backup or restore implementation exists
**Source baseline:** `origin/master` at
`30558b04e73fefadf22c4b9a61f49b1f14c4503d`
**Audit date:** 2026-07-22

This is the source-only audit required before a backup format or restore path
is implemented. It follows the
[database contract](../DATABASE_CONTRACT.md), the
[architecture](../KNOWNFIRST_ARCHITECTURE.md), and the accepted ADRs in
[`docs/decisions`](../decisions/README.md). No application database was opened,
copied, migrated, or changed while producing this report. No build or test was
run. Findings come from entities, schema initialization, services, tests,
documentation, and Git history.

## Executive result

The current personal data is logically exportable, but the physical SQLite
file is not a safe or portable backup contract. The object graph can be
reconstructed if an exporter assigns archive-local IDs, validates every
relationship and coordinate, and a restorer maps those IDs to newly allocated
database IDs in one transaction.

Implementation must not begin with a raw database copy. The main blockers are:

- schema version 7 is a marker, not a sequence of explicit migrations;
- initialization neither rejects a database from a newer application nor
  updates all schema objects atomically;
- relationships are integer conventions with no SQLite foreign keys or
  cascade rules;
- most columns have no database-level nullability constraint;
- current integer primary keys are internal and cannot be portable backup IDs;
- preferences and logs live outside SQLite and cannot participate in its
  transaction; and
- no restore coordinator, archive validation boundary, safety backup, or
  backup-specific source-generated JSON context exists.

The proposed v1 boundary therefore backs up the personal SQLite domain model,
excludes device preferences, consent, reference cache, and diagnostics, and
defines `ReplaceAll` over that explicit scope. The format and recovery contract
are in [backup-format-v1.md](backup-format-v1.md).

## Storage and initialization

`KnownFirstDatabase` resolves `knownfirst.db3` under the platform application
data directory. It opens the file with read/write, create, and shared-cache
flags. One `SemaphoreSlim` serializes all operations made through that database
instance. `ReadAsync` holds the gate for the complete callback;
`RunInTransactionAsync` holds it for a sqlite-net transaction.

Initialization calls `CreateTableAsync<T>` for 17 entity types, deletes lexical
cache rows whose key does not start with `v2|`, and then executes
`PRAGMA user_version = 7`. It does not:

- read or branch on the previous `user_version`;
- reject `user_version > 7`;
- run named, ordered old-to-new migration steps;
- wrap the complete schema update, cache invalidation, and version write in one
  transaction;
- enable `PRAGMA foreign_keys`;
- run `foreign_key_check`, `integrity_check`, or equivalent application-level
  whole-database validation; or
- retain an on-disk schema manifest against which columns, declared types,
  indexes, and constraints can be compared.

`ResetAsync` is the only connection-lifecycle operation. It closes the
connection, deletes the database file, recreates schema 7, and is intended only
for the explicit reset workflow. It is not a restore primitive.

## Schema history and migration coverage

Repository history exposes these version changes:

| Version | Source change | Effective transition |
| ---: | --- | --- |
| 3 | Initial checked-in `DatabaseSchema` | Created documents, vocabulary, forms, sentence spans, occurrences, meanings, legacy review aggregates, review sessions, and review candidates. No version 1 or 2 migration source is present in this repository history. |
| 4 | Preparation and learning MVP | Added lexical cache, preparation sessions/items, context snapshots, learning cards/reviews/sessions/queues, expanded meanings, and added vocabulary preparation state. |
| 5 | Form-relation and technical-token support | Added encountered form and grammatical relationship to prepared meanings and technical-family fields to occurrences. |
| 6 | Explicit lexical language/mode support | Added document lookup mode/target language and cache request fields; began deleting cache keys outside `v2|`. |
| 7 | Configurable automatic learning | Added interaction mode, recall/typing counters, and mastery-extension state to vocabulary. |

Every transition is currently performed by the same forward `CreateTableAsync`
pass. A property addition can be added by sqlite-net, but a property or table
rename has no explicit data-copy step: the old data can remain in an obsolete
column or table while the application reads a new default-valued one. A type or
constraint change likewise has no declared transformation. Cache invalidation
is executed on every initialization rather than only on a recorded 5-to-6
transition.

Two source fixtures exercise parts of this behavior with temporary databases:

1. `DatabaseMigrationTests` creates a minimal old `Words` table, preserves one
   row, and observes default values for two newer automatic-learning fields.
2. `TextReviewServiceTests` creates an older document, meaning, and occurrence,
   plus an unrelated `AppSettings` table. It verifies preservation, defaults
   for the v5 fields, preservation of the unrelated table, and `user_version`
   7.

Neither fixture identifies its source as a production schema version. Together
they do not cover exact schemas 3, 4, 5, and 6; all tables and indexes; SQL
nullability; type conversions; rename behavior; interrupted initialization;
rollback; cache invalidation boundaries; corrupt relationships; or a database
from a future application version.

## Type and constraint interpretation

The inventory below records CLR types because the repository contains entity
definitions, not canonical SQLite DDL. sqlite-net 1.11.285 generates the actual
declared SQL types. In general, integers, Boolean values, and enums use SQLite
integer affinity; `double` uses real affinity; strings use text affinity; and
`DateTime` storage is controlled by sqlite-net. That mapping is an
implementation dependency, not yet a tested KnownFirst schema contract.

All primary keys are `int` with `[PrimaryKey, AutoIncrement]`. Apart from those
keys, no entity property has `[NotNull]`, `[MaxLength]`, a check constraint, a
foreign-key constraint, or a database default. A non-nullable C# property or a
`string.Empty` initializer therefore must not be mistaken for SQL `NOT NULL`.
Properties marked `?` below are nullable in the CLR model; the physical schema
may accept `NULL` in additional columns. Existing-row migration tests assert
materialized CLR defaults, not stored SQL defaults.

## Complete schema inventory

### Imported text and vocabulary

| Table | Columns (CLR type) | Keys and indexes | Logical relationships |
| --- | --- | --- | --- |
| `Documents` | `Id:int`; `Title:string`; `TextLanguage:string`; `ExplanationLanguage:string`; `LookupMode:LexicalLookupMode`; `TargetLanguage:string`; `Content:string`; `ContentFingerprint:string`; `ImportedAt:DateTime`; `WordCount:int` | PK `Id`; non-unique index on `ContentFingerprint` | Parent of sentence spans, occurrences, one review session, and context source references. |
| `Words` | `Id:int`; `Language:string`; `CanonicalTerm:string`; `NormalizedTerm:string`; `Status:WordStatus`; `TokenKind:TokenKind`; `PreparationState:PreparationState`; `TotalOccurrenceCount:int`; `DocumentCount:int`; `AutomaticInteractionMode:LearningInteractionMode`; `ConsecutiveRecallSuccessCount:int`; `ConsecutiveTypingSuccessCount:int`; `ConsecutiveTypingFailureCount:int`; `MasteryReviewExtensionScheduled:bool`; `CreatedAt:DateTime`; `UpdatedAt:DateTime` | PK `Id`; unique `IX_Words_Language_NormalizedTerm(Language, NormalizedTerm)`; non-unique `IX_Words_Status(Status)` and `IX_Words_PreparationState(PreparationState)` | Parent vocabulary identity for forms, occurrences, meanings, legacy review aggregates, workflow items, snapshots, and cards. |
| `WordForms` | `Id:int`; `WordId:int`; `SurfaceForm:string`; `OccurrenceCount:int` | PK `Id`; non-unique `IX_WordForms_WordId(WordId)` | `WordId` conventionally references `Words`. The service, not the database, prevents duplicate `(WordId, SurfaceForm)` rows. |
| `SentenceSpans` | `Id:int`; `DocumentId:int`; `StartPosition:int`; `Length:int`; `Order:int` | PK `Id`; unique `IX_SentenceSpans_Document_Order(DocumentId, Order)` | `DocumentId` conventionally references `Documents`; occurrences reference a span. |
| `WordOccurrences` | `Id:int`; `WordId:int`; `DocumentId:int`; `SentenceSpanId:int`; `StartPosition:int`; `Length:int`; `SurfaceForm:string`; `TechnicalFamily:TechnicalTokenFamily`; `TechnicalInstanceYear:int?`; `TechnicalInstanceIdentifier:string`; `TechnicalVariant:string`; `Order:int` | PK `Id`; non-unique `IX_WordOccurrences_WordId(WordId)`, `IX_WordOccurrences_DocumentId(DocumentId)`, and `IX_WordOccurrences_Sentence_Order(SentenceSpanId, Order)` | Conventionally references one word, document, and sentence span. No unique occurrence constraint exists. |

### Prepared content and review workflow

| Table | Columns (CLR type) | Keys and indexes | Logical relationships |
| --- | --- | --- | --- |
| `Meanings` | `Id:int`; `WordId:int`; `ExplanationLanguage:string`; `SourceLanguage:string`; `DisplayTerm:string`; `EncounteredSurfaceForm:string`; `GrammaticalRelationship:string`; `TokenKind:TokenKind`; `SelectedMeaningId:string`; `AcronymExpansion:string`; `Translation:string`; `Definition:string`; `DictionaryExample:string`; `AdditionalNote:string`; `AcceptedAliasesJson:string`; `TranslationOrDefinition:string`; `Source:string`; `SourceProject:string`; `SourcePageTitle:string`; `SourceRevisionId:long?`; `Attribution:string`; `ConfirmedByUser:bool`; `CreatedAt:DateTime`; `UpdatedAt:DateTime`; `PreparedAt:DateTime` | PK `Id`; non-unique `IX_Meanings_WordId(WordId)` | `WordId` conventionally references `Words`; parent of context snapshots and referenced by cards. The service currently allows only one confirmed meaning per word, but SQL does not enforce it. |
| `ReviewStates` | `Id:int`; `WordId:int`; `ReviewCount:int`; `ForgotCount:int`; `PartialCount:int`; `KnownCount:int`; `LastReviewedAt:DateTime?` | PK `Id`; no secondary index or uniqueness | Legacy aggregate linked by convention to `Words`. No current production service inserts or updates it; cleanup paths still delete it. |
| `ReviewSessions` | `Id:int`; `DocumentId:int`; `Status:ReviewSessionStatus`; `TotalCandidates:int`; `ReviewedCount:int`; `KnownCount:int`; `UnknownCount:int`; `IgnoredCount:int`; `DecisionSequence:int`; `StartedAt:DateTime`; `CompletedAt:DateTime?` | PK `Id`; unique index on `DocumentId`; non-unique index on `Status` | `DocumentId` conventionally references `Documents`; parent of review candidates. SQL does not enforce only one globally active session. |
| `ReviewCandidates` | `Id:int`; `SessionId:int`; `WordId:int`; `Order:int`; `Status:WordStatus`; `PreviousWordStatus:WordStatus`; `PreviousTotalOccurrenceCount:int`; `PreviousDocumentCount:int`; `PreviousUpdatedAt:DateTime`; `DecisionSequence:int`; `WasWordCreatedForSession:bool`; `DecidedAt:DateTime?` | PK `Id`; unique `IX_ReviewCandidates_Session_Order(SessionId, Order)`; non-unique indexes on `WordId` and `Status` | Conventionally references one review session and word. Previous-value fields are required for persisted Undo/discard behavior. |
| `PreparationSessions` | `Id:int`; `Status:PreparationSessionStatus`; `Method:PreparationMethod`; `TotalItems:int`; `CompletedItems:int`; `StartedAtUtc:DateTime`; `UpdatedAtUtc:DateTime`; `CompletedAtUtc:DateTime?` | PK `Id`; non-unique index on `Status` | Parent of preparation candidates. SQL does not enforce one active session. |
| `PreparationCandidates` | `Id:int`; `SessionId:int`; `WordId:int`; `Order:int`; `Status:PreparationCandidateStatus`; `ResultJson:string`; `SelectedMeaningIndex:int`; `LastErrorCode:string`; `LookupAttemptCount:int`; `UpdatedAtUtc:DateTime` | PK `Id`; unique `IX_PreparationCandidates_Session_Order(SessionId, Order)`; non-unique indexes on `WordId` and `Status` | Conventionally references one preparation session and word. `ResultJson` is a resumable lexical draft, not the lexical cache. |
| `ContextSnapshots` | `Id:int`; `MeaningId:int`; `WordId:int`; `SourceDocumentId:int`; `SourceDocumentTitle:string`; `Text:string`; `TargetStart:int`; `TargetLength:int`; `NormalizedFingerprint:string`; `CreatedAtUtc:DateTime` | PK `Id`; unique `IX_ContextSnapshots_Meaning_Fingerprint(MeaningId, NormalizedFingerprint)`; non-unique indexes on `WordId` and `SourceDocumentId` | Conventionally references a meaning, word, and source document. Its exact text/target invariant is independently valuable personal learning data. |

### Learning and reference cache

| Table | Columns (CLR type) | Keys and indexes | Logical relationships |
| --- | --- | --- | --- |
| `LearningCards` | `Id:int`; `WordId:int`; `MeaningId:int`; `Direction:CardDirection`; `State:CardState`; `DueAtUtc:DateTime`; `IntervalDays:int`; `EaseFactor:double`; `SuccessfulReviewCount:int`; `LapseCount:int`; `LastReviewedAtUtc:DateTime?`; `LastRating:ReviewRating?`; `CreatedAtUtc:DateTime`; `UpdatedAtUtc:DateTime` | PK `Id`; unique `IX_LearningCards_Word_Direction(WordId, Direction)`; non-unique indexes on `MeaningId`, `State`, and `DueAtUtc` | Conventionally references a word and meaning; parent of ratings and queue references. |
| `LearningReviews` | `Id:int`; `CardId:int`; `SessionId:int`; `Rating:ReviewRating`; `WasTypedAnswer:bool`; `WasCorrect:bool`; `ReviewedAtUtc:DateTime`; `DueAtUtc:DateTime`; `IntervalDays:int`; `EaseFactor:double` | PK `Id`; non-unique indexes on `CardId` and `SessionId` | Conventionally references a learning card and learning session. No SQL uniqueness prevents a duplicate rating event. |
| `LearningSessions` | `Id:int`; `Status:LearningSessionStatus`; `TotalCards:int`; `CompletedCards:int`; `AgainCount:int`; `HardCount:int`; `GoodCount:int`; `EasyCount:int`; `StartedAtUtc:DateTime`; `UpdatedAtUtc:DateTime`; `CompletedAtUtc:DateTime?` | PK `Id`; non-unique index on `Status` | Parent of queue rows and referenced by review events. SQL does not enforce one active session. |
| `LearningSessionCards` | `Id:int`; `SessionId:int`; `CardId:int`; `QueueOrder:int`; `IsDueCard:bool`; `IsAgainRepeat:bool`; `AnswerRevealed:bool`; `SpellingChecked:bool`; `SpellingCorrect:bool`; `IsCompleted:bool`; `Rating:ReviewRating?`; `CompletedAtUtc:DateTime?` | PK `Id`; unique `IX_LearningSessionCards_Session_Order(SessionId, QueueOrder)`; non-unique index on `CardId` | Conventionally references a learning session and card. Repeated card IDs are intentional for the one allowed Again repeat. |
| `LexicalCache` | `Id:int`; `CacheKey:string`; `SourceLanguage:string`; `ExplanationLanguage:string`; `NormalizedLemma:string`; `LookupMode:LexicalLookupMode`; `TargetLanguage:string`; `CanonicalLookupTerm:string`; `TokenKind:TokenKind`; `Provider:string`; `ProviderSchemaVersion:int`; `ResultJson:string`; `SourceProject:string`; `PageTitle:string`; `RevisionId:long?`; `Attribution:string`; `FetchedAtUtc:DateTime` | PK `Id`; unique index on `CacheKey` | No personal-domain parent. This is replaceable provider reference data and is excluded from backup v1. |

## Enum persistence

The entities contain the following integer-backed enums:

| Enum | Persisted values |
| --- | --- |
| `WordStatus` | `Unreviewed=0`, `Known=1`, `UnknownBacklog=2`, `Prepared=3`, `Learning=4`, `Mastered=5`, `Ignored=6` |
| `TokenKind` | `Word=0`, `Acronym=1`, `Abbreviation=2`, `TechnicalTerm=3` |
| `PreparationState` | `Unprepared=0`, `Preparing=1`, `Prepared=2`, `PreparationFailed=3` |
| `LearningInteractionMode` | `Reading=0`, `Typing=1` |
| `TechnicalTokenFamily` | `None=0`, `Cve=1`, `Sha=2` |
| `LexicalLookupMode` | `Definition=0`, `Translation=1`, `DefinitionAndTranslation=2` |
| `ReviewSessionStatus` | `Active=0`, `Completed=1` |
| `PreparationMethod` | `AutomaticOnline=0`, `Manual=1` |
| `PreparationSessionStatus` | `Active=0`, `Completed=1`, `Cancelled=2` |
| `PreparationCandidateStatus` | `Pending=0`, `ResultReady=1`, `Prepared=2`, `Skipped=3`, `Failed=4`, `MarkedKnown=5`, `Excluded=6`, `Cancelled=7` |
| `CardDirection` | `TermToMeaning=0`, `MeaningToTerm=1` |
| `CardState` | `New=0`, `Learning=1`, `Review=2`, `Relearning=3`, `Suspended=4`, `Retired=5` |
| `ReviewRating` | `Again=0`, `Hard=1`, `Good=2`, `Easy=3` |
| `LearningSessionStatus` | `Active=0`, `Completed=1` |

There is no general database-load validation that an integer is defined by its
enum. Unknown values can therefore enter application logic as undefined enum
values. Backup v1 uses named strings and rejects an unknown required value
before any write; it never coerces it to zero or a default.

## Relationships and deletion behavior

No `[ForeignKey]` declarations, `REFERENCES` clauses, delete cascades, or
`PRAGMA foreign_keys` activation were found. Relationships are reconstructed
by service queries and child-first manual deletes. Important examples are:

- import atomically creates the document, spans, words/forms/occurrences, and
  active review session/items;
- review decisions and Undo are individual transactions;
- discard manually reverses word aggregates/forms and deletes import-specific
  rows;
- preparation acceptance atomically creates one meaning, up to three context
  snapshots, enabled card directions, and workflow updates;
- learning ratings atomically update a schedule, append review history, update
  the queue/session, and update vocabulary state;
- permanent-known cleanup manually deletes queue rows, history, cards,
  meanings, contexts, preparation items, occurrences, forms, and legacy review
  aggregates before retaining the minimal word marker; and
- document cleanup manually tests dependencies and deletes contexts, spans,
  review rows, and the document.

These operations provide useful application-level invariants but do not protect
against an orphan introduced by a migration, restore bug, corrupt file, or a
future repository that bypasses the services. There is also no central list of
delete order. A backup restorer must have its own complete relationship graph,
validate it before mutation, insert parent-first, and delete child-first.

## Database consumers and transaction boundaries

| Component | Access and responsibility |
| --- | --- |
| `KnownFirstDatabase` | Opens/initializes schema, serializes access, exposes reads and transactions, and implements destructive reset. |
| `TextReviewService` | Transactional import, decision, Undo, discard, and review completion; read-only review/diagnostic projections. |
| `PreparationService` | Transactional batch creation, persisted lookup state, meaning selection, acceptance, skip/cancel, Known, and exact exclusion. Network work occurs outside a database transaction. |
| `LearningService` | Transactional session creation, reveal/check/rating, permanent-known cleanup, and maintenance; read-only card/session projections. |
| `DocumentCleanupOperations` | Synchronous child-first cleanup invoked inside service transactions. |
| `LexicalCacheRepository` | Read-through cache and transactional cache upsert using source-generated JSON. |
| `DashboardService` and `WorkflowStateService` | Read-only aggregate/projection queries. |
| `StartupMaintenanceService` | Starts background cleanup through `LearningService`; it does not access SQLite directly. |

The per-database gate is sufficient to take one logical snapshot from current
application services, provided every export query runs inside one gated
callback. It does not make preferences part of that snapshot and it does not
provide a cross-process lock.

## Internal JSON fields

- `Meaning.AcceptedAliasesJson` stores a `string[]`.
- `PreparationCandidate.ResultJson` stores a resumable `LexicalResult`.
- `LexicalCache.ResultJson` stores a cached `LexicalResult`.

All production serialization of these fields passes generated
`JsonTypeInfo` from `LexicalJsonSerializerContext`. The test project disables
reflection serialization by default and contains round-trip coverage. The
backup object graph is new and must receive its own generated context; reuse of
the lexical context would couple unrelated compatibility contracts.

## Timestamps and naming

Write sites use `DateTime.UtcNow` or an injected UTC clock, but naming is not
consistent. UTC-valued columns without an `Utc` suffix include `ImportedAt`,
`CreatedAt`, `UpdatedAt`, `PreparedAt`, `StartedAt`, `CompletedAt`,
`DecidedAt`, `PreviousUpdatedAt`, and `LastReviewedAt`. Other tables use an
`AtUtc` suffix. SQLite round trips may not preserve a useful `DateTime.Kind`
contract, and no schema check enforces UTC.

Other compatibility-sensitive naming or duplication includes:

- `TextLanguage`, `SourceLanguage`, `ExplanationLanguage`, and
  `TargetLanguage`, with empty string used as a null sentinel in several
  entities;
- `TranslationOrDefinition`, a legacy convenience value alongside the
  structured translation and definition;
- `SelectedMeaningId`, which is provider metadata rather than a relational
  key;
- `Known` as the persisted minimal permanently-known marker while
  `Mastered` is a distinct learning state; and
- counters such as document/occurrence totals and session summaries that are
  derived from detail rows but persisted for workflow speed.

The backup must preserve authoritative detail, carry necessary resumable
workflow state, and validate stored aggregates against it. It must not silently
repair a mismatch during preview.

## Data outside SQLite

| Data | Current store | Backup v1 decision |
| --- | --- | --- |
| UI language (`knownfirst.uiLanguage`) | MAUI Preferences | Excluded; target-device preference remains unchanged. |
| Theme (`theme_preference`) | MAUI Preferences | Excluded; target-device preference remains unchanged. |
| Preparation limit (`preparation_limit`) | MAUI Preferences | Excluded; target-device preference remains unchanged. |
| Card direction (`card_direction`) | MAUI Preferences | Excluded; target-device preference remains unchanged. Existing restored cards retain their own directions. |
| Learning mode (`learning_mode`) | MAUI Preferences | Excluded; target-device preference remains unchanged. Per-word automatic-learning state in SQLite is included. |
| Online lookup consent (`online_lookup_consent`) | MAUI Preferences | Excluded and never granted by restore. Consent remains a device-local decision. |
| Diagnostic level/retention/size policy | Derived at runtime from build configuration; no persisted user setting was found | Excluded. It is application policy, not user learning data. |
| Structured application logs and lexical diagnostic log | Bounded files under a Logs directory | Excluded; may contain private operational context and are not learning state. Restore neither imports nor deletes them. |
| DEBUG timing, artificial clock, navigation history, live UI state | Memory only | Excluded; not durable product data. |
| Build identity, package metadata, binaries, signing material | Application/build environment | Excluded. No signing material or secret store is used for normal lookup. |

Reset currently deletes/recreates SQLite, clears all Preferences, then reapplies
defaults/device language. Those steps are not one transaction. Backup v1 avoids
that cross-store atomicity problem by defining `ReplaceAll` only over its
documented SQLite scope.

## Exportability and reconstruction assessment

- **Documents and coordinates:** Exportable. Original text, sentence ranges,
  occurrence ranges, and context target ranges must be checked against exact
  substrings before archive creation and again before restore.
- **Vocabulary identity:** Exportable. `(Language, NormalizedTerm)` is the
  database uniqueness key; canonical term and token kind must also be retained.
- **IDs:** Current auto-increment IDs are stable only inside one database. They
  must not be copied as portable identity. Archive-local opaque IDs can map all
  relationships to newly generated integer IDs on restore.
- **Forms and counts:** Exportable, but form counts, total occurrence count,
  document count, and session summary counts need consistency validation.
- **Meanings and contexts:** Exportable as structured fields. Internal JSON
  aliases and lookup drafts must be decoded into external v1 fields, never
  embedded as undocumented JSON strings.
- **Active workflows:** Exportable. Undo fields, selected lookup state, reveal
  and spelling state, queue order, and the one Again repeat are required to
  resume exactly.
- **History and scheduling:** Exportable. Review events and current card
  schedules are both retained; neither should be recomputed from the other.
- **Legacy review aggregates:** Exportable and included under an explicitly
  named legacy section because silently dropping an unused personal-history row
  would be data loss.
- **Lexical cache:** Technically exportable but deliberately excluded because it
  is replaceable provider reference data with its own version and attribution
  compatibility.

## Risk register

### Critical

| Risk | Evidence and required control |
| --- | --- |
| Newer-schema downgrade can destroy compatibility | Initialization never rejects `user_version > 7` and always writes 7. An older app could open a future database, run current maintenance, and relabel it. Before backup code is allowed to initialize or snapshot a database, initialization needs an explicit future-version refusal path and a regression fixture. |
| A naive `ReplaceAll` can erase all personal learning data | No restore code exists, no pre-validation or safety backup exists, and reset deletes the database file. Restore must never reuse reset. It must validate first, create and verify a safety backup, and make all in-scope deletes/inserts one transaction. |

### High

| Risk | Evidence and required control |
| --- | --- |
| Version marker without versioned migrations | Versions 3 through 7 all run the same implicit table-update pass. Introduce explicit supported-source fixtures and ordered migration policy before schema 8 or restore ships. |
| Schema update is not atomic | Seventeen table updates, cache deletion, and `user_version` assignment are separate awaits. Interruption and failure injection need tests; a migration runner should own one permissible transaction or an explicit resumable state machine. |
| Rename/type-change data loss | There are no rename maps or transformation steps. Any renamed table/property can make existing data invisible. Require explicit SQL/data copy plus preservation tests. |
| Missing relational enforcement and integrity checks | No foreign keys, cascades, or whole-graph check exist. Backup validation and post-insert validation must reject all orphans, duplicate semantic keys, invalid ranges, and aggregate mismatches. A separate schema decision should assess adding SQLite foreign keys. |
| Migration test matrix is incomplete | Two partial legacy shapes do not prove upgrades from every supported production version. Check in exact synthetic fixtures for every accepted source version and a future-version rejection case. |
| Archive input is an untrusted parser boundary | Without exact entry-name checks, byte/count limits, compression-ratio limits, and bounded streaming, traversal names, duplicate entries, ZIP bombs, and allocation attacks are possible. Apply all limits before JSON materialization. |
| Backup JSON could regress Android AOT | Reflection serialization previously caused a Release crash. Every manifest/data/preview DTO and collection must be registered in a dedicated source-generated context and tested with reflection disabled. |

### Medium

| Risk | Evidence and required control |
| --- | --- |
| Nulls can bypass CLR intent | No non-PK field has a SQL not-null constraint. Validation must distinguish missing/null/empty and reject invalid required data before mapping. |
| Unknown enum values | SQLite stores integer enums without a load guard. External v1 uses strings and rejects unknown required values; migration tests need undefined-integer cases. |
| Timestamp ambiguity | Mixed names and library-controlled persistence do not prove UTC kind. Export must normalize logically UTC values to ISO 8601 `Z` and fail on values that cannot be interpreted safely. |
| Internal IDs are not portable | Auto-increment IDs change after ReplaceAll. Use archive-local IDs and compare round trips semantically, not by database PK. |
| Aggregate drift | Counts and status summaries duplicate detail rows. Validate both before export and before commit; never silently accept a mismatch. |
| Checksum is not authenticity | A SHA-256 checksum detects corruption only when the manifest is trusted with the payload. An attacker can replace both. V1 must state that it is not signed, authenticated, or encrypted and still treat all input as hostile. |
| Cross-store settings cannot join SQLite rollback | Preferences are separate and reset is compensating, not atomic. V1 excludes them; a future settings feature needs a durable journal or a single transactional store. |
| Future format or optional-feature confusion | Best-effort parsing could partially import fields. Readers must reject unsupported format versions and unknown required features before writes. |

### Low

| Risk | Evidence and required control |
| --- | --- |
| Cache behavior differs after restore | Cache is deliberately not portable and is cleared on successful ReplaceAll. The next lookup may require consent/network; personal prepared meanings remain available. |
| Archive bytes are not reproducible across exports | ZIP timestamps/compression and archive-local IDs may differ. Correctness is defined by semantic round trip and the checksum of each archive's exact `data.json`, not byte-for-byte archive equality. |

## Gate to implementation

The next smallest safe step is review and acceptance of the
[backup format](backup-format-v1.md) and the
[implementation plan](../plans/backup-restore-v1-implementation-plan.md),
including the v1 exclusion of Preferences and cache. After that decision, the
first code change should add only external DTOs, strict validation primitives,
and source-generated JSON round-trip tests. It should not yet delete or replace
database rows.
