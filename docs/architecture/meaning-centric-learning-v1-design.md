# Meaning-centric learning v1 — data-model and migration architecture decision (KF-MEANING-001 Slice 0)

**Status:** Binding architecture decision, **corrected twice** (two focused review passes). **Option B is decided, not provisional** — it supersedes the provisional recommendation in [structured-vocabulary-import-and-sense-learning.md](../plans/structured-vocabulary-import-and-sense-learning.md) §5 and the "approved meaning-centric model" recorded in [backup-merge-v1-design.md](backup-merge-v1-design.md) §16 (KF-BACKUP-002 Slice 3), which this document extends into a full entity model and migration contract. **No implementation exists beyond this architecture branch** (`feature/meaning-centric-architecture-v1`): no schema change, no new entity class, no migration code, no archive-format code, no service/UI change. This document is the contract later implementation slices must follow.
**First correction pass note:** corrected four defects: (1) `WordEntity.Status` conflated authoritative word-review state with a Sense-learning rollup in one field; (2) `AnswerVariantEntity`'s three-way `Role` enum incorrectly modeled "preferred display" and "mastery requirement" as mutually exclusive; (3) the queue/review model had no way to record which specific answer variant a review was targeting versus which variant the user's answer actually matched; (4) content-derived identities were implicitly relied on as the only cross-device identity, with no immutable identity surviving a later content edit.
**Second correction pass note (this revision):** corrected five further defects, all downstream of a single root issue — the first pass still modeled preferred-display and mastery-requirement as **Sense-wide** facts, when both are actually **direction-specific**: (1) `AnswerVariantEntity` still carried `Requirement` directly, coupling a normalized answer expression to a single behavior instead of letting the same expression play different roles per `CardDirection`; a new `SenseAnswerVariantAssignmentEntity` now owns `Requirement`/`IsPreferred` per `(SenseId, CardDirection, AnswerVariantId)`. (2) `SenseEntity.PreferredMeaningId` was treated as the only authoritative preferred-Meaning pointer, when `TermToMeaning` and `MeaningToTerm` cards can legitimately prefer different Meanings; `SenseEntity` now holds a non-authoritative `DefaultMeaningId` fallback and each `LearningCardEntity` holds its own authoritative `PreferredMeaningId` (the former `MeaningId` column, migrated in place, upgraded from "legacy denormalization" to authoritative). (3) `AnswerVariantProgressEntity`'s uniqueness was `AnswerVariantId` alone, which cannot represent independent progress on the same variant across two directions; it is now `(CardId, AnswerVariantId)`. (4) `WordEntity.PreparationState`'s word-vs-sense scoping was left an open, deferred question; it is resolved here as a technical workflow/cache field only, never semantic-completeness state. (5) `AnswerLanguage` was implicitly usable to infer `CardDirection`; this is now an explicit anti-pattern, corrected because source and explanation languages may be identical.
**Backlog item:** [KF-MEANING-001](../BACKLOG.md), P1, required before KF-BACKUP-002's merge writer (design doc §12 slice 5).
**Builds on:** [backup-merge-v1-design.md](backup-merge-v1-design.md) §16–§18 (approved product model and stable identities, already implemented read-only in `Services/DataSafety/Merge/SemanticMeaningIdentities.cs`), [backup-format-v1.md](backup-format-v1.md) (binding v1 contract, unchanged by this document), `Data/DatabaseSchema.cs`, `Data/Entities/*`, `Models/BackupModels.cs`, `Services/Study/PreparationService.cs`, `Services/Study/LearningService.cs`, `Services/DataSafety/BackupArchiveReader.cs`/`BackupArchiveWriter.cs`/`BackupModelMapper.cs`.

## 0. Code audit conclusion: no blocking incompatibility with Option B

The task instructs Option B (`WordEntity` → `SenseEntity` → `MeaningEntity`/exact variants → `AnswerVariantEntity` → `AnswerVariantProgressEntity`) unless the code proves a concrete blocking incompatibility. It does not. Verified facts from the audit:

- `MeaningEntity.WordId` (`Data/Entities/MeaningEntity.cs:12-13`) is a plain, non-unique indexed FK — multiple Meaning rows per Word already coexist at the schema level (`backup-merge-v1-design.md` §4.2). Inserting a `SenseEntity` layer between `WordEntity` and `MeaningEntity` is additive, not a restructuring of an enforced constraint.
- `LearningCardEntity`'s only real constraint blocking sense-plurality is `IX_LearningCards_Word_Direction`, a *unique index on `(WordId, Direction)`* (`Data/Entities/LearningCardEntity.cs:12,18`) — a schema migration (adding `SenseId`, changing the unique index to `(SenseId, Direction)`) is a normal, additive-then-cutover migration, not a structural conflict.
- `LearningCardEntity.MeaningId` (`Data/Entities/LearningCardEntity.cs:16`) is a plain indexed (non-unique) FK, already one-per-card — renaming its role from "legacy denormalization" to "the card's own authoritative preferred Meaning" (§2.7, corrected this pass) requires no structural change, only a semantic/naming one plus, where feasible, a column rename.
- Archive format v1 independently enforces the identical one-card-per-`(VocabularyId, Direction)` constraint (`BackupArchiveWriter.ValidatePayloadGraph`'s `cardKeys` check, `Services/DataSafety/BackupArchiveWriter.cs:124,211-214`) — already identified and confirmed in `backup-merge-v1-design.md` §16.4/§17 item 7. This requires an archive **format** revision (§5 below), not a blocking incompatibility with Option B itself.
- `PreparationService.AcceptAsync` (`Services/Study/PreparationService.cs:313-469`) already creates exactly one `MeaningEntity` and its card(s) per accepted candidate, guarded only by "no existing confirmed Meaning for this word" (line 344-348) — a single-sense-at-a-time flow today, but nothing in its structure prevents calling it once per confirmed sense once the "already prepared" guard is relaxed to a Sense-scoped guard.
- `LearningService.PersistRating`/`ReadAutomaticState`/`ApplyAutomaticState` (`Services/Study/LearningService.cs:497-645,661-688`) key every automatic-progression counter by `wordId`/the `WordEntity` row directly — this is real Word-level state today, confirmed not Sense-level, and is exactly what §2.6 below relocates to `AnswerVariantProgressEntity`.
- `LearningService.CheckSpellingAsync` (`Services/Study/LearningService.cs:91-153`) compares typed input against `meaning.DisplayTerm` + `meaning.AcceptedAliasesJson` + `word.Language` (`LearningService.cs:113-119`) **unconditionally — regardless of `card.Direction`.** This is the code-verified basis for the alias-language rule in §4.2 and §7 below: aliases are always term-side (`SourceLanguage`) spelling variants, never explanation-side content, confirmed independently by the UI label `Prepare_AcceptedAliases` = "Accepted spelling aliases" (`Resources/Localization/SharedResource.resx:717-719`) and the input field in `Components/Pages/PrepareWords.razor:198`.
- `PreparationSelectionPolicy.Select` (called from `PreparationService.StartAsync`, `PreparationService.cs:105-117`) consumes `word.Status == WordStatus.UnknownBacklog`, `word.PreparationState`, and `preparedWordIds.Contains(word.Id)` (from `MeaningEntity.ConfirmedByUser`, `PreparationService.cs:100-104`) as inputs, and `word.TotalOccurrenceCount` for priority — confirming, as already established in the structured-vocabulary plan's own "Frequency affects priority, never existence" principle (`docs/plans/structured-vocabulary-import-and-sense-learning.md` §4 point 4, `VocabularyIdentityPolicy.cs`), that frequency is a pure ordering input today, never a suppression input — the same principle §4 below extends to Sense-level candidate surfacing (§2.1, [Decision 10](#10-the-rule-that-learning-one-sense-never-suppresses-a-later-unknown-sense-of-the-same-word)).

**Conclusion: Option B is adopted as designed.** No fallback to Option A, no escalation to Option C.

## 1. Product model (recap, now binding)

Unchanged from `backup-merge-v1-design.md` §16.1 in shape, corrected in the answer-variant/assignment and preferred-Meaning axes (§2.5, §2.6, §2.7):

- **Word** (`WordEntity`): language-scoped written-form lexical identity. Retains only global written-word knowledge — recognition of the term as a string, independent of which senses have been learned. Its `Status` field represents **only** global recognition/review judgment (§2.1, §3 Decision 9) — it never again represents Sense-learning progression. Its `PreparationState` field represents **only** the technical async lookup/preparation-process workflow, never semantic completeness (§2.1, corrected this pass).
- **Sense** (`SenseEntity`, new): one learnable, user-confirmed semantic unit of a Word (e.g. "bank" the financial institution vs. "bank" the river edge). Distinct Senses have independent cards, schedules, mastery, and contexts. `SenseEntity.Status` is the sole authority for Prepared/Learning/Mastered progression.
- **Meaning** (`MeaningEntity`, extended): one exact definition/translation/provider/manual content variant belonging to exactly one Sense. Different wording, synonyms, examples, notes, aliases, or provenance for the same sense produce additional Meaning rows under the same Sense — never a new Sense and never a new schedule. Which Meaning is preferred is now **direction-specific** (§2.7, corrected this pass): each `LearningCardEntity` (one per `(SenseId, Direction)`) has its own `PreferredMeaningId`; `SenseEntity.DefaultMeaningId` is a non-authoritative fallback used only before any card-specific preference exists.
- **AnswerVariant** (`AnswerVariantEntity`, new): **corrected this pass** — a pure, normalized answer expression belonging to a Sense (text + language), carrying no behavior of its own. Whether a variant is required for mastery, and whether it is preferred for display, are **direction-specific facts**, owned by a separate `SenseAnswerVariantAssignmentEntity` (§2.6) keyed by `(SenseId, CardDirection, AnswerVariantId)` — the same variant can be `Required` for `MeaningToTerm`, merely `AcceptedOnly` for `TermToMeaning`, and absent from a direction it has no assignment for.
- **AnswerVariantProgress** (`AnswerVariantProgressEntity`, new): mastery/streak state **per `(CardId, AnswerVariantId)`** — i.e., per concrete card, which is itself unique per `(SenseId, Direction)` — replacing today's word-level automatic-progression counters. Explicitly a derived, rebuildable cache over deduplicated ordered review events — never an independent source of truth (§2.8).

## 2. Final entity model (Schema 8)

All changes below are **additive to the existing tables** — no existing column is removed or repurposed in this migration (one column is *renamed*, §2.7, which is the one exception explicitly required by this pass and explained there). This keeps the migration close to a pure superset transformation: every Schema 7 fact remains readable after migration, and rollback (§4.7) never needs to reconstruct dropped data.

### 2.1 `WordEntity` — status split (first pass) and PreparationState contract (resolved this pass)

**Status (`WordStatus`) — unchanged from the first correction pass:**

- No column is added or removed. `WordEntity.Status` is **only ever written going forward with one of `Unreviewed`, `Known`, `UnknownBacklog`, `Ignored`** — the true, authoritative global-word-review judgment. `Language`, `CanonicalTerm`, `NormalizedTerm`, `TokenKind`, `TotalOccurrenceCount`, `DocumentCount`, `CreatedAt`, `UpdatedAt` are unchanged in meaning and in the code that reads them.
- `WordStatus`'s `Prepared`/`Learning`/`Mastered` enum members **remain declared** but become **migration-source-only, historical values**: no Schema-8-or-later write path ever sets `WordEntity.Status` to any of them again. `SenseEntity.Status` (§2.2) is the sole authority for that progression from Schema 8 onward.
- **No word-level learning summary is persisted in `WordEntity.Status`, or in any other `WordEntity` column, at all.** Any UI or query that needs an aggregate "how is this word doing overall" view computes it **live**, by querying/joining that word's `SenseEntity` rows and taking the highest `SenseEntity.Status` among them.
- `AutomaticInteractionMode`, `ConsecutiveRecallSuccessCount`, `ConsecutiveTypingSuccessCount`, `ConsecutiveTypingFailureCount`, `MasteryReviewExtensionScheduled` are **frozen legacy columns** after migration.

**Compatibility treatment of the legacy `Status` enum values (exact, unchanged from the first pass):** during migration (§4.2 step 2), for every `WordEntity` whose pre-migration `Status` is `Prepared`, `Learning`, or `Mastered`: (1) that exact value becomes the initial `SenseEntity.Status` of the Sense(s) created from that word's Meanings; (2) `WordEntity.Status` itself is then **reset to `UnknownBacklog`** — safe and lossless because `PreparationSelectionPolicy.Select` only ever selects candidates already in the `UnknownBacklog` tier (`PreparationService.cs:105-117`), so every word that ever reached `Prepared`/`Learning`/`Mastered` necessarily passed through `UnknownBacklog` first; (3) a word with a `Mastered` Sense and `WordEntity.Status = UnknownBacklog` is not a contradiction — it is the corrected model; (4) a post-migration `WordEntity.Status` of `Prepared`/`Learning`/`Mastered` observed anywhere is a migration invariant violation (§4.5), never silently tolerated.

**`WordEntity.PreparationState` — resolved this pass (was deferred in the first correction pass):**

`PreparationState` (`Unprepared`/`Preparing`/`Prepared`/`PreparationFailed`) is a **separate, pre-existing field** from `Status`. Its corrected, binding contract:

1. **`WordEntity.PreparationState` is a technical workflow/cache field only** — it records whether an async lookup/preparation *process* is currently in flight, completed, or failed for this word, purely to guard against duplicate concurrent lookups and to drive UI busy-state (`PreparationService.LookupCurrentAsync`, `PreparationService.cs:184-282`, sets it to `Preparing`/`PreparationFailed` around the network call). It is **not**, and after this correction must never be read as, a claim that every semantic sense of the word is known or fully prepared.
2. **`SenseEntity.Status` is authoritative for semantic preparation and learning completeness** — unchanged from §2.2/§3 Decision 9.
3. **An existing confirmed Sense does not prevent later discovery of another Sense** — restates [Decision 10](#10-the-rule-that-learning-one-sense-never-suppresses-a-later-unknown-sense-of-the-same-word), now explicitly extended to `PreparationState`: a word whose `PreparationState` is `Prepared` (because its first Sense's lookup completed and was confirmed) is **not** thereby excluded from ever being offered a second candidate sense.
4. **A new document context may produce a "known word / possible new sense" candidate even when the Word has `PreparationState.Prepared`.** Concretely: when a word already carrying one or more confirmed Senses is encountered again (a new document, a new occurrence) and a provider or the user's own reading of the new context suggests a sense the word does not yet have, that candidate is not suppressed merely because `PreparationState == Prepared` — `PreparationState` answers "is a lookup currently running," not "can this word ever be looked up again."
5. **The future preparation selector must compare the context/provider candidate against existing Senses**, using the same matching-key/`HasReliableSenseDiscriminator` policy already defined for migration grouping (§4.2 step 3, `SemanticMeaningIdentityPolicy`): **only a matching existing Sense** (reliable discriminator agrees) **can be skipped or linked** (the new occurrence is recognized as more evidence for a Sense the word already has, e.g. an increment to occurrence/context data, no new Sense created); **an unmatched or ambiguous candidate remains reviewable** — it is surfaced to the user as a candidate new Sense, exactly like a first-time word, never silently dropped.
6. **Frequency controls priority only and never suppresses the candidate** — consistent with the already-established, code-verified principle (`docs/plans/structured-vocabulary-import-and-sense-learning.md` §4 point 4, reaffirmed in §0 above): a low-occurrence candidate sense is deprioritized in ordering, never excluded from ever being reachable.

**Schema-8 compatibility treatment (binding):**

- `WordEntity.PreparationState` is **retained, unchanged in shape**, for active-workflow compatibility — the Schema 7→8 migration does not touch this column at all (it only processes *confirmed* Meanings, §4.2), so a word's `PreparationState` value survives migration exactly as it was.
- It is **never** used as semantic-completeness state by any Schema-8-or-later code path — every place that might have been tempted to read `PreparationState == Prepared` as "this word needs no further sense discovery" must instead consult `SenseEntity` rows per the rules above (this is a binding acceptance criterion for the preparation multi-sense selection slice, §6 slice 3).
- **Active preparation sessions migrate without losing queue position — trivially, by construction, not by a special migration step:** `PreparationSessionEntity`/`PreparationCandidateEntity` rows (the in-progress preparation batch, if one exists at migration time) are **never read or written by the §4.2 Sense-generation algorithm at all**, which only processes already-confirmed `MeaningEntity` rows. An active session's `Order`, `Status`, and position are therefore untouched by the migration — the user resumes exactly where they left off after the schema upgrade, with zero additional migration logic required to guarantee it.
- **Future removal or renaming of `PreparationState`** (e.g. once multi-sense preparation makes a word-level "is anything preparing" flag need its own per-candidate scoping) **may occur only in a separately tested migration** — not decided or authorized here; this document's Schema 8 leaves the field exactly as it is today, semantics narrowed only by documentation, not by a code or schema change.

### 2.2 `SenseEntity` (new) — [Decision 1](#1-senseentity-fields-natural-identity-status-timestamps-topicdomain-part-of-speech-and-preferred-exact-variant)

```
SenseEntity
  Id                      int, PK, autoincrement                       -- local SQLite numeric ID only (§2.11)
  StableId                string, unique indexed, immutable             -- NEW; archive/sync identity (§2.11)
  WordId                  int, indexed FK -> WordEntity.Id (non-unique: many Senses per Word)
  SourceLanguage          string
  ExplanationLanguage     string
  ProviderSenseId         string (nullable/empty) -- migrated from MeaningEntity.SelectedMeaningId,
                                                       which already stores the provider's own sense id
                                                       (BackupPreparedItem.ProviderMeaningId, verified at
                                                       BackupModelMapper.cs:417 / PreparationService.cs:376)
  TopicOrDomain           string, default "" -- new; closes the §16.6 persistence gap
  PartOfSpeech            string, default "" -- new; distinct from GrammaticalRelationship (below);
                                                  sourced from the lookup draft's PartOfSpeech, never
                                                  persisted today (BackupLookupMeaning.PartOfSpeech,
                                                  Models/BackupModels.cs:386, confirmed dropped at
                                                  PreparationService.AcceptAsync)
  GrammaticalRelationship string, default "" -- moved authority from MeaningEntity (existing morphological/
                                                  derivation discriminator, e.g. plural/tense relation)
  AcronymExpansion        string, default "" -- moved authority from MeaningEntity
  DefaultMeaningId        int, nullable FK -> MeaningEntity.Id -- CORRECTED THIS PASS (was
                                                                    "PreferredMeaningId", treated as the
                                                                    sole authority). Now an explicitly
                                                                    NON-authoritative fallback: which
                                                                    Meaning's Definition/Translation/
                                                                    provenance content to show in contexts
                                                                    with no card-specific preference yet
                                                                    (preparation preview, general display
                                                                    before any card exists for a direction).
                                                                    §2.7's per-card PreferredMeaningId is
                                                                    authoritative whenever a card exists.
  Status                  SenseStatus enum: Prepared | Learning | Mastered | Suspended
  CreatedAtUtc            DateTime
  UpdatedAtUtc            DateTime
```

Removed from the prior revision: `PreferredAnswerVariantId`. **Correction (this pass):** a single Sense-wide preferred-answer pointer cannot represent "the preferred display answer differs between the `TermToMeaning` and `MeaningToTerm` directions," which is a real, expected case (e.g. the term side prefers "bank" while, independently, nothing analogous need hold on the meaning side). Preferred-answer selection is now `IsPreferred` on `SenseAnswerVariantAssignmentEntity` (§2.6), scoped to `(SenseId, CardDirection)`, not a field on `SenseEntity` at all.

**Natural identity (app-level, not a DB unique constraint):** `(WordId, SourceLanguage, ExplanationLanguage, ProviderSenseId, TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion)` — unchanged from the first pass; see §2.11 for why this is a matching key, not a permanent identifier.

**Status tiers:** `Prepared`, `Learning`, `Mastered`, `Suspended` — unchanged from the first pass (§2.2 prior text), except that "Mastered" is now understood as a Sense-level rollup of **every one of the Sense's existing `LearningCardEntity` rows being `Retired`** (§2.8, §2.9, corrected this pass to be direction-aware), not a single Sense-wide mastery signal computed from one flat variant set.

**`DefaultMeaningId` versus each card's own `PreferredMeaningId` (corrected division of responsibility):**
- `SenseEntity.DefaultMeaningId` — a **fallback only**, defaulted to the sole `MeaningEntity` created alongside the Sense at acceptance time. Used by preparation preview/general display before any direction-specific card exists, or before a user has expressed a direction-specific preference. Never treated as authoritative once a card exists for the relevant direction.
- `LearningCardEntity.PreferredMeaningId` (§2.7) — **authoritative**, per `(SenseId, Direction)`. This is the corrected model's answer to "which exact Meaning does this specific card display."

### 2.3 Sense knowledge: directly on `SenseEntity`, no separate `SenseKnowledgeEntity` — [Decision 2](#2-whether-sense-knowledge-lives-directly-on-senseentity-or-in-a-separate-senseknowledgeentity)

**Decided: no `SenseKnowledgeEntity`.** Unchanged from the first pass. `SenseEntity.Status` is the entire "sense knowledge state" surface; per-`(Sense, Direction)` scheduling lives on `LearningCardEntity` (§2.7), per-`(Card, AnswerVariant)` mastery streaks live on `AnswerVariantProgressEntity` (§2.8) as a derived cache.

### 2.4 `MeaningEntity` — additive extension, [Decision 3](#3-meaningentitys-future-relationship-to-senseentity-and-which-existing-fields-remain-variant-specific)

Unchanged from the first correction pass. **New columns:** `SenseId` (indexed FK, non-unique), `StableId` (unique indexed, immutable, §2.11). `WordId` stays, denormalized, invariant-checked against `SenseEntity.WordId`.

**Variant-specific fields** (matching `ExactMeaningVariantIdentity`): `DisplayTerm`, `Definition`, `Translation`, `EncounteredSurfaceForm`, `DictionaryExample`, `AdditionalNote`, `ConfirmedByUser`, `Source`/`SourceProject`/`SourcePageTitle`/`SourceRevisionId`/`Attribution`, `AcceptedAliasesJson`.

**Fields whose authority moves to `SenseEntity`:** `GrammaticalRelationship`, `AcronymExpansion`, `SelectedMeaningId`/`ProviderSenseId` — retained on `MeaningEntity` as frozen historical copies, never re-written there after migration.

### 2.5 `AnswerVariantEntity` (new) — corrected: a pure normalized answer expression, no behavior of its own — [Decision 4](#4-answervariantentity-fields-and-roles)

**Correction (this pass, second defect):** the first pass's `AnswerVariantEntity` still carried `Requirement` directly on the row — a Sense-wide fact. This is wrong for the same reason `PreferredAnswerVariantId` on `SenseEntity` was wrong (§2.2): whether a variant is required for mastery is a **per-direction** fact (a variant can be `Required` for `MeaningToTerm` and merely `AcceptedOnly` — or entirely unassigned — for `TermToMeaning`). `AnswerVariantEntity` is corrected to represent **only** the normalized text expression itself:

```
AnswerVariantEntity
  Id                 int, PK, autoincrement                  -- local SQLite numeric ID only (§2.11)
  StableId            string, unique indexed, immutable       -- archive/sync identity (§2.11), since the
                                                                  text itself is user-editable (e.g.
                                                                  correcting a typo)
  SenseId             int, indexed FK -> SenseEntity.Id
  AnswerLanguage      string  -- which language this expression is written in. CORRECTED THIS PASS: this
                                 field must NEVER be used to infer CardDirection — source and explanation
                                 languages may be identical (e.g. a monolingual definition-only Sense), so
                                 "AnswerLanguage == SourceLanguage" is not a reliable proxy for "this is the
                                 MeaningToTerm-side answer." Direction association is exclusively the job of
                                 SenseAnswerVariantAssignmentEntity (below); AnswerLanguage only records what
                                 language the text is written in, independent of how it is used.
  DisplayText         string  -- the raw, as-authored text, preserved verbatim for display
  NormalizedText      string  -- the normalized form of DisplayText (same normalization
                                 AnswerVariantIdentity already applies via CanonicalText.NormalizeOptional,
                                 SemanticMeaningIdentities.cs:280), persisted (not merely computed at query
                                 time) so the unique index below can be a plain column-based index
  SourceMeaningId     int, nullable FK -> MeaningEntity.Id  -- provenance: which exact variant first
                                 introduced this text (manual-vs-provider distinguishing, KF-PROVENANCE-001)
  CreatedAtUtc        DateTime
  UpdatedAtUtc        DateTime
```

**No `Requirement` field. No preferred-display pointer or flag.** Both are now owned entirely by `SenseAnswerVariantAssignmentEntity` (§2.6) — `AnswerVariantEntity` answers only "what normalized text, in what language, exists for this Sense," never "how is it used."

**Unique index:** `(SenseId, AnswerLanguage, NormalizedText)` — over the persisted `NormalizedText` column directly (not a computed expression), deliberately identical in composition to `AnswerVariantIdentity` (`SemanticMeaningIdentities.cs:52-55`) — DB-level deduplication for free, and the live schema and the merge-planner identity remain the same policy.

### 2.6 `SenseAnswerVariantAssignmentEntity` (new, this pass) — direction-specific requirement and preference

**This is the entity introduced to fix the second pass's root defect.** It records, independently per `(Sense, CardDirection)` pair, how one `AnswerVariantEntity` behaves:

```
SenseAnswerVariantAssignmentEntity
  Id              int, PK, autoincrement
  StableId         string, unique indexed, immutable          -- archive/sync identity (§2.11)
  SenseId          int, indexed FK -> SenseEntity.Id           -- denormalized from AnswerVariant.SenseId
                                                                  for query convenience and as an
                                                                  invariant-checked cross-reference: this
                                                                  value must always equal the referenced
                                                                  AnswerVariantEntity's own SenseId
                                                                  (validated at every write, §4.5)
  CardDirection    CardDirection: TermToMeaning | MeaningToTerm
  AnswerVariantId  int, indexed FK -> AnswerVariantEntity.Id
  Requirement      AnswerVariantRequirement enum: Required | AcceptedOnly
  IsPreferred      bool  -- true for at most one assignment per (SenseId, CardDirection), enforced below
  CreatedAtUtc     DateTime
  UpdatedAtUtc     DateTime
```

**Unique index:** `(SenseId, CardDirection, AnswerVariantId)` (`IX_SenseAnswerVariantAssignments_Sense_Direction_Variant`) — one assignment row per variant per direction; a variant with no row for a given direction is simply **absent** from that direction (neither required, accepted, nor preferred there).

**Singleton-preferred enforcement — a database mechanism is safely available and is the binding approach:** SQLite natively supports **partial unique indexes** (`CREATE UNIQUE INDEX ... WHERE <condition>`, stable since SQLite 3.8, well within this app's bundled SQLite version) even though sqlite-net's declarative `[Indexed]` attribute cannot express a `WHERE` clause. Schema 8's migration therefore issues one additional **raw SQL** statement — exactly the same "raw `ExecuteAsync` alongside ORM-declared tables" pattern `DatabaseSchema.InitializeAsync` already uses (`Data/DatabaseSchema.cs:37`, the `DELETE FROM LexicalCache ...` statement) — to create:

```sql
CREATE UNIQUE INDEX IX_SenseAnswerVariantAssignments_Sense_Direction_Preferred
ON SenseAnswerVariantAssignments (SenseId, CardDirection)
WHERE IsPreferred = 1;
```

This makes "exactly one preferred assignment per `(SenseId, CardDirection)`" a **database-enforced** invariant — an attempted second `IsPreferred = 1` insert/update for the same `(SenseId, CardDirection)` fails at the engine level, not merely at an application check that a concurrent writer could race past. **Defense in depth, not a substitute:** the migration's own invariant-validation gate (§4.5) additionally re-asserts this count explicitly as a named migration invariant, and Slice 1's test suite (§6 slice 1) must include a concurrency test asserting two overlapping transactions racing to set `IsPreferred = 1` for the same `(SenseId, CardDirection)` result in exactly one committed winner and one constraint-violation failure, never a silent double-write.

**The rules this entity encodes (all binding):**
- A variant may be `Required` in one direction, `AcceptedOnly` in another, and have **no row at all** (absent) for a third — three independent, non-exclusive possibilities per direction.
- A variant may be **preferred and `Required` simultaneously** in the same direction — `IsPreferred` and `Requirement` are independent columns on the same row; neither implies or excludes the other.
- **Providers must not automatically mark every synonym as `Required`** ([Decision 12](#12-the-rule-that-providers-must-not-automatically-make-every-synonym-mandatory)) — every assignment created by migration or by ordinary provider-driven preparation defaults to `Requirement = AcceptedOnly`; only a distinct, explicit, later user action creates or promotes an assignment to `Required`.
- `AnswerLanguage` (on the referenced `AnswerVariantEntity`, §2.5) is **never** consulted to decide which direction(s) an assignment applies to — the assignment's own `CardDirection` column is the sole authority. This matters concretely whenever `SourceLanguage == ExplanationLanguage` (e.g. a monolingual Sense), where language-based inference would be actively wrong, not merely redundant.

### 2.7 `LearningCardEntity` — corrected: direction-specific preferred Meaning — [Decision 6](#6-learningcard-identity)

```
LearningCardEntity (Schema 8)
  Id                int, PK, autoincrement
  SenseId           int, indexed FK -> SenseEntity.Id      -- authoritative half of the unique index
  Direction         CardDirection                           -- unchanged
  -- unique index IX_LearningCards_Sense_Direction on (SenseId, Direction), replacing
  -- IX_LearningCards_Word_Direction on (WordId, Direction)
  WordId            int, indexed (non-unique) -- RETAINED as migration/legacy metadata only, always kept
                                                  equal to card.Sense.WordId by construction; removal
                                                  deferred to the sense-addressed learning-cards slice
                                                  (§6 slice 5)
  PreferredMeaningId int, indexed FK -> MeaningEntity.Id -- CORRECTED THIS PASS: this is the former
                                                  `MeaningId` column, renamed in place by the migration
                                                  (§4.2 step 8a) and UPGRADED in authority. It is no longer
                                                  "legacy denormalization" — it is the single authoritative
                                                  answer to "which exact Meaning's Definition/Translation/
                                                  provenance content does THIS card, for THIS direction,
                                                  display." Two cards for the same Sense (TermToMeaning and
                                                  MeaningToTerm) may legitimately reference two DIFFERENT
                                                  MeaningEntity rows — e.g. a definition-mode Meaning
                                                  preferred for TermToMeaning display and a separate
                                                  translation-mode Meaning preferred for MeaningToTerm's own
                                                  prompt construction. Changing a card's PreferredMeaningId
                                                  never deletes any other MeaningEntity row for the Sense —
                                                  it only repoints which one this card currently prefers.
  State, DueAtUtc, IntervalDays, EaseFactor, SuccessfulReviewCount, LapseCount, LastReviewedAtUtc,
  LastRating, CreatedAtUtc, UpdatedAtUtc   -- unchanged; still exactly one shared SRS schedule per card
```

**Correction rationale:** the first pass treated the pre-existing `MeaningId` column as inert "legacy denormalization," with `SenseEntity.PreferredMeaningId` as the supposed sole authority. That is corrected: a single Sense-wide preferred Meaning cannot represent the real product requirement that `TermToMeaning` and `MeaningToTerm` cards may legitimately prefer different exact Meaning variants for the same Sense (e.g. differing translation-mode versus definition-mode content). The existing `MeaningId` column is migrated **in place** (renamed, not replaced by a new column and not left to decay as legacy-only) into this card-specific, now-authoritative `PreferredMeaningId` — this is the direct fix for "`MeaningId` is not treated merely as disposable legacy metadata."

**Column rename mechanics (implementation note, not a blocking architecture question):** the migration issues `ALTER TABLE LearningCards RENAME COLUMN MeaningId TO PreferredMeaningId` (supported by SQLite since 3.25, and no other renamed-column complication exists here since the column's declared type and nullability are unchanged) as part of step 1 of the atomic order (§4.3). If Slice 1's implementer finds the bundled SQLite version does not support `RENAME COLUMN` on the target platform, the documented fallback is the standard portable pattern (add `PreferredMeaningId`, copy every row's `MeaningId` value into it, drop `MeaningId`) — an implementation detail, not a change to this document's binding decision that the column becomes authoritative and is populated from the legacy value with zero data loss either way.

- **Identity: `(SenseId, Direction)` unique**, unchanged from the first pass.
- **`WordId` retained temporarily as migration metadata**, unchanged rationale from the first pass.
- **No `AnswerVariant`/assignment reference is stored on the card at all.** "Which variants are required/preferred for this card" is answered by querying `SenseAnswerVariantAssignmentEntity` for `(card.SenseId, card.Direction)` (§2.6) — never duplicated onto the card row.

### 2.8 Context ownership — [Decision 7](#7-context-ownership)

Unchanged from the first correction pass. `ContextSnapshotEntity` gains `SenseId` (primary ownership key); `MeaningId` becomes optional provenance; `SourceDocumentId` unchanged; `WordId` retained as migration bridge.

### 2.9 `AnswerVariantProgressEntity` (new) — corrected: direction-specific via `(CardId, AnswerVariantId)` — [Decision 5](#5-answervariantprogressentity-fields-for-reading-and-typing-streaks-failures-mastery-and-learning-mode-behavior)

**Correction (this pass, third defect):** the first pass keyed this table by `AnswerVariantId` alone (unique). That cannot represent independent progress on the same variant across two different directions/cards — which is now a real, structurally possible case once §2.6 allows the same variant to be `Required` in both directions independently. Corrected:

```
AnswerVariantProgressEntity
  Id                                  int, PK, autoincrement
  CardId                              int, indexed FK -> LearningCardEntity.Id
  AnswerVariantId                     int, indexed FK -> AnswerVariantEntity.Id
  -- unique index IX_AnswerVariantProgress_Card_Variant on (CardId, AnswerVariantId)
  InteractionMode                     LearningInteractionMode: Reading | Typing
  ConsecutiveReadingSuccessCount      int
  ConsecutiveTypingSuccessCount       int
  ConsecutiveTypingFailureCount       int
  LastAssessedAtUtc                   DateTime, nullable -- NEW field name for "last assessment"; the
                                                             timestamp of the most recent review event
                                                             consumed by this row's replay (§ below)
  MasteryReviewExtensionScheduled     bool
  IsMastered                          bool -- cached/derived (Rule R1)
  ReplayVersion                       int, default 1 -- NEW: bumped whenever the replay algorithm itself
                                                          changes in a future slice; a stored row whose
                                                          ReplayVersion is older than the code's current
                                                          version is treated as stale and rebuilt from the
                                                          review log before use, rather than trusted as-is —
                                                          declared now so the rebuildable-cache guarantee
                                                          (below) remains meaningful even across a future
                                                          change to how replay itself works
  CreatedAtUtc                        DateTime
  UpdatedAtUtc                        DateTime
```

**`(CardId, AnswerVariantId)` is equivalent to `(SenseId, CardDirection, AnswerVariantId)` — documented, not coincidental:** because `LearningCardEntity` is itself unique by `(SenseId, Direction)` (§2.7), a `CardId` value is a 1:1 stand-in for exactly one `(SenseId, Direction)` pair. `AnswerVariantProgressEntity`'s uniqueness by `(CardId, AnswerVariantId)` is therefore the same fact as uniqueness by `(SenseId, CardDirection, AnswerVariantId)`, expressed via the card's own surrogate key rather than by repeating `SenseId`/`Direction` on this table — chosen because every progress row necessarily corresponds to a review that already happened against a real, existing card (unlike `SenseAnswerVariantAssignmentEntity`, §2.6, which can exist before any card does), so referencing the concrete `CardId` directly is both simpler and enforces "a progress row can only exist for a variant that has actually been assessed on a real card" as a structural FK constraint rather than an extra check.

**Corrected framing — review events are authoritative; this table is a rebuildable cache, not a second source of truth.** Unchanged principle from the first pass, now explicit about the `(CardId, AnswerVariantId)` scope: the row is, at all times, fully re-derivable by replaying the deduplicated, ordered set of `LearningReviewEntity` rows for that `CardId` attributed to that `AnswerVariantId` (below) — never an independent fact.

**`LearningReviewEntity` retains (unchanged fields from the first pass, now explicitly restated as the pair that determines directional assignment):**
- `CardId` (existing) — which card (hence which `(SenseId, Direction)`) this review belongs to.
- `TargetAnswerVariantId` (int, nullable FK, first pass) — which variant the queue-building logic was drilling.
- `MatchedAnswerVariantId` (int, nullable FK, first pass) — which variant the user's answer actually matched, if any.

**The `(CardId, AnswerVariantId)` pair — read from a review's own `CardId` plus its `TargetAnswerVariantId`/`MatchedAnswerVariantId` — is what determines the directional assignment a given review event is evidence for:** looking up `SenseAnswerVariantAssignmentEntity` by `(card.SenseId, card.Direction, AnswerVariantId)` tells you whether that variant is `Required`/`AcceptedOnly`/preferred **for this specific card's direction** — the same variant might be a completely different `Requirement` for the Sense's other direction, and that other direction's own progress (a different `CardId`) is entirely unaffected.

**The synonym-credit rule (binding, corrected — now assignment-aware, the key defect fixed this pass):** entering a different accepted synonym is semantically correct (`WasCorrect = true`) but **does not satisfy the specifically targeted `Required` variant**, which remains pending. Replay consumes review events where `TargetAnswerVariantId == thisVariantId` **or** `MatchedAnswerVariantId == thisVariantId`, both scoped to the same `CardId`:

- If `MatchedAnswerVariantId == TargetAnswerVariantId`: standard progress credit/failure applies, unchanged.
- If `MatchedAnswerVariantId` is non-null and differs from `TargetAnswerVariantId`: **the matched variant may receive its own credit only if it is itself a valid assignment for that same card** — i.e., only if `SenseAnswerVariantAssignmentEntity` has a row for `(card.SenseId, card.Direction, MatchedAnswerVariantId)` (necessarily true for the matched variant to have been recognized as a correct answer for this card's direction at all, since an unassigned variant would not have been accepted as correct in the first place — this clause exists to make the invariant explicit, not to describe a case that can arise from a differently-behaving path) **and** that assignment's `Requirement == Required`. If both hold, the matched variant's own progress row (keyed by `(CardId, MatchedAnswerVariantId)`) gets credited. The **targeted** variant's own counters (`(CardId, TargetAnswerVariantId)`) are **untouched** — no advance, no reset, no failure.
- If `MatchedAnswerVariantId` is null and `WasCorrect = false`: standard failure handling applies to the targeted variant's `(CardId, TargetAnswerVariantId)` row only.

**`AcceptedOnly` assignments never become mastery prerequisites** — only variants with a `Required` assignment for that specific card's direction ever gain a consulted-for-mastery progress row.

**Mastery is per-card (per direction), corrected from the first pass's Sense-wide framing:** a card is eligible to reach `Retired` only once **every** `SenseAnswerVariantAssignmentEntity` row with `Requirement = Required` for that card's own `(SenseId, Direction)` has a corresponding `AnswerVariantProgressEntity` row (keyed by that same `CardId`) with `IsMastered = true`. `SenseEntity.Status = Mastered` (§2.2) is then the further rollup: **every existing `LearningCardEntity` for that Sense (across whichever directions have been created) is `Retired`** — a Sense with only a `TermToMeaning` card mastered, while its `MeaningToTerm` card (if any) still has pending required assignments, is not yet `Mastered` at the Sense level, even though its `TermToMeaning` card individually is.

**Reactivation rule (binding, unchanged principle from the first pass, now explicitly per-card):** adding a new `Required` assignment to a card's direction that was already `Retired` — or promoting an existing `AcceptedOnly` assignment for that direction to `Required` — reactivates **that card** (state moves off `Retired`), and, if it was the last mastered card keeping the Sense at `Mastered`, the Sense's `Status` recomputes back to `Learning`. The exact re-entry scheduling mechanics remain left to the sense-addressed learning-cards slice (§6 slice 5); the rule itself is binding here.

**Preservation across active-session resume, archive v2, merge, and scheduler replay:** unchanged in substance from the first pass, now read as per-`(CardId, AnswerVariantId)`: `LearningSessionCardEntity.TargetAnswerVariantId` is read back unchanged on resume; archive v2 carries `TargetAnswerVariantId`/`MatchedAnswerVariantId` on reviews and `TargetAnswerVariantId` on completed queue items (§5.2); the merge planner's review-event fingerprint must include both fields; scheduler replay for the card-level SRS fields (`State`/`DueAtUtc`/`IntervalDays`/`EaseFactor`) remains per-card, computed from the full review history regardless of variant, while `AnswerVariantProgressEntity`'s replay filters by `CardId` + variant.

### 2.10 Topic/domain persistence path, end to end — [Decision 8](#8-topicdomain-persistence-from-lookup-candidate-through-preparation-database-archive-learning-display-and-merge)

Unchanged from the first correction pass. `PreparedMeaningInput` gains `TopicOrDomain`/`PartOfSpeech`, written onto `SenseEntity`; `BackupSense` (§5.2) carries them; `LearningCardView` gains them for display; `SemanticMeaningIdentityPolicy.Compute`'s `canonicalTopicOrDomain` parameter finally receives a real value.

### 2.11 Stable identity taxonomy — three distinct concepts, never conflated

Unchanged in structure from the first correction pass, **`SenseAnswerVariantAssignmentEntity` added to the immutable-`StableId` tier** (its own `Requirement`/`IsPreferred` values are exactly the kind of user-editable-after-creation content the taxonomy exists to protect against being used as a permanent identifier):

| Tier | What it is | Where it lives | Stability | Used for |
| --- | --- | --- | --- | --- |
| **Local SQLite numeric ID** | The autoincrement `Id` int PK on every entity | Every entity | Device-local only; never crosses devices | In-process FK joins only |
| **Content-derived semantic matching key** | `SemanticMeaningIdentity` / `ExactMeaningVariantIdentity` / `AnswerVariantIdentity` / `FutureCardIdentity` | Computed on demand, never persisted | Changes if hashed fields change (by design) | First-contact reconciliation only (bootstrap matching, KF-BACKUP-002 merge planner) |
| **Immutable entity stable ID** | `StableId` on `SenseEntity`, `MeaningEntity`, `AnswerVariantEntity`, `SenseAnswerVariantAssignmentEntity` | Generated once at row creation (random GUID), never regenerated | Permanent for the row's lifetime | Archive/sync cross-device continuity reference; `StableId`-first matching, falling back to tier 2 only when no correlation exists yet |

`WordEntity` and `LearningCardEntity` still need no `StableId` of their own, for the same reasons as the first pass (`WordEntity`'s natural key has no edit path; `LearningCardEntity`'s matching key `(Sense.StableId, Direction)` is already immutable by construction).

**Migration behavior:** every `SenseEntity`/`MeaningEntity`/`AnswerVariantEntity`/`SenseAnswerVariantAssignmentEntity` row created by the Schema 7→8 migration or by any Schema-8-and-later code path receives a freshly generated `StableId` at creation time.

**Rule (binding):** no code path may treat a content-derived matching key, or any mutable field it is computed from (`Definition`, `Translation`, `TopicOrDomain`, an `AnswerVariant`'s `DisplayText`, or an assignment's `Requirement`/`IsPreferred`), as a row's sole or permanent identifier. `StableId` is the only field entitled to that role.

## 3. Remaining required decisions (9–12)

### 9. Global Word status versus independent Sense status

Unchanged from the first correction pass: `WordEntity.Status` stays authoritative and word-level for `Unreviewed`/`Known`/`UnknownBacklog`/`Ignored`; `SenseEntity.Status` is sole authority for Prepared/Learning/Mastered; no word-level learning summary is persisted anywhere on `WordEntity`.

### 10. The rule that learning one sense never suppresses a later unknown sense of the same word

Unchanged in principle from the first pass, now explicitly extended to `WordEntity.PreparationState` as well (§2.1, resolved this pass): neither `WordEntity.Status`'s rollup absence nor `WordEntity.PreparationState` being `Prepared` excludes a word from surfacing a later, distinct candidate Sense. Candidate selection compares against existing Senses by matching key; only a genuine match may be skipped/linked (§2.1 point 5).

### 11. The rule that providers may suggest multiple senses, but only user-selected senses become active learning units

Unchanged from the first pass: `PreparationService.AcceptAsync` persists only the one explicitly-submitted candidate; the "preparation multi-sense selection" slice (§6 slice 3) extends this to multiple explicit accept calls, never a batch auto-accept.

### 12. The rule that providers must not automatically make every synonym mandatory

**Corrected location, same rule:** every alias migrated from `MeaningEntity.AcceptedAliasesJson` becomes an `AnswerVariantEntity` (§2.5) with a `SenseAnswerVariantAssignmentEntity` row (§2.6) whose `Requirement = AcceptedOnly`, never `Required`, for every direction it is assigned to. This is both a migration rule (§4.2 step 7) and an ongoing rule for new preparation: a provider's returned synonym list creates `AcceptedOnly` assignments by default; only a distinct, later, explicit user action promotes a specific `(Sense, Direction, AnswerVariant)` assignment to `Required`.

## 4. Schema 7 → 8 migration contract

### 4.1 Ambiguity never blocks startup — the governing principle

Unchanged from the first correction pass. Sense-grouping ambiguity (§4.2 step 3) always resolves via the safe split-into-more-Senses default and never blocks startup; only referential-integrity corruption (§4.6) fails closed.

### 4.2 The Sense/AnswerVariant/Assignment generation algorithm (referenced by the ordered steps in §4.3)

For each `WordEntity` with at least one `MeaningEntity` row:

1. **Compute each Meaning's matching key** (unchanged from the first pass).
2. **Translate the word's pre-migration `Status`** into the initial `SenseEntity.Status` (unchanged from the first pass; §2.1 then resets `WordEntity.Status` to `UnknownBacklog`).
3. **Group Meanings into Senses — only when strong evidence proves the same Sense** (unchanged from the first pass; split-not-merge default).
4. **Create the grouped `SenseEntity` row(s).** `StableId` = fresh GUID; `DefaultMeaningId` (renamed from the first pass's `PreferredMeaningId`) = the representative Meaning's own `Id`; other fields as in the first pass. **No `PreferredAnswerVariantId` is set on the Sense** (that field no longer exists, §2.2).
5. **Link every legacy `MeaningEntity` in the group to that Sense and give it a `StableId`** (unchanged from the first pass).
6. **Create the `AnswerVariantEntity` rows from the existing `DisplayTerm`/`Translation` contract.** One variant with `DisplayText`/`NormalizedText = DisplayTerm` (normalized), `AnswerLanguage = SourceLanguage`; and — only when `Translation` is non-empty — a second variant with `DisplayText`/`NormalizedText = Translation` (normalized), `AnswerLanguage = ExplanationLanguage`. When `Translation` is empty but `Definition` is present, no separate explanation-side variant is created. Every created variant gets a fresh `StableId` and `SourceMeaningId` = the representative Meaning's `Id`. **No `Requirement` is set on the variant itself** (that field no longer exists on `AnswerVariantEntity`, §2.5) — requirement/preference are set in step 6a.
6a. **Create the corresponding `SenseAnswerVariantAssignmentEntity` rows.** For each `CardDirection` the word has an existing legacy `LearningCardEntity` row for (§4.2 step 8 below determines this per word): create one assignment `(SenseId, CardDirection, AnswerVariantId)` for the term-side variant when `CardDirection = MeaningToTerm`, and for the explanation-side variant (if created) when `CardDirection = TermToMeaning` — mirroring which side of the Meaning each existing direction's card actually displayed/expected under the pre-migration behavior (`MeaningToTerm` expects the term; `TermToMeaning` expects the explanation-side answer when one exists, otherwise no assignment is created for that direction at all, consistent with Definition-only content never being a typed-answer target). `Requirement = AcceptedOnly` for every migration-created assignment ([Decision 12](#12-the-rule-that-providers-must-not-automatically-make-every-synonym-mandatory)); `IsPreferred = true` for the term-side variant's `MeaningToTerm` assignment (the direct migrated equivalent of "this is the term you're expected to produce") — no assignment is marked preferred for a direction that has no existing legacy card, since there is nothing yet to prefer a display answer for.
7. **Migrate existing `AcceptedAliasesJson` entries as `AnswerVariantEntity` rows with `AcceptedOnly` assignments, never `Required`.** One `AnswerVariantEntity` per distinct alias string (`AnswerLanguage = SourceLanguage` — resolved, not assumed, §0/§7), deduplicated by `(SenseId, AnswerLanguage, NormalizedText)` (§2.5). Each gets an assignment (§2.6) for the `MeaningToTerm` direction only, when that direction has an existing card (aliases are always term-side spelling alternatives, §0/§7, so they are only ever meaningful as accepted answers on the direction that expects a typed/produced term) with `Requirement = AcceptedOnly`, `IsPreferred = false`.
8. **Create the sense-addressed card corresponding to every legacy card, preserving its row ID, and migrate `MeaningId` in place.** Update each existing `LearningCardEntity` row **in place** — never delete-and-recreate — setting `SenseId` from the group's Sense, renaming `MeaningId` to `PreferredMeaningId` (§2.7, via `ALTER TABLE ... RENAME COLUMN` or the documented fallback) with its existing value preserved verbatim (the legacy `MeaningId` value becomes the new authoritative `PreferredMeaningId` directly — no re-derivation, no ambiguity, since a legacy card had exactly one `MeaningId` to begin with). Because the row's own `Id` never changes, every existing `LearningReviewEntity.CardId`/`LearningSessionCardEntity.CardId` reference continues to resolve with zero rewriting. `State`/`DueAtUtc`/`IntervalDays`/`EaseFactor`/etc. copy byte-for-byte, unchanged from the first pass. Every `ContextSnapshotEntity`/`LearningSessionCardEntity` for the group updates in place, unchanged from the first pass.
9. **Backfill `TargetAnswerVariantId`/`MatchedAnswerVariantId` for legacy review/queue rows**, scoped per card (unchanged mechanism from the first pass, now explicitly per-`CardId`): because a legacy word had at most one confirmed Meaning and therefore exactly one relevant term-side `AnswerVariant` per existing direction, every legacy review/queue row for a given `CardId` unambiguously targeted that card's own migrated preferred variant (§4.2 step 6a) — set `TargetAnswerVariantId` accordingly. `MatchedAnswerVariantId` backfills to the same variant only where `WasCorrect = true` and `WasTypedAnswer = true`; otherwise `null`.
10. **Migrate current word-level automatic reading/typing counters into `AnswerVariantProgressEntity`, keyed by `(CardId, AnswerVariantId)`, without duplicating progress.** For each word with non-default automatic counters, create exactly one `AnswerVariantProgressEntity` row per **legacy card** that existed (i.e., per `CardId`), attached to that card's own migrated preferred variant (§4.2 step 6a) — a legacy word had exactly one Sense's worth of automatic state *per direction* (one confirmed Meaning, at most two cards), so there is no cross-direction ambiguity: a word with both `TermToMeaning` and `MeaningToTerm` cards migrated gets up to two `AnswerVariantProgressEntity` rows (one per `CardId`), both sourced from the same single legacy counter set on `WordEntity` (the legacy schema never distinguished per-direction automatic state either, so this is a faithful, non-lossy split, not an invention of new information). `ReplayVersion = 1` for every migration-created row.
11. **Retain `WordEntity`'s global knowledge independently** (unchanged from the first pass).

### 4.3 The exact atomic migration order

1. **Create new tables and nullable back-reference columns, and rename `LearningCards.MeaningId` to `PreferredMeaningId`.** `CreateTableAsync<SenseEntity>()`, `<AnswerVariantEntity>()`, `<SenseAnswerVariantAssignmentEntity>()`, `<AnswerVariantProgressEntity>()`; add nullable columns to `MeaningEntity` (`SenseId`, `StableId`), `LearningCardEntity` (`SenseId`), `ContextSnapshotEntity` (`SenseId`), `LearningReviewEntity` (`TargetAnswerVariantId`, `MatchedAnswerVariantId`), `LearningSessionCardEntity` (`TargetAnswerVariantId`); execute the `PreferredMeaningId` column rename (§4.2 step 8, §2.7) via raw SQL. Also create the `SenseAnswerVariantAssignmentEntity` partial unique index (§2.6) here, since it depends only on the new table's own shape, not on any backfilled data. No existing unique index on `LearningCards` is touched yet.
2. **Generate and validate Senses, variants, and assignments.** Run §4.2 steps 1–7 for every legacy `MeaningEntity`/its cards. "Validate" here means: every created row satisfies its own internal shape (non-empty `StableId`, resolvable FKs, `SenseAnswerVariantAssignmentEntity.SenseId` agrees with its referenced `AnswerVariantEntity.SenseId`); no cross-row backfill has happened yet.
3. **Backfill Meaning, Context, Card (including the `PreferredMeaningId` value copy), Queue, and Review references.** Run §4.2 steps 8–10.
4. **Validate all references and counts.** Every `MeaningEntity.SenseId` resolves with matching `WordId`; every `LearningCardEntity.SenseId` resolves and its `PreferredMeaningId` resolves to a Meaning belonging to the same Sense; every `ContextSnapshotEntity.SenseId` resolves; every non-null `TargetAnswerVariantId`/`MatchedAnswerVariantId` resolves; every `SenseAnswerVariantAssignmentEntity` row's `(SenseId, CardDirection, AnswerVariantId)` is unique and `SenseId` agrees with the referenced variant's own `SenseId`; the count of migrated `LearningCardEntity` rows equals the pre-migration count. Any failure rolls back before step 5.
5. **Explicitly drop `IX_LearningCards_Word_Direction`.**
6. **Create the unique `(SenseId, Direction)` index** (`IX_LearningCards_Sense_Direction`).
7. **Validate migration invariants** (final gate): the new card index reports zero violations; the `SenseAnswerVariantAssignmentEntity` partial unique index (step 1) reports zero violations for `IsPreferred = 1` per `(SenseId, CardDirection)` (re-asserted explicitly, not merely relied on structurally); every word migrated out of `Prepared`/`Learning`/`Mastered` now has `Status = UnknownBacklog` and a matching Sense `Status`; no `WordEntity.Status` holds `Prepared`/`Learning`/`Mastered` anywhere; no `LearningCardEntity.MeaningId` column remains (the rename from step 1 fully replaced it).
8. **Set `PRAGMA user_version = 8` last** — only after step 7 passes with zero violations.
9. **Commit the transaction.**

### 4.4 Atomicity and mechanism

Unchanged from the first correction pass: `DatabaseSchema.CurrentVersion = 8`; the existing future-version guard is unchanged; §4.3's steps run inside one real SQLite transaction via the same `RunInTransactionAsync`-equivalent mechanism already used throughout the codebase; legacy rows are never deleted (the one column rename, §2.7/§4.2 step 8, is the sole exception to "purely additive," explicitly authorized by this pass for the reason given there).

### 4.5 Migration-invariant validation gate (referenced by §4.3 steps 4 and 7)

Unchanged in structure from the first pass, now also covering: `LearningCardEntity.PreferredMeaningId` resolution to a Meaning of the same Sense (step 4); the `SenseAnswerVariantAssignmentEntity` partial-unique-index invariant (step 7); `SenseAnswerVariantAssignmentEntity.SenseId` agreement with its variant's own `SenseId` (both steps). Both gates fail closed (§4.6) — neither is a warning-only check.

### 4.6 Fail-closed behavior (referential-integrity corruption only — see §4.1)

Unchanged from the first correction pass, extended to the new entities: a `SenseAnswerVariantAssignmentEntity` whose `AnswerVariantId` does not resolve, or whose `SenseId` disagrees with its variant's own `SenseId`, is a corrupt reference and fails the migration closed — never guessed at or silently corrected.

### 4.7 Rollback, crash-safety, idempotency, and repeated initialization

Unchanged from the first correction pass. The `PreferredMeaningId` rename (§2.7) does not change this section's guarantees: it happens inside the same single transaction as everything else, so a crash before commit leaves the column named `MeaningId` still, exactly as it was pre-migration — rollback is still "logically unchanged," including the column's own name.

## 5. Archive format v2

### 5.1 Compatibility contract

Unchanged from the first correction pass in mechanism (writer emits v2 only after Schema 8 ships; reader accepts `{1, 2}`; v1 upgrades in memory using the identical §4.2 algorithm, never rejecting for grouping ambiguity; `cardKeys` becomes `(SenseId, Direction)`-keyed for v2; old applications reject v2 safely; excluded-data categories unchanged; `StableId` is the cross-device reference, never an archive-local ID). Preserved data now explicitly includes, per this pass's corrections: `AnswerVariant` rows (pure text expressions), `SenseAnswerVariantAssignment` rows (direction-specific `Requirement`/`IsPreferred`), `LearningCard.PreferredMeaningId` (per-direction, not per-Sense), target/matched `AnswerVariant` references in reviews, target `AnswerVariant` references in completed queue rows, and the `(CardId, AnswerVariantId)`-scoped progress cache (or sufficient review events to rebuild it).

### 5.2 New/changed v2 domain shapes (contract, not code)

```
BackupPayload (v2)
├─ sourceMaterials      -- unchanged
├─ vocabulary           -- unchanged (Word-level fields only, §2.1)
├─ senses               -- one BackupSense per SenseEntity
│    BackupSense(Id, StableId, VocabularyId, SourceLanguage, ExplanationLanguage, ProviderSenseId,
│                TopicOrDomain, PartOfSpeech, GrammaticalRelationship, AcronymExpansion,
│                DefaultMeaningId, Status, CreatedAtUtc, UpdatedAtUtc)
│                -- CORRECTED: DefaultMeaningId (non-authoritative fallback), no PreferredAnswerVariantId
├─ preparedLearning     -- BackupPreparedItem (Meaning) gains SenseId, StableId
├─ answerVariants       -- one BackupAnswerVariant per AnswerVariantEntity
│    BackupAnswerVariant(Id, StableId, SenseId, AnswerLanguage, DisplayText, NormalizedText,
│                        SourceMeaningId, CreatedAtUtc, UpdatedAtUtc)
│                        -- CORRECTED: no Requirement field here (moved below)
├─ senseAnswerVariantAssignments -- NEW: one BackupSenseAnswerVariantAssignment per assignment row
│    BackupSenseAnswerVariantAssignment(Id, StableId, SenseId, CardDirection, AnswerVariantId,
│                                       Requirement, IsPreferred, CreatedAtUtc, UpdatedAtUtc)
├─ answerVariantProgress -- one entry per AnswerVariantProgressEntity
│    BackupAnswerVariantProgress(CardId, AnswerVariantId, InteractionMode,
│                                ConsecutiveReadingSuccessCount, ConsecutiveTypingSuccessCount,
│                                ConsecutiveTypingFailureCount, LastAssessedAtUtc,
│                                MasteryReviewExtensionScheduled, IsMastered, ReplayVersion,
│                                CreatedAtUtc, UpdatedAtUtc)
│                                -- CORRECTED: keyed by (CardId, AnswerVariantId), not AnswerVariantId alone
├─ learning
│    cards       -- BackupLearningCard gains SenseId (new primary key component) and PreferredMeaningId
│                    (renamed/repurposed from the archive's existing PreparedItemId field — CORRECTED:
│                    this is now the card's own authoritative preferred-Meaning reference, per direction,
│                    not a legacy-only value); VocabularyId retained as legacy denormalization
│    reviewEvents -- BackupLearningReview gains TargetAnswerVariantId and MatchedAnswerVariantId (nullable)
├─ workflows
│    learningSessions[].queueItems -- BackupLearningQueueItem gains TargetAnswerVariantId (nullable),
│                                      preserved for completed queue items
│    -- VocabularyReviewItem/PreparationItem continue referencing VocabularyId as today
└─ extensions           -- unchanged
```

**Preferred-variant conflicts remain card/`FutureCardIdentity`-specific (corrected framing, consistent with the per-card model above):** when a matched card (same `FutureCardIdentity`, i.e. same `SemanticMeaningIdentity` + `Direction`) on target and archive references a different `PreferredMeaningId`, the resulting `PreferredVariantSelectionDecision` (`backup-merge-v1-design.md` §16.5/§18) compares each side's referenced `ExactMeaningVariantIdentity` **for that one card's own direction** — never a Sense-wide comparison — because `PreferredMeaningId` is now itself a per-card fact (§2.7), the comparison this decision already performs (per-`FutureCardIdentity`, which already includes `Direction`) was already correctly scoped; this pass only corrects which schema field the comparison reads from (`LearningCardEntity.PreferredMeaningId`, not a Sense-wide pointer).

The `cardKeys` invariant and every other existing `ValidatePayloadGraph` check apply to the new collections identically in kind: `senses`/`answerVariants`/`senseAnswerVariantAssignments`/`answerVariantProgress` each get their own `EnsureUniqueIds` pass, `StableId` uniqueness is a separate additional check, and every cross-reference (`SenseId`/`AnswerVariantId`/`CardId`/`TargetAnswerVariantId`/`MatchedAnswerVariantId`) must resolve before any row is valid.

### 5.3 Filenames and extensions

Unchanged from the first correction pass: manifest version bump alone is sufficient; `.kfarchive` and the two-entry ZIP layout are retained.

## 6. Slice plan

| # | Slice | Depends on | Scope | Acceptance criteria |
| --- | --- | --- | --- | --- |
| 0 | **This document** (architecture decision, corrected twice) | KF-BACKUP-002 Slice 3 (merged) | Documentation only | Internally consistent; no code changed |
| 1 | **Schema 8 entities and migration** | Slice 0 | New entity classes (`SenseEntity`, `AnswerVariantEntity`, `SenseAnswerVariantAssignmentEntity`, `AnswerVariantProgressEntity`), additive columns, the `LearningCards.MeaningId → PreferredMeaningId` rename, the `SenseAnswerVariantAssignments` partial unique index, `DatabaseSchema.CurrentVersion = 8`, the §4.3 nine-step migration. Tests cover: grouping (never blocking startup), the Word-status **and** PreparationState compatibility treatment (§2.1), the partial-unique-index singleton enforcement **including a concurrency test** (two racing transactions, exactly one wins), fail-closed referential-corruption cases, idempotency, future-schema rejection, crash-mid-migration rollback, row-ID preservation (`LearningCardEntity.Id`, `LearningReviewEntity.CardId`, `LearningSessionCardEntity.CardId`), and the `MeaningId`-rename's zero-data-loss property. **No archive change, no service/UI change.** | Every §4 invariant has a passing test, including the concurrency test for `IsPreferred` singleton enforcement |
| 2 | **Archive v2 dual-reader/writer** | Slice 1 | `BackupModels.cs` gains the §5.2 DTOs (including `senseAnswerVariantAssignments`); writer/reader dual-format support; `cardKeys` becomes `(SenseId, Direction)`-keyed for v2. | A v1 fixture archive upgrades identically to the live migration; a v2 archive with direction-specific assignments and per-card `PreferredMeaningId` round-trips; `StableId` (including on assignments) round-trips |
| 3 | **Preparation multi-sense selection, topic persistence, and PreparationState-aware candidate matching** | Slice 1 | `PreparationService.AcceptAsync`'s guard becomes Sense-scoped; `PreparedMeaningInput` gains `TopicOrDomain`/`PartOfSpeech`; **implements the resolved `WordEntity.PreparationState` contract (§2.1):** the preparation selector compares a new context/provider candidate against existing Senses by matching key, links/skips only genuine matches, and surfaces unmatched/ambiguous candidates as reviewable regardless of `PreparationState`. **No archive change, no UI.** | A word with `PreparationState = Prepared` and one confirmed Sense still surfaces a genuinely distinct second-sense candidate from a new document context; a matching candidate is linked/skipped, never duplicated |
| 4 | **Answer-variant assignment and direction-specific mastery progress** | Slice 1 | `SenseAnswerVariantAssignmentEntity` read/write logic; `AnswerVariantProgressEntity` keyed by `(CardId, AnswerVariantId)`; `LearningSessionCardEntity`/`LearningReviewEntity` target/matched fields populated; the assignment-aware synonym-credit rule and the per-card reactivation rule implemented. **No schema shape change beyond Slice 1, no archive change, no UI.** | The same variant can be `Required` for one direction and `AcceptedOnly`/absent for the other on the same Sense, with independently tracked progress; a matched-but-non-targeted synonym credits only a variant that is itself a valid `Required` assignment for that same card; a card reaches `Retired` only once every `Required` assignment for its own direction is mastered |
| 5 | **Sense-addressed learning cards and queue behavior** | Slice 3, Slice 4 | `LearningService` reads via `card.SenseId`/`card.PreferredMeaningId`; queue-building interleaves `(Word, Sense, AnswerVariant)` per direction; exact reactivation due-date mechanics specified; `WordId` on `LearningCardEntity` becomes removable. | A word with two Senses shows two independently due cards; a card's own `PreferredMeaningId` drives its displayed content independently of its sibling direction's card |
| 6 | **MergePreflight adaptation** | Slice 1, Slice 2 | `MergePreflightPlanner`/`MergePreflightService` updated for the real schema; `AnswerVariantRole`/`AnswerVariantRolePrecedencePolicy` revised to the `SenseAnswerVariantAssignmentEntity` model; review-event fingerprint extended; `StableId`-first matching (including for assignments); `PreferredVariantSelectionDecision` reads per-card `PreferredMeaningId`. **No merge writer, no Import routing, no UI.** | Existing merge tests pass against the corrected model |
| 7 | **Merge writer and Import routing** | Slice 6 | KF-BACKUP-002's slices 5–6, now implementable. **No UI.** | KF-BACKUP-002's existing acceptance criteria hold |
| 8 | **UI and end-to-end convergence validation** | Slice 5, Slice 7 | Multi-sense preparation UI, per-direction assignment/mastery display, topic/domain display, preferred-Meaning/assignment editing UI. | Full acceptance checklist passes end to end |

## 7. Alias-language rule — resolved (was an open assumption; now code-verified)

Unchanged from the first correction pass: aliases are always term-side (`SourceLanguage`) spelling variants, verified by `LearningService.CheckSpellingAsync`'s unconditional (direction-independent) comparison against `meaning.DisplayTerm`+aliases+`word.Language`, and by the UI label "Accepted spelling aliases."

## 8. Documentation updates made by this slice

- **Corrected (this pass):** this document (§0/§1 corrected-pass notes list the five defects fixed).
- **Updated (minimally):** [BACKLOG.md](../BACKLOG.md), [CURRENT_WORK.md](../CURRENT_WORK.md), [structured-vocabulary-import-and-sense-learning.md](../plans/structured-vocabulary-import-and-sense-learning.md), [backup-merge-v1-design.md](backup-merge-v1-design.md) — each updated only to reflect this correction pass.

No implementation beyond this architecture branch is claimed anywhere in these updates.

## 9. Remaining unresolved decisions

All items the review asked to resolve are retired:

- ~~Partial-uniqueness enforcement for a `PrimaryAnswer` role~~ — retired (first pass): no such role exists in the corrected model.
- ~~Legacy-alias-to-alias language assignment~~ — resolved (§7).
- ~~`WordEntity.PreparationState`'s word-vs-sense scoping~~ — **resolved this pass** (§2.1): technical workflow/cache field only, never semantic-completeness state; full contract documented, including Schema-8 compatibility treatment.
- ~~Sense-wide preferred-answer/requirement modeling~~ — **corrected this pass**: replaced by `SenseAnswerVariantAssignmentEntity` (§2.6), scoped per `(SenseId, CardDirection, AnswerVariantId)`.
- ~~Sense-wide preferred-Meaning modeling~~ — **corrected this pass**: `SenseEntity.DefaultMeaningId` (fallback) plus `LearningCardEntity.PreferredMeaningId` (authoritative, per card/direction, §2.7).
- ~~Progress uniqueness by `AnswerVariantId` alone~~ — **corrected this pass**: `(CardId, AnswerVariantId)` (§2.9).

Genuinely remaining, forward-looking items:

1. **Exact reactivation scheduling mechanics** (§2.9): the rule "a newly-`Required` assignment can un-retire a card" is binding; the precise due-date/interval mechanics are left to §6 slice 5.
2. **`AnswerVariantRole`/`AnswerVariantRolePrecedencePolicy` merge-planner revision** (§2.6, §6 slice 6): the already-merged KF-BACKUP-002 Slice 3 types need a forward revision to the `SenseAnswerVariantAssignmentEntity` model — tracked as required future work, not performed in this document.
3. **`LearningCardEntity.MeaningId → PreferredMeaningId` rename mechanics** (§2.7): `ALTER TABLE ... RENAME COLUMN` is the binding first choice; the portable add-copy-drop fallback is documented but its necessity depends on the bundled SQLite version, to be confirmed at Slice 1 implementation time — this is an implementation-detail contingency, not an open architectural decision.
4. **Content-fingerprint collision risk** for the Sense-grouping algorithm — accepted, carried-forward residual risk, unchanged by this pass.
5. **Exact wording/localization of any future sense-merge, requirement-editing, or preferred-Meaning/assignment-editing UI** is explicitly out of scope for every slice except Slice 8.
