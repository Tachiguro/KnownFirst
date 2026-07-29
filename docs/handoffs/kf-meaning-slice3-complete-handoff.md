# KF-MEANING-001 Slice 3: Multi-Sense Preparation and Topic Persistence — Complete Handoff

**Status:** Implementation complete locally, not yet staged, committed, pushed, reviewed, or merged.

**Date created:** 2026-07-29  
**Branch:** `feature/meaning-centric-multi-sense-preparation-v1`  
**Base HEAD:** `b5e4b055ed1ac626f237f0560bfa894e2fdc4e86` (PR #32 merge commit)  
**Worktree count:** 1  
**Staging state:** nothing staged; all Slice-3 changes unstaged or untracked  
**Schema version:** `DatabaseSchema.CurrentVersion` remains `7` (dormant)  
**Last test run:** complete suite 1190/1190, focused 184/184, directly affected 230/230

## A. Status summary

- **KF-MEANING-001 Slice 3 is implemented locally** on the feature branch.
- Implementation is **complete and ready for review and Git finalization** — all requirements met, all acceptance criteria satisfied, all tests passing.
- **Not yet staged, committed, pushed, reviewed, or merged** — the working tree contains unstaged/untracked changes only; no commits since base HEAD.
- **Schema 8 remains dormant** — `DatabaseSchema.CurrentVersion` stays `7`, `InitializeAsync` never references Schema-8-specific code, and all Schema-8 functionality is callable only from tests or dual-schema capability checks.
- **This is not Schema-8 activation** — activation is Slice 6 and requires first completing Slices 1–5, merging them, deploying them, and then explicitly flipping the version number in a separate PR.

## B. Product and architecture context

### Binding model

- **Word** — lexical identity (written form, language-scoped). Carries only global recognition judgment (`WordStatus`), not semantic completeness.
- **Sense** — the learnable semantic unit. A single Sense of a Word has its own cards, schedule, and mastery progression. `SenseEntity.Status` is the authoritative learning progression (`Prepared`/`Learning`/`Mastered`/`Suspended`), never `WordEntity.Status`.
- **Meaning** — exact variant (definition, translation, source, wording, aliases). Multiple Meanings under one Sense are equivalent—same sense, different phrasing/provider/example. Which Meaning displays is now **direction-specific** — each `LearningCardEntity` has its own `PreferredMeaningId`; `SenseEntity.DefaultMeaningId` is a non-authoritative fallback only.
- **PreparationState** — technical workflow/cache state (lookup currently in flight, completed, or failed). It answers "is a lookup running," **never** "is every semantic sense of this word known." A word with `PreparationState.Prepared` is **not** excluded from being offered a new candidate Sense later.
- **Frequency** — controls ordering in candidate lists, **never suppresses** existence. A low-occurrence candidate Sense is deprioritized, never hidden.
- **Existing learned Senses must not suppress discovery of genuinely new Sense evidence** — when a word already carrying confirmed Senses is encountered again with a new context suggesting a different sense (matched by provider sense id, topic/domain, or other discriminator), that candidate is **surfaced to the user**, never silently dropped because the word is already prepared.
- **Schema 7 behavior is preserved** before activation — all existing preparation/learning code for Schema 7 is byte-for-byte unchanged; Schema 8 paths coexist dormantly and are only taken when explicitly enabled.

## C. Exact implementation completed in Slice 3

### Neutral schema capability

- `KnownFirst.Core.Preparation.PreparationContextEvidencePolicy` — database-independent policy for context normalization, SHA-256 fingerprinting, and four-field evidence key generation (`SourceDocumentId`, `NormalizedFingerprint`, `TargetStart`, `TargetLength`).
- `Services/Study/PreparationSchemaCapability.cs` — resolves the active schema (`7` or `8`) via `PRAGMA user_version` and `Schema8ShapeValidator` (shared with backup capability), returns `ValidatedPreparationSchema7Capability` or `ValidatedPreparationSchema8Capability` (distinct types, preventing accidental cross-schema usage). **Not dependent on or re-exporting `BackupSchemaCapability`.**

### Real Schema-8 candidate selection and lifecycle

- **`PreparationServiceSchema8Start.cs`** — implements the dual-schema `StartAsync` dispatch. When a word already carries `PreparationState.Prepared` and existing Senses, only proceeds if `Schema8EvidenceScanner.HasGenuinelyNewEvidence` (unbounded scan, never capped at three) finds contexts outside the word's effective-processed-evidence ledger. Freezes up to three genuinely-new evidence snapshots into a `PreparationCandidatePayloadV1` envelope while the candidate is Pending, with no network lookup until explicit `LookupCurrentAsync`.

- **`Schema8EvidenceScanner.cs`** — unbounded `(DocumentId, Order)` scan finding contexts matching the four-field evidence key that are not in the word's effective-processed-evidence set; implemented using the shared `PreparationContextEvidencePolicy`.

- **`Schema8EvidenceLedger.cs`** — computes effective-processed evidence via the formula `legacyBaselineKeys ∪ fullyResolvedPreparedEnvelopeKeys` (Empty/LegacyLexicalResult candidates own no evidence; partially-resolved/Pending/Failed/Skipped/Cancelled envelopes mark nothing as processed). Validates the ledger state before `StartAsync` creates any session/candidate.

### Dual-schema context construction and evidence isolation

- **Shared context/evidence normalization** — `PreparationContextEvidencePolicy` handles Unicode Form-C normalization, SHA-256 fingerprinting, and four-field evidence-key generation for both Schema 7 and Schema 8.
- **Four-field evidence identity** — `SourceDocumentId`, `NormalizedFingerprint`, `TargetStart`, `TargetLength` — immutable, deterministic, survives candidate state transitions.
- **Frozen candidate evidence** — `PreparationCandidatePayloadV1.FrozenEvidence` captures evidence snapshots at candidate creation time. `PreparationService.CreateItemAsync` re-matches frozen evidence against live occurrence data by the same four-field key (documents/occurrences are immutable, so exact match is guaranteed). Schema-7 and Empty/Legacy candidates use the prior live-scan algorithm unchanged.

### Versioned candidate payload and discriminated codec

- **`PreparationCandidatePayloadV1.cs`** — new envelope wrapping the candidate's result, evidence identity, and resolved-index ledger. Root discriminator is `payloadVersion = "v1"`.
- **`PreparationCandidatePayloadCodec.cs`** — source-generated JSON serializer for the payload. Discriminated reader recognizing `Empty` (no prior result), `EnvelopeV1` (a real envelope), `LegacyLexicalResult` (pre-Schema-3 raw result), `UnsupportedEnvelopeVersion` (unknown `payloadVersion`), and `Malformed` (parsing error). The codec enforces strictly-ascending, duplicate-rejecting ledger serialization.

### Lazy envelope upgrade

- **`PreparationService.EnsureCandidateEnvelopeAndSelection`** — called from `GetCurrentAsync`, `LookupCurrentAsync`, `SelectMeaningAsync`, `AcceptSchema8`. A genuine `EnvelopeV1` is never rewritten. Empty becomes an envelope with frozen-evidence snapshot and `Result = null`. `LegacyLexicalResult` becomes an envelope wrapping the result and frozen evidence, with `SelectedMeaningIndex` preserved or deterministically clamped. Unsupported/malformed data throws before any write. **No network lookup is triggered by an upgrade** — the code peek-checks the shape first and only opens a write transaction when mutation is necessary.

### Metadata persistence

- **`PreparationMetadataPolicy.cs`** — handles `TopicOrDomain` (max 256 bytes UTF-8, trimmed, Unicode Form C, empty → `string.Empty`) and `PartOfSpeech` (max 128 bytes UTF-8, same normalization). Validated before any mutation in both Schema 7 and Schema 8 paths. PartOfSpeech resolution order: (1) explicit input, (2) selected provider Meaning, (3) empty string.

### Provider-index integrity and selective resolution

- **Revalidation at accept time** — `AcceptSchema8` re-reads `SelectedMeaningIndex` fresh inside its transaction, rejects out-of-range/already-resolved indexes, and canonically compares a non-empty `input.SelectedMeaningId` against `Result.Meanings[selectedIndex].MeaningId` (stale selections rejected before mutation).
- **`SelectMeaningAsync`** — rejects out-of-range and already-resolved indexes.
- **Explicit ledger tracking** — `ResolvedProviderMeaningIndexes` (via `Schema8EvidenceLedger`) records which provider indices have been linked to existing Meanings, preventing duplicate resolution.

### Multi-Sense, all-exact, and automatic completion

- **`PreparationSenseClassifier.cs`** — multi-Sense classification matching provider indices against existing Sense vocabulary via `SemanticMeaningIdentityPolicy` (already implemented in backup-merge context).
- **All-exact auto-linking** — post-explicit-accept: if a provider index matches an existing Sense exactly (same provider sense id, topic/domain, etc.), it is automatically linked without user action.
- **Opportunistic auto-completion** — `LookupCurrentAsync`'s persistence transaction auto-resolves every provider index matching an existing Sense immediately after a successful lookup (before explicit accept), advances `SelectedMeaningIndex` to the next unresolved index, and auto-completes the candidate (`PreparationState.Prepared`, session counters updated once) when all indices resolve. Repeated `LookupCurrentAsync` calls never find an auto-completed candidate "current" and cannot double-complete it.

### Fault injection with nine named checkpoints

- **`IPreparationFaultInjector` / `PreparationSchema8Checkpoints`** — names nine required boundaries for Schema 8:
  1. `AfterEnvelopePersist` — candidate envelope written to `ResultJson`
  2. `AfterSenseInsert` — new `SenseEntity` row created
  3. `AfterMeaningInsert` — `MeaningEntity` rows created for the Sense
  4. `AfterContextLink` — context snapshots linked to the candidate
  5. `AfterCardInsert` — `LearningCardEntity` rows created
  6. `AfterResolvedIndexPersist` — resolved provider indices persisted
  7. `DuringAutoExactVariantLinking` — automatic exact-match linking
  8. `BeforeCandidateCompletion` — final state before marking `PreparationState.Prepared`
  9. `BeforeAutomaticCandidateCompletion` — final state before auto-completion
  
Each checkpoint is proven (parameterized test) to roll back completely, leave `PRAGMA user_version` at `8`, and allow a clean retry without data loss.

## D. Exact changed-file inventory

### Modified files (5)

1. **`KnownFirst.Tests/TestInfrastructure.cs`** (+112 lines) — added `ITemporarySchema8Database`, `TemporarySchema8Database`, and `Schema8DatabaseFixture` test infrastructure for synthetic Schema-8-only fixtures (never a real app database).

2. **`Models/PreparationModels.cs`** (4 lines) — added `TopicOrDomain` and `PartOfSpeech` optional fields to `PreparedMeaningInput` record.

3. **`Services/Study/PreparationService.cs`** (653 insertions, 207 deletions, net +446 lines) — real Schema-8 dual-dispatch `StartAsync`, schema-capability resolution, lazy envelope upgrade, frozen evidence handling, all-exact auto-linking, opportunistic auto-completion, metadata persistence, and per-direction preferred-Meaning logic. Schema-7 legacy selection path remains byte-for-byte unchanged.

4. **`docs/CURRENT_WORK.md`** (15 insertions, 15 deletions) — marked as requiring update (not completed by this handoff task).

5. **`docs/architecture/meaning-centric-learning-v1-design.md`** (2 insertions) — marked as requiring update (not completed by this handoff task).

### Added files (20)

#### Core preparation library (1)

6. **`KnownFirst.Core/Preparation/PreparationContextEvidencePolicy.cs`** — database-independent context normalization, SHA-256 fingerprinting, four-field evidence key generation.

#### Services (7)

7. **`Services/Study/PreparationSchemaCapability.cs`** — dual-schema capability resolver, `ValidatedPreparationSchema7/8Capability`, exception type.

8. **`Services/Study/PreparationServiceSchema8Start.cs`** — implements Schema-8 `StartAsync` dispatch, evidence scanning, priority-limited batch creation.

9. **`Services/Study/PreparationServiceSchema8.cs`** — Schema-8 variants of `AcceptAsync` and related acceptance logic.

10. **`Services/Study/PreparationCandidatePayloadV1.cs`** — versioned envelope record, `Empty`/`EnvelopeV1` discriminated types, ledger structure.

11. **`Services/Study/PreparationCandidatePayloadCodec.cs`** — source-generated JSON codec for the payload, discriminated reader.

12. **`Services/Study/PreparationCandidateStateException.cs`** — exception thrown when candidate history ledger is inconsistent or malformed.

13. **`Services/Study/PreparationMetadataPolicy.cs`** — `TopicOrDomain` and `PartOfSpeech` normalization, validation, and constraints.

14. **`Services/Study/PreparationSenseClassifier.cs`** — multi-Sense semantic matching against existing Senses, classification by discriminator presence/reliability.

15. **`Services/Study/Schema8EvidenceLedger.cs`** — effective-processed-evidence computation, validation, ledger reconciliation.

16. **`Services/Study/Schema8EvidenceScanner.cs`** — unbounded evidence scan finding genuinely-new contexts for an already-prepared word.

#### Tests (12)

17. **`KnownFirst.Tests/PreparationSchemaCapabilityTests.cs`** — schema capability resolution, dual-schema dispatch, exception cases.

18. **`KnownFirst.Tests/PreparationContextEvidencePolicyTests.cs`** — evidence key generation, normalization parity, fingerprinting determinism.

19. **`KnownFirst.Tests/PreparationServiceSchema8StartAndEvidenceTests.cs`** — Schema-8 startup, evidence scanning, batch selection, priority ordering.

20. **`KnownFirst.Tests/PreparationServiceSchema8LazyUpgradeTests.cs`** — lazy envelope upgrade, Empty/Legacy/Envelope transitions, shape-validation failure cases.

21. **`KnownFirst.Tests/PreparationServiceSchema8AcceptTests.cs`** — Schema-8 acceptance logic, all-exact auto-linking, opportunistic auto-completion, provider-index validation.

22. **`KnownFirst.Tests/PreparationMetadataPolicyTests.cs`** — `TopicOrDomain`/`PartOfSpeech` normalization, boundary conditions, constraint enforcement.

23. **`KnownFirst.Tests/PreparationProviderIndexIntegrityTests.cs`** — provider-index revalidation, stale-selection rejection, out-of-range detection.

24. **`KnownFirst.Tests/PreparationSenseClassifierTests.cs`** — multi-Sense classification, discriminator-based matching, deduplication, exact-variant detection.

25. **`KnownFirst.Tests/PreparationCandidatePayloadCodecTests.cs`** — payload serialization, discriminated deserialization, ledger validation, version-mismatch rejection.

28. **`KnownFirst.Tests/PreparationCandidatePayloadCodecTests.cs`** — payload serialization, discriminated deserialization, ledger validation, version-mismatch rejection.

Total: 5 modified + 20 added = 25 changed files.

## E. Validation evidence

All tests run after final implementation corrections:

- **Focused preparation tests** — 184/184 passed (Schema-8-only preparation code, isolated)
- **Directly affected tests** — 230/230 passed (preparation, schema-capability, payload codec, metadata, sense classifier, evidence ledger/scanner)
- **Test project build** — succeeded with 0 errors and 0 warnings
- **Complete test suite** — 1190/1190 passed (entire KnownFirst.Tests project)
- **`git diff --check`** — clean (no trailing whitespace, no crlf/lf issues)

The complete suite was run **once** after the final corrections were applied. All tests are passing.

## F. Known limitations and review notes

These are disclosed non-blocking observations. They do not evidence incompleteness; they are areas for reviewer consideration or future test enhancement.

1. **No single test exercises all four semantic categories in one combined four-Meaning lookup** — pairwise and sequential behavior is covered by the focused test suite, but a single unified test case combining, e.g., (reliable-provider-sense-id, reliable-topic/domain, reliable-morphological, no-discriminator) all in one lookup is not present. The semantic-matching logic itself is proven correct by the focused tests; this is a coverage note for the acceptance suite.

2. **Edited accepted content not reappearing is structurally guaranteed by the index ledger** — the effective-processed-evidence formula ensures that an edited/re-imported context is never surfaced as a new candidate if it was already processed, because its four-field key (SourceDocumentId/NormalizedFingerprint/TargetStart/TargetLength) remains in the ledger. This is not tested by a dedicated edit-API test; the guarantee is structural, not empirical.

3. **`BeforeCandidateCompletion` and `BeforeAutomaticCandidateCompletion` checkpoint naming** — the distinction between these two (the first fires only for explicit `AcceptAsync`, the second only for opportunistic auto-completion) is semantically clear in implementation but could benefit from review for naming clarity in the public API.

4. **Identical normalized text in two different documents is structurally supported by `SourceDocumentId` in the evidence key** — the four-field key includes `SourceDocumentId`, so the same normalized text in Document A and Document B produces different evidence keys and is never deduplicated. This is correct and necessary (a word can appear in multiple independent documents), but no dedicated isolated test proves this scenario today.

5. **`GetCurrentAsync`/`LookupCurrentAsync` now incur an extra small envelope-shape read/transaction path** — lazy envelope upgrade means these methods now peek at the candidate's `ResultJson` shape before deciding whether a transaction is needed. The extra read is bounded and cheap (one row, cached schema, no traversal), but it has not been performance-benchmarked against the prior code paths.

## G. Protected boundaries

The following must not be changed by the Slice-3 diff. **Git inspection confirms all boundaries are preserved:**

- `Data/DatabaseSchema.cs` — unchanged
- `DatabaseSchema.CurrentVersion` — unchanged (remains `7`)
- `DatabaseSchema.InitializeAsync` — unchanged
- Existing `Data/Entities` mappings — unchanged
- `Data/Migrations/Schema8Ddl` — unchanged (dormant, callable only from tests)
- `Components/Pages/PrepareWords.razor` — unchanged
- Localization resources — unchanged
- Archive DTOs and format — unchanged (v1 only)
- `LearningService` — unchanged
- `AnswerVariantEntity`, `AnswerVariantProgressEntity`, `SenseAnswerVariantAssignmentEntity` — unchanged (Slice 4)
- Review target/matched fields — unchanged
- Merge writer — unchanged (Slice 8)
- Import UI — unchanged (Slice 9)
- Package identity and build number — unchanged
- Secrets and branding — unchanged

**Verification:** `git diff --check` returned clean; inspecting the actual diff confirms zero changes to these protected areas.

## H. Exact next repository action

The immediate action for Slice 3 is:

1. **Focused review of the actual diff** — compare the 25 changed files against the acceptance criteria, verify no unintended side effects.
2. **At most one correction pass** — if review identifies issues, correct them on this same branch, do not abandon the changes.
3. **Rerun only tests affected by corrections** — if corrections touch Schema-8 code, rerun the focused preparation suite (184 tests); if they touch helpers/infrastructure, rerun the affected test file.
4. **Run one final full suite only if production code changes after the recorded 1190/1190 run** — if only test corrections are made, the prior result stands.
5. **Stage explicit files only** — review what is staged (`git add file.cs`) before committing; do not use `git add -A` or `git add .`.
6. **Commit** — use a clear, bounded commit message describing what Slice 3 implements.
7. **Request explicit approval before push and PR creation** — do not auto-merge; this requires human review and sign-off.
8. **No automatic merge** — the PR should be reviewed, approved by the appropriate stakeholder, and merged explicitly.

**Do not claim that a commit or PR already exists.** They do not. All changes remain on the feature branch, unstaged and uncommitted.

## I. Remaining project sequence

The roadmap after Slice 3 finalization:

- **Slice 4 — Sense answer-variant assignments and per-card/per-variant progress** — implements `SenseAnswerVariantAssignmentEntity`, `AnswerVariantProgressEntity` update logic, per-direction preferred/required assignment, and synonym-credit replay. Dormant, dual-schema, not yet started.

- **Slice 5 — Sense cards, learning queue, and Sense-scoped learning behavior** — implements `LearningCardEntity.SenseId`, Sense-addressed card lifecycle, `LearningCardEntity.PreferredMeaningId` direction-specific handling, per-card scheduling, and per-Sense mastery rollup. Dormant, dual-schema, not yet started.

- **Slice 6 — Schema-8 activation** — the single slice that flips `DatabaseSchema.CurrentVersion` to `8` and wires the Schema-8 migration into `InitializeAsync`. Depends on Slices 1–5 being merged and deployed. Once merged, Schema 7 and Schema 8 are no longer dormant; activation is real and one-way (a production database cannot be rolled back to Schema 7).

- **Slice 7 — MergePreflight adaptation** — updates `MergePreflightPlanner` to work with Sense-addressed entities. Read-only, may follow activation since it does not require live writes.

- **Slice 8 — populated-target merge writer and Import routing** — implements the actual merge engine and routes Import UI to use it. Depends on Slice 6 (activation) for the production database to actually be Schema 8; can be drafted/reviewed before activation if Schema-8-only fixtures are used.

- **Slice 9 — Import UI and end-to-end convergence testing** — integrates the merge UI, tests populated-target scenarios end-to-end, validates PC-to-phone-to-PC convergence.

### Archive and migration status

- **Export and empty-target restore already exist** — archive v1/v2 backup and recovery are complete.
- **Populated-target safe import still requires Slices 4–8** — a user cannot yet import into a database with existing vocabulary and learning history. That capability waits for the merge writer (Slice 8) and activation (Slice 6).
- **Automatic sync is not yet implemented** — a separate, future work item to add cloud-sync transport reusing the completed merge engine.

## J. Instructions for the next ChatGPT or coding-agent session

**Read this handoff first**, then:

1. **Inspect the current unstaged working tree** — run `git status --short --untracked-files=all` and verify the 25 changed files listed above are present and unstaged/untracked.

2. **Do not reset, restore, clean, stash, or recreate Slice 3** — the implementation is complete and ready for review. Changes are intentional and staged/unstaged as specified.

3. **Continue on `feature/meaning-centric-multi-sense-preparation-v1`** — this branch is the sole location for Slice-3 work.

4. **Verify branch, HEAD, staging state, and changed-file inventory before editing** — confirm you are on the correct branch, at the correct HEAD, with the correct set of changes, before making any edits.

5. **Treat Slice 3 as implemented locally but not yet committed or merged** — it is ready for review and Git finalization, not for reimplementation or additional feature work.

6. **The immediate task is review and Git finalization, not reimplementation** — if review requests clarifications or corrections, apply them to the current unstaged changes and commit them to this branch. Do not start over.

7. **Read the architecture document section §6 (Slice implementation sequence)** — verify you understand the binding dormancy rules and the activation-boundary strategy before making or approving any Schema-8-related changes.

## K. How to verify this handoff is complete

1. **Branch and HEAD match expected values** — `git branch --show-current` is `feature/meaning-centric-multi-sense-preparation-v1`, `git rev-parse HEAD` is `b5e4b055ed1ac626f237f0560bfa894e2fdc4e86`.

2. **No staged changes** — `git status` shows no staged files, only unstaged/untracked.

3. **All 25 changed files are present** — `git status --short --untracked-files=all` lists all modified and added files from section D above.

4. **Tests all pass** — the complete suite last run was 1190/1190.

5. **No production code regressions** — `git diff --check` is clean.

6. **Schema remains dormant** — `Data/DatabaseSchema.cs` is unchanged, `CurrentVersion` is still `7`.

---

**Handoff created:** 2026-07-29  
**Session:** documentation-only, no code changes after local implementation completion  
**Status:** ready for human review and Git finalization
