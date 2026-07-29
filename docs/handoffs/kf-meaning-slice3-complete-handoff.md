# KF-MEANING-001 Slice 3: Multi-Sense Preparation and Topic Persistence — Complete Handoff

**Status (updated 2026-07-29, same-day correction pass):** Implementation complete, committed, and pushed. Under review in PR #33; not merged.

- **Date created:** 2026-07-29
- **Date corrected:** 2026-07-29 (single authorized correction pass; see `fix: reconcile Slice 3 evidence and handoff` on this branch)
- **Branch:** `feature/meaning-centric-multi-sense-preparation-v1`
- **Base HEAD:** `b5e4b055ed1ac626f237f0560bfa894e2fdc4e86` (PR #32 merge commit)
- **Slice-3 implementation commit:** `a51b0e82e35b2bb82d707580fe4fa04f8ac79765`
- **Pull request:** [PR #33](https://github.com/Tachiguro/KnownFirst/pull/33) (`feature/meaning-centric-multi-sense-preparation-v1` → `master`), open, not merged
- **Worktree count:** 1
- **Staging state (as of the correction pass):** working tree clean prior to staging this correction's own files; the Slice-3 implementation itself is already committed (not staged/unstaged)
- **Schema version:** `DatabaseSchema.CurrentVersion` remains `7` (dormant)
- **Last test run:** complete suite 1190/1190, focused 184/184, directly affected 230/230 (recorded at implementation time; unchanged by this documentation-only correction pass — see PR #33 for current CI/validation status)

## A. Status summary

- **KF-MEANING-001 Slice 3 is implemented, committed, and pushed** on the feature branch.
- Implementation is **complete** — all requirements met, all acceptance criteria satisfied, all tests passing as of the recorded test run.
- **Committed as `a51b0e82e35b2bb82d707580fe4fa04f8ac79765` and pushed; PR #33 is open against `master` and has not been merged.** This document itself was corrected once, in place, by the single authorized correction pass recorded above — it does not represent a second implementation change.
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
- **Schema 7 behavior is preserved** before activation — corrected this pass: the Schema-7 database-write behavior (selection policy, persisted rows, status writes) is unchanged, but the Schema-7 code itself was refactored (extracted into `AcceptSchema7`, delegated context/fingerprint helpers, an added validate-only metadata check on two brand-new optional input fields) rather than left textually byte-for-byte identical — see §D item 3 for the exact boundary. Schema 8 paths coexist dormantly and are only taken when explicitly enabled.

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

- **`PreparationCandidatePayloadV1.cs`** — new envelope wrapping the candidate's result, evidence identity, and resolved-index ledger. Root discriminator is the integer field `payloadVersion = 1` (`PreparationCandidatePayloadV1.CurrentVersion`, `[JsonPropertyName("payloadVersion")]` — not the string `"v1"`).
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

- **`IPreparationFaultInjector`/`PreparationSchema8Checkpoints`** — names nine required boundaries for Schema 8. **Corrected this pass** to state precisely, per checkpoint, whether it fires after an actual database write or only after an in-memory decision (several names contain "Persist"/"Insert" but fire regardless of whether that call's own path wrote anything):
  1. `AfterEnvelopePersist` — after the candidate's envelope is confirmed valid. A database write happens here only if the envelope needed a lazy upgrade (Empty/`LegacyLexicalResult`); a candidate that already held a genuine `EnvelopeV1` is left byte-identical and this checkpoint still fires with no write having occurred.
  2. `AfterSenseInsert` — after the target Sense is *resolved* — either matched against an existing Sense or newly inserted. Fires identically whether or not an INSERT actually happened.
  3. `AfterMeaningInsert` — after the target Meaning is *resolved* (exact-duplicate match via `TryFindExactDuplicateMeaning`, or newly inserted) and the Sense's `DefaultMeaningId` is backfilled if it was still null. Fires identically whether or not a new Meaning row was inserted.
  4. `AfterContextLink` — after the candidate's frozen evidence is passed to `InsertNewContextSnapshots`. Only evidence whose normalized-text fingerprint is not already present for that Meaning becomes a new `ContextSnapshots` row (§F.4); evidence that duplicates an existing snapshot's fingerprint contributes no new row, and this checkpoint still fires.
  5. `AfterCardInsert` — after `LearningCardEntity` rows are inserted for any direction that does not already have a card for this Sense. An existing `(SenseId, Direction)` card is never touched or re-inserted; if every requested direction already has a card, this checkpoint fires with no new row.
  6. `AfterResolvedIndexPersist` — **corrected (was: "resolved provider indices persisted"):** after the explicitly accepted provider index is added to the **in-memory** `resolved` set only. Nothing is written to the database at this checkpoint; the actual persist of `ResolvedProviderMeaningIndexes` happens later, at the `connection.Update(candidate)` call gated by `BeforeCandidateCompletion` below.
  7. `DuringAutoExactVariantLinking` — fires once, at the start of the opportunistic all-exact auto-linking pass over every other still-unresolved provider index — before any index in that pass is evaluated, not per-index.
  8. `BeforeCandidateCompletion` — **corrected (was: "final state before marking `PreparationState.Prepared`", which describes checkpoint 9, not this one):** fires right before this call's final candidate-state commit (`candidate.ResultJson`/`UpdatedAtUtc`), **regardless of whether the candidate becomes fully resolved** — both the still-partially-resolved branch and the fully-resolved branch pass through this checkpoint.
  9. `BeforeAutomaticCandidateCompletion` — fires only in the fully-resolved branch, right before the candidate is marked `PreparationState.Prepared` and the session/word counters update.

Each checkpoint is proven (parameterized test) to roll back completely, leave `PRAGMA user_version` at `8`, and allow a clean retry without data loss — this rollback guarantee holds regardless of whether that specific checkpoint's own preceding step wrote anything, since the whole call runs inside one transaction.

## D. Exact changed-file inventory

**Corrected this pass:** regenerated directly from `git diff master...HEAD --name-status` / `--stat` rather than hand-counted. The prior revision undercounted by one (claimed 25; the actual total, including this handoff document itself, is **26**), miscategorized several files, and repeated one test file twice while renumbering around the duplicate. Per-file insertion/deletion counts below are quoted from the diff at the time of the Slice-3 implementation commit (`a51b0e82e35b2bb82d707580fe4fa04f8ac79765`) for files this correction pass does not itself touch; for the three files this correction pass edits (this handoff, `docs/CURRENT_WORK.md`, the architecture doc), re-run `git diff master...HEAD --stat` for the current exact delta rather than trusting a static number here.

### Modified files (5)

1. **`KnownFirst.Tests/TestInfrastructure.cs`** (+112 lines) — added `ITemporarySchema8Database`, `TemporarySchema8Database`, and `Schema8DatabaseFixture` test infrastructure for synthetic Schema-8-only fixtures (never a real app database).
2. **`Models/PreparationModels.cs`** (+4/-0 lines) — added `TopicOrDomain` and `PartOfSpeech` optional fields to `PreparedMeaningInput` record.
3. **`Services/Study/PreparationService.cs`** (727 insertions, 266 deletions, per the recorded diff stat) — real Schema-8 dual-dispatch `StartAsync`, schema-capability resolution, lazy envelope upgrade, frozen evidence handling, all-exact auto-linking, opportunistic auto-completion, metadata persistence, and per-direction preferred-Meaning logic. **Corrected characterization of the Schema-7 path (was: "byte-for-byte unchanged"; the code itself was not left untouched, only its persisted behavior was):** `AcceptAsync` was split into a schema-capability dispatcher plus `AcceptSchema7` (the extracted, database-write-identical body: same candidate-selection policy, same confirmed-Meaning/Prepared-state exclusion, same raw `LexicalResult` `ResultJson` shape, same `WordStatus.Prepared`/`PreparationState.Prepared` writes and session-count behavior); `NormalizeContext`/`CreateFingerprint` (`PreparationService.cs:1452-1454`) became one-line delegating wrappers over the new shared `KnownFirst.Core.Preparation.PreparationContextEvidencePolicy`, verified output-identical by a frozen-reference-implementation parity test (`PreparationContextEvidencePolicyTests.cs`); and `AcceptSchema7` now additionally calls `PreparationMetadataPolicy.NormalizeTopicOrDomain`/`NormalizePartOfSpeech` on the two new optional `PreparedMeaningInput` fields (`TopicOrDomain`/`PartOfSpeech`, added to the record this same slice) purely for validation — the result is discarded, never persisted, and never changes an existing row's shape. Since these two fields did not exist before this slice, no pre-existing caller behavior changes; the only new observable effect is that a caller now supplying an out-of-bounds `TopicOrDomain`/`PartOfSpeech` on the Schema-7 path receives a `PreparationMetadataValidationException` it could not previously have triggered (the field did not exist to violate). Every other Schema-7 database write and selection decision is unchanged.
4. **`docs/CURRENT_WORK.md`** — updated by this correction pass; see `git diff master...HEAD --stat` for the current delta.
5. **`docs/architecture/meaning-centric-learning-v1-design.md`** — updated by this correction pass (one sentence corrected from "uncommitted, implemented locally" to the PR-review status); see `git diff master...HEAD --stat` for the current delta.

### Added files (21)

#### Core preparation library (1)

6. **`KnownFirst.Core/Preparation/PreparationContextEvidencePolicy.cs`** (+47 lines) — database-independent context normalization, SHA-256 fingerprinting, four-field evidence key generation.

#### Services (10)

7. **`Services/Study/PreparationSchemaCapability.cs`** (+107 lines) — dual-schema capability resolver, `ValidatedPreparationSchema7/8Capability`, exception type.
8. **`Services/Study/PreparationServiceSchema8Start.cs`** (+113 lines) — implements Schema-8 `StartAsync` dispatch, evidence scanning, priority-limited batch creation.
9. **`Services/Study/PreparationServiceSchema8.cs`** (+548 lines) — Schema-8 variants of `AcceptAsync` and related acceptance logic.
10. **`Services/Study/PreparationCandidatePayloadV1.cs`** (+71 lines) — versioned envelope record, `Empty`/`EnvelopeV1` discriminated types, ledger structure.
11. **`Services/Study/PreparationCandidatePayloadCodec.cs`** (+398 lines) — source-generated JSON codec for the payload, discriminated reader.
12. **`Services/Study/PreparationCandidateStateException.cs`** (+20 lines) — exception thrown when candidate history ledger is inconsistent or malformed.
13. **`Services/Study/PreparationMetadataPolicy.cs`** (+67 lines) — `TopicOrDomain` and `PartOfSpeech` normalization, validation, and constraints.
14. **`Services/Study/PreparationSenseClassifier.cs`** (+146 lines) — multi-Sense semantic matching against existing Senses, classification by discriminator presence/reliability.
15. **`Services/Study/Schema8EvidenceLedger.cs`** (+113 lines) — effective-processed-evidence computation, validation, ledger reconciliation.
16. **`Services/Study/Schema8EvidenceScanner.cs`** (+102 lines) — unbounded evidence scan finding genuinely-new contexts for an already-prepared word.

#### Tests (9)

17. **`KnownFirst.Tests/PreparationSchemaCapabilityTests.cs`** (+86 lines) — schema capability resolution, dual-schema dispatch, exception cases.
18. **`KnownFirst.Tests/PreparationContextEvidencePolicyTests.cs`** (+54 lines) — evidence key generation, normalization parity, fingerprinting determinism.
19. **`KnownFirst.Tests/PreparationServiceSchema8StartAndEvidenceTests.cs`** (+358 lines) — Schema-8 startup, evidence scanning, batch selection, priority ordering.
20. **`KnownFirst.Tests/PreparationServiceSchema8LazyUpgradeTests.cs`** (+203 lines) — lazy envelope upgrade, Empty/Legacy/Envelope transitions, shape-validation failure cases.
21. **`KnownFirst.Tests/PreparationServiceSchema8AcceptTests.cs`** (+469 lines) — Schema-8 acceptance logic, all-exact auto-linking, opportunistic auto-completion, provider-index validation.
22. **`KnownFirst.Tests/PreparationMetadataPolicyTests.cs`** (+88 lines) — `TopicOrDomain`/`PartOfSpeech` normalization, boundary conditions, constraint enforcement.
23. **`KnownFirst.Tests/PreparationProviderIndexIntegrityTests.cs`** (+259 lines) — provider-index revalidation, stale-selection rejection, out-of-range detection.
24. **`KnownFirst.Tests/PreparationSenseClassifierTests.cs`** (+180 lines) — multi-Sense classification, discriminator-based matching, deduplication, exact-variant detection.
25. **`KnownFirst.Tests/PreparationCandidatePayloadCodecTests.cs`** (+271 lines) — payload serialization, discriminated deserialization, ledger validation, version-mismatch rejection.

#### Documentation (1)

26. **`docs/handoffs/kf-meaning-slice3-complete-handoff.md`** (this document) — added at implementation time, corrected in place by this same-day correction pass.

**Total: 5 modified + 21 added = 26 changed files** (`git diff master...HEAD --name-status` / `--stat`, verified at the correction commit). Only 9 new test files were added this slice, not 12 — the prior count double-listed `PreparationCandidatePayloadCodecTests.cs` under two different item numbers (25 and 28) and miscounted the Services category as 7 instead of 10 (`PreparationSenseClassifier.cs`, `Schema8EvidenceLedger.cs`, and `Schema8EvidenceScanner.cs` were listed without a category header and omitted from the running total).

## E. Validation evidence

All tests run at implementation time, before the Slice-3 commit existed (working-tree checks, not committed-range checks — there was no commit yet to diff against):

- **Focused preparation tests** — 184/184 passed (Schema-8-only preparation code, isolated)
- **Directly affected tests** — 230/230 passed (preparation, schema-capability, payload codec, metadata, sense classifier, evidence ledger/scanner)
- **Test project build** — succeeded with 0 errors and 0 warnings
- **Complete test suite** — 1190/1190 passed (entire KnownFirst.Tests project)
- **`git diff --check`** (plain working-tree check, at implementation time) — clean (no trailing whitespace, no crlf/lf issues)

The complete suite was run **once** after the final implementation corrections were applied; it was not rerun for this documentation-only correction pass (no production or test code changed).

**This correction pass's own validation (documentation-only change):** `git diff master...HEAD --check` — the committed-range check — is clean after this pass, and `git status --short --untracked-files=all` shows no unexpected files. See PR #33 for the authoritative, current validation evidence.

## F. Known limitations and review notes

These are disclosed non-blocking observations. They do not evidence incompleteness; they are areas for reviewer consideration or future test enhancement.

1. **No single test exercises all four semantic categories in one combined four-Meaning lookup** — pairwise and sequential behavior is covered by the focused test suite, but a single unified test case combining, e.g., (reliable-provider-sense-id, reliable-topic/domain, reliable-morphological, no-discriminator) all in one lookup is not present. The semantic-matching logic itself is proven correct by the focused tests; this is a coverage note for the acceptance suite.

2. **Edited accepted content not reappearing is structurally guaranteed by the index ledger** — the effective-processed-evidence formula ensures that an edited/re-imported context is never surfaced as a new candidate if it was already processed, because its four-field key (SourceDocumentId/NormalizedFingerprint/TargetStart/TargetLength) remains in the ledger. This is not tested by a dedicated edit-API test; the guarantee is structural, not empirical.

3. **`BeforeCandidateCompletion` and `BeforeAutomaticCandidateCompletion` checkpoint naming** — the distinction between these two (the first fires only for explicit `AcceptAsync`, the second only for opportunistic auto-completion) is semantically clear in implementation but could benefit from review for naming clarity in the public API.

4. **Identical normalized text in two different documents — corrected this pass to distinguish two separate layers that the prior wording conflated:**

   - **Evidence-ledger identity (authoritative, four-field, per `ContextEvidenceKey`):** `Schema8EvidenceScanner`/`Schema8EvidenceLedger` key every context by `(SourceDocumentId, NormalizedFingerprint, TargetStart, TargetLength)`. Identical normalized text in Document A and Document B produces two different keys (differing `SourceDocumentId`), so Document B's occurrence is correctly treated as genuinely new evidence even after Document A's identical-looking sentence has already been processed. This is the mechanism that decides Schema-8 re-eligibility (`HasGenuinelyNewEvidence`) and is what gets frozen into a candidate's `FrozenEvidence`. Structurally guaranteed by the code; no dedicated isolated test proves this scenario today (unchanged limitation).
   - **`ContextSnapshots` display-row persistence (a separate, narrower policy — this is the fact the prior revision incorrectly generalized to "the evidence key," and is not itself keyed by all four fields):** `PreparationServiceSchema8.InsertNewContextSnapshots` deduplicates the up-to-`MaximumContextSnapshots` (3) illustrative snapshot rows it writes for one Meaning by `NormalizedFingerprint` alone, scoped to that `MeaningId` — matching the pre-existing, unchanged `IX_ContextSnapshots_Meaning_Fingerprint` unique index on `(MeaningId, NormalizedFingerprint)` (`Data/Entities/ContextSnapshotEntity.cs`, not touched by this slice). **This means identical normalized text linked to the same Meaning from two different documents produces only one `ContextSnapshots` row, not two** — the opposite of the prior wording's blanket claim. This is intentional, not a defect: `ContextSnapshots` exist to show illustrative usage examples (capped at 3 per Meaning), not to serve as the evidence-provenance ledger — that role belongs entirely to the candidate envelope's `FrozenEvidence` and `Schema8EvidenceLedger` above, which do not depend on which `ContextSnapshots` rows happen to exist (the ledger's `existingContextSnapshotKeys` component is only a legacy-baseline fallback for pre-envelope-era data, unioned with envelope-sourced keys, never the sole source of truth). No architecture-doc or schema change is required: the (`MeaningId`, `NormalizedFingerprint`) unique index is a real, unchanged database-level constraint that would reject a second identical-fingerprint row for the same Meaning regardless of application-level dedup logic, so a four-field `ContextSnapshots` dedup key is not achievable without a schema change, which is out of scope for this slice.

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

**Verification:** `git diff master...HEAD --check` (the committed-range check, not a plain working-tree `git diff --check`) is clean after this correction pass; inspecting `git diff master...HEAD` confirms zero changes to these protected areas.

## H. Exact next repository action (historical — superseded by the correction pass below)

**Corrected this pass:** the numbered plan below was written before Slice 3 was committed and described future steps; those steps have since been carried out. It is retained here as a historical record of what was planned, not as a claim about current state — see the header status block and §A for the current, accurate state.

1. ~~Focused review of the actual diff~~ — done; see this document and PR #33.
2. **At most one correction pass** — this document's own same-day correction pass (commit message `fix: reconcile Slice 3 evidence and handoff`) is that one authorized pass; no further correction pass is authorized after it.
3. ~~Rerun only tests affected by corrections~~ — not applicable: this correction pass changes documentation only, no production or test code.
4. ~~Run one final full suite only if production code changes~~ — not applicable for the same reason; the recorded 1190/1190 result stands.
5. ~~Stage explicit files only~~ — done for the Slice-3 implementation commit; the correction commit likewise stages only its own explicit file list, never `git add -A`/`git add .`.
6. ~~Commit~~ — done: `a51b0e82e35b2bb82d707580fe4fa04f8ac79765`.
7. ~~Request explicit approval before push and PR creation~~ — done: pushed, PR #33 opened.
8. **No automatic merge** — still binding: PR #33 requires explicit human review and approval before merge; nothing in this correction pass merges it.

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

1. **Inspect Git state** — run `git branch --show-current`, `git rev-parse HEAD`, `git status --short --untracked-files=all`, and `git log -2 --format="%H %s"`; confirm HEAD is on (or descends from) `a51b0e82e35b2bb82d707580fe4fa04f8ac79765` and the working tree is clean.

2. **Do not reset, restore, clean, stash, or recreate Slice 3** — it is already committed and pushed. Do not rewrite the existing Slice-3 commit; any further change is a new commit on top.

3. **Continue on `feature/meaning-centric-multi-sense-preparation-v1`** — this branch is the sole location for Slice-3 work, and PR #33 is its open pull request against `master`.

4. **Verify branch, HEAD, and PR #33's state before editing** — confirm you are on the correct branch, at the correct HEAD, and check whether PR #33 is still open before making any further change.

5. **Treat Slice 3 as implemented, committed, and under review** — it is not a target for reimplementation or additional feature work absent an explicit new task.

6. **The single authorized correction pass for this PR has already been applied** (this document's own revision, commit `fix: reconcile Slice 3 evidence and handoff`) — a further correction requires a new, separately authorized task, not an assumption that another pass is still open.

7. **Read the architecture document section §6 (Slice implementation sequence)** — verify you understand the binding dormancy rules and the activation-boundary strategy before making or approving any Schema-8-related changes.

## K. How to verify this handoff is complete

1. **Branch and commit match expected values** — `git branch --show-current` is `feature/meaning-centric-multi-sense-preparation-v1`; the Slice-3 implementation commit is `a51b0e82e35b2bb82d707580fe4fa04f8ac79765` (HEAD may be at or ahead of it, e.g. after this correction pass's own commit).

2. **PR #33 is open, targets `master`, and has not been merged** — verify via `gh pr view 33` or the GitHub UI.

3. **The changed-file inventory matches Git** — `git diff master...HEAD --name-status` shows the 26 files in §D above (5 modified, 21 added).

4. **Tests all pass** — the complete suite last recorded run was 1190/1190, at implementation time; unaffected by this documentation-only correction pass.

5. **No production code regressions** — `git diff master...HEAD --check` (the committed range) is clean.

6. **Schema remains dormant** — `Data/DatabaseSchema.cs` is unchanged, `CurrentVersion` is still `7`.

---

- **Handoff created:** 2026-07-29
- **Handoff corrected:** 2026-07-29 (this same-day, single authorized correction pass; documentation-only, no production or test code changes)
- **Status:** committed (`a51b0e82e35b2bb82d707580fe4fa04f8ac79765`), pushed, under review in PR #33 — not merged
