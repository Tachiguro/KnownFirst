# KF-BACKUP-002 — Non-destructive portable archive merge (design)

**Status:** Complete implementation. Slices 1–9 are merged to `master`. Slice 9 was merged through PR #45. This document remains the architecture and historical implementation design for non-destructive populated-target import. The title is retained for historical continuity and does not mean this document alone owns the complete archive-format-v2 payload contract. Schema 10 is now active on master. The Schema-8/9 implementation slices described below remain historical foundations of the current path. PR #51 activated Schema-9 review-session history storage. PR #52 merged Package A (completed-review identity, planner, target-index parity, and characterization coverage) to `master`. Package A did not create the entire Schema-9 schema or all archive-format-v2 behavior. **Package B (writer evidence) is implemented, independently reviewed (approved), automated-validated, and merged via PR #65 (merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`); a PR review found one documentation-currentness finding and no code/test finding, addressed by the branch documentation — see §19.** Package C (convergence hardening) is implemented, independently reviewed and corrected, `TEST_ONLY`-validated (`ALL_AUTOMATED` 1776/0/0), passed final PR review, and merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`). `POST_MERGE_SYNC_ONLY` completed successfully. See §20. **Package D (KF-BACKUP-003, PreparationSession/LearningSession/LearningReview v2 canonical-ordering hardening) is implemented, automated-validated (`ALL_AUTOMATED` 1786/0/0), approved by its final PR re-review (0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT), and merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`); `POST_MERGE_SYNC_ONLY` completed successfully — see §21.** **KF-BACKUP-004 (Schema-9 `LearningReview` merge integrity: collision-free physical review action keys, meaning-aware answer-variant identity, replay alignment) is implemented, automated-validated (`ALL_AUTOMATED` 1795/0/0), approved by final PR review (0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT), and merged via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`); `POST_MERGE_SYNC_ONLY` completed successfully — see §22.** **KF-BACKUP-005A (Schema-10 stable learning-workflow identity foundation: immutable `StableId` columns for `LearningSessions`/`LearningSessionCards`, deterministic Completed bootstrap, one-time Active GUID bootstrap, archive V2 DTO evolution) is implemented, automated-validated (`ALL_AUTOMATED` 1812/0/0; Windows Debug/Release passed, Android Debug/Release passed, 0 build errors), approved by final PR review (0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT), and merged via PR #79 (merge commit `e56b8bfa27dfe1d630fbacfed24e6d56ea876026`); `POST_MERGE_SYNC_ONLY` completed successfully — see §23.** **§16's meaning-centric product model is the binding architecture decision** — see [meaning-centric-learning-v1-design.md](meaning-centric-learning-v1-design.md) (KF-MEANING-001 Slice 0), which restates §16.1's model as decided and adds the full Schema 8 entity model and Schema 7→8/archive v1→v2 migration contracts. Populated Schema-8/9/10 import can consume format v2 directly, or format v1 after in-memory upgrade.
**Backlog item:** [KF-BACKUP-002](../BACKLOG.md), P1, blocks public release readiness, does not block Beta 12 Internal Testing.
**Builds on:** [backup-format-v1.md](backup-format-v1.md) (binding contract for the *existing* Restore-into-empty behavior, unchanged by this proposal), `Models/BackupModels.cs`, `Data/BackupImportRepository.cs`, `Data/BackupSnapshotRepository.cs`, `Services/DataSafety/BackupService.cs`, `Services/TextReviewService.cs`, `Services/Study/PreparationService.cs`, `Services/Study/LearningService.cs`.

**KF-BACKUP-005C and `LegacyReviewSummaries` canonical ordering package status (2026-08-11):** Populated-target Schema-10 Active learning-workflow convergence and conflict safety are binding `master` behavior, merged via PR #83 (feature head `bc30e9ee9a3689cc4d8b7d108ac83dc037a1b962`, merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`); `POST_MERGE_SYNC_ONLY` completed successfully. Focused `Schema10ActiveArchive`: **8 passed / 0 failed / 0 skipped**; controlled affected/regression scope: **254 passed / 0 failed / 0 skipped** with Workers=1 and **254 passed / 0 failed / 0 skipped** with Workers=8; final relevant reviews: **0 BLOCKER / 0 MAJOR / 0 MINOR**. See §§24–25 for the historical 005B and binding 005C contracts. The `LegacyReviewSummaries` canonical ordering correction is now binding `master` behavior, merged via PR #85 (feature head `baf5fcda0a017c1492a08dac730d683c1554784d`, merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`); `POST_MERGE_SYNC_ONLY` completed successfully. See §21.1 for the full defect, correction, and evidence record.

**German Package 5A-2 (portable/cross-installation derived-term-evidence transport) package status — merged `master` state:** implemented, independently reviewed (0 BLOCKER / 0 MAJOR findings; three MINOR findings deferred as non-blocking), and merged via PR #137 (merge commit `5d1d3c05bae6ab9f1c56d8c5f9a227121f432f9a`; validated PR head `2ff447e9f874d49e72fee0a549820adc1bdc3b39`); exact-head `FULL_VALIDATION` 2248 passed / 0 failed, all four Windows/Android Debug/Release build gates PASS, exit code 0; `POST_MERGE_SYNC_ONLY` completed successfully. This package adds `DerivedTermEvidence` as its own merge entity kind, using a semantic merge identity built from the owning candidate's existing `ReviewCandidateIdentity`, the source compound's own vocabulary identity, and the transported source-position/component fields — no SQLite id and no archive-local id participate, consistent with this document's established content-derived identity approach. It does not change archive format V2 or `DatabaseSchema.CurrentVersion` (11). Full field-level contract and test evidence: [DATABASE_CONTRACT.md](../DATABASE_CONTRACT.md) "Schema-11 Derived-Term Evidence Contract"; operational status: [CURRENT_WORK.md](../CURRENT_WORK.md) and [PROJECT_STATE.md](../PROJECT_STATE.md).

## Product contract: single "Import data" action

This section states the final user-facing behavior this design and [backup-format-v1.md](backup-format-v1.md) jointly implement, so slice-by-slice implementation work has one unambiguous target. It does not change anything below — §§0–15 already imply every point here; this section only makes the resulting product contract explicit in one place.

- KnownFirst has **one** primary user-facing action: **"Import data"**. There is no separate user-facing "Merge" button as a first-class alternative the user chooses between — Import is a single entry point.
- If the target installation has **no durable user data** (`BackupImportRepository.HasDurableUserData` is false), Import uses the existing, unchanged **Restore-into-empty** path (§1, §7).
- If the target installation **already contains durable user data**, Import uses the **Merge** path defined by this document (§4–§12) instead of failing. This is the only behavioral difference Merge introduces at the product level: Import stops being conditional on an empty target.
- A valid, compatible archive **must not** be rejected merely because the target already contains data — `PortableImportStatus.TargetNotEmpty` (§1) becomes a routing signal to the Merge path, not a terminal user-facing failure, once Merge ships.
- Importing an archive whose contents are already fully present in the target **succeeds with a no-change result** — every entity classifies as an exact duplicate (§11), zero rows are written, and the user sees a successful "nothing new to import" outcome rather than an error.
- Corrupt, manipulated, incompatible, or unsafe archives **still fail closed without mutation**, exactly as today's Restore path already guarantees — Merge only relaxes the empty-target precondition; it does not relax validation, checksum verification, or format-compatibility checks.
- Restoring a private pre-merge recovery copy (§8, "safety copy") remains a **separate, distinct Recovery action** in Settings — it is not part of the "Import data" flow and is never triggered implicitly by Import.

## 0. Revision history

This is a **revision** of the design first written for this task, not a restart. Everything in §§0–15 below either carries a prior section forward unchanged, or replaces it in response to one of ten defect categories raised in review. The table maps each category to what changed and why; sections not listed here (worked-scenario framing, format-compatibility conclusion, the R1 "history is truth" principle, the out-of-scope note) carry forward from the prior revision essentially unchanged.

| # | Defect category | What changed |
| --- | --- | --- |
| 1 | Source-material identity was content-hash-only | §4.1 now defines a 4-part identity and explains, with a verified code citation, why single-device dedup logic cannot be reused as-is for cross-device identity |
| 2 | Assumed one Meaning per Word; LearningCard identity ignored Meaning | §4.2–§4.3 replace the assumption with a code-verified answer: the schema already tolerates multiple Meanings per Word; LearningCard's real DB-enforced identity is `(WordId, Direction)`, and the design now explains precisely how Meaning plurality and that fixed uniqueness constraint interact |
| 3 | Generic newest-timestamp-wins for text content | §5.2 replaced with an explicit preserve-both-variants policy and a documented schema-compatible representation |
| 4 | No mandatory safety copy | §8 added: full safety-copy design, with "safety copy failure means Merge does not begin" as a hard precondition |
| 5 | No clock-skew policy | §6 added: SHA-256 event fingerprints, exact fingerprinted fields, tie-break rule, and an honest statement of what the design can and cannot guarantee under skew |
| 6 | Generic rank language for enums | §5.1, §5.3, §5.4 replaced with entity-specific, evidence-grounded conflict tables; every cell has an explicit, non-destructive resolution |
| 7 | ReviewSession identity assumed Document-alone; Order-renumbering unproven | §4.4 verifies the assumption against the code and proves it holds *given* the corrected Document identity from §4.1; §4.4 also proves Order/QueueOrder renumbering is safe by citing every read site |
| 8 | Idempotency asserted, not proven | §11 replaced with concrete proof sketches for six named scenarios |
| 9 | Preflight summary too coarse | §9 revised to the seven required categories, including safety-copy status |
| 10 | Implementation slice 1 mixed contract work with mutation | §12 revised so slice 1 is strictly mutation-free |

## 1. Confirmed current limitation

Unchanged from the prior revision, still directly verified against shipped code:

- `BackupService.ImportPortableArchiveAsync` calls `BackupImportRepository.HasDurableUserData(connection)`; any existing row in any of the 16 in-scope tables produces `PortableImportStatus.TargetNotEmpty` with zero mutation.
- `BackupImportRepository.ImportIntoEmptyDatabase` re-checks the same condition inside the transaction and throws if it is ever invoked against non-empty data — there is no code path today that writes into a populated database.
- Archive-local IDs (`BackupSourceMaterial.Id`, `BackupVocabularyItem.Id`, etc.) are opaque, export-time-only, and explicitly documented as meaningful only inside one archive — unusable as a cross-device merge key.

## 2. Revised invariants

Restates task requirements 1–12 (from the original KF-BACKUP-002 task) as concrete rules, now including two invariants added in this revision (13, 14):

| # | Invariant | Mechanism |
| --- | --- | --- |
| 1 | Restore into empty stays as-is | `MergePortableArchiveAsync` is a new method; `ImportPortableArchiveAsync` is untouched |
| 2 | Merge is a separate, explicit mode | New service method, new UI action, new status/error codes |
| 3 | Never delete/replace/shorten/regress | No `DELETE`; every conflict rule in §5 is monotonic, additive, or explicitly preserves both variants — never a silent overwrite |
| 4 | Preserve archive-only data | Unmatched archive entities insert unchanged, same code path as today's importer |
| 5 | Preserve target-only data | Merge never reads target-only rows for anything but matching |
| 6 | Re-import is a no-op | Matching is by stable content-derived key (§4), never by archive-local ID |
| 7 | Merge(A→B) then Import(A) again stays idempotent | Proven per-scenario in §11 |
| 8 | PC/phone divergence scenario | Worked through in §10 using the revised identity/conflict rules |
| 9 | Atomic | Reuses `IKnownFirstDatabase.RunInTransactionAsync`, the same mechanism Restore already uses |
| 10 | Full rollback on failure | Same transaction; extended with merge-specific invariant checks (§7 unchanged from prior revision, folded into §12 slice 4) |
| 11 | Schema 7 / archive format v1 unchanged unless proven unavoidable | Still proven unnecessary — see §"Archive-version compatibility" (unchanged from prior revision) |
| 12 | Preferences/consent stay outside | Unchanged: neither `BackupSnapshotRepository` nor `BackupImportRepository` ever reference `Preferences.Default` |
| 13 *(new)* | Merge does not begin without a validated safety copy | §8 |
| 14 *(new)* | No enum or state conflict is resolved by silent destructive fallback | Every cell in §5's matrices is one of: keep target, adopt archive, deterministic monotonic result, preserve-both-and-derive, or unresolved-with-preflight-warning — never an unexplained default |

## 3. Design principle: history is truth, current state is derived (unchanged)

> **Rule R1.** Wherever a field is an aggregate or scheduler snapshot computed from child rows or chronological events, merge does not combine two snapshots directly. It merges/dedupes the underlying child rows or event history first, then **recomputes** the aggregate/scheduler field — the same computation the app already performs when that data is first created.

This remains the foundation for LearningCard scheduling state (§5.3), Word automatic-learning streak counters, and session aggregate counts. It is what makes the design idempotent and commutative by construction, proven concretely in §11.

## 4. Revised entity-by-entity stable identity rules

### 4.1 Source material / Document — corrected per defect #1

**Stable identity:** `(ContentFingerprint, TextLanguage, LookupMode, CanonicalTargetLanguage)`, where `CanonicalTargetLanguage` is `TargetLanguage` normalized to `string.Empty` for `LookupMode = Definition` and for any `null` archive value, and the raw target-language code otherwise.

Verified basis:

- `TextReviewService.CreateContentFingerprint` is `Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))` — plain SHA-256 of the exact UTF-8 bytes, no trimming or normalization beyond that. This is the *same* value the backup format already calls `ContentSha256`; "fingerprint" here is not a new concept, it is the existing one, spelled with the DB entity's own field name.
- `DocumentEntity.TargetLanguage` is already stored as `request.TargetLanguage ?? string.Empty` — the live database is already canonical. `BackupSourceMaterial.TargetLanguage` is `string?` in the archive DTO, so the archive side is **not** guaranteed canonical and must be normalized to `string.Empty` before the identity key is computed or compared, exactly as `LookupMode = Definition` archives already do at the DB layer.
- **Title is excluded from identity.** `ImportText.razor` exposes `document-title` as a free-text field the user types at import time (`Import_TitlePlaceholder`), entirely independent of the imported content — there is no code path that derives `Title` from `Content`. Two devices importing byte-identical text can legitimately end up with different titles purely from what the user happened to type; title is mutable presentation metadata, not identity.

**Why the same text imported for Definition and for Translation must remain separate source materials, even though the live single-device app's own duplicate check is coarser:**

`TextReviewService.CreateImport` (lines 557–574) checks only `(ContentFingerprint, TextLanguage)` before rejecting a second import as `ExactDuplicate` — it does **not** compare `LookupMode`, `ExplanationLanguage`, or `TargetLanguage`. This is safe on a single device specifically *because* the rejected import created nothing: no document, no sentences, no occurrences, no review session ever existed under the second configuration, so there is no second graph of real user data to lose. The coarse check exists to stop the *same* device from ever holding two documents for the same text, not to declare that two different lookup configurations of the same text are semantically the same object.

Merge faces a different situation: PC and phone can each *independently* import the identical text under different `LookupMode`/`ExplanationLanguage`/`TargetLanguage` choices, and — because the single-device coarse check never saw the other device's import — **each side has already built a complete, real, independently-earned graph**: its own `SentenceSpanEntity` rows, `WordOccurrenceEntity` rows, a `ReviewSessionEntity`, possibly `MeaningEntity` rows and cards derived from words that only exist in that configuration's `ExplanationLanguage`. If merge collapsed the two into one Document using the coarse `(fingerprint, TextLanguage)` key, it would be forced to discard one side's entire document-scoped graph — a direct violation of invariant 5 ("preserve target-only data") or invariant 4 ("preserve archive-only data"), whichever side lost. Using the finer 4-part key instead means the worst case is one extra Document row holding a second, legitimately distinct copy of the same text — never a forced deletion. This is exactly the same reasoning already used in the prior revision for why tokenization-affecting configuration belongs in the identity key; this revision makes the canonical-empty-value rule for `TargetLanguage` explicit and grounds the whole argument in the verified single-device dedup code rather than asserting it.

### 4.2 Meaning — corrected per defect #2

**Verified finding: the schema does not enforce one Meaning per Word, and at least one existing code path already treats a word's meanings as a set.**

- `MeaningEntity` carries `WordId` as a plain indexed (non-unique) foreign key — no unique index constrains it.
- `PreparationService.cs:344-345` (the Accept-preparation flow) only guards against creating a **second confirmed** meaning: `connection.Table<MeaningEntity>().Any(m => m.WordId == word.Id && m.ConfirmedByUser)` throws `InvalidOperationException("This vocabulary item is already prepared.")`. This is a forward-looking, application-level guard on ordinary single-device preparation — not a stored database invariant, and it says nothing about how many *unconfirmed* or (as of this design) *merge-preserved* meaning rows may coexist.
- `LearningService.cs:257-269` (the "mark word permanently known" cleanup path) already queries `connection.Table<MeaningEntity>().Where(m => m.WordId == wordId)` as a set and bulk-deletes with `DELETE FROM Meanings WHERE WordId = ?` — proving the codebase already handles "meanings for a word" as zero-to-many in at least one real, shipped path.
- Every other read of a specific meaning (`LearningCardEntity.MeaningId` lookups in `TextReviewService.cs:111`, `LearningService.cs:422`) is by the meaning's own primary key, never re-derived from `WordId` — so nothing downstream assumes singularity.

**Stable identity (allows multiple Meanings per Word by design):**

`(stable WordId, SourceLanguage, ExplanationLanguage, NormalizedDefinition, NormalizedTranslation, ProviderSourceIdentity)`, where:

- `NormalizedDefinition`/`NormalizedTranslation` are the trimmed, ordinal-compared `Definition`/`Translation` field values (empty string when absent — never `null` vs `""` ambiguity).
- `ProviderSourceIdentity` is `(Source, SourceProject, SourcePageTitle, SourceRevisionId)` — the existing `BackupSourceReference` fields, already present in every archive.

Two Meaning rows for the same word are the **same** entity (dedupe) only when every component matches exactly. Two Meanings for the same word that differ in `ExplanationLanguage`, `TargetLanguage`-equivalent content, or the definition/translation text itself are **distinct, both-preserved** entities — this is the direct mechanism that satisfies defect #3's "preserve conflicting content" requirement at the data-model level: distinct content simply becomes a second row, not a conflict to adjudicate away.

### 4.3 LearningCard — corrected per defect #2

**Verified finding: `LearningCardEntity` has a real, DB-enforced unique index on `(WordId, Direction)`** (`IX_LearningCards_Word_Direction`) — not on `(MeaningId, Direction)`. `MeaningId` is a plain FK field on the card row.

Given this, and given §4.2 now allows multiple Meanings per word, the design must not claim a card's identity "includes" Meaning identity as an *additional key component*, because the physical schema only ever allows one persisted card per `(WordId, Direction)` regardless of how many Meanings exist for that word — proposing `(MeaningId, Direction)` as the identity would silently violate the existing unique index the moment two matched archives disagree about which Meaning a card belongs to.

The design instead treats this as two separate questions, both required by defect #2's underlying concern (don't treat any Meaning as interchangeable):

1. **Card *matching* identity** (which target row does an archive card correspond to): `(stable WordId, Direction)` — the true, schema-enforced identity. This part is unchanged from the prior revision.
2. **Which Meaning the surviving card references** (the part the prior revision under-specified): when target and archive both have a card at `(WordId, Direction)`, the merged card keeps pointing at whichever Meaning the **target's** card already references — that reference is live, schedulable state and is never silently repointed. If the archive's card referenced a *different*, distinct Meaning (per §4.2's identity — e.g. a translation-mode meaning versus the target's definition-mode meaning for the same word), that Meaning is still inserted as an additional preserved row per §4.2 (no data loss), but it does **not** get its own card, because the `(WordId, Direction)` slot is already occupied. This is surfaced explicitly in the preflight summary as a "preserved variant without an active card" (§9) rather than silently dropped — satisfying invariant 14 (no silent destructive fallback) precisely at the point where the schema's own uniqueness constraint forces a choice.

`LearningReviewEntity` history for a matched card is still merged/deduped and replayed per Rule R1 (§5.3) regardless of which Meaning is referenced — the Meaning reference and the scheduling state are independent concerns.

### 4.4 Session and candidate identities — verified per defect #7

**ReviewSession identity — verified safe to remain Document-alone, given the corrected Document identity in §4.1.**

`ReviewSessionEntity` carries `[Indexed(Unique = true)] DocumentId`, and `TextReviewService.cs:619-621` creates exactly one `ReviewSessionEntity` per successfully-inserted `DocumentEntity`, at the same call site, immediately after the document insert — there is no other creation path. Because §4.1 now defines Document identity as the 4-part key (not content hash alone), two devices that imported the same text under *different* `LookupMode`/`ExplanationLanguage`/`TargetLanguage` are correctly treated as two distinct Documents with two distinct, independently-legitimate `ReviewSessionEntity` rows — no merge conflict ever arises for that case. A conflict only arises when both sides' Documents match on the full 4-part key, which by construction means both sides analyzed the text under the *same* configuration — safe to treat as "the same document reviewed independently on two devices," which is exactly the scenario invariant 8 (PC/phone divergence) describes. **Conclusion: `ReviewSession` identity = stable `DocumentId` remains correct, and is now provably safe rather than assumed.**

Full identity table for the remaining session/candidate/event entities:

| Entity | Stable identity | Basis |
| --- | --- | --- |
| `ReviewSessionEntity` | `(stable DocumentId)` | Verified above; DB-enforced unique index |
| `ReviewCandidateEntity` | `(stable DocumentId via session, stable WordId)` | No enforced unique index beyond `(SessionId, Order)`, which is positional, not cross-run-stable; `WordId` is the semantically stable component |
| `PreparationSessionEntity` | Content fingerprint: `(StartedAtUtc, CompletedAtUtc, SHA-256 over the ordered list of stable WordIds among its Items)` | No FK ties a preparation batch to anything else; only completed/cancelled batches are ever portable (existing v1 rule) |
| `PreparationCandidateEntity` | Cascades from parent session's fingerprint decision | Same reasoning as ReviewCandidate, one level down |
| `LearningSessionEntity` | Content fingerprint: `(StartedAtUtc, CompletedAtUtc, SHA-256 over the ordered list of (stable CardId, Rating) among its QueueItems)` | Same reasoning as PreparationSession — no natural FK, only completed sessions portable |
| `LearningSessionCardEntity` | Cascades from parent session | Same reasoning |
| `LearningReviewEntity` | `(stable CardId, ReviewedAtUtc)`, tie-broken by the §6 event fingerprint | Append-only historical log; no enforced unique index; identity is content-derived — see §6 for the exact fingerprint definition |

**Order / QueueOrder renumbering — proven safe, not merely asserted.**

Every read site for these fields was checked directly:

- `TextReviewService.cs:108,126,130,445,724,886` and `Services/Study/PreparationService.cs:767-769,851,894,913,999,1042` and `Services/Study/LearningService.cs:393,734,812` and `Services/DataSafety/BackupModelMapper.cs:295,305,490,529,572` — **every single usage is `.OrderBy(item => item.Order)` / `.OrderBy(item => item.QueueOrder)` (a sort key) or a relative comparison (`item.Order > currentOrder`, finding "the next item after this one")**. No code compares an `Order`/`QueueOrder` value to an external absolute constant, and no code persists an `Order` value anywhere outside the row it belongs to.
- Therefore: **renumbering `Order`/`QueueOrder` sequentially within a merged session, while preserving each item's relative sequence, cannot change any business meaning**, because every consumer only ever asks "what comes after what," never "is this exactly value N." Only completed workflows are portable, so no live "resume from position N" flow can even observe a renumbered value.

## 5. Revised conflict-resolution rules and explicit matrices

### 5.1 `WordEntity.Status` (KnowledgeState) — corrected per defect #6

Verified write-site evidence: `ReviewCandidateEntity.Status`/review-decision code sets `Known`/`UnknownBacklog`/`Ignored` as three mutually exclusive review outcomes from `Unreviewed`; `PreparationService` moves a word from the backlog toward `Prepared`; `LearningService` creates cards once prepared, moving toward `Learning`; a mastery condition (evidenced by `LearningService.cs:557,708` setting `CardState.Retired` on the card, mirrored at the word level) moves toward `Mastered`. Grouping into tiers where every cross-tier pair has one unambiguous, monotonic, forward-only answer, and every same-tier pair is treated explicitly:

| Tier | States | Cross-tier rule vs any lower tier | Cross-tier rule vs any higher tier | Same-tier rule |
| --- | --- | --- | --- | --- |
| 0 | `Unreviewed` | — | adopt archive/keep target, whichever is higher (deterministic monotonic) | not applicable (only one state) |
| 1 | `Known`, `UnknownBacklog`, `Ignored` | adopt the tier-1 value (deterministic monotonic: any real review decision beats no decision) | keep the higher tier's value (deterministic monotonic) | **unresolved conflict** — three mutually exclusive review judgments with no natural ordering among them; **keep target's current value unchanged** (non-destructive default), record the archive's alternative value in the preflight summary (§9) as an unresolved status conflict — flagged in §13 for product sign-off on whether a future release should instead prompt the user to choose |
| 2 | `Prepared` | adopt archive/keep target, whichever is higher | keep the higher tier's value | not applicable |
| 3 | `Learning` | adopt archive/keep target, whichever is higher | keep the higher tier's value | not applicable |
| 4 | `Mastered` | adopt archive/keep target, whichever is higher | — | not applicable |

No cell in this table silently discards information: cross-tier resolutions always keep the more-advanced, strictly-implies-the-lower-tiers value; the one genuinely ambiguous case (tier 1) explicitly keeps the target unchanged and surfaces the alternative rather than guessing.

### 5.2 `PreparationState` — corrected per defect #6

| Pair | Resolution |
| --- | --- |
| `Unprepared` vs any other | Adopt the other (deterministic monotonic — any progress beats none) |
| `Preparing` vs `Prepared`/`PreparationFailed` | Adopt the terminal value (deterministic monotonic — an in-progress marker never outranks a completed outcome) |
| `Prepared` vs `PreparationFailed` | Adopt `Prepared` (deterministic monotonic) — unlike §5.1's tier-1 case, this is not a symmetric product judgment call: a recorded failure carries no user decision to preserve, while a successful preparation directly produces the confirmed Meaning content that §4.2/§5.4 already guarantee is preserved. Choosing success over a failure record discards no user intent. |
| Equal values | No-op (dedupe) |

### 5.3 `LearningCardEntity` fields — corrected per defect #6

Per Rule R1, `State`, `DueAtUtc`, `IntervalDays`, `EaseFactor`, `SuccessfulReviewCount`, `LapseCount`, `LastReviewedAtUtc`, `LastRating` are **derived**, not matrix-resolved: merge/dedupe the card's `LearningReviewEntity` rows (§4.4, §6), sort by the §6 ordering rule, and replay through `KnownFirst.Core.Learning.SimpleSpacedRepetitionScheduler` (confirmed to exist and to already special-case `Suspended`/`Retired` at `SimpleSpacedRepetitionScheduler.cs:12`) starting from the card's initial state.

`CardState.Suspended` and `CardState.Retired` were checked directly rather than assumed to need a manual conflict table:

- `Suspended` is referenced only in exclusion filters (`WorkflowStateService.cs:26`, `PreparationService.cs:57`, `LearningService.cs:338,477`) and enum mappings — **no code path in the current application ever sets a card to `Suspended`**. It is a reserved-but-unused state, like the format's `safety-backup-failed` error code.
- `Retired` **is** actively set, but only at `LearningService.cs:557,708`, both inside the ordinary review-processing flow (a card reaches a mastery/retirement condition as a *consequence* of a rated review) — it is a scheduler-driven outcome, not an out-of-band manual action.

**Conclusion: no separate conflict matrix is needed for `LearningCard.State` — it is fully covered by R1's replay, because every value it can hold, including `Retired`, is itself a product of replaying review events through the deterministic scheduler.** `MeaningId`: resolved per §4.3, not by this table. `CreatedAtUtc`: `min`.

### 5.4 Text content fields (Definition, Translation, AdditionalNote, aliases) — corrected per defect #3

The prior revision's "newest-`UpdatedAt`-wins" rule for `Definition`/`Translation`/`DictionaryExample` is withdrawn. It permitted a silent overwrite of live, schedulable content — inconsistent with invariant 3. Replacement policy, applied uniformly:

- **Exact equivalent content is deduplicated.** This is now handled at the identity layer, not the conflict layer: per §4.2, a Meaning whose `NormalizedDefinition`/`NormalizedTranslation`/`ExplanationLanguage`/`ProviderSourceIdentity` all match an existing Meaning for the same word **is** that existing Meaning — nothing to resolve, it is a plain duplicate skip.
- **Distinct non-empty content is preserved.** A Meaning that differs on any identity component from every existing Meaning for that word is inserted as an **additional** `MeaningEntity` row (§4.2). No text field is ever overwritten to make room for a competing value.
- **Existing target content is never silently replaced.** The target's existing Meaning row is left byte-for-byte as it was; the archive's differing content becomes a new row, never an in-place update to the target's row's `Definition`/`Translation`/`AdditionalNote`.
- **Existing learning-card references remain valid.** Per §4.3, a matched card's `MeaningId` continues pointing at whichever Meaning it already referenced; inserting an additional preserved Meaning row never repoints any existing card, so no FK is ever invalidated or silently redirected.
- **`AdditionalNote` and aliases specifically:** because divergent `Definition`/`Translation` content now produces a genuinely separate Meaning row (carrying its own `AdditionalNote` and `AcceptedAliasesJson`), there is no cross-row note/alias merge to perform — each preserved Meaning keeps its own note and alias list exactly as authored. `ConfirmedByUser`: each row keeps its own value; if the archive's distinct Meaning was user-confirmed, it is inserted as confirmed, independent of the target's existing confirmed Meaning — this is the concrete case flagged in §4.3 as "preserved variant without an active card" when it cannot also receive a `LearningCard` due to the `(WordId, Direction)` uniqueness constraint.
- **Representation within the current schema — no extension required.** `MeaningEntity` already has no unique constraint on `WordId` (§4.2); multiple rows per word are a schema-compatible representation today, evidenced by `LearningService.cs:257-269` already querying and bulk-operating on "meanings for a word" as a set. **No schema extension is needed** to represent preserved variants — this resolves the open question from the task ("document how multiple preserved variants are represented using the current schema, or explicitly identify a schema extension as required") in favor of the no-extension answer.
- **Unresolved content variants appear in the preflight summary** (§9) as a distinct category — "existing records enriched" (a genuinely new Meaning row added for a word that already had one) — separate from plain new-word insertions, so the user sees specifically where a second definition/translation is being added rather than discovering it only after the fact.

## 6. Clock-skew and event-fingerprint rules — added per defect #5

**Do not use timestamps alone for duplicate detection.** For `LearningReviewEntity`, the duplicate/identity key combines content with time:

**Event fingerprint (`LearningReviewEntity`):** `SHA-256` over an unambiguous canonical byte encoding of these **immutable** fields, in this fixed order — implemented as `KnownFirst.Services.DataSafety.Merge.LearningReviewFingerprintPolicy`/`CanonicalFingerprintBuilder` (slice 1):

```
stableCardKey            (the card's stable (VocabularyIdentity, Direction) match identity — never the raw int CardId)
ReviewedAtUtc             (normalized to UTC, invariant-culture round-trip "O" format)
Rating                    (enum symbolic name: Again | Hard | Good | Easy)
WasTypedAnswer            (true | false)
WasCorrect                (true | false)
DueAtUtc                  (normalized to UTC, invariant-culture round-trip "O" format)
IntervalDays              (invariant-culture decimal integer)
EaseFactor                (invariant-culture "G17" round-trippable double)
```

This is **not** a `|`-joined string. Naive delimiter-joining is ambiguous whenever a field's own content can contain the delimiter or produce equal concatenations from different splits (e.g. `("a|b","c")` and `("a","b|c")` both join to `"a|b|c"`). Instead, each field is written as an explicit null/non-null marker byte followed (for non-null values) by a fixed-width 4-byte big-endian UTF-8 byte-length prefix and the raw payload bytes, so every field's boundary is self-describing and no two distinct field sequences can ever collide on the same byte stream. The complete byte sequence is prefixed with a version/domain discriminator string (`KnownFirst.Merge.LearningReview.v1`) so this fingerprint family can never collide with a structurally similar one (e.g. a Meaning or SourceMaterial identity), then reduced to an **uppercase** SHA-256 hex digest. Every other stable identity in §4 uses the same canonical encoding with its own domain discriminator (e.g. `KnownFirst.Merge.SourceMaterial.v1`, `KnownFirst.Merge.Meaning.v1`).

These fields are the complete, immutable record of "what this review event was and what it produced" — nothing about them can legitimately differ between two exports of the *same* real-world event.

**Forward correction (KF-BACKUP-004, §22).** The field list above remains the exact, unchanged contract of the *persisted Slice-1* `LearningReviewFingerprintPolicy` / `KnownFirst.Merge.LearningReview.v1` domain, and is retained here as the historical Slice-1 record. It is **no longer the complete Schema-9 populated-target contract**: the Schema-9 meaning-aware fingerprint (§16.2c) now also encodes the stable nullable `TargetAnswerVariant` and `MatchedAnswerVariant` identities that §16.2's correction note required it to incorporate. `LearningSessionId` remains deliberately outside event identity in both domains — see §22 for the exact current field list and the reasoning.

**Exact duplicate rule:** two `LearningReviewEntity` rows (one target, one archive) with an identical fingerprint are the same event — dedupe, insert nothing. Any field difference at the same `(stableCardKey, ReviewedAtUtc)` means they are **not** the same event (however unlikely a same-instant collision is for human-paced review) — both are retained rather than guessing which is canonical, consistent with "distinct non-empty content is preserved."

**Ordering when timestamps are equal, and under clock skew:** the merged, deduplicated event set for one card is sorted for scheduler replay by `(ReviewedAtUtc, fingerprint bytes as a fixed tie-break)`. This produces a **total, deterministic order** from data alone — it does not depend on which archive was imported first, how many times a merge is repeated, or which device's clock is "more correct." That determinism is what makes replay idempotent and commutative (§11), and it holds *regardless* of clock skew, because the ordering key never consults wall-clock "now" or import sequence.

What this design does **not** claim: when two devices' clocks disagree substantially, the resulting replay order is a fixed, reproducible **convention**, not a proof of true real-world chronological order. A "Hard" review timestamped 09:00 on a fast-clocked phone and a "Good" review timestamped 09:05 on a correctly-clocked PC will always replay in that fixed order on every merge, but if the phone's clock was actually 20 minutes fast, the *true* order may have been reversed — no timestamp-based design can recover information the source data does not contain. To surface this honestly rather than hide it: if a device's own review-event timestamps are not monotonically non-decreasing relative to that device's own `LearningCard.CreatedAtUtc`/prior events for the same card, or if two devices' overall exported timestamp ranges overlap in a way inconsistent with either device's own internal ordering, the matcher emits an informational preflight warning (§9) — never a blocking error, since merge must remain deterministic and complete regardless of clock quality.

**Commutativity summary (answers "which results are commutative and which need a tie-break"):**

- **Commutative, no tie-break needed:** the *set* of surviving events after dedup (§6's exact-duplicate rule is symmetric and order-independent); all §5.1/§5.2 monotonic-tier resolutions (max of two tiers is commutative and associative); all min/max timestamp rules; the union-based Meaning-preservation rule in §5.4.
- **Requires the deterministic tie-break:** the *replay order* fed to `SimpleSpacedRepetitionScheduler` for two distinct events sharing a `ReviewedAtUtc` on the same card — without the fingerprint tie-break, two textually-different events at an identical timestamp would have no defined relative order, which would make the replayed `EaseFactor`/`IntervalDays` outcome depend on implementation-incidental enumeration order. The fingerprint tie-break removes that ambiguity.

Workflow-level events (`ReviewCandidateEntity`, `PreparationCandidateEntity`, `LearningSessionCardEntity`) do not need their own fingerprint scheme beyond what §4.4 already defines, because their identity already resolves duplication at the parent-session level (fingerprint match ⇒ whole subtree is a duplicate) or the `(DocumentId, WordId)` level for `ReviewCandidate` — there is no additional timestamp-ordering question for these, since none of them feed a scheduler replay.

## 7. Archive-version compatibility (unchanged conclusion)

No `formatVersion` bump is required. Every stable key and every piece of history this revision needs — natural-key fields, `ExplanationLanguage`/`TargetLanguage`, provider source identity, full `LearningReview` rows with their scheduler-outcome fields — already exists in the v1 payload. Merge only changes how existing exported fields are *interpreted*; it exports nothing new. Historically, the active-workflow exclusion applied identically to this merge design. KF-BACKUP-005B does not rewrite archive-format-v1 semantics: source schema ≤9 remains Completed-only, while §24 records the later Schema-10/archive-V2 empty-target Active capability.

## 8. Safety-copy design — added per defect #4

**Rule: safety-copy failure means Merge does not begin.** No merge transaction is ever opened until a validated safety copy exists.

**Precondition — no active workflow.** Merge additionally requires that the target has **no** `Active`-status `ReviewSession`/`PreparationSession`/`LearningSession` before it will even attempt a safety copy, using the same existence-check pattern `TextReviewService.cs:552` already uses to guard ordinary imports (`ActiveReviewExistsException`). This closes a real gap found while designing this section: `CreateBackupAsync`/`BackupSnapshotRepository.CaptureSnapshot` (the *full* capture, including active workflows) produces an archive that the existing reader would then **reject on any future restore attempt**, because `BackupArchiveReader` enforces the same `active-workflow-unsupported` rule as ordinary Restore (verified at `BackupArchiveReader.cs:290-294`) — a "safety copy" that cannot be read back is not a safety copy. Requiring no active workflow before Merge starts means the safety copy can safely use the same `CapturePortableSnapshot`/`CreatePortableArchiveAsync` path already used for user-facing Export, which **is** guaranteed restorable, and there is never an active workflow at capture time to lose in the first place.

**Mechanism — reuses existing, already-proven code, not new infrastructure:**

1. Call the existing `BackupService.CreatePortableArchiveAsync`-equivalent capture (`BackupSnapshotRepository.CapturePortableSnapshot` → `BackupModelMapper.MapToExternal` → `BackupArchiveWriter.WriteArchiveAsync`) against the target's *current, pre-merge* state.
2. Write to a private, app-internal staging file using the exact write-then-reopen-and-validate pattern `BackupService.WriteValidatedPortableArchiveAsync` already implements (temp file → flush → size check → `BackupArchiveReader.ValidateAsync` on the written bytes → only then treat it as real).
3. On successful validation, move the file into the permanent safety-copy location (step below). On any failure at any point in 1–3, abort before any live-database transaction is ever opened.

**Storage location and lifecycle:** a private, non-exported, app-internal directory sibling to the database, e.g. `Path.Combine(FileSystem.AppDataDirectory, "merge-safety-copies")` — mirroring `KnownFirstDatabase.DatabasePath`'s existing use of `FileSystem.AppDataDirectory`. This directory is never offered to the native Save/Share picker and is not the same location a user-initiated Export writes to, so it cannot be confused with a user-managed backup.

**File naming and metadata:** `merge-safety-{yyyyMMddTHHmmssfffZ}-{shortGuid}.kfarchive` — the existing `.kfarchive` container format (§7: no format change), so the existing reader/writer/validator all apply unmodified. A small sidecar metadata record (created-at UTC, triggering merge attempt's archive source description, target record counts at capture time) is written alongside for the recovery UI in §"user-visible recovery information" below, without altering the archive format itself.

**Validation before merge:** step 2 above (write → validate with the real `BackupArchiveReader.ValidateAsync`, the identical validator Restore uses) — the safety copy is proven readable *before* it is trusted, not assumed valid because it was just written.

**Retention policy:** the safety copy for the most recent merge attempt (successful or failed) is retained until superseded. On starting a *new* merge, any previous safety copy is deleted only *after* the new one is written and validated — never before — guaranteeing at least one valid safety copy exists throughout any merge attempt. A safety copy is **not** auto-deleted after a successful merge; per this project's general preference for explicit user control over silent cleanup, it remains visible and removable from a "Recovery" section in Settings until the user clears it or a later merge supersedes it.

**User-visible recovery information:** the Recovery section shows the safety copy's creation timestamp, file size, and a "Restore this recovery copy" action. Recovering from a completed-but-unwanted merge requires two already-shipped features, composed rather than newly built: the existing **Reset Data** action in `Settings.razor` (`Settings_ResetData`/`ResetDataAsync`, already a real, confirmed-by-dialog feature) to return the target to empty, followed by the existing **Restore from archive** flow against the safety copy — both fully supported by code that already exists today. This revision does not propose an in-place "undo merge" — it proposes ensuring the two existing primitives needed to recover are always available and pointed at correctly.

**Behavior when the safety copy cannot be created:** any exception during capture, write, or validation aborts before the merge transaction ever opens; the target database is untouched (nothing was ever opened against it). Reported via the existing reserved-but-previously-unused error code `safety-backup-failed` (declared in the codebase per backup-format-v1.md but never thrown until now) and a new `PortableMergeStatus.SafetyCopyFailed`.

**Relationship to the SQLite transaction rollback — complementary, not redundant:** the transaction (§"Cancellation and rollback model" below) guarantees atomicity *during* the merge write itself — if anything fails mid-merge, the transaction rolls back and the database is exactly as it was. The safety copy guards against everything the transaction cannot cover: a successfully *committed* merge the user later decides they don't want, an app crash or power loss at a moment outside the transaction's scope, or filesystem-level damage unrelated to this specific write. Neither mechanism substitutes for the other.

**Slice 2 implementation note (supersedes this section's storage-location sketch; everything else above is unchanged in substance):** `MergeSafetyCopyService` (`Services/DataSafety/Merge/MergeSafetyCopyService.cs`) implements the safety copy exactly as above with four refinements verified during implementation:

- **Race-free active-workflow check.** `BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy` performs the active-`ReviewSession`/`PreparationSession`/`LearningSession` check and the portable snapshot capture inside the *same* `IKnownFirstDatabase.ExecuteSnapshotAsync` transaction/connection, closing the time-of-check/time-of-use gap a separate existence-check call followed by a separate capture call would leave open. Slice 2 guarantees only that the safety-copy snapshot itself was captured with no active workflow at that instant; the future merge writer (§12 slice 5) must re-check the same condition again, immediately before opening its mutation transaction.
- **Storage location.** Derived from `IKnownFirstDatabase.DatabasePath`'s own parent directory (`<database-directory>/merge-safety-copies/`), not `FileSystem.AppDataDirectory` directly — `KnownFirstDatabase.DatabasePath` already redirects under an active GUI test profile (`Services/Isolation/GuiTestProfile.cs`), so deriving from it makes safety copies automatically follow that redirection with no separate isolation logic in the safety-copy service itself. An invalid, relative, or parentless `DatabasePath` is rejected (`SafetyCopyFailed`/`safety-backup-failed`) rather than falling back to another directory.
- **Final-path revalidation.** The staged archive is validated once after writing (`BackupArchiveReader.ValidateAsync`), then again from its *final*, post-move path, before `Success` is ever returned — this is stricter than "write → validate once" and specifically covers move/rename-time corruption that a single staged validation cannot observe.
- **Archive-and-metadata pair finalization and retention ordering.** A private `.metadata.json` sidecar (source-generated `System.Text.Json`, per `MergeSafetyCopyMetadataJsonSerializerContext`) is staged, finalized, and re-read-validated after the archive's final validation succeeds; `Success` requires both files finalized and validated. Only after that full pair is valid does retention remove an older recognized safety-copy pair (exact `merge-safety-{timestamp}-{shortId}.kfarchive` / `+.metadata.json` naming only; unrecognized files in the directory are never touched; a cleanup failure for the older pair is non-fatal and may leave multiple valid copies). Any failure or cancellation at any earlier point cleans up only the current attempt's staging/final files and retains every previously valid pair.

## 9. Preflight merge-summary design — revised per defect #9

The read-only matcher (unchanged mechanism from the prior revision — the same function runs once for preview and again, immediately pre-mutation, inside the transaction) now reports exactly seven categories per entity kind, plus safety-copy status at the top level:

```
MergePreflightSummary
├─ SafetyCopy: { Status, Path, CreatedAtUtc, SizeBytes }   -- always populated first; see §8
├─ PerEntity: IReadOnlyDictionary<EntityKind, EntityPlanCounts>
│    EntityPlanCounts(
│      NewCount,                    -- archive-only, will be inserted
│      ExactDuplicateSkippedCount,  -- byte-identical to an existing row, no-op
│      EnrichedCount,               -- existing record gains new preserved content (§5.4's additional Meaning rows)
│      PreservedVariantCount,       -- content preserved but not schedulable per §4.3 (no card slot available)
│      UnresolvedConflictCount,     -- §5.1 same-tier KnowledgeState clashes, kept-target-with-warning
│      DeduplicatedEventCount)      -- events/sessions recognized as the same via §4.4/§6 fingerprints, collapsed
├─ SampleDetails: bounded (first 20) human-readable descriptions per non-trivial category
├─ WarningCodes: IReadOnlyList<string>, including the §6 clock-skew informational warning
├─ ChecksumVerified, FormatVersion, SourcePlatform, CreatedAtUtc  -- reused from BackupManifest unchanged
```

This is a strict superset of the prior revision's three-count summary, separating what used to be a single "conflict-resolved" bucket into the four distinct, individually-actionable categories the task requires (enriched, preserved-variant, unresolved-conflict, deduplicated-event), plus the safety-copy status the prior revision did not surface at all.

**Required decision: preferred-meaning conflict.** When a matched `LearningCard` (same stable `(VocabularyIdentity, Direction)` key on both target and archive) references two distinct stable Meaning identities, both Meaning rows are still preserved (per §4.3/§5.4 — content is never dropped), but the plan additionally records a required `PreferredMeaningConflict` decision: target and archive Meaning summaries, and the choices `KeepTargetMeaning`, `UseArchiveMeaning`, `KeepBothSelectTarget`, `KeepBothSelectArchive`. The target card is never automatically repointed. Schema 7's `(WordId, Direction)` uniqueness means a matched physical card slot always requires this decision once the referenced Meanings diverge, even when the divergence is only `ExplanationLanguage`/`TargetLanguage` (still distinct content, per §4.1's reasoning for Document identity). **Any unresolved required decision — a preferred-meaning conflict or a same-tier `KnowledgeState` conflict (§5.1) — means the plan is not executable by the future writer**, regardless of how many other entities in the same plan are otherwise clean inserts or exact duplicates.

**Slice 3 implementation note (read-only preflight; supersedes nothing above, makes the two-stage flow explicit):** `MergePreflightPlanner` (`Services/DataSafety/Merge/MergePreflightPlanner.cs`) is the pure function described above — no SQLite, filesystem, network, environment, current-time, random, Preferences, or MAUI dependency, and no write transaction. `MergePreflightService` (`Services/DataSafety/Merge/MergePreflightService.cs`) is the read-only orchestration: it validates the archive stream via the existing `BackupArchiveReader`, captures the target with the same race-free `BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy` call Slice 2 already uses (the identical fail-closed active-workflow classification, unchanged), maps the snapshot through the existing `BackupModelMapper`, and calls the planner. **The two-stage flow this implements:** (1) **Preview** — the user-visible "Import data" confirmation screen calls this read-only preflight; it creates no safety copy and mutates nothing, so it may be called freely, repeatedly, and cancelled without consequence. (2) **Commit** — only after the user confirms, a future slice creates the validated safety copy (§8), reruns the *same* matcher immediately before mutation (to close any time-of-preview/time-of-commit gap), and only then invokes the merge writer (§12 slice 5) — and only if that immediate rerun still reports no unresolved required decision. Slice 3 implements stage (1) only; stage (2)'s rerun-then-write sequence remains future work. `MergePreflightPlan` (`Services/DataSafety/Merge/MergePreflightModels.cs`) is the concrete `MergePreflightSummary` type described above, adapted to Slice 3's exact scope: it omits the top-level `SafetyCopy` status (Slice 3 creates none) and exposes the required-decision structures explicitly (`KnowledgeStateConflictDecision`, `PreferredMeaningConflictDecision`) alongside the six per-entity counts.

## 10. Worked scenario: shared baseline, PC/phone divergence (updated)

Same shape as the prior revision's walkthrough, re-verified against the corrected identity rules:

1. **Shared baseline.** Both devices start from the same state: word "quarantine" (en), `Status = Prepared`, one `LearningCard` (TermToMeaning, 3 reviews so far), one confirmed `MeaningEntity` (Definition mode, `ExplanationLanguage = de`).
2. **New documents on both devices**, both under `LookupMode = Definition`, `ExplanationLanguage = de` — identical configuration to the baseline, so §4.1's 4-part key applies uniformly; no Document conflicts arise since the texts differ.
3. **Learning activity on both devices** on the "quarantine" card: PC logs two reviews (`Good`, `Good`); phone logs one (`Hard`) at an overlapping time. Per §6, all three get distinct fingerprints (differing `Rating`/`WasCorrect`/outcome fields even if a timestamp happened to collide) — no event is lost to a naive timestamp-only dedup.
4. **Preparation activity on both devices**, but this time divergent: PC additionally prepares "quarantine" for **Translation** mode into Russian (`ExplanationLanguage = ru`, `TargetLanguage = ru`) while the phone leaves it as-is. Under §4.2, the PC's new Meaning differs from the existing baseline Meaning on `ExplanationLanguage`/`Translation` content — it is a **distinct** Meaning, inserted as an additional row per §5.4, not merged into or replacing the original German-definition Meaning.
5. **Merge PC-archive into phone.** New documents insert cleanly (§4.1). The "quarantine" `LearningCard` at `(WordId, TermToMeaning)` is matched (§4.3): its 3 target + 2 archive review events (§6) merge to a 5-event history, replayed through the scheduler (§5.3) for the final card state, still pointing at the original German-definition Meaning (§4.3's rule — the existing card reference is never repointed). The new Russian-translation Meaning is inserted as a **preserved-but-uncarded variant** (§4.3/§9) because the `(WordId, TermToMeaning)` slot is already taken by the German-definition card — surfaced in the preflight summary, not silently dropped.
6. **Re-import the same PC archive again.** Documents match by the 4-part key (exact duplicates, skip); the Russian Meaning matches its own identity exactly (§4.2, exact duplicate, skip); all PC-side review events match their fingerprints (§6, exact duplicates, skip) — full-match plan, zero mutations, proven in §11.

## 11. Idempotency proof sketches — added per defect #8, replacing the prior assertion-only claim

For each scenario, the proof rests on the same two facts, stated once and then applied: **(a)** every entity's identity in §4/§6 is a pure function of *content already present in the archive*, never of import order or wall-clock "now"; **(b)** every field this design writes is either a plain insert of a genuinely new row, or a value produced by R1's *recomputation from the merged set* — never an in-place accumulation of "old value + new delta."

**Merge(A) applied twice (target starts empty, archive A merged in, then A merged in again).**
First merge: no target rows exist, so every entity in A is classified `New` by (a) and inserted. Second merge: for every entity, the matcher now finds a target row whose identity — by (a), a pure function of content — is byte-identical to A's corresponding entity's own identity, because it's literally the same content that was just inserted. Every entity classifies as `ExactDuplicate`/dedup. Result: second call performs zero inserts and zero updates. ∎

**Merge A into B, then merge A again.**
After the first merge, B's state, restricted to A's entities, is exactly what a fresh insert of A would have produced (first case). The second `Merge(A → B)` therefore hits the same all-duplicates classification as above for every one of A's original entities. For entities that existed only in B before either merge, (a) guarantees the matcher never re-examines or alters them (they have no counterpart in A). Result: identical to the first proof, restricted to A's entity set. ∎

**Merging two exports derived from the same older baseline (B1, B2, both exported from a common ancestor state, then diverged).**
Every entity present in the common ancestor has identical identity-defining content in both B1 and B2 (by (a), same content ⇒ same identity), so `Merge(B1 → B2)` classifies all ancestor-shared entities as exact duplicates regardless of which side "already has" them. Entities that diverged after the common point (new documents, new reviews, new Meanings) have distinct identity-defining content on at least one side and are classified `New`/`Enriched` per §5. No entity can be double-counted, because the matcher's classification is computed once per archive entity against the *current* target state at merge time, and R1 guarantees aggregate/scheduler fields are recomputed from the resulting set rather than summed twice. ∎

**Duplicate archive events (the same `LearningReviewEntity` appears twice within one archive, or reappears across two archives describing the same real event).**
By (a) applied to §6's fingerprint definition: two rows with identical `(stableCardKey, ReviewedAtUtc, Rating, WasTypedAnswer, WasCorrect, DueAtUtc, IntervalDays, EaseFactor)` produce identical SHA-256 fingerprints. The merged *set* (§6 is defined as a set union, not a list concatenation) collapses them to one member regardless of how many times the same fingerprint appears across one or many archives or merge calls. Scheduler replay (§5.3) operates on this deduplicated set, so a repeated event can never be counted twice in the replayed outcome. ∎

**Duplicate documents with different titles.**
By §4.1, `Title` is excluded from the identity key. Two `BackupSourceMaterial` entries with identical `(ContentFingerprint, TextLanguage, LookupMode, CanonicalTargetLanguage)` and different `Title` values produce identical identity keys by construction, so the matcher classifies the second as an exact duplicate regardless of the title difference — proven directly from the identity definition, not a separate case to reason about. ∎

**Same text with different lookup modes.**
By §4.1, `LookupMode` (and, for non-Definition modes, `TargetLanguage`) is *part of* the identity key. Two `BackupSourceMaterial` entries with identical content but different `LookupMode` therefore have *different* identity keys by construction, so the matcher classifies the second as `New`, not a duplicate — both are preserved as distinct Documents, each with its own occurrence/review-session subtree, exactly as §4.1's rationale requires. This is the intended non-collapse, proven directly from the identity definition rather than asserted as a design goal. ∎

**Commutativity, restated from §6:** every proof above relies only on (a) — content-derived identity — and never on which archive was processed first or how many times a merge ran previously. The same reasoning therefore establishes `Merge(Merge(Base, A), B)` and `Merge(Merge(Base, B), A)` converge to the same resulting state: both reduce, entity by entity, to "the deduplicated union of Base ∪ A ∪ B's content-identity-distinct entities, with scheduler fields recomputed from the corresponding deduplicated event union" — a set union and a replay over a fully-deduplicated ordered set are both independent of the order in which the two source archives were presented.

## 12. Revised implementation slices — reordered per defect #10

Slice 1 is now strictly mutation-free, containing only contract tests, preflight computation, and safety-copy creation/validation — no code in slice 1 ever opens a write transaction against the live database.

1. **Stable-identity, fingerprint, and conflict-policy library, fully unit-tested** — pure functions over `BackupModels.cs` DTOs (§4.1–§4.4, §5.1–§5.4, §6) with **no database access of any kind**. Contract tests assert every §5 matrix cell and every §11 proof scenario against fixture data, and assert the canonical-empty-`TargetLanguage` normalization (§4.1) and the SHA-256 event-fingerprint definition (§6) byte-for-byte. This slice is complete only once every cell in §5.1/§5.2 and every §11 scenario has a passing test — before any of slice 2–4 is written. **Implemented** (branch `feature/backup-merge-contracts-v1`; see `docs/CURRENT_WORK.md`).
2. **Safety-copy creation and validation**, wired to the *existing* `CreatePortableArchiveAsync` capture and the *existing* `BackupArchiveReader.ValidateAsync`, plus the new active-workflow precondition check (§8). Testable in isolation: given a populated `IKnownFirstDatabase`, produce and validate a safety copy, with no merge logic involved yet. Failure-path tests (disk full, validation failure) assert the `safety-backup-failed` code and confirm zero database mutation. **Implemented** (branch `feature/backup-merge-safety-copy-v1`; see `docs/CURRENT_WORK.md`).
3. **Read-only matcher** — given a validated `BackupPayload`, a **read-only** connection to a non-empty target, and slice 1's pure functions, produce the full `MergePreflightSummary` (§9). Still no mutation; this is also the UI preview engine. Contract tests replay the §10 worked scenario end-to-end against fixtures and assert the exact preflight counts. **Implemented** (branch `feature/backup-merge-preflight-v1`; see `docs/CURRENT_WORK.md` — `MergePreflightPlanner`/`MergePreflightService`; the "read-only connection" above is realized as one `ExecuteSnapshotAsync` capture, not a literal read-only ADO connection, since `IKnownFirstDatabase` has no separate read-only connection mode). **Revised** for the approved meaning-centric model — see §16: identity computation moved from row-level `MeaningIdentity` to `SemanticMeaningIdentity`/`ExactMeaningVariantIdentity`/`AnswerVariantIdentity`, five child entity kinds (SentenceRange, Occurrence, EncounteredForm, LegacyReviewSummary, ContextSnapshot) gained real stable-parent-based identities in place of parent-lockstep classification, and a new `AnswerVariant` entity kind was added. **Extended by KF-MEANING-001 Slice 7** (`MergePreflightPlannerV2.cs`): the matcher now plans active Schema-8 targets against archive-v2 (Schema-8) sources natively, and against archive-v1 sources through the existing in-memory upgrade path; multiple Senses of the same Word are planned independently, and Sense-addressed meanings, answer variants, `SenseAnswerVariantAssignment`s, `AnswerVariantProgress`, cards, reviews, queue items, and vocabulary/preparation workflows are all covered — still strictly read-only, with no safety copy and no writer invocation. See `docs/CURRENT_WORK.md` (checkpoint commit `bea01a75ae6da2e6f7a7ea269dae0e1c7cbe3675`).
4. **Scheduler-replay extraction** — confirm or extract a pure function in `KnownFirst.Core.Learning` (alongside the existing `SimpleSpacedRepetitionScheduler`) that takes an ordered review-event sequence and an initial card state and returns final scheduling fields, for reuse by §5.3. Pure, unit-testable, no database access.
5. **Merge writer** — the first slice permitted to mutate. Extends `BackupImportRepository`'s dependency-ordered insertion pattern with insert/enrich/skip branching driven by slices 1–4's now-fully-tested logic, wrapped in `RunInTransactionAsync`, gated on slice 2's safety copy having already succeeded, ending with the invariant/aggregate-recompute pass.
6. **Service/API surface** — `BackupService.MergePortableArchiveAsync`, `PortableMergeStatus`, and the new error codes, mirroring `ImportPortableArchiveAsync`'s shape.
7. **UX** — "Merge from archive" entry point, preflight summary screen (§9's seven categories plus safety-copy status), Recovery section (§8), localized EN/DE/RU strings, explicit confirmation, result summary. Reuses the KF-UX-001 hide-normal-actions and KF-STATE-001 notify-only-on-real-success patterns.
8. **Full test suite** — the §11 proofs as executable idempotency/commutativity tests against real (not just fixture) `IKnownFirstDatabase` instances, rollback/failure-injection tests reusing `IBackupImportFailureInjector`, and the divergence scenario from §10 run end-to-end.
9. **Documentation finalization** — only after implementation and validation: update `backup-format-v1.md`'s "Future work" section, `PROJECT_STATE.md`, `DATABASE_CONTRACT.md`, `ROADMAP.md` milestone 7, and file the ADR this design earns. Not part of this design task.

## 13. Remaining unresolved decisions

Several items from the prior revision are now resolved by verified code (Meaning cardinality, LearningCard/Meaning relationship, safety-copy format choice, Order-renumbering safety, schema-extension question). What remains open:

1. **Tier-1 KnowledgeState conflict UX.** §5.1 defines the technical resolution (keep target, surface archive's alternative) as the safe default, but whether a future release should instead let the user pick between `Known`/`UnknownBacklog`/`Ignored` interactively during the merge confirmation is a product decision, not a technical one.
2. **Preflight sample-detail bound.** §9's `SampleDetails` is bounded (proposed: first 20 per category) for UI practicality; the exact bound is a minor product/UX parameter, not a correctness question.
3. **Content-fingerprint collision risk for PreparationSession/LearningSession** (§4.4) remains, as in the prior revision, a very-low-probability but non-zero risk worth a dedicated review before implementation, unchanged by this revision.
4. **Clock-skew warning threshold.** §6 defines *that* an informational warning fires when a device's own event ordering looks internally inconsistent, but the exact detection heuristic (how much inversion is "suspicious" versus ordinary out-of-order network delivery) needs a concrete algorithm at implementation time, not asserted here.

## 14. Test matrix (carried forward, extended)

All rows from the prior revision's matrix still apply; the following are added or sharpened by this revision:

| Category | Scenario | Expected outcome |
| --- | --- | --- |
| Safety copy | Merge attempted while an active workflow exists on the target | Rejected before any safety-copy attempt, same `ActiveReviewExistsException`-style guard as ordinary import; zero mutation |
| Safety copy | Safety-copy write fails (simulated IO failure) | `PortableMergeStatus.SafetyCopyFailed`, `safety-backup-failed`; merge transaction never opens; target database byte-identical to before the attempt |
| Safety copy | Safety-copy write succeeds but validation fails | Same as above — validation failure is treated identically to a write failure |
| Meaning plurality | Two devices confirm different Meanings (different `ExplanationLanguage`) for the same word | Both Meaning rows survive merge; exactly one is referenced by the existing `LearningCard`; the other appears in the preflight summary as a preserved variant |
| Clock skew | Two review events for the same card share an identical `ReviewedAtUtc` but differ in `Rating` | Both retained (not deduplicated); scheduler replay order is the §6 fingerprint tie-break, reproducible across repeated merges |
| Clock skew | A device's own review timestamps are internally out of order relative to its card history | Preflight emits an informational warning; merge still completes deterministically |
| Enum matrix | `Known` on target, `UnknownBacklog` on archive, same word | Target's `Known` is kept unchanged; archive's `UnknownBacklog` is surfaced as an unresolved conflict in the preflight summary, not silently applied |
| Enum matrix | `Prepared` on target, `PreparationFailed` on archive, same word | `Prepared` is kept/adopted deterministically; no unresolved-conflict flag (this pair has a defined monotonic answer, unlike the KnowledgeState tier-1 case) |
| Order renumbering | A merged `PreparationSession` combines items from both sides | Item ordering (relative sequence) matches the archive's original ordering for its own items and the target's for its own; absolute `Order` values are renumbered contiguously; no test asserts a specific absolute value |
| Idempotency | Each of the six §11 scenarios | Executable as a dedicated test per scenario, asserting zero mutation (or the specific proven convergent state) on the second/comparative merge call |

## 15. UX proposal, localized warnings, and out-of-scope note (carried forward)

Unchanged in substance from the prior revision — two separate entry points ("Restore from archive" unchanged; new "Merge from archive"), a preflight confirmation screen, and explicit affirmative confirmation — except the preflight screen now surfaces the seven categories from §9 (including safety-copy path/status) instead of the prior three-bucket summary, and the confirmation text's backup recommendation (previously conditional on an unresolved decision) is now unconditional: because §8 makes the safety copy mandatory and automatic, the confirmation text states that a recovery copy *has already been created* (with its path) rather than recommending the user create one manually. This document continues to implement nothing: no `Services/DataSafety/BackupService.cs` change, no entity/schema change, no `.kfarchive` format change, no ADR filed (slice 9, §12).

## 16. Decided meaning-centric model — Word → SemanticMeaning → AnswerVariant (KF-MEANING-001)

**This product model is now a binding architecture decision (KF-MEANING-001 Slice 0), not provisional.** This section records the model KF-MEANING-001 will implement and the identity/merge-rule revisions KF-BACKUP-002 Slice 3 already made to support it in its read-only preflight, without implementing any of KF-MEANING-001 itself (no schema migration, no scheduler, no writer — see §12 slice list; KF-MEANING-001 is its own backlog item, gated to land before slice 5's merge writer). The full entity model (`SenseEntity`, `AnswerVariantEntity`, `AnswerVariantProgressEntity`), the Schema 7→8 migration contract, and the archive v1↔v2 compatibility contract this section's identities anticipate are recorded in [meaning-centric-learning-v1-design.md](meaning-centric-learning-v1-design.md) — that document, not this section, is the binding reference for anything beyond the stable-identity/merge-planner scope below.

**Correction-pass note (flags a required future revision to this section's already-shipped types, not made here; updated after a second focused review):** [meaning-centric-learning-v1-design.md](meaning-centric-learning-v1-design.md) corrects the live-schema answer-variant model twice. First: `PrimaryAnswer`/`RequiredAnswerVariant`/`AcceptedAlias` are no longer one mutually-exclusive role. Second (root-cause correction): even "preferred display" and "mastery requirement" as two orthogonal *Sense-wide* facts was still wrong, because both are actually **direction-specific** — the same `AnswerVariant` can be `Required` for `MeaningToTerm`, merely `AcceptedOnly` (or entirely unassigned) for `TermToMeaning`. `AnswerVariantEntity` (§2.5) is now a pure normalized answer expression with no behavior of its own; a new `SenseAnswerVariantAssignmentEntity` (§2.6), unique by `(SenseId, CardDirection, AnswerVariantId)`, owns `Requirement` (`Required`/`AcceptedOnly`) and `IsPreferred` per direction, with the singleton-preferred rule enforced by a raw SQLite partial unique index. The already-merged `AnswerVariantRole` enum and `AnswerVariantRolePrecedencePolicy.Reconcile` below (§16.2) still reflect the original three-way model and will need a forward revision in the MergePreflight adaptation slice (that document's §6 slice 6) to the `SenseAnswerVariantAssignmentEntity` model — tracked there, not performed in this pass. That document's §2.9 also adds `LearningReviewEntity.TargetAnswerVariantId`/`MatchedAnswerVariantId` and `LearningSessionCardEntity.TargetAnswerVariantId`, scoped by `(CardId, AnswerVariantId)`, which the meaning-aware review-event fingerprint in §16.2c below must eventually incorporate. Separately, that document's §2.2/§2.7 also corrects `SenseEntity.PreferredMeaningId` (was treated as the sole authority) into a non-authoritative `SenseEntity.DefaultMeaningId` fallback plus an authoritative, per-direction `LearningCardEntity.PreferredMeaningId` — the `PreferredVariantSelectionDecision` this design's §16.5/§18 already scopes per `FutureCardIdentity` (which already includes `Direction`) should read from the card's own field once implemented, not a Sense-wide one; the decision's own per-card scoping does not need to change, only which schema field it reads.

### 16.1 Product model

- **Word**: lexical identity (e.g. "bank"). Unchanged from §4.1's `VocabularyIdentity`.
- **SemanticMeaning**: one learnable sense of a Word (e.g. "bank" the financial institution vs. "bank" the river edge). Distinct SemanticMeanings require separate future LearningCards and independent learning progress.
- **AnswerVariant**: one valid expression of a SemanticMeaning (e.g. "bank", "financial institution"). Variants of the same SemanticMeaning share one future card and one SRS schedule, but may require separate mastery evidence (§16.3).
- **ContextSnapshot**: a user-selected text context belonging to a SemanticMeaning (unchanged structurally from §4.2/§9, now explicitly scoped to the semantic-meaning level rather than the row level — see §16.6).
- **Topic/domain**: a disambiguation label belonging to a SemanticMeaning. **Not persisted today** — see §16.6.

Notes, aliases, examples, attribution, and manual-edit/provenance metadata alone never define a new SemanticMeaning — they distinguish an `ExactMeaningVariantIdentity` (§16.2), never a `SemanticMeaningIdentity`.

### 16.2 Stable identities (`Services/DataSafety/Merge/SemanticMeaningIdentities.cs`)

These are additive to Slice 1's `MeaningIdentity`/`MeaningIdentityPolicy` and `LearningCardMatchIdentity`/`LearningCardIdentityPolicy`, which remain unchanged (their own contract tests still pass unweakened). Slice 3's planner uses the new types below going forward; the old `MeaningIdentity` continues to exist only as the historical Slice 1 contract artifact and is no longer used by the planner.

- **`SemanticMeaningIdentity`** (revision 3, `.v3` domain) — stable Word identity, canonical source/explanation language, provider sense id (`BackupPreparedItem.ProviderMeaningId`) when present, a topic/domain component (always empty today, forward-compatible parameter — §16.6), and `GrammaticalRelationship` (the closest available part-of-speech/grammatical discriminator) and `AcronymExpansion`. **Correction (focused review, `.v2`):** revision 1 also hashed `Translation`, which incorrectly turned synonyms or alternative translations of the exact same sense into separate semantic meanings. Translation/answer text is exclusively an `AnswerVariantIdentity` concern and is no longer part of this identity at all. **Correction (final focused review, `.v3`):** revision 2 still hashed `Definition` unconditionally — the identical defect, one field over: a same-provider-sense-id pair with merely differently-worded definitions was misclassified as two distinct semantic meanings, and a no-discriminator pair with differing Definition wording was silently auto-split instead of raising a grouping decision. `Definition` is no longer part of this identity at all; it now only ever distinguishes an `ExactMeaningVariantIdentity` or informs the grouping-ambiguity check below. Excludes notes, examples, aliases, attribution, and confirmation state. `SemanticMeaningIdentityPolicy.HasReliableSenseDiscriminator` reports whether a row carries at least one of the strong fields — provider sense id, topic, grammar, or acronym — **deliberately excluding Definition**, which is never itself trustworthy evidence of sameness or difference (see the grouping-ambiguity rule below).
- **`ExactMeaningVariantIdentity`** (revision 3, `.v3` domain) — every field the schema actually persists for a Meaning row: `SemanticMeaningIdentity` plus `DisplayTerm`, **`Definition`**, **`Translation`**, `EncounteredSurfaceForm`, `DictionaryExample`, `AdditionalNote`, `ConfirmedByUser`, the full `BackupSourceReference` (provider name/project/page/revision/attribution — this is where manual-versus-provider origin is protected, per `ProviderName`), and the ordinal-sorted distinct alias set (order-independent). **Correction (focused review, `.v2`):** `Translation` was missing from revision 1's hash entirely (an oversight discovered once `Translation` was removed from `SemanticMeaningIdentity` above) — two rows differing only in Translation would have wrongly collapsed to one exact duplicate. `Translation` is now written directly into this hash. **Correction (final focused review, `.v3`):** the identical gap existed for `Definition` once it was removed from `SemanticMeaningIdentity` above — previously Definition differences were only visible transitively via the embedded (now-Definition-free) `SemanticMeaningIdentity` component. `Definition` is now written directly into this hash too.
- **`AnswerVariantIdentity`** — `SemanticMeaningIdentity` plus normalized answer text plus answer language. Deliberately excludes the variant's role (`PrimaryAnswer`/`RequiredAnswerVariant`/`AcceptedAlias`) from the identity itself — role is presentational/behavioral metadata, not identity, so the same accepted text is the same variant regardless of which row or role first introduced it. Equivalent normalized text deduplicates; distinct text is preserved. `AnswerVariantRolePrecedencePolicy.Reconcile` ranks `RequiredAnswerVariant` above `AcceptedAlias` for the same identity (either device explicitly requiring mastery must be preserved); `PrimaryAnswer` is excluded from this ranking — it is a separate, singleton per-SemanticMeaning preference resolved by `PreferredVariantSelectionDecision` instead. This ranking is forward-compatible only: current archives can never actually produce a `RequiredAnswerVariant` value (§16.3), so the ranking is unreachable from live planner data today, and the planner never invents one merely because optional aliases exist.
- **`FutureCardIdentity`** — `SemanticMeaningIdentity` plus `CardDirection`. **Correction (focused review):** Slice 3's planner now actually matches `LearningCardEntity` rows by `FutureCardIdentity`, not merely `(VocabularyIdentity, Direction)` — this is the real semantic matching key. The physical `(VocabularyIdentity, Direction)` identity (`LearningCardIdentityPolicy`, unchanged) is retained solely to detect when two distinct `FutureCardIdentity` values collide on the one physical slot the live schema currently allows (§16.4).

### 16.3 Future mastery contract (KF-MEANING-001, not implemented by Slice 3)

- **Answer-variant classes**: `PrimaryAnswer` (preferred display), `RequiredAnswerVariant` (user-selected, individually required for mastery), `AcceptedAlias` (correct but not individually required). A provider must never make every returned synonym mandatory automatically; the user chooses which variants are required (future UI, not built here).
- **Mastery per required variant**: Automatic mode needs the configured number of successful reading assessments, then the configured number of successful typing assessments, per required variant; Reading mode needs the configured number of reading successes; Typing mode needs the configured number of typing successes. The current default/constant is two; making it configurable is out of Slice 3's scope.
- **"Consecutive"** means consecutive assessments *of that variant*, not immediate consecutive queue appearances — the queue should avoid presenting the same (Word, SemanticMeaning, AnswerVariant) immediately twice while another due item exists, and may repeat immediately only when no alternative due item remains.
- Typing a different accepted synonym is semantically correct but does not satisfy the specifically targeted required variant — that variant must be scheduled again later.
- A failure resets only the targeted variant's own relevant streak, never all variants or the whole SemanticMeaning.
- A SemanticMeaning reaches mastery only once every required variant independently satisfies the active learning-mode requirement.

### 16.4 Current-schema compatibility (verified against code, not migrated here)

- `LearningCardEntity` has a real, DB-enforced unique index `IX_LearningCards_Word_Direction` on `(WordId, Direction)` — **not** `(MeaningId, Direction)` and not `(SemanticMeaningIdentity-equivalent, Direction)`. Only one physical card can exist per Word per Direction, regardless of how many SemanticMeanings the word has.
- Automatic-learning progression (`ConsecutiveRecallSuccessCount`, `ConsecutiveTypingSuccessCount`, `ConsecutiveTypingFailureCount`, `MasteryReviewExtensionScheduled`) is stored on `WordEntity` — Word-level, not per-Meaning, not per-AnswerVariant. `LearningService.cs` reads/writes these fields keyed only by `wordId` (`LearningService.cs:224,251,258,271,289-292,666-687`), and card retirement queries `LearningCardEntity.Where(card => card.WordId == wordId)` (`LearningService.cs:698,703,708`) — word-level, not meaning-level.
- **Exact archive-format v1 compatibility finding (focused review):** `BackupArchiveWriter.ValidatePayloadGraph` (lines ~124, 209-214) builds a `HashSet<(string VocabularyId, BackupCardDirection Direction)>` (`cardKeys`) across every `BackupLearningCard` in one payload and throws `BackupErrorCodes.InvariantViolation` the moment a second card shares a `(VocabularyId, Direction)` pair — **regardless of `PreparedItemId`**. This method runs on both the write path (`BackupArchiveWriter.WriteArchiveAsync`) and the read/validate path (`BackupArchiveReader.ValidateAsync`). **Conclusion: archive format v1 cannot encode two cards for one Word/Direction even when `PreparedItemId` differs — verified in code, not claimed by inference.** Even once the live schema is migrated to hold two SemanticMeaning cards for one Word/Direction, a v1 export could never round-trip that state.
- **Conclusion: the current schema is incompatible with independent SemanticMeaning progress and per-AnswerVariant mastery.** This is documented, not migrated, in Slice 3. KF-MEANING-001 owns the migration.
- Slice 3's planner exposes this incompatibility as deterministic, stable prerequisite/warning codes (`Services/DataSafety/Merge/MergePreflightModels.cs`, `MergePreflightSchemaGapCodes`), never a silent collapse of distinct content. **Blocking** (recorded in `MergePreflightPlan.BlockingPrerequisites`, force `IsExecutable` false):
  - `meaning-card-schema-migration-required` — two distinct `FutureCardIdentity` values (distinct SemanticMeanings, same Word and Direction) collide on the one physical `LearningCardEntity` slot the live schema allows. Both senses are preserved as planned content — the archive's colliding card classifies `MergeEntityKind.LearningCard` → `New` (a distinct planned future card, **never** a non-blocking `PreservedVariant`) — but neither can be scheduled today.
  - `archive-format-migration-required` — always emitted alongside the above, per the verified finding above.
  - `workflow-history-schema-migration-required` — a matched workflow row's full historical content diverges in a way the live schema's own uniqueness constraints prevent preserving as two rows (verified case: `ReviewSessionEntity.DocumentId` is uniquely indexed).

  **Informational only** (recorded in `MergePreflightPlan.WarningCodes`, never affect `IsExecutable`):
  - `answer-variant-progress-migration-required` — an already-matched SemanticMeaning gains a new distinct AnswerVariant. Verified: the schema has no per-review record of which answer variant a review event exercised, so no concrete case ever requires blocking; never emitted merely because optional aliases exist without any associated progress signal.
  - `topic-persistence-required` — see §16.6.

### 16.5 Merge rules (Slice 3 planner behavior, corrected)

| Rule | Slice 3 classification |
| --- | --- |
| Same SemanticMeaning, exact same variant | `PreparedMeaning` → `ExactDuplicateSkipped` |
| Same SemanticMeaning, new synonym/answer variant | Derived `AnswerVariant` plan → `Enriched` (+ informational `answer-variant-progress-migration-required`) — **not** a physical archive action (§16.2b) |
| Same SemanticMeaning, different note/example/provenance | `PreparedMeaning` → `PreservedVariant` (no second semantic card implied) |
| Same Word/language, no reliable sense discriminator on either side, differing Translation and/or Definition text | `PreparedMeaning` → `UnresolvedConflict`, blocking `SemanticMeaningGroupingDecision` (`TreatAsSameSemanticMeaning` / `TreatAsDistinctSemanticMeanings`) — the planner never guesses, even when the two sides' Definition wording happens to match (matching free text is not itself a reliable discriminator) |
| Different SemanticMeaning (existing Word), matched physical card slot | `PreparedMeaning` → `Enriched`; `LearningCard` → `New` (never `PreservedVariant`) + blocking `meaning-card-schema-migration-required` + `archive-format-migration-required` |
| Different SemanticMeaning, no physical slot collision | `PreparedMeaning` → `Enriched`; `LearningCard` → plain `New`, not blocked |
| Matched card (same `FutureCardIdentity`) whose target/archive sides reference different `ExactMeaningVariantIdentity` values | Blocking `PreferredVariantSelectionDecision` (`SelectTargetVariant`/`SelectArchiveVariant` — exactly two choices; never deletes the other variant; changes only which exact variant the card references/displays as preferred). **Correction (final focused review, §18):** the comparison key is each side's referenced `ExactMeaningVariantIdentity`, never `DisplayTerm` text — `DisplayTerm` is presentation content only and appears in the decision solely as a human-readable summary. |
| Progress for the same AnswerVariant | Always merged from deduplicated events, keyed by the meaning-aware, `FutureCardIdentity`-based fingerprint (§16.2c) — never combined via maximum counters |

**Corrected invariants:** `IsExecutable` is true if and only if `Status` is `Ready` or `NoChanges`. `RequiresUserDecision` is true if and only if `KnowledgeStateConflictDecisions`, `WorkflowStatusConflictDecisions`, `SemanticMeaningGroupingDecisions`, or `PreferredVariantSelectionDecisions` is non-empty — **all four are now blocking** (the original Slice 3 revision incorrectly treated the last two as non-blocking; the focused review corrected this). `BlockedByPrerequisite` is true if and only if no decision is outstanding but `BlockingPrerequisites` is non-empty (current storage cannot represent the planned result at all, so there is nothing to decide, only to migrate). If both a decision and a prerequisite exist, `RequiresUserDecision` wins for user-facing framing, but `IsExecutable` is false either way.

#### 16.2b Derived answer-variant plans are not physical archive actions

**Correction (focused review):** the original Slice 3 revision added `AnswerVariant` as a `MergeEntityKind` and gave it primary actions/counts, but the archive format has no independent answer-variant row — `AnswerVariant` is a decomposition of one `BackupPreparedItem`'s `DisplayTerm`/accepted-alias text, not a physical entity. `AnswerVariant` was removed from `MergeEntityKind` entirely; derived plans are now exposed separately as `MergePreflightPlan.DerivedAnswerVariantPlans` (`DerivedAnswerVariantPlan` records), which never increment `MergeEntityPlanCounts` and are never counted toward "every archive row receives exactly one primary action." A single `BackupPreparedItem` with three accepted aliases is always exactly **one** `PreparedMeaning` primary action, alongside four derived answer-variant plan entries (one `PrimaryAnswer` + three `AcceptedAlias`) — verified by test.

#### 16.2c Meaning-aware review-event fingerprint

**Correction (focused review):** the original Slice 3 revision reused Slice 1's persisted `LearningReviewFingerprintPolicy` (keyed by the physical `LearningCardMatchIdentity`, i.e. `(VocabularyIdentity, Direction)`) directly inside the planner. Because card matching is now semantic (`FutureCardIdentity`), two review events for genuinely different SemanticMeanings — sharing the same Word, Direction, timestamp, rating, and outcome — would have wrongly collapsed into one. The planner now computes its own fingerprint, keyed by `FutureCardIdentity` instead, using the same canonical-encoding infrastructure. Slice 1's own persisted `LearningReviewFingerprintPolicy` is unchanged for its existing callers/tests — no persisted archive field is silently changed.

**Current contract (KF-BACKUP-004, §22).** The eight-field `KnownFirst.Merge.Preflight.MeaningAwareLearningReview.v1` domain described above is the **historical** Schema-9 planner fingerprint and is no longer the current contract. It omitted the emitted `TargetAnswerVariantId`/`MatchedAnswerVariantId` this section's own correction note required it to incorporate, so reviews exercising genuinely different answer variants were wrongly deduplicated. The binding Schema-9 contract is now the ten-field `KnownFirst.Merge.Preflight.MeaningAwareLearningReview.v2` domain in `Schema9LearningReviewMergeIdentity`, shared by `MergePreflightPlannerV2` and `MergeWriterExecutor`'s scheduler replay — see §22 for the exact field list, the null-presence rule, and why `LearningSessionId` is excluded.

### 16.6 Context and topic rules

- `ContextSnapshot` is an explicit child of a SemanticMeaning, not of a Meaning row: its Slice 3 identity is `(SemanticMeaningIdentity, SourceMaterialIdentity, NormalizedFingerprint, TargetStart, TargetLength)`, classified independently of its parent row's own classification (a context can be new even when its row is an exact duplicate, and vice versa). A future preparation UI is expected to let the user choose which imported-text contexts belong to each SemanticMeaning; that UI is not built here.
- **Topic/domain persistence gap (verified against code):** neither `MeaningEntity` nor `BackupPreparedItem` has a topic/domain/sense-disambiguation field. The only place anything resembling it appears is the *transient* lookup draft (`BackupLookupMeaning.PartOfSpeech`/`UsageLabels`, `LexicalResult`), which `PreparationService.AcceptCandidateAsync` (`Services/Study/PreparationService.cs:365-399`) does not copy into the persisted `MeaningEntity` when the user confirms a meaning. Topic/domain data does not survive past the preparation-candidate stage today. Slice 3's planner therefore emits `topic-persistence-required` whenever the plan processes at least one `PreparedMeaning`, and never infers a topic from Definition/Translation text (verified by test: two definitions a reader might associate with different topics still dedupe purely on identical text). `SemanticMeaningIdentityPolicy.Compute` already accepts a `canonicalTopicOrDomain` parameter (default `""`) so it is already correct once a future schema change adds the field — no identity-function change will be needed then, only a new caller-side source of the value.

## 17. Focused correction pass (KF-BACKUP-002 Slice 3)

A follow-up focused review of §16 found and corrected the following defects, all on the same feature branch (`feature/backup-merge-preflight-v1`) as §16 itself — **Slice 3 remains unmerged and uncommitted; this section documents a correction to work still in progress, not a change to shipped behavior**:

1. **Answer text is not sufficient semantic-sense identity.** `SemanticMeaningIdentity` incorrectly hashed `Translation` (§16.2). Removed; `Translation` now only ever distinguishes an `ExactMeaningVariantIdentity` or an `AnswerVariantIdentity`, never a `SemanticMeaningIdentity`.
2. **Ambiguous translation-only grouping requires a user decision.** When target and archive share a Word and language but neither side carries a reliable sense discriminator (§16.2, `HasReliableSenseDiscriminator`), and their content differs, the planner no longer silently treats them as the same sense (a real risk once `Translation` was removed from the identity) or silently splits them — it raises a blocking `SemanticMeaningGroupingDecision` (§16.5).
3. **Semantic cards use `FutureCardIdentity`.** The planner's actual `LearningCardEntity` matching is now `(SemanticMeaningIdentity, Direction)`, not `(VocabularyIdentity, Direction)` (§16.2, §16.4).
4. **Current physical slot collisions block execution until KF-MEANING-001.** A matched physical slot referencing two distinct SemanticMeanings is `MergeEntityKind.LearningCard` → `New` (never `PreservedVariant`) plus the blocking prerequisites `meaning-card-schema-migration-required` and `archive-format-migration-required` (§16.4, §16.5). `MergePreflightPlan.IsExecutable` is false whenever any blocking prerequisite exists, via the new `BlockedByPrerequisite` status.
5. **Preferred answer selection is a blocking user decision.** `PreferredVariantSelectionDecision` was non-blocking in the prior revision; it is now blocking (§16.5) — still exactly two choices, still preserves both variants unconditionally.
6. **Derived answer-variant plans are separate from physical archive actions.** `AnswerVariant` was removed from `MergeEntityKind`; derived plans live in `MergePreflightPlan.DerivedAnswerVariantPlans` and never affect primary per-entity counts (§16.2b).
7. **Exact archive-v1 compatibility finding.** Verified directly in `BackupArchiveWriter.ValidatePayloadGraph`: a single v1 archive cannot contain two `BackupLearningCard` entries sharing one `(VocabularyId, Direction)` pair regardless of `PreparedItemId` (§16.4).
8. **Workflow children now compare complete historical content**, not identity alone (§7's original scope): `VocabularyReviewItem`, `PreparationItem`, and `LearningQueueItem` preserve divergent history as an additional row when the schema tolerates it (no stricter uniqueness beyond `(SessionId, Order)`/`(SessionId, QueueOrder)`); `VocabularyReviewWorkflow` (session) content divergence for the same document is blocking (`workflow-history-schema-migration-required`), because `ReviewSessionEntity.DocumentId` is uniquely indexed and cannot hold two rows.
9. **Meaning-aware review-event fingerprint** (§16.2c): the planner's review-event matching is now keyed by `FutureCardIdentity`, not the physical `LearningCardMatchIdentity` — two reviews for different SemanticMeanings never collapse merely because every other field matches. Slice 1's persisted `LearningReviewFingerprintPolicy` is unchanged.

No SQLite mutation, schema migration, scheduler execution, merge writer, Import routing, UI, provider call, or synchronization transport was added by this correction pass — it is exclusively a revision to the pure planner, its models, and its tests.

## 18. Final focused review correction (KF-BACKUP-002 Slice 3)

A final independent review of §16/§17 found and corrected the following defects, still on the same unmerged `feature/backup-merge-preflight-v1` branch:

1. **Definition text was still an unconditional hard identity field — the identical defect §17 item 1 fixed for `Translation`, one field over.** `SemanticMeaningIdentityPolicy.Compute` hashed normalized `Definition` unconditionally, and `HasReliableSenseDiscriminator` treated Definition's mere presence as a reliable sense discriminator. Two concrete consequences, both now fixed (`SemanticMeaningIdentity`/`ExactMeaningVariantIdentity` bumped to `.v3`, §16.2):
   - A same-provider-sense-id pair whose Definition wording merely differed hashed to two different `SemanticMeaningIdentity` values (Definition dominated the hash even though `ProviderMeaningId` agreed), so the planner classified it `Enriched`/"new semantic sense" — a silent split — instead of one `SemanticMeaning` with the difference preserved as a second `PreservedVariant`.
   - A no-discriminator pair whose Definition wording differed never reached the ambiguity check at all (the `SemanticMeaningIdentity` mismatch alone routed it to the same silent-split branch above), so no `SemanticMeaningGroupingDecision` was ever raised for a Definition-only divergence — only for a Translation-only one.
   - `Definition` is now excluded from `SemanticMeaningIdentity` and from `HasReliableSenseDiscriminator` entirely (symmetric with `Translation`'s §17 fix), and is now hashed directly into `ExactMeaningVariantIdentity` (previously it was protected only transitively, through the since-removed `SemanticMeaningIdentity` component). One further, previously-unstated consequence of applying the same rule consistently: two prepared items whose Definition wording merely happens to match, but which carry no `ProviderMeaningId`/topic/grammar/acronym discriminator on either side, are also now ambiguous if their Translation differs — matching free text was never itself reliable evidence of sameness, so this case now raises a `SemanticMeaningGroupingDecision` too, rather than being silently treated as the same sense.
2. **`PreferredVariantSelectionDecision` compared `DisplayTerm` presentation text instead of the card-referenced `ExactMeaningVariantIdentity`.** `DisplayTerm` is presentation content, not the stable identity of the Meaning variant a card actually references — two matched cards (same `FutureCardIdentity`) can reference different `ExactMeaningVariantIdentity` values (e.g. differing `AdditionalNote`, `Translation`, or provenance) while happening to share the same `DisplayTerm`; the prior `DisplayTerm`-comparison would silently miss that conflict and retain one card's variant reference without ever asking. The planner now, for every matched card, resolves each side's referenced `BackupPreparedItem` and compares the resulting `ExactMeaningVariantIdentity` values directly (§16.5 table). `PreferredVariantSelectionDecision` gained `FutureCardIdentity`/`TargetExactMeaningVariantIdentity`/`ArchiveExactMeaningVariantIdentity` fields; `DecisionId` is now derived from `FutureCardIdentity` plus both exact-variant identities (`KnownFirst.Merge.Decision.PreferredVariant.v2`), never from `DisplayTerm` text or an archive-local id. `TargetPreferredAnswerText`/`ArchivePreferredAnswerText` (`DisplayTerm`) remain on the decision as human-readable summary fields only — never the comparison key. Behavior change: a matched-card pair with identical `DisplayTerm` but a differing exact variant now correctly raises the decision it previously missed; a pair with differing `DisplayTerm` but an identical exact variant (not otherwise reachable from current archive data, since `DisplayTerm` is itself part of `ExactMeaningVariantIdentity`) would no longer be flagged on `DisplayTerm` alone.

No SQLite mutation, schema migration, scheduler execution, merge writer, Import routing, UI, provider call, or synchronization transport was added by this correction pass either — it is exclusively a revision to the pure planner's identity policies, its models' doc comments, and its tests.

## 19. Package B — Schema-9 completed-review writer evidence (merged via PR #65)

**Status:** Package A (§17/§18's planner and identity work, PR #52) already proved that a divergent completed `ReviewSession` for an already-known Document classifies `New` rather than colliding with the target's existing identity. Package B closed the remaining gap: writer-level evidence that `MergeWriterExecutor` actually inserts that `New` history correctly, plus a real canonicalization defect in the archive-export path that only became reachable once a Schema-9 database could hold two completed sessions for one Document. Implemented, independently reviewed, and automated-tested (three changed files: `Services/DataSafety/BackupModelMapperV2.cs`, `KnownFirst.Tests/MergeWriterServiceTests.cs`, `KnownFirst.Tests/BackupCreationTests.cs`); committed as `d00144cd8789f5392c9fb695dac8856f992c2200` on branch `feature/schema9-completed-review-writer-evidence-v1` and merged via PR #65 (`fix: complete schema 9 completed-review package B`, merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`). An independent PR review found exactly one documentation-currentness finding — this section's and its siblings' present-tense status wording — and no code/test finding; the branch documentation addressed that finding. **Merged and part of `master` — this section describes current `master` behavior.**

**Verified against a genuinely Schema-9-shaped target** (the non-unique `ReviewSessions(DocumentId)` index plus the partial unique Active-session index, confirmed via `BackupSchemaCapability.Resolve` returning `Schema9CapabilityResult`, not merely a `PRAGMA user_version` write on a Schema-8 shape):

- **No executable `MergeWriterExecutor` change was required.** `WriteVocabularyReviewWorkflows` already inserted a `New`-classified session with a target-generated `Id` and resolved an existing session purely by identity, with no `UPDATE` path — Package B added the missing test evidence for this behavior, not a code fix.
- **Divergent completed histories coexist for one Document.** Two independently completed `ReviewSession` rows can exist for the same target `Document` after merge, both `Completed`, with the target's own database-level at-most-one-Active-session invariant still holding afterward.
- **New candidates attach to the correct newly inserted parent session**; the pre-existing session's own candidates remain attached to their original parent.
- **Document and Vocabulary relationships resolve through writer-side target-index mappings**: every inserted session/candidate references the target's own `Documents`/`Words` rows, never a re-created or archive-local row.
- **Exact completed-history duplicates remain skipped** — a plan carrying one exact-duplicate session and one divergent session inserts only the divergent one.
- **Reimport of an already-merged completed history converges to no change** at the writer boundary: a fresh plan reports `NoChanges`, and reapplying leaves row counts and content unchanged.
- **The existing active-session safety precondition remains fail-closed**: an Active session appearing on the target between planning and writing blocks the merge with zero mutation, exactly as the pre-existing `BlockedByActiveWorkflow`/`ActiveWorkflowUnsupported` contract already specified.
- **Transactional rollback holds for this path**: an injected failure after the new session and candidate are written rolls back both `ReviewSessions` and `ReviewCandidates` completely.
- **A Schema-8 target (the legacy unique `ReviewSessions(DocumentId)` index) still fails closed** on the identical divergent-history plan, with zero mutation — a characterization test, not a new contract; no new error code was introduced.

**`BackupModelMapperV2` canonical-ordering defect, found and fixed by Package B:** the pre-existing `ReviewSessions` export ordering omitted `KnownCount`/`UnknownCount`/`IgnoredCount`/`CompletedAt` from its sort key. This was unreachable before Schema 9 (at most one session per Document ever existed), but once two completed sessions for one Document became representable, two sessions could tie on every field the old key considered and receive a stable-sort-order-dependent `vr-`/`rc-` archive-local id — non-canonical output from otherwise-identical exports. The fix adds explicit typed `ThenBy` comparisons over every retained session field (mirroring the long-standing v1 `BackupModelMapper` precedent for the same entity), ending in `ReviewSessionEntity.Id` as a final deterministic tie-break. Two clarifications carried in the code's own doc comment, restated here for architectural traceability:

- **The final row-id tie-break guarantees deterministic ordering only within one captured snapshot.** It is reached only after every session-level field already matches.
- **Sessions that tie on every session-level field may still differ through their candidate rows**, and are then genuinely distinct under the Schema-9 v2 full-history session identity (§4.4/§16.2c's identity already includes a candidate digest). `MergeWriterTargetIndex` rejects only wholly identical full-history identities — it does not, and must not, treat a session-level tie as proof of sameness.
- **Cross-installation canonical ordering for that tied-session-level-fields-but-divergent-candidates case is not claimed by Package B.** Two independently exporting installations could still assign different local ids to such sessions. Package B did not resolve this; **it is resolved by Package C (§20) on the local `feature/schema9-completed-review-convergence-v1` branch**, not yet on `master` as of this writing.

No archive DTO, `.kfarchive` format version, database schema, migration, public error/status contract, or release identity changed. No Package C convergence scenario (bidirectional two-installation synchronization, `PortableImportEndToEndConvergenceTests` expansion) was implemented or tested by Package B.

## 20. Package C — cross-installation canonical-ordering hardening (merged via PR #68)

**Status:** Implemented, independently reviewed, MINOR-1 corrected, independently re-reviewed, `TEST_ONLY`-validated, passed final PR review, and merged via PR #68 (feature commit `be62f797ebdfab09cb88f6b32cba8ba2389dd6cf`, merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`). `POST_MERGE_SYNC_ONLY` completed successfully. This work is present on `master`.

Package C closes exactly the residual §19 identified — completed `ReviewSession` sessions whose session-level fields tie but candidate histories differ — plus one further canonical-output defect discovered during Package C's own investigation and review: `SourceMaterial` ordering.

### C-1 — completed `ReviewSession` canonical ordering

`BackupModelMapperV2`'s `ReviewSessions` ordering continues, after every session-level field, with the full Schema-9 completed-review identity from `ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2` — the same identity §16.2c and §19 already define, reached through a new caller-neutral `Services/DataSafety/Merge/Schema9ReviewSessionRowIdentities.cs` helper that centralizes raw-row-to-identity plumbing for both the mapper and `MergeWriterTargetIndex`. No second, competing completed-review identity was defined. `MergeWriterTargetIndex` retains its existing fail-closed behavior unchanged: a duplicate candidate vocabulary identity or a duplicate full-history identity still throws `BackupFormatException(BackupErrorCodes.DuplicateId)`.

Because the identity itself deliberately treats candidate `Order` as positional (safe to renumber) while the archive emits its absolute value, the ordering continues past the identity with a further content-derived key over each session's complete emitted candidate content, including absolute `Order`. A session whose candidates cannot resolve to a valid identity (a dangling reference, or two candidates sharing one vocabulary identity) receives a deterministic key derived from its own actual candidate content rather than a shared sentinel, so two differently malformed sessions cannot collide onto the local row id; this does not change the archive writer's or the merge planner's/writer's existing fail-closed handling of such shapes.

A final local `ReviewSessionEntity.Id` comparison remains syntactically present as a total-order guarantee, but is proven output-neutral: it is reached only once every field two sessions could emit differently already compares equal (parent document, `Status`, all five retained counters, `DecisionSequence`, `StartedAt`, the presence and value of `CompletedAt`, the full-history identity, and the candidate-content key), so which physical row is used at that point cannot change the emitted `BackupVocabularyReviewWorkflow`/`BackupVocabularyReviewItem` content. Local SQLite row ids remain outside semantic identity, exactly as §16's model requires.

### C-2 — `SourceMaterial` canonical ordering

Independent review of Package C's own work found that the pre-existing v2 `SourceMaterial` ordering, `(ContentFingerprint, Title)`, was not total over valid distinct exported documents — two documents differing only in `TextLanguage` (locally reachable: the live duplicate check is `(ContentFingerprint, TextLanguage)`) or in `LookupMode`/`TargetLanguage` (merge-reachable, per §4.1's PC/phone case) could tie completely, leaving `sm-*` and every dependent `ss-*` id dependent on raw snapshot enumeration order.

The corrected ordering compares every retained scalar `SourceMaterial` field (mirroring the v1 `BackupModelMapper` precedent) and then a deterministic content-derived key over the document's complete emitted child subgraph — every field of the emitted `Sentences` and `Occurrences` collections, including the emitted vocabulary reference (never the local `WordId`) and the referenced sentence's position within the document (never the local `SentenceSpanId`). No Document, SentenceSpan, WordOccurrence, or Word local SQLite id participates as ordering content, and no local-id fallback was introduced or is needed: two documents equal on the scalar fields and the child key are provably indistinguishable in the emitted payload.

An independent review of the first version of this correction found it covered only the scalar fields and not the child subgraph (MINOR-1); the RED-first correction adding the child-subgraph key was independently re-reviewed with verdict **`PACKAGE C MINOR-1 CORRECTION REVIEW APPROVED`** — no BLOCKER, MAJOR, or MINOR findings remain.

### Two-installation convergence evidence

Focused tests exercise two installations that each hold one completed review history over the same document, tying on every session-level field and differing only through candidate content, exchanged through the real `.kfarchive` write/validate/`BackupService.ImportPortableArchiveAsync`/preflight/safety-copy/transactional-writer path in both directions (A→B and B→A). Both histories are preserved on both sides, candidates remain attached to their correct parent sessions, and after convergence both installations export the same canonical affected subgraph (`SourceMaterials`, `Vocabulary`, `Workflows.VocabularyReviews`, and their items) regardless of the local row ids each installation happens to hold — including the case where the exchange itself produces the opposite local row-id assignment between the two installations. A further exchange round afterward is no-change/idempotent on both sides and preserves every completed history exactly once.

This is deliberately distinct from, and does not imply, universal byte-for-byte equality of two independently created complete archives: `Sense`/`Meaning`/`AnswerVariant`/`SenseAnswerVariantAssignment` `StableId` values remain `Guid.NewGuid()`-generated and therefore installation-random by design, unaffected by Package C. Semantic database convergence, merge idempotence, and canonical archive-ordering independence remain three distinct properties; Package C strengthens only the third, for the two affected subgraphs, without weakening the first two.

### Scope boundary

No archive DTO shape, `.kfarchive` format version, database schema, migration, or public error/status code changed. No executable `MergeWriterExecutor` redesign was required — the correction is confined to the canonical export/identity boundary (`BackupModelMapperV2`, the new `Schema9ReviewSessionRowIdentities` helper, and a behavior-preserving refactor of `MergeWriterTargetIndex` to reuse that helper). No Schema 10.

### `TEST_ONLY` evidence

- `BackupCreationTests`: 50 passed / 0 failed / 0 skipped
- Merge planner/writer/identity scope (`MergeWriterServiceTests`, `MergePreflightPlannerTests`, `MergeWorkflowIdentityTests`, `MergePreflightServiceTests`): 157 passed / 0 failed / 0 skipped
- Archive/restore/Schema-9 compatibility scope (`BackupArchiveV2Tests`, `PortableRecoveryTests`, `BackupServiceImportRoutingTests`, `Schema8BackupRestoreTests`, `Schema9RuntimeCompatibilityTests`): 117 passed / 0 failed / 0 skipped
- `PortableImportEndToEndConvergenceTests`: 6 passed / 0 failed / 0 skipped
- `ALL_AUTOMATED`: **1776 passed / 0 failed / 0 skipped** (pre-Package-C `master` baseline: 1769/0/0; the delta is exactly the seven tests Package C adds)

**Evidence limitation:** all evidence above is automated (pure mapper/unit, integration, and SQLite-persistence) evidence on isolated temporary databases. No rendered-GUI evidence, no Windows or Android runtime/device/platform evidence, and no standalone-build, packaging, signing, publishing, or release evidence exists for Package C.

## 21. Package D — Schema-9 v2 workflow/review canonical-ordering hardening (KF-BACKUP-003)

**Status:** Implemented, automated-validated, independently reviewed, and **merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`)**; the final PR re-review approved it 0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT and `POST_MERGE_SYNC_ONLY` completed successfully. This section documents shipped `master` behavior.

**Backlog item:** [KF-BACKUP-003](../BACKLOG.md).

Package D closes the gap KF-BACKUP-003 originally flagged for the three v2 export collections Package C did not touch: completed `PreparationSessions`/`PreparationCandidates`, completed `LearningSessions`/`LearningSessionCards`, and `LearningReviews`. A `PLAN_ONLY` pass initially attributed the gap to a delimiter collision in `BackupModelMapperV2.ContentKey`; direct byte inspection then established that `ContentKey` uses a literal U+0001 separator, not an empty string, and that no string-typed field reaches any of the three affected `ContentKey` call sites — so no delimiter ambiguity was ever representable there. The plan was corrected before implementation. **The proven defect is incomplete ordering material, not a delimiter collision:**

- `PreparationSessions` ordered by `(Method, Status, TotalItems, CompletedItems, StartedAtUtc, UpdatedAtUtc)` — omitting the emitted `CompletedAtUtc` and every emitted `PreparationCandidate` child field.
- `LearningSessions` ordered by `(Status, TotalCards, CompletedCards, AgainCount, HardCount, GoodCount, EasyCount, StartedAtUtc, UpdatedAtUtc)` — omitting the emitted `CompletedAtUtc` and every emitted `LearningSessionCard` child field.
- `LearningReviews` ordered by card reference, `ReviewedAtUtc`, `Rating`, `WasTypedAnswer`, `WasCorrect`, `DueAtUtc`, `IntervalDays`, `EaseFactor` — omitting the emitted `LearningSessionId`, `TargetAnswerVariantId`, and `MatchedAnswerVariantId`.

None of the three chains ended in any tie-break at all — not even the local-row-id fallback `ReviewSessions` and the shipped v1 `BackupModelMapper` both retain — so two histories tying on the considered fields fell through to raw snapshot enumeration order, i.e. installation-local SQLite row order. Two installations holding the same completed workflow content under different local row ids therefore bound different archive-local `pb-*`/`pi-*`/`ls-*`/`lq-*` ids to the same history, and the emitted `LearningReviews` sequence could differ by enumeration order alone.

**Production correction, confined to `Services/DataSafety/BackupModelMapperV2.cs`:**

- `PreparationSessions` ordering now adds `CompletedAtUtc` presence/value (`HasValue` compared before the timestamp, so an absent value can never tie with a present one at tick 0 — mirroring the existing `ReviewSessions`/Package-C precedent) and a new `CanonicalFingerprintBuilder`-based key (`BuildPreparationWorkflowChildOrderingKeys`, domain `KnownFirst.Archive.PreparationWorkflow.ChildGraphOrdering.v1`) over the complete emitted candidate subgraph: mapped vocabulary reference, `Order`, `Status`, `SelectedMeaningIndex`, `LastErrorCode`, `LookupAttemptCount`, `UpdatedAtUtc`, and the parsed lookup-draft content.
- `LearningSessions` ordering adds the same `CompletedAtUtc` presence/value treatment and a new key (`BuildLearningWorkflowChildOrderingKeys`, domain `KnownFirst.Archive.LearningWorkflow.ChildGraphOrdering.v1`) over the complete emitted queue subgraph: mapped card reference, `QueueOrder`, the five queue-state booleans, `IsCompleted`, `Rating`, `CompletedAtUtc`, and the mapped target-answer-variant reference.
- `LearningReviews` ordering adds the mapped `LearningSessionId`, and `TargetAnswerVariantId`/`MatchedAnswerVariantId` presence/value (each via the mapped `av-*` reference), after the pre-existing fields. `EaseFactor` continues to be compared as `double`, never as culture-formatted text.
- Every new key uses `StringComparer.Ordinal`. The local row id remains a trailing syntactic total-order fallback on all three chains, documented as reachable only once every emitted field already compares equal — exactly the same role it plays in the pre-existing `ReviewSessions`/`SourceMaterials` ordering.
- These new keys are ordering fingerprints, each with its own domain discriminator, kept in a separate hash family from every merge identity — they are never used, and must never be used, as merge identity material.

No archive DTO shape, `.kfarchive` format version, database schema, migration, merge identity, import routing, public error/status code, or v1 `BackupModelMapper` behavior changed.

### `TEST_ONLY` evidence (merged via PR #76)

- Focused RED, final test code against unmodified `master` production code: **56 passed / 4 failed / 0 skipped / 60 total** — all four failures were assertion failures on differing emitted content (opposite `pb-*`/`ls-*` bindings and reordered `LearningReviews`), not compilation, fixture, or environment failures.
- Identical focused GREEN after the production correction: **60 passed / 0 failed / 0 skipped / 60 total**.
- Broader data-safety integration scope (`MergeWriterServiceTests`, `MergePreflightPlannerTests`, `MergePreflightServiceTests`, `MergeWorkflowIdentityTests`, `MergeIdempotencyConvergenceTests`, `BackupArchiveV2Tests`, `BackupServiceImportRoutingTests`, `PortableRecoveryTests`, `Schema8BackupRestoreTests`, `Schema9RuntimeCompatibilityTests`, `BackupJsonContractTests`, `BackupModelContractTests`, `BackupCreationTests`, `PortableImportEndToEndConvergenceTests`): **376 passed / 0 failed / 0 skipped**.
- `ALL_AUTOMATED`: **1786 passed / 0 failed / 0 skipped**.
- A focused two-installation end-to-end test exchanges one completed preparation history and one completed learning history that tie on every currently-ordered field, through the real `.kfarchive` write/validate/preview/preflight/safety-copy/transactional-writer path in both directions. Both histories converge on both sides with candidates/queue items attached to their correct parents, the two installations are asserted to hold the opposite local row order (so the ordering boundary is genuinely exercised), and a further exchange round reports `MergeNoChange` with unchanged row counts and content on both sides.

**Evidence limitation:** all evidence above is automated (pure mapper/unit, integration, and SQLite-persistence) evidence on isolated temporary synthetic databases. No rendered-GUI evidence, no Windows or Android runtime/device/platform evidence, and no standalone-build, packaging, signing, publishing, or release evidence exists for Package D. As with Package C, this is not a claim of universal whole-archive byte equality: the end-to-end review-event comparison is a content-sorted projection, because the Cards collection's own ordering depends on `Guid.NewGuid()`-generated Sense `StableId` values that remain installation-random by design and are unaffected by Package D.

### Scope boundary

Left out of Package D, and not silently closed by it at that package boundary: `LegacyReviewSummaries` ordering (same defect class, no archive-local id derives from it); `MergePreflightPlannerV2.ComputeReviewFingerprint`'s omission of `LearningSessionId`/answer-variant references from its duplicate-detection key; the then-open mid-session review-event export policy (`Schema8BackupSnapshotRepository.CapturePortableSnapshot` excluded reviews recorded inside a still-Active session); and the `Learning.Cards` collection's own cross-installation ordering, which remains keyed by `Sense.StableId` and therefore installation-random. KF-BACKUP-004 later addressed the fingerprint residual, and merged KF-BACKUP-005B addresses the mid-session export policy (§24). The merged status of the `LegacyReviewSummaries` correction is recorded in §21.1; that correction does not rewrite the historical Package-D boundary.

The duplicate-detection residual above is taken up by KF-BACKUP-004 in §22, which resolves the answer-variant half and records why `LearningSessionId` is deliberately kept out of merge identity. The mid-session policy is binding master behavior through 005B; the `LegacyReviewSummaries` correction is binding master behavior through PR #85; the `Learning.Cards` correction is binding master behavior through PR #87 as described in §21.2; and the Occurrence action-key correction is binding master behavior through PR #89. The direct legacy planner-label statement remains historical.

### 21.1 LegacyReviewSummaries canonical ordering — merged master correction

**Lifecycle status:** merged and binding `master` behavior via PR #85 (feature head `baf5fcda0a017c1492a08dac730d683c1554784d`, merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`); `POST_MERGE_SYNC_ONLY` completed successfully. This correction does not rewrite the historical Package-D scope boundary recorded above.

**Proven defect.** A valid database can contain multiple `ReviewState` rows for one word. The old V2 mapper's composite string key collapsed `LastReviewedAt = null` and a present UTC `DateTime.MinValue` onto the same final ordering value. Stable ordering could therefore retain installation-dependent snapshot enumeration for rows otherwise equal in emitted content. Logically equivalent installations could consequently emit a different `LegacyReviewSummaries` sequence order, changing the serialized V2 `data.json` bytes; because the manifest checksum is computed over those exact `data.json` bytes, the resulting manifest checksum also differs. This is non-canonical ordering of logically equivalent content, not archive corruption or semantic data loss.

**Correction.** `BackupModelMapperV2` now uses typed ascending ordering over `ReviewCount`, `ForgotCount`, `PartialCount`, `KnownCount`, timestamp presence (null first), and normalized UTC ticks for a present timestamp. Local `ReviewStateEntity.Id` is not used. Exact duplicate emitted summaries may tie only because their permutation is byte- and semantics-equivalent; multiplicity is preserved.

**Scope and compatibility.** This correction is confined to `BackupModelMapperV2`. Historical V1 mapper/writer behavior, V1 reader compatibility, existing V1 archives, and v1-to-v2 upgrade remain unchanged. Planner/writer positional list-index handling remains aligned. Database Schema 10 and outer `.kfarchive` V2 remain unchanged; there is no DTO, migration, identity, classification, error/status, UI, transport, or merge-engine change.

**Evidence.** Focused genuine TDD recorded **1 failed / 0 passed** before the correction and **1 passed / 0 failed / 0 skipped** after it. Independent implementation review found **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**. The bounded affected/regression `TEST_ONLY` scope returned **110 passed / 0 failed / 0 skipped**; `git diff --check` passed. This is bounded automated unit/integration/contract evidence only, not ALL_AUTOMATED, ValidateAll, GitHub CI, platform/runtime, rendered-GUI, device/emulator, package, signing, publishing, or distribution evidence.

At the PR #85 boundary, the remaining ordered residuals were `Learning.Cards`/Sense `StableId` cross-installation ordering, then the legacy v1 planner analogous synthesized label. PR #87 later closed the `Learning.Cards` residual.

### 21.2 Learning.Cards canonical ordering — merged master correction

**Lifecycle status:** merged and binding `master` behavior via PR #87 (feature head `2cab8042887bed1004e7c26573a52fd59cc3b380`, merge commit `e97c83ac0cf7decf2915162e0e3a4abf24ee30d8`); `POST_MERGE_SYNC_ONLY` completed successfully. Earlier Package-D and KF-BACKUP-004 statements that this residual was open at those package boundaries remain historical facts.

**Proven defect.** For valid exportable databases, `(resolved Sense StableId, Direction)` is unique, so the old V2 ordering was locally total. It was not cross-installation canonical: Sense StableIds are independently generated installation-random GUID material. Equivalent installations can therefore reverse the semantic association of the StableId sort order, causing the same positional archive-local `c-*` ids to bind different semantic cards. Downstream `Learning.ReviewEvents`, `Learning.AnswerVariantProgress`, and learning-workflow queue references consume those `c-*` bindings. This is canonical archive-emission and local-reference instability, not demonstrated archive corruption, semantic merge divergence, or data loss.

**Merged correction.** For cards whose Sense and vocabulary resolve, `BackupModelMapperV2` now orders semantic-first with existing identity policies: `FutureCardIdentity` (semantic Sense identity plus Direction), preferred-Meaning `ExactMeaningVariantIdentity`, and then every typed emitted Card-state distinction (`State`, UTC due/review/create/update ticks, interval, ease, review/lapse counts, nullable last-review/rating presence and value). Sense StableId is only late/final non-local material on this valid path. Preferred Meaning exact identity therefore participates before typed mutable state, and installation-random Sense GUID ordering is no longer the primary key.

The mapper remains a mapper, not a validator. A malformed snapshot with a missing or unresolved card Sense is still mapped for downstream validation to reject; it is not silently accepted. Its explicit deterministic non-local fallback uses available vocabulary identity, Direction, preferred-Meaning exact identity or stable content, the emitted Card fields, and fixed missing-reference literals. It never uses local numeric ids or raw snapshot enumeration order. Multiplicity is preserved: no grouping, `Distinct`, set conversion, or deduplication is introduced.

**Compatibility and scope.** Database Schema 10 and `PRAGMA user_version` remain unchanged. The outer `.kfarchive` format remains V2, DTO shape is unchanged, and migrations/validators are unchanged. Historical V1 mapper/reader/writer behavior and v1-to-v2 compatibility are unchanged. Merge identities, planner classifications, writer semantics, and scheduler semantics are unchanged. There is no UI, transport, synchronization, public status/error-code, or persistence-contract change.

**Exact bounded evidence.**

1. Initial focused TDD for `CreateBackupV2_TwoInstallationsWithOppositeSenseStableIdOrder_ProduceIdenticalCanonicalLearningCardOrder`: RED **1 failed / 0 passed / 0 skipped**; GREEN **1 passed / 0 failed / 0 skipped**.
2. First bounded affected/regression `TEST_ONLY`: **118 passed / 1 failed / 0 skipped**. It exposed a regression in `CreateBackupV2_TwoInstallationsWithOppositeRowIds_TiedLearningSessionSortKeys_ProduceIdenticalCanonicalOutput`.
3. Correction focused reproduction: **1 passed / 1 failed / 0 skipped**.
4. Correction focused GREEN: **2 passed / 0 failed / 0 skipped**.
5. Final bounded affected/regression `TEST_ONLY` over `BackupCreationTests`, `BackupArchiveV2Tests`, `BackupModelContractTests`, and `PortableImportEndToEndConvergenceTests`: **119 passed / 0 failed / 0 skipped**.
6. Pre- and post-candidate `git diff --check`: passed.
7. Independent review: **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**.

**Evidence limitation:** this is bounded automated unit/integration/contract evidence only. It is not ALL_AUTOMATED, ValidateAll, GitHub CI, platform/runtime build, rendered-GUI/device, package, signing, publishing, or distribution evidence. It also does not establish universal whole-archive byte equality: unrelated installation-random StableIds elsewhere in an otherwise equivalent archive remain an invalid general byte-equality oracle. The former legacy-planner residual wording is historical; GUI automation remains after the current Priority-15 Occurrence candidate.

## 22. KF-BACKUP-004 — Schema-9 LearningReview merge integrity

**Status:** Implemented, automated-validated, independently reviewed (approved 0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT), and **merged via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`)**; `POST_MERGE_SYNC_ONLY` completed successfully. This section documents shipped `master` behavior.

Package D made archive *export ordering* total. This package closes the residual it deliberately left open on the *populated-target merge* path, plus a second, more serious defect that the `PLAN_ONLY` investigation proved along the way.

### 22.1 Defect 1 — the LearningReview plan-action key was not unique per physical row

A `LearningReview` is the one planned entity with no archive-local id of its own, so `MergePlanAction.ArchiveLocalId` carries a synthesized label. That label was `CardId + "@" + ReviewedAtUtc`, which is **not** unique per physical archive row: §6's own exact-duplicate rule and §11's clock-skew table both require two same-instant reviews for one card that differ in any field to be retained as distinct events, and the schema enforces no unique index on `(CardId, ReviewedAtUtc)`.

`MergeWriterExecutor` indexes plan actions by `(EntityKind, ArchiveLocalId)` with a plain last-wins assignment and no duplicate detection, and `WriteLearningReviews` resolved each source row through that same label. Two distinct archive review rows for one card at one instant therefore both received whichever action was recorded last — inserting the duplicate the planner had classified `DeduplicatedEvent`, or dropping the event it had classified `New`, while the preview still reported one of each. Scheduler replay then ran on the wrong surviving event set. Existing coverage proved the retention rule only against the pure `MergeFixtureSet.DeduplicatedUnion` helper, never through the real planner → writer path.

**Correction.** The label is now a deterministic positional key, `lr#<archiveRowIndex>`, derived from the row's position in `archive.Learning.ReviewEvents` by both the planner and the writer through the same shared helper, so the two can never drift. Every physical archive review row keeps exactly one primary plan action. The key is a **lookup label only**: never the fingerprint, never a semantic merge identity, never a target-local id. This also restores the "synthesized positional label" contract `MergePreflightModels` already documented.

### 22.2 Defect 2 — the Schema-9 meaning-aware fingerprint omitted the answer-variant references

§16.2's correction note already recorded that `LearningReviewEntity.TargetAnswerVariantId`/`MatchedAnswerVariantId` "must eventually incorporate" into the meaning-aware review fingerprint. Until this package it had not, so two reviews tying on every other emitted field but exercising genuinely different answer variants were wrongly deduplicated — losing an event together with its variant provenance.

**Correction.** A new domain, `KnownFirst.Merge.Preflight.MeaningAwareLearningReview.v2`, encodes exactly:

```
FutureCardIdentity                    (the card's stable semantic identity — never a raw archive-local CardId)
ReviewedAtUtc
Rating
WasTypedAnswer
WasCorrect
DueAtUtc
IntervalDays
EaseFactor
TargetAnswerVariant stable identity   (nullable AnswerVariantIdentity)
MatchedAnswerVariant stable identity  (nullable AnswerVariantIdentity)
```

Both variant references are resolved to `AnswerVariantIdentity` — a content-derived value over the sense identity, normalized text, and answer language — so equivalent variants held under different archive-local `av-*` or target-local numeric ids on two installations still match. **Archive-local and target-local variant ids are never fingerprinted directly.** Null presence is significant and encoded explicitly by the canonical builder's null marker, so an absent reference never collides with a present one.

### 22.3 LearningSessionId is deliberately excluded from event identity

`LearningSessionId` is **not** part of `LearningReview` merge identity, in either the v2 planner domain or scheduler replay. Three binding reasons:

- §6 defines the immutable real-world event identity — "what this review event was and what it produced" — without it.
- §16.2's forward correction names only `TargetAnswerVariantId`/`MatchedAnswerVariantId` as fields the meaning-aware fingerprint must incorporate.
- §21 states explicitly that Package D's archive-ordering keys — which *do* include the mapped `LearningSessionId` — "are never used, and must never be used, as merge identity material". Ordering material is not identity material.

Including it would couple event deduplication to workflow-session identity stability: two installations whose session rows differ on any identity-bearing field would fingerprint the *same* real event differently and both insert it, duplicating historical reviews and breaking repeated-import convergence. A review's session is therefore treated as **referential workflow attachment and provenance**, not identity. The writer still writes each inserted review's session reference from its own source row, so the positional-key correction can never swap or drop session attachment.

### 22.4 Scheduler replay must share the preflight event contract

`MergeWriterExecutor.ReplayCardSchedules` previously deduplicated a card's surviving events with the narrower persisted Slice-1 fingerprint. Left unaligned, a wider planner contract would let replay silently re-collapse events the planner had just kept distinct — a fresh preview/writer divergence. Replay now uses the same v2 event contract: the card's real `FutureCardIdentity`, the eight immutable review fields, and the same stable nullable variant identities. No scheduling algorithm, initial-state policy, or update set changed — only event-set deduplication and tie-breaking.

### 22.5 What is unchanged

Slice 1's persisted `LearningReviewFingerprintPolicy` and its `KnownFirst.Merge.LearningReview.v1` domain are unchanged for their existing callers and compatibility contract; §6's field list remains that domain's exact historical record. The v2 fingerprint and the positional action key are ephemeral in-plan merge contracts — neither is persisted, and neither is an archive-format field. No archive DTO, `.kfarchive` `formatVersion`, database schema, migration, public error/status code, import-UI, or Package D mapper-ordering change. Populated-target merge remains non-destructive and transactional, and repeated import remains convergent.

The legacy v1 `MergePreflightPlanner` keeps its analogous `LearningReview` synthesized label. It can collide in a direct legacy plan, but import routing upgrades V1 input through `BackupArchiveV1UpgradePolicy`; production preflight and stale-plan validation recompute with `MergePreflightPlannerV2`, whose positional LearningReview action key is already correct. This is therefore not a current production-writer defect.

### 22.6 Evidence

- Focused RED, final test code against unmodified `master` production code: **114 passed / 7 failed / 0 skipped / 121 total** — every failure an assertion failure on missing intended behavior, not a compilation, fixture, or environment failure.
- Identical focused GREEN after the production correction: **121 passed / 0 failed / 0 skipped / 121 total**.
- Affected merge/data-safety `TEST_ONLY` scope: **348 passed / 0 failed / 0 skipped / 348 total**, with all nine new tests included and passing.
- `ALL_AUTOMATED`: **1795 passed / 0 failed / 0 skipped / 1795 total**.

**Evidence limitation:** all of the above is automated unit, integration, persistence, and contract evidence executed against isolated temporary synthetic SQLite databases; no real user database was accessed. There is no rendered-GUI, Windows or Android runtime/device/platform, Release-build, packaging, signing, publishing, or release evidence for this package, and no claim of universal whole-archive byte equality.

### Scope boundary

At the historical KF-BACKUP-004 package boundary, its direct legacy-planner label, `LegacyReviewSummaries` ordering, the mid-session review-event export policy, and `Learning.Cards` ordering remained outside that package. Later packages closed the latter three. The routing finding above establishes that the direct legacy LearningReview label is not production-writer reachable; PR #89 later completed the distinct Occurrence action-key correction described below.

### 22.7 Priority-15 completion — Occurrence action lookup identity

**Lifecycle status:** PR #89 merged the binding correction at `49d25cb8d7d113d1f0b1826369d9105a37d9207b` from feature head `d45a7e8fad533ddda5dda425356bf2095e8bafb6`; implementation commit is `edbb49a87ff3f37337c413111a60f6cfa6805b88`. `POST_MERGE_SYNC_ONLY` completed. Independent review found **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**. Priority 15 is complete on `master`.

**Proven defect.** Both legacy and V2 planners used `SentenceId:VocabularyId` for an Occurrence `ArchiveLocalId`/action lookup key. Two valid physical occurrences of the same vocabulary in one sentence can have distinct semantic occurrence identities and different classifications (for example `ExactDuplicateSkipped` and `New`) yet share that key. `MergeWriterExecutor` indexes actions by `(MergeEntityKind.Occurrence, ArchiveLocalId)` with last-wins assignment and reconstructed the same ambiguous key, so it could apply one occurrence's classification to both physical rows.

**Binding correction.** `MergePreflightPlanner`, `MergePreflightPlannerV2`, and `MergeWriterExecutor` use the same invariant-culture lookup-only key: `SourceMaterialArchiveId:Occurrence.Order`. V1 and V2 graph validation require `Order` to be unique within a SourceMaterial, archive IDs cannot contain `:`, and the key is deterministic. It deliberately uses explicit occurrence `Order`, not collection index or a target-local SQLite ID.

**Unchanged contracts.** `ComputeOccurrenceIdentity` remains semantic identity; classifications, reason codes, preview counts, and multiplicity are unchanged. There is no grouping or deduplication. V1 compatibility and V1-to-V2 payload shape, archive V2, DTOs, Schema 10, migrations, LearningReview identity/action-key contracts, scheduler, `BackupModelMapperV2` canonical ordering, `Learning.Cards`, `LegacyReviewSummaries`, UI, transport, synchronization, persistence, and public status/error-code contracts are unchanged.

**Evidence and limits.** Focused TDD recorded genuine RED **0 passed / 2 failed / 0 skipped** and identical GREEN **2 passed / 0 failed / 0 skipped**; an initial fixture compilation error was corrected before the RED and is not RED evidence. Bounded affected/regression `TEST_ONLY` returned **257 passed / 0 failed / 0 skipped**, including both new occurrence regressions, with pre/post `git diff --check` passing. This is automated component/integration/contract evidence on isolated synthetic SQLite only; it is not ALL_AUTOMATED, ValidateAll, GitHub CI, Windows/Android build validation, rendered GUI/runtime/device, or package/sign/publish/distribution evidence.

---

## §23 KF-BACKUP-005A — Schema-10 Stable Learning-Workflow Identity

### 23.1 Motivation

Prior to Schema 10, `LearningSessions` and `LearningSessionCards` rows were identified only by installation-local SQLite integer primary keys. This made portable archive transport of learning-workflow identity fragile:

- Two installations holding semantically identical Completed sessions assigned different local row ids.
- Archive-local `ls-*` / `lq-*` identifiers (established by KF-BACKUP-003 Package D for canonical ordering) are ephemeral, export-time-only, and explicitly not merge identity material (§21).
- No stable cross-installation identity existed to anchor portable workflow identity in future synchronization scenarios.

Schema 10 supplies a durable, immutable `StableId` on each `LearningSession` and `LearningSessionCard` row. This identity:

- is assigned once and never changed;
- does not depend on installation-local SQLite row ids or archive-local ordinals;
- is intentionally reusable by later cross-device synchronization, not a backup-only disposable scheme.

### 23.2 Schema-10 identity model

Schema 10 adds a nullable `TEXT` `StableId` column to `LearningSessions` and `LearningSessionCards` via `ALTER TABLE ... ADD COLUMN`, with non-null canonical `StableId` values enforced on all valid rows through migration backfill, shape validation, and unique indexes (`IX_LearningSessions_StableId` and `IX_LearningSessionCards_StableId`). No other table receives `StableId` columns in this package.

**Canonical form:** lowercase hexadecimal, exact length 32 (GUID origin) or 64 (SHA-256 origin), unique within its table, immutable after assignment.

**New-row allocation:** Fresh post-Schema-10 `LearningSession` and `LearningSessionCard` rows receive a 32-character `StableId` from `Guid.NewGuid().ToString("N")`.

### 23.3 Completed session deterministic bootstrap

A legacy Completed `LearningSession` (migrated from Schema 9) receives a 64-character SHA-256 `StableId` under the frozen domain:

```
KnownFirst.Identity.LearningSession.LegacyCompletedBootstrap.v1
```

Semantic material:
- `StartedAtUtc`
- `CompletedAtUtc`
- For each queue item in `QueueOrder` order: `(FutureCardIdentity, Rating)`

The hash never includes installation-local SQLite ids or archive-local ordinals. Two installations holding semantically identical Completed sessions produce identical `StableId` values.

**Completed queue row bootstrap** uses a companion domain:

```
KnownFirst.Identity.LearningQueueItem.LegacyCompletedBootstrap.v1
```

Semantic material:
- Parent Completed session `StableId`
- `QueueOrder`
- `FutureCardIdentity`
- `IsAgainRepeat`

### 23.4 Active session one-time GUID bootstrap

A legacy Active `LearningSession` and its associated `LearningSessionCard` rows receive fresh 32-character GUID `StableId` values at migration time. These identities:

- are not deterministic or independently reproducible from workflow content on another installation; they identify this specific durable workflow and are transported unchanged by the later KF-BACKUP-005B Schema-10 portability path;
- remain stable through all subsequent workflow mutations: rating, pruning, Again/repeat, counter changes, and eventual completion;
- are never reassigned after the initial migration bootstrap.

### 23.5 Identity immutability and preservation through workflow mutation

A `StableId` assigned to any `LearningSession` or `LearningSessionCard` row must never be updated, overwritten, or removed by:

- normal workflow operations (rating, pruning, session completion, counters);
- import/merge operations (transported StableIds from source ≥10 archives are preserved unchanged);
- schema migrations (Schema 10 assigns once; later migrations must not reassign).

This immutability constraint is the foundation for future cross-device synchronization reuse.

### 23.6 Archive/source compatibility

| Source schema | Completed portable workflow | Active portable workflow |
| --- | --- | --- |
| ≤9 | Supported; ordinary portable export remains Completed-only and Completed sessions/queue rows may receive deterministic bootstrap StableIds during import | Unsupported/rejected |
| ≥10 | Supported; StableIds must be present, canonical, unique, and transported unchanged | Supported on current `master` by KF-BACKUP-005B for ordinary export and restore into an empty Schema-10 target; StableIds must be present, canonical, unique, and transported unchanged |

For source ≥10 archives, transported `StableId` values are preserved unchanged and validated for canonical form and uniqueness before any mutation.

The outer `.kfarchive` format remains version V2 (not incremented). Schema 10 extends the existing V2 learning-workflow DTOs with trailing nullable `StableId` fields on `BackupLearningWorkflowV2` and `BackupLearningQueueItemV2`. These fields are nullable specifically so pre-Schema10 (source ≤9) archives remain readable without carrying workflow StableIds. Source schema ≥10 archives must supply valid StableIds according to the Schema-10 contract. This is a V2 DTO evolution, not an outer format-version increment.

### 23.7 KF-BACKUP-004 LearningReview identity boundary

`LearningSessionId` is **not** part of `LearningReview` merge identity — this boundary established by KF-BACKUP-004 (§22.3) is unchanged by Schema 10. `LearningSessionId` is preserved as referential workflow attachment and provenance for each inserted review row. Schema-10 `StableId` assignment on `LearningSessions` does not alter or interact with the `LearningReview` event-identity contract.

### 23.8 Historical 005A Active portability exclusion

KF-BACKUP-005A does not implement portable Active learning-workflow continuation:

- Portable export excludes Active `LearningSessions`/workflow rows.
- Portable import continues rejecting unsupported Active workflow archives where applicable.

This was the historical 005A package boundary; merged KF-BACKUP-005B later added Schema-10 empty-target portability (§24). Merged KF-BACKUP-005C supersedes that boundary only for its documented bounded populated-target Active learning-workflow convergence (§25). Source schema ≤9 Active workflows, Active VocabularyReview, and Active PreparationBatch remain unsupported; archive-Completed/target-Active remains blocked, and divergent same-`StableId` Active state remains fail-closed as a non-executable user decision rather than automatic reconciliation.

### 23.9 Succession: KF-BACKUP-005B and 005C

- **KF-BACKUP-005B:** Merged via PR #81 and binding current master behavior; provides portable Active learning-workflow export and empty-target restore from the last durably committed application/database state. Requires the stable identities established by 005A.
- **KF-BACKUP-005C:** merged via PR #83 (merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`) and binding current master behavior for populated-target Active workflow convergence and conflict safety; `POST_MERGE_SYNC_ONLY` completed successfully.

Actual network/cloud synchronization is not implemented. The StableId architecture is intentionally reusable by later cross-device synchronization rather than being a backup-only disposable identity scheme.

### 23.10 Evidence

- Candidate `ValidateAll` (`.\scripts\knownfirst.ps1 -Action ValidateAll -Force` on candidate checkpoint `551399df22131e0214e87b43a3eeaea9ae40ddf9`): **FULL GREEN** (`ALL_AUTOMATED` **1812 passed / 0 failed / 0 skipped / 1812 total**; Windows Debug passed; Windows Release passed; Android Debug passed; Android Release passed; 0 build errors; 0 AOT/trimming/source-gen warnings; 8 non-blocking Android Release XML-documentation warnings). Later commits after candidate checkpoint `551399df...` were documentation-only with no executable/test-tree difference. Merged via PR #79 (merge commit `e56b8bfa27dfe1d630fbacfed24e6d56ea876026`); `POST_MERGE_SYNC_ONLY` completed successfully.
- Final `ALL_AUTOMATED`: **1812 passed / 0 failed / 0 skipped / 1812 total** (`dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj -c Debug`; duration: 9m 18s).
- Focused five-class correction scope: 215 passed / 0 failed / 0 skipped / 215 total.
- Original Stage-1 scope: 845 passed / 0 failed / 0 skipped / 845 total.
- Wikipedia architecture sentinel (Schema 10 `CurrentVersion` sentinel): 7 passed / 0 failed / 0 skipped / 7 total.

**Evidence limitation:** all of the above is automated unit, integration, persistence, contract, and platform compilation build evidence (Windows Debug/Release, Android Debug/Release) executed against isolated temporary synthetic SQLite databases; no real user database was accessed. There is no rendered-GUI, runtime/device/platform, APK/AAB packaging, signing, publishing, or release evidence for this package. Active portable-workflow resume behavior is explicitly excluded from this package's scope.

---

## §24 KF-BACKUP-005B — Portable Active Learning-Workflow Restore Into Empty Target

**Lifecycle status:** Binding master behavior, merged via PR #81. Feature commit: `e8236bba3d23e942014e6979b661e0c77a2a3bdd`; merge commit: `dc56e8412966ac32531c4b0358526582702d6d24`; `POST_MERGE_SYNC_ONLY` completed successfully. Final focused `TEST_ONLY`: **135 passed / 0 failed / 0 skipped**. Final independent PR review: **0 BLOCKER / 0 MAJOR / 0 MINOR**. No GitHub CI evidence existed for the 005B head.

### 24.1 Schema and archive continuity

KF-BACKUP-005B changes portable behavior without introducing a new persistence or container version:

- `DatabaseSchema.CurrentVersion` remains **10**; there is no Schema 11.
- The outer `.kfarchive` format remains **V2**; there is no archive V3.
- Existing Schema-10 `LearningSession.StableId` and `LearningSessionCard.StableId` values are carried unchanged.
- Source ≥10 workflow and queue StableIds remain mandatory, lowercase-hex canonical, valid-length, and unique.

The Schema-10 stable identities remain intended for later cross-device synchronization reuse. This package implements no network/cloud transport, account system, or remote synchronization service and must not be treated as a backup-only identity dead end.

### 24.2 Ordinary Schema-10 export contract

For a source database at Schema 10, ordinary portable export includes:

- the Active `LearningSession` itself;
- its persisted `LearningSessionCard` queue state;
- committed `LearningReview` rows belonging to that Active workflow;
- the workflow and queue-row StableIds assigned by Schema 10.

The export represents the last durably committed application/database state. It does not claim to capture transient in-memory or uncommitted UI state, and the user is not required to finish the learning session before creating a portable archive.

### 24.3 Empty-target restore and durable resume

Restore into an **empty Schema-10 installation** recreates the Active workflow and resumes it through the normal production `LearningService` path. The restored session remains Active; no fake `Completed` state or completion timestamp is fabricated.

The proven durable state includes:

- queue items already completed before export;
- their persisted ratings and completion state;
- committed mid-session `LearningReview` history;
- remaining incomplete queue items;
- the persisted queue ordering;
- unchanged transported workflow and queue StableIds.

The target allocates a new installation-local integer `LearningSession.Id`. Every restored `LearningReview.LearningSessionId` is remapped to that new local parent while the review's durable event content is preserved. This is referential remapping only: KF-BACKUP-004's rule that `LearningSessionId` is excluded from `LearningReview` merge identity remains unchanged.

### 24.4 Completed workflow regression

KF-BACKUP-005B does not regress the established Completed Schema-10 workflow path. Explicit regression coverage proves that empty-target restore preserves:

- `Completed` status;
- a non-null completion timestamp;
- queue and review history;
- the persistent workflow StableId;
- persistent queue-item StableIds.

### 24.5 Legacy and non-learning workflow boundary

- Schema-8 ordinary portable export remains Completed-only for learning workflows.
- Schema-9 ordinary portable export remains Completed-only for learning workflows.
- Source schema ≤9 Active learning-workflow archives remain unsupported/rejected under the established Active-workflow boundary.
- Active `VocabularyReview` remains unsupported.
- Active `PreparationBatch` remains unsupported.

The historical archive-format-v1 Completed-only contract in [backup-format-v1.md](backup-format-v1.md) is unchanged. The 005B capability belongs to the current Schema-10/archive-V2 path.

### 24.6 Historical KF-BACKUP-005B populated-target guard

KF-BACKUP-005B was deliberately limited to empty-target Active restore. At its package boundary, a target already containing durable data blocked a valid Schema-10 archive containing an Active learning workflow:

- preview fails closed with `BackupErrorCodes.ActiveWorkflowUnsupported`;
- actual import fails closed with the same code;
- the target is not mutated;
- executable merge/writer behavior does not run.

KF-BACKUP-005C now supplies the bounded populated-target convergence and conflict semantics in §25. Cross-device network/cloud synchronization, accounts, and remote sync services remain out of scope.

### 24.7 Validation evidence and limits

**Final exact-tree focused `TEST_ONLY`:**

- Classes: `BackupArchiveV2Tests`, `BackupModelContractTests`, `Schema8BackupRestoreTests`, `BackupServiceImportRoutingTests`, `MergePreflightServiceTests`.
- Result: **135 passed / 0 failed / 0 skipped**.
- Normal process completion; 0 build warnings; 0 build errors.
- Pre- and post-run `git diff --check`: passed.

**Earlier supplementary evidence:** an earlier test-project run returned **1820 passed / 0 failed / 0 skipped** against the same unchanged 005B production implementation. Because it ran before the final acceptance-test additions, it is supplementary production-regression evidence and not exact-final-test-tree evidence.

**Not validated for 005B:** `ValidateAll`; Windows platform build; Android platform build; rendered GUI; physical device/emulator behavior; Release-build behavior; APK/AAB; signing; publishing; Google Play distribution. The KF-BACKUP-005A `ValidateAll` result in §23.10 validates the 005A executable tree only and must not be reused as 005B evidence.

### 24.8 Lifecycle succession

The lifecycle-stable package state is: 005B implementation complete → focused final `TEST_ONLY` green → final independent PR review approved → PR #81 manually merged → `POST_MERGE_SYNC_ONLY` complete. KF-BACKUP-005C then completed its bounded implementation, controlled validation, final relevant review, manual PR #83 merge, and `POST_MERGE_SYNC_ONLY`; its current contract is binding master behavior.

---

## §25 KF-BACKUP-005C — Populated-Target Active Learning-Workflow Convergence and Conflict Safety

**Lifecycle status:** binding current-`master` behavior, merged via PR #83 at `bed54d01624e80ca6dd5adf8af097e64fe33e588` (feature head `bc30e9ee9a3689cc4d8b7d108ac83dc037a1b962`); `POST_MERGE_SYNC_ONLY` completed successfully. Section §24 records the historical 005B empty-target boundary.

### 25.1 Bounded populated-target behavior

For a valid Schema-10/archive-V2 source with an Active learning workflow, a populated target with no blocking Active target workflow may accept the workflow additively. The existing merge writer allocates the target-local integer `LearningSession.Id`, preserves workflow/queue `StableId` values, remaps committed `LearningReview` references to that local parent, and applies the established scheduler replay. This adds no separate Active-workflow update engine.

### 25.2 Exact convergence and conflict safety

The same Active workflow `StableId` converges to `NoChanges` only when durable state is exactly equivalent: workflow status/counters/timestamps; queue StableId topology and cardinality; semantic card identity; queue position/flags/completion/rating/timestamp fields; semantic nullable target-answer-variant identity; and LearningReview content. LearningReview comparison reuses KF-BACKUP-004 semantic event identity (including stable card and nullable answer-variant identities, while excluding `LearningSessionId`) but compares fingerprint counts as a multiset, so multiplicity is significant. A `NoChanges` plan returns before safety-copy creation, writer invocation, and scheduler replay.

Every non-exact same-`StableId` Active state — archive ahead, target ahead, independently advanced state, scalar mismatch, queue topology/content mismatch, review-event mismatch, review multiplicity mismatch, or archive-Active/target-Completed — yields `RequiresUserDecision`, a deterministic workflow conflict decision, a non-executable plan, and zero target mutation. No automatic divergent Active-state reconciliation exists. Archive-Completed/target-Active retains `BlockedByActiveWorkflow` / `ActiveWorkflowUnsupported`.

### 25.3 Safety and compatibility boundaries

The Active-aware Schema-10 capture is read-only and preflight-only. It does not relax the existing safety-copy capture, which remains fail-closed for Active target workflows; every executable additive merge still requires the existing validated safety copy before writer mutation, and writer stale-plan/target-state safeguards remain binding. No write-path bypass was introduced. Source schema ≤9 Active workflows, Active VocabularyReview, and Active PreparationBatch remain unsupported.

Schema 10 and `PRAGMA user_version` remain 10; the outer `.kfarchive` remains V2. No Schema 11, archive V3, DTO redesign, StableId-format change, public merge/error/status code, UI, network transport, or synchronization service was introduced.

### 25.4 Final bounded evidence and limits

Focused `Schema10ActiveArchive` evidence is **8 passed / 0 failed / 0 skipped**. The identical 254-test affected/regression scope completed **254/0/0** with MSTest Workers=1 and **254/0/0** with normal Workers=8. Both included the idempotent re-import, semantic-mismatch, and review-multiplicity conflict scenarios. Independent implementation re-review and historical-failure risk review each found **0 BLOCKER / 0 MAJOR / 0 MINOR**.

Two earlier bounded runs each had one safety-copy-count assertion observation (idempotent re-import 1→2; semantic mismatch 1→0). Neither reproduced standalone, in the routing class where applicable, or in the controlled serial/normal pair. They remain unexplained historical transient observations, are not recorded as passing runs, and established no concrete product, planner, safety-copy, parallelism, shared-state, or resource-collision correction target. They were not fixed by a code change.

This is automated unit/integration/persistence/contract evidence using isolated synthetic data where applicable. It is not ALL_AUTOMATED, ValidateAll, platform/runtime, Release-build, rendered-GUI, device/emulator, APK/AAB, signing, publishing, distribution, or GitHub CI evidence.
