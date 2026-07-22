# Backup and restore v1 implementation plan

**Status:** Proposed phased plan; implementation has not begun
**Depends on:** [database audit](../architecture/database-audit.md) and
[backup format v1](../architecture/backup-format-v1.md)
**Current internal schema:** 7

This plan deliberately separates format code, archive creation, untrusted-input
validation, preview, destructive restore, migration compatibility, verification,
and UI. A later phase cannot start merely because the previous code compiles;
its acceptance gate must pass with temporary/synthetic data.

No phase uses a real user database, live Wikimedia request, ADB, device action,
or generated application package in automated tests.

## Required implementation order

1. Backup data models.
2. Source-generated JSON context.
3. Backup creation.
4. Backup validation.
5. Restore preview.
6. Transactional `ReplaceAll` core.
7. Safety backup and public restore orchestration.
8. Migration tests and migration hardening.
9. Round-trip, failure, security, and AOT-focused tests.
10. Backup/restore UI only after the service contract is green.

The format inclusion boundary is fixed for v1: personal SQLite domain state is
included; Preferences, online consent, lexical cache, and logs are excluded.
Changing that boundary requires revisiting atomicity before code is written.

Phases 1 and 2 are intentionally database-free. Before Phase 3 may initialize
or read a database, add the future-schema refusal guard described in Phase 8,
with a test proving `user_version > CurrentVersion` causes no table, cache, or
version change. The remaining migration-runner and fixture expansion can stay
in Phase 8, but this critical guard cannot.

## Phase 1: backup data models

### Intended files and interfaces

- Add `Models/BackupModels.cs` for manifest, payload, archive-local IDs,
  preview, record counts, and external enum DTOs.
- Add `Services/DataSafety/BackupFormatLimits.cs` for the single source of all
  byte, ratio, depth, string, and record-count limits.
- Add `Services/DataSafety/BackupErrors.cs` for stable error codes and a typed
  exception/result that never carries private content.
- Add `Services/DataSafety/BackupEnumMappings.cs`; do not serialize persistence
  enums directly.
- Update `KnownFirst.Tests/KnownFirst.Tests.csproj` to link the new production
  folder and the new Data repositories explicitly, following the existing
  linked-source test structure.
- Add `KnownFirst.Tests/BackupModelContractTests.cs`.

No public backup service is needed. The first phase exposes only immutable
external DTOs, explicit enum mappings, limits, and safe error contracts.

### Data flow

Synthetic domain values -> explicit external enum/scalar mapping -> immutable
manifest/data DTO graph -> structural and scalar validation. No entity instance
is accepted as an external format record.

### Transaction boundary

None. This phase cannot access JSON, `IKnownFirstDatabase`, Preferences, the
file system, or ZIP APIs.

### Error behavior

Invalid archive-local IDs, enum mapping inputs, timestamps, non-finite numbers,
and out-of-range scalar values produce stable format errors. No mapper coerces
an unknown value to zero, empty string, local time, or a default enum.

### Automated tests

- Construct a fully populated graph including every nullable/non-nullable
  branch, Unicode, embedded newlines, empty collections, every external enum,
  form relations, active workflows, and one Again queue repeat.
- Assert every manifest/data field and the fixed record-count property set.
- Round-trip every persistence enum through its explicit external string mapper
  and reject undefined integer values and unknown strings.
- Prove UTC formatting and exact original string preservation.
- Prove DTOs contain no entity type, SQLite attribute, table name, database PK,
  file path, `JsonElement`, or internal `ResultJson` string.

### Acceptance gate

The complete v1 object graph can be constructed and validated independently of
SQLite and JSON; every external enum mapping is total for known values and
strict for unknown values; no database/file operation is reachable.

### Main risks

- Reusing entity classes would leak table names, integer IDs, empty-string null
  sentinels, and future schema changes into the public format.
- Adding “convenient” internal JSON fields would create a second undocumented
  format inside `data.json`.
- Treating empty string as null would carry current schema ambiguity into the
  external contract.

## Phase 2: source-generated JSON context

### Intended files and interfaces

- Add `Services/DataSafety/BackupJsonSerializerContext.cs` for every DTO and
  collection reachable from `manifest.json` and `data.json`.
- Add `Services/DataSafety/BackupJsonCodec.cs` with separate typed
  manifest/data serialize and deserialize methods.
- Configure strict string enums and no numeric enum acceptance without a
  reflection resolver.
- Add `KnownFirst.Tests/BackupJsonContractTests.cs`.

No database or archive interface is introduced. The codec consumes and returns
only the Phase-1 external models.

### Data flow

Synthetic external DTO -> explicit generated `JsonTypeInfo` -> UTF-8 bytes ->
strict generated deserialization -> semantic equality. Separate manifest and
data codecs prevent accidental use of internal lexical JSON.

### Transaction boundary

None. This phase cannot access `IKnownFirstDatabase`, Preferences, the file
system, or ZIP APIs.

### Error behavior

Malformed UTF-8/JSON, missing required fields, numeric/unknown enum values,
invalid nulls, and trailing JSON produce stable format errors. Error paths name
only properties/indices and never echo values. Duplicate-property and resource
limit scanning is added in Phase 4.

### Automated tests

- Round-trip the maximum-shape Phase-1 graph and every empty collection.
- Run with `JsonSerializerIsReflectionEnabledByDefault=false`.
- Prove every production call passes a generated `JsonTypeInfo` and no fallback
  resolver is configured.
- Verify UTF-8 without BOM, exact strings, UTC output, null handling, and strict
  named enums.
- Source-inspect the Data Safety folder for untyped `JsonSerializer` overloads.

### Acceptance gate

The complete graph round-trips with reflection disabled; every nested DTO and
collection is present in generated metadata; no serialization path uses runtime
type discovery; and no database/file operation is reachable.

### Main risks

- Registering only top-level DTOs can leave nested collections AOT-unsafe.
- Automatic enum converters can accept numeric values unless explicitly
  configured and tested.
- Permissive source-generated defaults can ignore misspelled properties unless
  the codec opts into the strict contract.

## Phase 3: logical snapshot and backup creation

### Intended files and interfaces

- Add `Data/BackupSnapshotRepository.cs` to read every included entity and
  project an immutable internal snapshot.
- Add a read-snapshot operation to `Data/IKnownFirstDatabase.cs` and
  `Data/KnownFirstDatabase.cs` if the existing gate cannot guarantee one SQLite
  snapshot without holding it during file I/O. The operation should accept a
  synchronous projection over `SQLiteConnection` and release before JSON work.
- Add `Services/DataSafety/IBackupService.cs` with a stream-oriented creation
  method and cancellation token; do not bind the service to a page or file
  picker.
- Add `Services/DataSafety/BackupModelMapper.cs` for archive-local ID assignment,
  internal JSON decoding, enum mapping, and aggregate/invariant checks.
- Add `Services/DataSafety/BackupArchiveWriter.cs` for bounded `data.json`,
  checksum, manifest, two-entry ZIP, temporary output, and final replacement.
- Add `Services/DataSafety/IBackupPlatformInfo.cs` for the source platform and
  app version, with deterministic fakes in tests.
- Register services in `MauiProgram.cs` only after their interfaces are stable.
- Add `KnownFirst.Tests/BackupCreationTests.cs`.

### Data flow

1. Acquire the central database gate and capture all included rows in one
   consistent logical snapshot.
2. Release the database before encoding or destination I/O.
3. Validate source relationships, counts, timestamps, enums, and coordinates.
4. Map internal integer IDs to opaque archive-local IDs and decode aliases and
   preparation lookup drafts into external fields.
5. Serialize bounded `data.json` to an app-private temporary file while hashing
   exact bytes.
6. Build the manifest from actual counts/hash, create and close a temporary
   two-entry archive, and perform structural writer checks. Keep creation
   internal/non-shipping until Phase 4 can reopen it through the strict reader.
7. Once Phase 4 is integrated, pass the staged archive through that reader and
   atomically place only the verified result at the authorized destination.

### Transaction boundary

Snapshot capture is one read snapshot under the database gate. Serialization,
compression, hashing, and destination writes happen after the snapshot is
released. Backup creation never writes SQLite and never reads Preferences or
logs.

### Error behavior

- Reject a database with a future schema version or invalid graph before
  creating a destination artifact.
- Surface `invariant-violation`, `archive-too-large`, `insufficient-space`,
  `operation-cancelled`, or `io-failure` without private values.
- Delete incomplete temporary output best-effort. Never delete or truncate a
  pre-existing destination unless an explicitly authorized atomic replacement
  has a verified new archive ready.
- Do not claim success until the archive has been reopened and validated.

### Automated tests

- Export a maximal-shape synthetic temporary database and inspect exact ZIP
  entry names/count.
- Verify checksum against the exact uncompressed `data.json` bytes.
- Verify source graph rejection for an orphan, duplicate semantic vocabulary,
  invalid coordinate, aggregate mismatch, unknown persisted enum, and invalid
  internal JSON.
- Verify Preferences/cache/logs never appear in DTOs or archive bytes.
- Verify cancellation and write failure leave no claimed backup and preserve an
  existing destination.
- Verify concurrent app database work cannot interleave with snapshot capture,
  while long file I/O does not hold the database gate.

### Acceptance gate

A structurally valid archive is produced from a temporary database, contains
exactly two entries, and a semantic inspection accounts for every included
source row. Source state and excluded stores are unchanged. Production exposure
remains blocked until Phase 4 reopens writer output through the normal strict
reader.

### Main risks

- Multiple ungated queries could capture counts and detail from different
  commits.
- Keeping the database gate during compression could freeze normal workflows.
- Loading an unlimited database into memory would violate mobile resource
  constraints; snapshot/materialization must honor the same record limits as
  import.
- Copying `ResultJson` verbatim would expose an internal compatibility format
  and diagnostics.

## Phase 4: strict backup validation

### Intended files and interfaces

- Add `Services/DataSafety/BackupArchiveReader.cs` for central-directory and
  bounded stream handling.
- Add `Services/DataSafety/BackupDuplicatePropertyScanner.cs` using
  `Utf8JsonReader` without reflection.
- Add `Services/DataSafety/BackupValidator.cs` for scalar, count, ID,
  relationship, coordinate, schedule, and workflow validation.
- Add an internal `IBackupValidator` that returns an immutable validated import
  model and manifest summary; it performs no database work.
- Add `KnownFirst.Tests/BackupValidationTests.cs` and
  `KnownFirst.Tests/BackupArchiveSecurityTests.cs`.

### Data flow

Untrusted source stream -> archive-size check -> exact entry lookup -> bounded
manifest parse -> version/feature check -> bounded streamed data checksum ->
duplicate/depth scan -> generated DTO decode -> graph validation -> immutable
validated import model. The same model type is later consumed by preview and
restore, but neither may bypass a fresh validation pass.

### Transaction boundary

None. Validation has no `IKnownFirstDatabase` access, does not create a safety
backup, and does not touch Preferences, cache, logs, or the selected archive.

### Error behavior

Return the stable codes defined by the format for layout, size/ratio, version,
feature, checksum, JSON, enum, ID/reference, count, and invariant failures.
Report a safe logical path and expected rule, never a document/context/meaning
value. Stop at the first security boundary failure; detailed multi-error output
is allowed only after bounded decoding succeeds.

### Automated tests

- Reject extra, missing, duplicate, case-variant, directory, absolute,
  backslash, and traversal entry names.
- Reject encrypted/unsupported methods, false central-directory sizes, overlong
  streams, high ratios, deep JSON, oversized strings, too many records, nested
  archives, invalid UTF-8, BOM, duplicate JSON properties, and trailing data.
- Reject wrong checksum before DTO materialization.
- Reject format 0, format 2, unknown required feature, invalid optional-feature
  placement, unknown enum, missing/null core field, duplicate ID, orphan, and
  count mismatch.
- Prove validation performs no database or preference call with recording
  fakes.
- Fuzz bounded manifest/data inputs and assert controlled failures rather than
  hangs or unbounded allocations.

### Acceptance gate

Every malicious archive category is rejected within the declared byte/time
budget, every valid boundary archive produces the expected immutable model, and
a recording database proves zero access/mutations. Phase-3 writer output is
reopened here before any final destination success is reported.

### Main risks

- `ZipArchiveEntry.Length` and manifest counts are attacker-controlled and
  cannot substitute for counted decompressed bytes.
- Extracting by name would create traversal/symlink risk; entries must remain
  streams.
- Deserializing before checksum/limits could allocate attacker-selected object
  graphs.
- Permissive unknown-property behavior could hide a misspelled required field.

## Phase 5: read-only restore preview

### Intended files and interfaces

- Add `Services/DataSafety/IBackupPreviewService.cs` with
  `PreviewAsync(Stream, CancellationToken)`.
- Add `Services/DataSafety/BackupPreviewService.cs` to call the Phase-4
  validator and project only the safe summary fields defined by the format.
- Keep `BackupPreview` in `Models/BackupModels.cs`; it contains source metadata,
  record counts, supported feature/warning codes, checksum status, and the
  exact ReplaceAll/exclusion summary.
- Add `KnownFirst.Tests/BackupPreviewTests.cs`.

The preview interface is separate from destructive restore so callers can be
given only read-only capability.

### Data flow

Untrusted archive stream -> fresh Phase-4 validation -> immutable validated
model -> privacy-safe preview projection -> caller. The preview does not retain
an open ZIP/file handle and does not expose original text, terms, meanings,
contexts, internal IDs, or machine paths.

### Transaction boundary

None. The preview service has no `IKnownFirstDatabase`, recovery-store,
Preferences, cache, or log dependency and cannot begin a transaction.

### Error behavior

Propagate the Phase-4 stable validation code and safe logical path. Cancellation
before completion returns `operation-cancelled`. An invalid preview is never
represented as a warning or a partially usable object.

### Automated tests

- Project exact source app/schema/platform/date, record counts, optional
  features, checksum state, and v1 overwrite/exclusion text from a valid
  synthetic archive.
- Prove no private payload string or archive-local/database ID appears in the
  preview or error.
- Use recording fakes/source dependencies to prove zero database, Preferences,
  log, cache, recovery-store, and destination writes.
- Change the archive after one preview and prove the next call revalidates it
  rather than trusting a prior object/token.
- Verify cancellation and every validator failure yield no preview.

### Acceptance gate

Every valid archive produces one complete privacy-safe preview, every invalid
archive produces none, and the preview code has no reachable mutation API.

### Main risks

- Treating a preview as authorization would allow time-of-check/time-of-use
  replacement; destructive restore must reopen and revalidate.
- Displaying sample terms or text would leak personal data unnecessarily.
- Summarizing exclusions imprecisely could make users believe Preferences or
  consent will be overwritten.

## Phase 6: transactional `ReplaceAll` core

### Intended files and interfaces

- Add `Data/BackupRestoreRepository.cs` containing the complete delete/insert
  order, archive-ID maps, and in-transaction verification queries.
- Add an internal `Services/DataSafety/IBackupReplaceAllEngine.cs` and
  `BackupReplaceAllEngine.cs`. This capability accepts only a Phase-4 validated
  model; it is not registered for UI/application use until Phase 7 wraps it.
- Add `KnownFirst.Tests/BackupRestoreTransactionTests.cs`.

### Data flow

1. Receive an immutable import model that already passed Phase-4 validation.
2. Acquire the central database gate and start one SQLite transaction.
3. Delete current detail child-first, including cache, then parents.
4. Insert documents and vocabulary, assign new PKs, then insert every dependent
   object using archive-ID maps.
5. Query the transaction's new graph and rerun all counts, uniqueness,
   references, coordinates, schedules, and workflow invariants.
6. Commit once and return a semantic restore result to the Phase-7
   orchestrator.

Suggested child-first delete order is:

```text
LearningSessionCards
LearningReviews
LearningCards
ContextSnapshots
PreparationCandidates
ReviewCandidates
ReviewStates
Meanings
WordOccurrences
WordForms
PreparationSessions
ReviewSessions
LearningSessions
SentenceSpans
Documents
Words
LexicalCache
```

Suggested insert order is documents and vocabulary; sentence/form/occurrence
detail; meanings and legacy aggregates; review/preparation sessions and items;
contexts and cards; learning sessions; review events and queue items.

### Transaction boundary

All deletes, cache clearing, ID allocation, inserts, and post-insert checks are
inside one `RunInTransactionAsync` call. This engine performs no file reads,
JSON parsing, free-space work, or safety-backup creation. Preferences and logs
are outside scope and are not touched. Cancellation is deferred during the
transaction so the delegate either commits or throws and rolls back.

### Error behavior

- Any mapping, insert, injected database, or post-check failure throws out
  of the transaction and returns `restore-failed` only after rollback.
- The internal engine returns no success result before commit confirmation and
  emits no UI event.
- Cancellation observed before the transaction prevents entry; cancellation
  observed after entry is deferred until commit/rollback completes.

### Automated tests

- Replace non-empty target data and compare semantic output to the source
  archive while proving new database PKs are allowed.
- Inject failure after each delete group, each parent/child insert group, and
  every post-check; assert the exact pre-restore graph and cache are retained.
- Cancel before transaction and assert no mutation; request cancellation during
  transaction and assert one complete old or new state, never a mixture.
- Verify Preferences and logs are byte/state-identical across success and
  failure.
- Verify restored active review, preparation, and learning sessions resume at
  the same semantic item, including Undo, selected meaning, reveal/check state,
  and Again repeat.
- Verify permanent-known minimal markers, exact strings/coordinates, aliases,
  schedules, history, and source attribution.

### Acceptance gate

Failure injection at every mutation boundary yields a complete pre-restore
state; a successful restore yields a complete validated imported state; no test
can observe a partial in-scope state. The engine remains internal and
unregistered until Phase 7 guarantees a safety backup.

### Main risks

- Calling `ResetAsync` would create an unrecoverable gap and is prohibited.
- Missing one child collection can leave target-only personal data after
  ReplaceAll.
- Applying archive integer IDs directly can collide with SQLite state or expose
  unstable identity.
- Post-commit preference work would reintroduce cross-store partial restore and
  is outside v1.
- A very long transaction can consume journal space; Phase 7 must enforce a
  measured free-space estimate before invoking the engine.

## Phase 7: safety backup and public restore orchestration

### Intended files and interfaces

- Add `Services/DataSafety/IBackupRestoreService.cs` with explicit
  `ReplaceAllAsync(Stream, RestoreConfirmation, CancellationToken)`; confirmation
  is supplied by a future UI and is never inferred from file selection.
- Add `Services/DataSafety/BackupRestoreService.cs` to revalidate, estimate
  space, create recovery state, and invoke the Phase-6 engine.
- Add `Services/DataSafety/IBackupRecoveryStore.cs` and
  `AppPrivateBackupRecoveryStore.cs` for validated safety archives and bounded
  retention. Tests use a temporary-directory implementation.
- Add `Services/DataSafety/IBackupFreeSpaceService.cs` with platform adapters
  and deterministic test fakes.
- Reuse the Phase-3 writer for safety backup; do not create a second format.
- Add `KnownFirst.Tests/BackupSafetyBackupTests.cs` and
  `BackupRestoreOrchestrationTests.cs`.

### Data flow

1. Reopen and fully validate the selected archive into an immutable import
   model; never trust an earlier preview object.
2. Calculate conservative free-space requirements for input staging, current
   logical safety backup, SQLite journal growth, and the fixed margin.
3. Create an app-private format-1 safety archive of current in-scope data and
   reopen it through the Phase-4 validator.
4. Only after validation succeeds, invoke the Phase-6 ReplaceAll engine.
5. Return committed result plus a recovery handle. Retain the safety archive
   until explicit acknowledgement and later apply the documented bounded
   retention policy.

### Transaction boundary

Archive validation, space estimation, and safety-backup creation finish before
the destructive transaction. Phase 6 owns the single SQLite transaction. The
orchestrator never modifies Preferences or logs and never performs a second
post-commit data mutation.

### Error behavior

- Insufficient or unmeasurable space returns `insufficient-space` before
  safety-backup creation/restore.
- A failed or unverified safety archive returns `safety-backup-failed` and does
  not invoke the ReplaceAll engine.
- A Phase-6 failure returns `restore-failed` only after rollback; the selected
  source and safety archive remain available.
- If commit outcome is uncertain after process failure, startup performs a
  bounded integrity assessment and offers the retained safety archive; it never
  silently retries ReplaceAll or guesses success.

### Automated tests

- Prove safety backup is created and validated before the first ReplaceAll
  engine call and can restore the original semantic state.
- Inject writer, recovery-store, validation, free-space, and engine failures;
  assert either the engine is never called or its transaction rolls back.
- Reject insufficient space before mutation and verify conservative journal
  estimates with worst-case temporary fixtures.
- Verify cancellation at every pre-transaction stage and deferred cancellation
  after engine entry.
- Verify recovery retention/acknowledgement never deletes an unacknowledged
  safety archive and never overwrites the selected source archive.
- Prove Preferences/logs are unchanged and cache is cleared only by a committed
  successful ReplaceAll.

### Acceptance gate

No public restore call can reach the Phase-6 engine without a fresh valid input
and a fresh validated safety archive. Every pre-transaction failure leaves the
database untouched; every engine failure rolls back; and the safety archive can
recover the exact original semantic graph.

### Main risks

- Exposing the Phase-6 engine directly would bypass the mandatory recovery
  gate.
- A safety archive that is written but not reopened could be corrupt when it is
  needed most.
- Retention cleanup can become destructive unless acknowledgement, exact paths,
  and ownership are explicit and separately tested.
- Free-space APIs may be unavailable for provider streams; uncertainty must
  stop rather than weaken the guarantee.

## Phase 8: migration hardening and compatibility fixtures

### Intended files and interfaces

- Refactor `Data/DatabaseSchema.cs` to read the existing version first, reject a
  future version, and delegate ordered steps rather than only setting the final
  marker.
- Add `Data/Migrations/IDatabaseMigration.cs`,
  `DatabaseMigrationRunner.cs`, and explicit step files for every accepted
  source transition.
- Check in canonical synthetic SQL fixtures under
  `KnownFirst.Tests/Fixtures/Database/` for schemas 3, 4, 5, 6, and 7 if release
  evidence confirms that range. Do not claim support merely because a commit
  exists.
- Add a canonical schema-7 assertion fixture for tables, columns, declared
  types, indexes, uniqueness, and `user_version`.
- Replace/expand `DatabaseMigrationTests.cs` with
  `BackupMigrationCompatibilityTests.cs` while retaining focused regression
  names.
- Update `docs/DATABASE_CONTRACT.md` and add/accept an ADR in the implementation
  work package once the supported source range and migration runner are agreed.

### Data flow

Read `user_version` -> reject future/unsupported version before maintenance ->
run exactly the ordered transitions to current -> validate canonical schema and
personal rows -> set the new version only after its transition succeeds ->
allow export/normal startup.

For backup compatibility, each accepted old fixture is migrated to current in
a temporary database, exported as format 1, restored into a fresh current
temporary database, and compared semantically.

### Transaction boundary

Each migration is transactional where SQLite permits. Table rebuilds or
operations that cannot share one transaction require an explicit resumable
journal/state and a fixture that interrupts every stage. Cache invalidation is
scoped to the relevant transition, not every startup. A failed migration never
rewrites `user_version` to current.

### Error behavior

Unsupported old or future schemas produce a non-destructive compatibility
error with recovery guidance. Migration failure preserves the last committed
version and never falls back to database deletion/reset. Validation failure
blocks backup creation and restore into that database.

### Automated tests

- Exact old-to-new preservation tests for every accepted production schema.
- Immediately previous schema and oldest supported schema as required by the
  database contract.
- Future-version rejection proving no table, cache row, or version changes.
- Missing/extra/wrong-type column, index, and uniqueness cases.
- Property/table rename fixtures with explicit data-copy assertions.
- Invalid enum, null required value, orphan, and duplicate semantic key cases.
- Failure/interruption at every migration step and cache invalidation boundary.
- Idempotent current-schema initialization without data changes.

### Acceptance gate

The repository states one evidence-backed oldest supported schema, every
supported fixture reaches the exact canonical current schema without personal
data loss, a future schema is untouched, and format-1 export/restore works after
each supported migration.

### Main risks

- Git history is not proof that every schema was shipped or must be supported.
- Retrofitting canonical DDL can reveal differences in sqlite-net-generated
  types/nullability; migrations must preserve real logical values, not force a
  guessed DDL.
- SQLite table rebuilds can silently omit columns/indexes unless asserted.

## Phase 9: round-trip, failure, security, and AOT verification

### Intended files and interfaces

- Add `KnownFirst.Tests/BackupRoundTripTests.cs`.
- Add `KnownFirst.Tests/BackupRestoreFailureTests.cs`.
- Complete `BackupArchiveSecurityTests.cs` with a reusable malicious-archive
  builder that writes only test temporary files.
- Add `KnownFirst.Tests/BackupAotJsonTests.cs`.
- Add synthetic, privacy-safe golden archives under
  `KnownFirst.Tests/Fixtures/Backup/`; never derive them from a user database.
- Add a semantic snapshot comparer that ignores database PK and ZIP metadata
  but compares every v1 field and relationship.

### Data flow

Synthetic current/legacy fixture -> migrate if applicable -> create archive ->
validate/preview -> ReplaceAll into a different temporary database -> create a
second archive -> compare decoded semantic graphs. Separately, mutate one
bounded aspect at a time to verify deterministic rejection or rollback.

### Transaction boundary

Tests use only isolated temporary SQLite files through the production database
abstraction. Failure fakes wrap the restore repository/connection boundary and
prove rollback. No test opens an application-data path.

### Error behavior

Every negative case asserts the stable code, absence of private data in the
message/log, unchanged target graph, and cleanup/retention behavior for
temporary and safety files. Test failures must not dump complete payloads.

### Automated tests

At minimum, cover:

- empty state and a fully populated state;
- English/German Unicode, combining characters, CRLF/LF, quotes, and maximum
  legal coordinate edges;
- all enum values and nullable branches;
- minimal Known/Ignored markers, active Unknown, prepared/learning/mastered,
  legacy review aggregates, two card directions, every rating, suspended and
  retired cards;
- active/completed/cancelled workflows, Undo values, failed lookup draft,
  selected alternative meaning, revealed/typed state, and Again repeat;
- archive/source/target version combinations allowed by the contract;
- checksum byte changes, truncation, duplicate fields/IDs, orphan references,
  invalid counts/ranges/timestamps/enums, and future features/version;
- exact and one-byte-over limits for archive, entries, ratio, depth, strings,
  documents, vocabulary, occurrences, and aggregate record count;
- all restore failure-injection points and safety-backup recovery;
- repeated backup creation and restore without duplicate domain rows; and
- reflection-disabled serialization of every production backup type.

A later explicitly authorized implementation task should also run the complete
affected .NET suite, Windows Debug build, and the project-required Android
Release/AOT verification. No APK/AAB or device result is implied by unit tests.

### Acceptance gate

All focused and complete affected automated tests pass; every supported golden
v1 archive remains readable; malicious input stays bounded; every injected
restore failure rolls back; and source inspection plus reflection-disabled
tests prove generated JSON usage. Required platform builds are recorded
separately when authorized.

### Main risks

- Comparing only row counts can miss broken relationships or altered text.
- Comparing database PKs would make valid reconstruction appear broken.
- Golden fixtures can freeze an accidental format unless reviewed against this
  contract.
- Unit tests alone cannot prove trimming/AOT or mobile memory behavior.

## Phase 10: later backup/restore UI

This phase is intentionally last and is not part of the first service-layer
implementation.

### Intended files and interfaces

- Update `Components/Pages/Settings.razor` and its scoped CSS, or add a focused
  Data Safety page linked from Settings.
- Add localized English/German resource strings for create, preview, ReplaceAll
  confirmation, progress, errors, safety-backup location/retention, and success.
- Add platform-neutral file-picker/saver interfaces with Windows and Android
  adapters; UI passes streams to the service and never exposes raw filesystem
  assumptions to domain code.
- Register adapters in `MauiProgram.cs`.
- Extend `KnownFirst.Tests/UiWorkflowContractTests.cs` and the manual GUI matrix.

### Data flow

Create: user chooses destination -> service creates and verifies archive -> UI
reports success only after final placement.

Restore: user chooses archive -> read-only validation/preview -> UI displays
source metadata, counts, exclusions, and overwrite scope -> explicit destructive
confirmation -> service revalidates, safety-backs up, and ReplaceAll restores ->
UI reloads workflow state after committed success.

### Transaction boundary

The UI owns no transaction and never calls `ResetAsync`. It disables duplicate
submission/navigation during the short final operation but delegates all
atomicity to `IBackupRestoreService`. File picker cancellation occurs before
mutation.

### Error behavior

Map stable service codes to localized, actionable messages. Do not display raw
JSON, SQL, paths beyond a user-selected display name, document text, or stack
traces. Distinguish invalid/unsupported backup, insufficient space, safety
backup failure, rolled-back restore failure, and uncertain recovery state.

### Automated and manual tests

- UI contract tests for preview-before-confirmation, explicit ReplaceAll scope,
  double-submit prevention, progress, cancellation, and no success-before-commit.
- Localization completeness and accessible names/live-region behavior.
- Keyboard, focus restoration, screen-size, safe-area, and Android Back flows.
- Manual Windows and Android file-provider tests with synthetic backups only.
- A failed restore returns to a usable state and points to the retained safety
  backup without exposing private content.

### Acceptance gate

No restore can start without a fresh valid preview and explicit confirmation;
all errors are localized and safe; the UI remains responsive/accessibly
navigable; and Windows/Android synthetic-file workflows are manually recorded.

### Main risks

- Platform file providers may return non-seekable streams or revoke permission;
  the service must stage within limits.
- UI confirmation text can mislead if it says "all settings"; v1 leaves
  Preferences and logs unchanged and clears only cache plus included SQLite
  state.
- Navigation during restore can create duplicate calls unless guarded at UI
  and service layers.

## Cross-phase definition of done

Backup and restore v1 is not complete until all of the following are true:

- the format and supported database source range are accepted and documented;
- format DTOs remain independent from entity/table names and internal JSON;
- every production JSON path is source-generated and reflection-disabled tests
  pass;
- backup creation is consistent, bounded, checksum-protected, and verifies its
  own output;
- preview is demonstrably read-only;
- `ReplaceAll` always creates a validated safety backup and commits or rolls
  back all included SQLite state;
- future versions, unknown enums/features, corrupt graphs, and hostile ZIPs are
  rejected before mutation;
- exact strings, coordinates, meanings, relationships, schedules, history, and
  resumable workflows survive semantic round trip;
- migration fixtures cover the evidence-backed supported range and a future
  schema refusal;
- Preferences, consent, logs, secrets, and real user data never enter fixtures
  or archives; and
- the UI, if included in that later work package, accurately describes v1 scope
  and has recorded platform validation.

The next smallest implementation work package after plan approval is Phase 1
only: external models, strict enum/scalar contracts, centralized limits/errors,
and model-contract tests. It performs no JSON/file/database operation and does
not add UI.
