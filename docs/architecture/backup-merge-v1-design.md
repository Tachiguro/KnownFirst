# KF-BACKUP-002 — Non-destructive portable archive merge (design)

**Status:** Proposed design, revision 2. Not implemented. No code, schema, or archive-format change has been made as part of this document.
**Backlog item:** [KF-BACKUP-002](../BACKLOG.md), P1, blocks public release readiness, does not block Beta 12 Internal Testing.
**Builds on:** [backup-format-v1.md](backup-format-v1.md) (binding contract for the *existing* Restore-into-empty behavior, unchanged by this proposal), `Models/BackupModels.cs`, `Data/BackupImportRepository.cs`, `Data/BackupSnapshotRepository.cs`, `Services/DataSafety/BackupService.cs`, `Services/TextReviewService.cs`, `Services/Study/PreparationService.cs`, `Services/Study/LearningService.cs`.

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

**Event fingerprint (`LearningReviewEntity`):** `SHA-256` over the UTF-8 bytes of a fixed-order, `|`-joined canonical string of these **immutable** fields:

```
stableCardKey            (the card's stable (Language, NormalizedTerm, Direction) — never the raw int CardId)
ReviewedAtUtc             (RFC 3339 UTC round-trip string, already canonical per format v1)
Rating                    (enum string: again | hard | good | easy)
WasTypedAnswer            (true | false)
WasCorrect                (true | false)
DueAtUtc                  (RFC 3339 UTC round-trip string)
IntervalDays              (integer)
EaseFactor                (fixed-precision decimal string)
```

These fields are the complete, immutable record of "what this review event was and what it produced" — nothing about them can legitimately differ between two exports of the *same* real-world event.

**Exact duplicate rule:** two `LearningReviewEntity` rows (one target, one archive) with an identical fingerprint are the same event — dedupe, insert nothing. Any field difference at the same `(stableCardKey, ReviewedAtUtc)` means they are **not** the same event (however unlikely a same-instant collision is for human-paced review) — both are retained rather than guessing which is canonical, consistent with "distinct non-empty content is preserved."

**Ordering when timestamps are equal, and under clock skew:** the merged, deduplicated event set for one card is sorted for scheduler replay by `(ReviewedAtUtc, fingerprint bytes as a fixed tie-break)`. This produces a **total, deterministic order** from data alone — it does not depend on which archive was imported first, how many times a merge is repeated, or which device's clock is "more correct." That determinism is what makes replay idempotent and commutative (§11), and it holds *regardless* of clock skew, because the ordering key never consults wall-clock "now" or import sequence.

What this design does **not** claim: when two devices' clocks disagree substantially, the resulting replay order is a fixed, reproducible **convention**, not a proof of true real-world chronological order. A "Hard" review timestamped 09:00 on a fast-clocked phone and a "Good" review timestamped 09:05 on a correctly-clocked PC will always replay in that fixed order on every merge, but if the phone's clock was actually 20 minutes fast, the *true* order may have been reversed — no timestamp-based design can recover information the source data does not contain. To surface this honestly rather than hide it: if a device's own review-event timestamps are not monotonically non-decreasing relative to that device's own `LearningCard.CreatedAtUtc`/prior events for the same card, or if two devices' overall exported timestamp ranges overlap in a way inconsistent with either device's own internal ordering, the matcher emits an informational preflight warning (§9) — never a blocking error, since merge must remain deterministic and complete regardless of clock quality.

**Commutativity summary (answers "which results are commutative and which need a tie-break"):**

- **Commutative, no tie-break needed:** the *set* of surviving events after dedup (§6's exact-duplicate rule is symmetric and order-independent); all §5.1/§5.2 monotonic-tier resolutions (max of two tiers is commutative and associative); all min/max timestamp rules; the union-based Meaning-preservation rule in §5.4.
- **Requires the deterministic tie-break:** the *replay order* fed to `SimpleSpacedRepetitionScheduler` for two distinct events sharing a `ReviewedAtUtc` on the same card — without the fingerprint tie-break, two textually-different events at an identical timestamp would have no defined relative order, which would make the replayed `EaseFactor`/`IntervalDays` outcome depend on implementation-incidental enumeration order. The fingerprint tie-break removes that ambiguity.

Workflow-level events (`ReviewCandidateEntity`, `PreparationCandidateEntity`, `LearningSessionCardEntity`) do not need their own fingerprint scheme beyond what §4.4 already defines, because their identity already resolves duplication at the parent-session level (fingerprint match ⇒ whole subtree is a duplicate) or the `(DocumentId, WordId)` level for `ReviewCandidate` — there is no additional timestamp-ordering question for these, since none of them feed a scheduler replay.

## 7. Archive-version compatibility (unchanged conclusion)

No `formatVersion` bump is required. Every stable key and every piece of history this revision needs — natural-key fields, `ExplanationLanguage`/`TargetLanguage`, provider source identity, full `LearningReview` rows with their scheduler-outcome fields — already exists in the v1 payload. Merge only changes how existing exported fields are *interpreted*; it exports nothing new. The active-workflow exclusion is unchanged and applies identically to Merge.

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

1. **Stable-identity, fingerprint, and conflict-policy library, fully unit-tested** — pure functions over `BackupModels.cs` DTOs (§4.1–§4.4, §5.1–§5.4, §6) with **no database access of any kind**. Contract tests assert every §5 matrix cell and every §11 proof scenario against fixture data, and assert the canonical-empty-`TargetLanguage` normalization (§4.1) and the SHA-256 event-fingerprint definition (§6) byte-for-byte. This slice is complete only once every cell in §5.1/§5.2 and every §11 scenario has a passing test — before any of slice 2–4 is written.
2. **Safety-copy creation and validation**, wired to the *existing* `CreatePortableArchiveAsync` capture and the *existing* `BackupArchiveReader.ValidateAsync`, plus the new active-workflow precondition check (§8). Testable in isolation: given a populated `IKnownFirstDatabase`, produce and validate a safety copy, with no merge logic involved yet. Failure-path tests (disk full, validation failure) assert the `safety-backup-failed` code and confirm zero database mutation.
3. **Read-only matcher** — given a validated `BackupPayload`, a **read-only** connection to a non-empty target, and slice 1's pure functions, produce the full `MergePreflightSummary` (§9). Still no mutation; this is also the UI preview engine. Contract tests replay the §10 worked scenario end-to-end against fixtures and assert the exact preflight counts.
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
