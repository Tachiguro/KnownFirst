# Structured Vocabulary Import and Sense-Level Learning Architecture Plan

## Document Status

- **Type**: Initiative Requirements & Architecture Proposal
- **Created**: 2026-07-23
- **Status**: Draft / Proposal — Pending Data-Model Decision
- **Target Scope**: Structured List/PDF Import, Sense-Level Learning, Sync Preparation, Linux Feasibility
- **Database Schema Constraint**: Preserves SQLite `PRAGMA user_version = 7` (No schema migration in this package)

**Important Distinction**: The product requirements listed under "Decided Requirements" are binding. The data model options, technical architecture proposals, and milestone sequences are a draft/proposal pending a separate decision. This document authorizes no implementation or database migration.

---

## 1. Product Vision & Terminology

KnownFirst is evolving beyond written word tokens to independently recognize, persist, test, and learn distinct senses, parts of speech, and multiword expressions.

### Key Conceptual Examples

- **Homonyms & Senses (`bat`)**:
  - `noun`, animal: *Fledermaus*
  - `noun`, sports equipment: *Schläger*
- **Part-of-Speech Distinction (`answer`)**:
  - `noun`: *Antwort*
  - `verb`: *antworten*
- **Regional Variants & Synonyms (`lorry` vs. `truck`)**:
  - Distinct lexemes with shared/overlapping senses (British English *lorry* vs. American English *truck*).
- **Multiword Expressions (`board game`)**:
  - Multiword unit (*board game*) must remain a single lexical unit and must not be blindly tokenized into independent single-word learning items ("board" + "game").

### Domain Concept Definitions

- **Surface Form**: The exact string as encountered in text or input list (e.g., `"running"`, `"board games"`).
- **Canonical Lemma**: The base/dictionary form of a word or expression (e.g., `"run"`, `"board game"`).
- **Lexeme**: The abstract lexical unit encompassing all inflected forms and syntactic behaviors.
- **Part of Speech (POS)**: Grammatical category (e.g., `noun`, `verb`, `adjective`, `adverb`, `preposition`, `conjunction`, `pronoun`, `determiner`).
- **Sense / Meaning**: A specific semantic definition or usage of a lexeme in a given language context.
- **Translation**: Target-language equivalent for a specific sense (e.g., `"Schläger"` for `bat [sports]`).
- **Definition**: Explanatory text in source or target language clarifying the sense.
- **Synonym**: A lexeme or sense with identical or highly similar meaning.
- **Regional Variant**: Dialectal variant (e.g., `UK` vs `US` spelling/terms).
- **Multiword Expression (MWE)**: Fixed phrase or collocation treated as a single entry (e.g., `"a lot of"`, `"ice cream"`, `"living room"`).
- **Source Entry**: Raw record imported from a list or document prior to normalization.
- **Example Sentence**: Contextual sentence illustrating a specific sense.
- **Global Word Knowledge**: User's overall familiarity marker for a written lemma across all texts.
- **Sense-Level Knowledge**: User's mastery status for a specific sense of a lemma.
- **Learning Item / Learning Card**: Actionable practice item scheduled in spaced repetition for a specific sense and interaction direction.

---

## 2. Decided Requirements

### A. General Import
1. **User-Provided Lists**: KnownFirst shall import user-supplied vocabulary lists and documents.
2. **PDF Support**: Text-based and structured PDF documents are target input formats.
3. **No Proprietary Bundling**: KnownFirst does **not** package or ship copyrighted word lists (such as the Cambridge English Wordlist). Users import their own legally acquired files locally.
4. **General Importer**: The import engine shall be generic and format-agnostic, not hardcoded to specific vendor layouts.
5. **Privacy & Repository Rules**: Imported user files remain local and are never committed to the public repository or bundled into release APKs/AABs/installers.

### B. Structured vs. Standard Text Analysis
1. **Structured Recognition**: Vocabulary lists differ from continuous prose; columns, tables, header rows, and entry rows must be structurally parsed.
2. **Abbreviation Parsing**: Grammatical markers (`n`, `v`, `adj`, `adv`, `prep`, `pron`, `det`) must be recognized as part-of-speech metadata and filtered out from becoming standalone learning items.
3. **Header Filtering**: Page headers, column titles, and page numbers must be excluded from vocabulary extraction.
4. **Interactive Preview**: Import workflows must present a staging preview allowing user validation and manual correction before database persistence.

### C. Multiword Expressions (MWE)
1. **Unit Preservation**: MWEs like `"board game"`, `"ice cream"`, `"living room"`, `"a lot of"` must be retained as single compound entries.
2. **No False Sentence Tokenization**: MWEs must not be processed as multi-sentence prose.
3. **Component Reference**: Individual components may remain referenceable, but the MWE identity takes precedence for learning.

### D. Senses and Parts of Speech
1. **Homonym Support**: Words with identical spelling but distinct senses or parts of speech are modeled separately.
2. **User-Confirmed Senses**: Dictionary lookup returns potential senses, but only senses confirmed by the user become active learning items.
3. **Independent Mastery**: Each sense has its own knowledge state and spaced repetition progress.

### E. Context & Contextual Suggestions
1. **Contextual Hinting**: Sentence context or list notes (e.g., *"as sports equipment"*) pre-select suggested senses.
2. **Confirmation Duty**: Automatic sense mapping is advisory; final assignment requires user confirmation.

### F. Example Sentences
1. **Sense Attachment**: Examples belong to specific senses, not merely to the general word.
2. **Licensing & Attribution**: Examples from external dictionary APIs must adhere to license and attribution contracts.
3. **Origin Classification**: User-authored, dictionary-provided, and synthetic AI examples must be clearly distinguished.

### G. CEFR & Curriculum Metadata
1. **CEFR Scale**: Entries may carry CEFR levels (`Pre-A1`, `A1`, `A2`, `B1`, `B2`, `C1`, `C2`, `unknown`).
2. **Metadata Retention**: Source name, list edition, topic, examination name, and original record preserved.
3. **Separation from Priority**: CEFR level is curriculum metadata and does not override personal text frequency. Frequency-one items are retained.

### H. Knowledge vs. Learning
1. **Global Word vs. Sense Status**: A user may mark a written word as globally known while learning a new, specific sense.
2. **Graduated Knowledge States**: System distinguishes: `Known Globally`, `Uncertain`, `Unknown Word`, `Word Known / Sense Unknown`.
3. **Addressability**: Spaced repetition cards target specific senses.

### I. Singularization & Plural Exceptions
1. **Preserve Raw Form**: Raw surface form and canonical lemma are both persisted.
2. **Pluralia Tantum**: Fixed plurals (`clothes`, `trousers`, `scissors`) must not be invalidly singularized to non-existent forms.

### J. Synchronization Architecture
1. **Decoupled Transport**: Sync domain logic is separated from cloud/network storage transports.
2. **Supported Transports (Future)**: User-chosen cloud file, WebDAV, self-hosted storage, local network.
3. **No Mandatory Central Server**: KnownFirst does not require a proprietary central cloud service.

### K. Linux Platform Strategy
1. **No Direct MAUI Target**: Linux is not targeted via .NET MAUI.
2. **Alternative Host Feasibility**: Linux support will be evaluated as a separate host shell reusing core logic, data layers, and Razor views where applicable.

---

## 3. Open Design Questions

1. Should KnownFirst introduce an explicit `LexemeEntity` between `WordEntity` and `MeaningEntity`?
2. Does every sense require a permanent `SenseEntity` replacing or upgrading `MeaningEntity`?
3. Should senses use global stable identifiers (e.g., Wikidata ID / Sense Key)?
4. How should senses from different lexical providers (Wikipedia, Wiktionary, custom lists) be merged?
5. What criteria deterministically decide if two imported senses are identical?
6. How are etymological homonyms (same spelling, different origin) distinguished from polysemous senses?
7. How are part-of-speech shifts (e.g., verb `run` vs noun `run`) represented in preparation workflows?
8. How should component words and MWEs be linked bidirectionally?
9. Should global word knowledge remain on `WordEntity.Status` or migrate to a language-scoped lexeme marker?
10. What discrete states belong to `SenseKnowledge` (e.g., `PassiveRecognized`, `ActiveRecallable`)?
11. Should `LearningCardEntity` expand `WordId` to target `MeaningId`/`SenseId` exclusively, or support both?
12. How can existing `LearningCardEntity` records (which reference `WordId`) be migrated safely?
13. How will legacy `MeaningEntity` records from Schema 7 be backfilled?
14. What filtering mechanisms prevent bloating the DB with irrelevant dictionary senses?
15. How should synonyms and regional variants (`lorry`/`truck`) be linked?
16. How should conflicting CEFR ratings from multiple sources be resolved?
17. What guarantees idempotent re-import of modified PDF or CSV lists?
18. How are updated editions of the same curriculum list delta-merged?
19. How are duplicates between PDF list imports and document analysis texts resolved?
20. How will multi-column PDF layouts be parsed into correct reading order?
21. What heuristics will separate table grid layouts from multi-column text in PDFs?
22. Should scanned (image-only) PDFs be supported in initial scope?
23. Is OCR part of Phase 1 or a deferred module?
24. Which PDF parsing library satisfies .NET 10 cross-platform, AOT, trimming, and license constraints?
25. Which source data may be stored locally, exported, backed up, or synchronized under applicable copyright law, source licenses, and provider terms?
26. How should low-confidence parse results be highlighted in the preview UI?
27. How will example sentence attribution be presented on small mobile screens?
28. What tag or metadata field identifies synthetic/AI-generated examples?
29. What event schema or changelog entries track synchronization events?
30. How will multi-device concurrent edits be merged without data loss?
31. Which data items are sync-eligible vs local-only caches (e.g., dictionary web response cache)?
32. How are deletions and sense merges propagated across synced devices?
33. How will end-to-end encryption (E2EE) be integrated into file/WebDAV sync?
34. Which framework (Photino, Avalonia, Blazor Desktop) provides the best Linux host candidate?
35. Which UI components can be shared 100% between MAUI (Android/Windows) and a Linux desktop host?

---

## 4. Current Implementation Baseline

An audit of the codebase (`C:\Dev\KnownFirst`) reveals:

1. **`WordEntity` (`Data/Entities/WordEntity.cs`)**:
   - Represents a canonical term string scoped per language (`IX_Words_Language_NormalizedTerm`).
   - Stores global `WordStatus` (`Unreviewed`, `Ignored`, `Known`, `Learning`), occurrence counts, and SRS progress parameters (`ConsecutiveRecallSuccessCount`, etc.).
   - Does **not** distinguish part of speech or distinct senses.

2. **`MeaningEntity` (`Data/Entities/MeaningEntity.cs`)**:
   - Belongs to a single `WordId` via `IX_Meanings_WordId`.
   - Contains fields: `DisplayTerm`, `EncounteredSurfaceForm`, `GrammaticalRelationship`, `TokenKind`, `SelectedMeaningId`, `Translation`, `Definition`, `DictionaryExample`, `AdditionalNote`, `ConfirmedByUser`, `Attribution`, `Source`.
   - **Database Capacity**: Multiple `MeaningEntity` rows per `WordId` *are* supported at the SQLite table level.
   - **Service Constraint**: `StudyWorkflowService` and preparation flows currently assume at most *one* active confirmed `MeaningEntity` per word during card creation.

3. **`LearningCardEntity` (`Data/Entities/LearningCardEntity.cs`)**:
   - Primary key with unique index `IX_LearningCards_Word_Direction` over `(WordId, Direction)`.
   - Contains an indexed `MeaningId` field, but SRS uniqueness and lookup are currently tied to `WordId`.

4. **Lexical Identity (`KnownFirst.Core/Text/VocabularyIdentityPolicy.cs`)**:
   - Identity is strictly `Language + NormalizedTerm`.
   - Frequency affects priority, never existence.
   - Permanently known words apply across all documents.

---

## 5. Data Model Options

### Option A: Incremental Extension of `MeaningEntity`
1. **Description**: Retain `WordEntity` and `MeaningEntity`, but lift the single-meaning assumption in `StudyWorkflowService`. Add fields for `PartOfSpeech`, `CEFRLevel`, `SenseKey`, and update `LearningCardEntity` unique index to `(MeaningId, Direction)`.
2. **Advantages**: Minimal schema shift from Schema 7; reuses existing table structures.
3. **Disadvantages**: `MeaningEntity` is currently overloaded with both provider raw data and user sense selection.
4. **Migration risk**: Low structural risk, moderate data-backfill risk.
5. **Learning-card impact**: Cards become meaning-addressed (`MeaningId`).
6. **Global-word-knowledge impact**: Requires careful decoupling from `WordEntity.Status`.
7. **Synchronization impact**: Sync requires tracking `MeaningEntity` mutations heavily.
8. **Backup/Restore impact**: Format v1 must be expanded to handle meaning-addressed cards.
9. **AOT and serialization impact**: Low (additive schema changes).
10. **Complexity**: Low to moderate.
11. **Principal unresolved risks**: Semantic overloading of `MeaningEntity` continues to grow.

### Option B: Dedicated `SenseEntity` alongside `WordEntity` and `MeaningEntity`
1. **Description**: Introduce `SenseEntity` (representing a distinct semantic definition) and treat `MeaningEntity` as raw provider/source lookup data.
2. **Advantages**: Clean separation between raw dictionary lookups and curated user senses. Multiple provider sources can be mapped to one curated sense.
3. **Disadvantages**: Introduces a 3-tier entity hierarchy (`Word` -> `Sense` -> `Meaning/Source`).
4. **Migration risk**: Moderate structural migration required.
5. **Learning-card impact**: Cards reference `SenseId`.
6. **Global-word-knowledge impact**: `WordEntity` retains global written-form knowledge; `SenseEntity` represents curated, user-confirmed meaning.
7. **Synchronization impact**: `SenseEntity` provides a clean boundary for sync events.
8. **Backup/Restore impact**: Format v1 needs new domain tables but cleanly separates curated data.
9. **AOT and serialization impact**: Moderate (new DTOs required).
10. **Complexity**: Moderate.
11. **Principal unresolved risks**: Backfilling legacy Schema 7 `MeaningEntity` selections into new `SenseEntity` records.

### Option C: Explicit Lexeme-Sense-Source Domain Model
1. **Description**: Full domain refactoring into `LexemeEntity`, `SenseEntity`, `SenseKnowledgeEntity`, `SourceEntryEntity`, and `LearningCardEntity`.
2. **Advantages**: Complete alignment with linguistic standards and multiword expressions.
3. **Disadvantages**: High migration complexity; breaking change for existing persistence layer.
4. **Migration risk**: Very high.
5. **Learning-card impact**: Completely decoupled from raw text.
6. **Global-word-knowledge impact**: Moved to explicit Lexeme level.
7. **Synchronization impact**: Complex graph sync.
8. **Backup/Restore impact**: Format v1 would require a complete rewrite.
9. **AOT and serialization impact**: High.
10. **Complexity**: High.
11. **Principal unresolved risks**: Threatens local data continuity and delays feature delivery significantly.

### Provisional Recommendation
> **Provisional recommendation — requires dedicated data-model decision**: Option B is preferred.
> - `WordEntity` can retain global written-form knowledge for now.
> - `SenseEntity` can represent curated, user-confirmed meaning.
> - `MeaningEntity` can continue to represent source-specific or prepared meaning and attribution.
> - Learning cards can long-term target `SenseId`.
> - Multiple provider sources can be mapped to one curated sense.
> - This separation is cleaner than further overloading `MeaningEntity` and less disruptive than the full Lexeme-Sense-Source model (Option C).
>
> **Fallback**: Option A remains the fallback if the audit proves that a new `SenseEntity` creates disproportionate migration or backup complexity.
>
> **Long-term**: Option C remains the long-term reference architecture but is not recommended for the next migration step.

---

## 6. PDF and List Import Pipeline Design

```
[1. Select File] -> [2. Detect File Type & Text Layer] -> [3. Extract Layout & Pages]
        |
[4. Detect Columns / Tables] -> [5. Identify Entry Boundaries]
        |
[6. Parse Lemma, POS, Definitions & MWEs] -> [7. Generate Staging Preview]
        |
[8. Flag Low-Confidence / Ambiguous Fields] -> [9. User Review & Manual Edit]
        |
[10. Deduplicate & Match Existing Senses] -> [11. Atomic Commit to Database]
        |
[12. Generate Import Summary Report]
```

### Format Hierarchy
1. **CSV / TSV / JSON**: Phase 1 structured list baseline (deterministic columns).
2. **Text-Based PDF (Single-Column)**: Extraction via layout-aware text parser.
3. **Tabular / Multi-Column PDF**: Advanced layout analysis (detecting column gutters and table borders).
4. **Scanned / Image PDF**: Deferred to optional future OCR extension module.

---

## 7. Sense-Level Learning Progression

- **Stage 1 (Quick Diagnosis)**: Classify: `Known Globally`, `Uncertain`, `Unknown`, `Word Known / Sense Unknown`.
- **Stage 2 (Sense Confirmation)**: Confirm Definition, Translation, Part of Speech, Source, Context, Example.
- **Stage 3 (Passive Recognition)**: Multiple choice / recognition of sense in context.
- **Stage 4 (Active Recall)**: Target-language prompt to sense definition/translation.
- **Stage 5 (Cloze & Collocation)**: Fill-in-the-blank in example sentence.
- **Stage 6 (Spelling & Production)**: Active typing / spelling of canonical lemma or MWE.
- **Stage 7 (Free Application)**: Usage in user-created context sentences.

---

## 8. Synchronization Architecture Preparation

### Domain Requirements
- Stable client device ID (`DeviceId`).
- Globally unique entity/event IDs (UUIDs/GUIDs).
- Vector clocks or UTC modification timestamps with tombstone markers for deletion.

### Syncable vs Local-Only Data
- **Syncable**: Global word knowledge markers, `SenseKnowledge`, confirmed user senses, SRS card states/schedules, curriculum tags, custom user examples.
- **Local-Only**: Raw HTTP response cache, temporary PDF upload files, local logs, diagnostic artifacts.

---

## 9. Implementation Milestones

### M0: Data-model decision & architecture sign-off
- **Goal**: Decide schema implications.
- **In Scope**: Documentation & Schema proposal.
- **Out of Scope**: Code implementation.
- **Dependencies**: PR #10 merged.
- **Data-model consequences**: Determines path (Option B preferred).
- **Tests**: None.
- **Acceptance criteria**: Decision approved.
- **Risks**: Delays if consensus isn't reached.

### M1: Structured CSV/TSV list import engine
- **Goal**: Basic structured import.
- **In Scope**: CSV/TSV parsing.
- **Out of Scope**: PDF parsing.
- **Dependencies**: M0.
- **Data-model consequences**: Source metadata fields.
- **Tests**: Unit tests for parser.
- **Acceptance criteria**: Parses term, POS, translation without errors.
- **Risks**: Encoding issues.

### M2: Import staging preview & interactive correction UI
- **Goal**: User validation before DB commit.
- **In Scope**: Preview UI, correction logic.
- **Out of Scope**: Auto-learning.
- **Dependencies**: M1.
- **Data-model consequences**: None.
- **Tests**: UI tests.
- **Acceptance criteria**: User can modify or reject low-confidence items.
- **Risks**: UI complexity.

### M3: Curriculum & CEFR metadata tagging
- **Goal**: Support standard curriculum tags.
- **In Scope**: Tagging logic, CEFR levels.
- **Out of Scope**: Auto-grading users.
- **Dependencies**: M1.
- **Data-model consequences**: Add CEFR to entities.
- **Tests**: Parser tests.
- **Acceptance criteria**: CEFR metadata preserved and displayed.
- **Risks**: Conflicting tags.

### M4: Multi-meaning & Part-of-Speech domain support
- **Goal**: Differentiate homonyms and POS.
- **In Scope**: POS filtering, multiple senses per word.
- **Out of Scope**: Advanced grammar checking.
- **Dependencies**: M0.
- **Data-model consequences**: `SenseEntity` or `MeaningEntity` extension.
- **Tests**: Integration tests.
- **Acceptance criteria**: Identical spelling with different senses, multiple parts of speech.
- **Risks**: Migration of legacy data.

### M5: Sense-level knowledge tracking
- **Goal**: Track mastery per sense.
- **In Scope**: SenseKnowledge state machine.
- **Out of Scope**: Global word tracking changes.
- **Dependencies**: M4.
- **Data-model consequences**: New knowledge table/fields.
- **Tests**: Workflow tests.
- **Acceptance criteria**: global word knowledge versus sense knowledge correctly tracked.
- **Risks**: User confusion.

### M6: Sense-addressed learning cards & example sentence management
- **Goal**: SRS for specific senses.
- **In Scope**: Card addressing, example contexts.
- **Out of Scope**: Audio.
- **Dependencies**: M5.
- **Data-model consequences**: `LearningCardEntity` targets `SenseId`.
- **Tests**: Scheduler tests.
- **Acceptance criteria**: sense-specific examples appear on cards.
- **Risks**: Duplicate cards.

### M7: Single-column text PDF import parser
- **Goal**: Text PDF support.
- **In Scope**: Single-column text extraction.
- **Out of Scope**: Scanned PDFs.
- **Dependencies**: M2.
- **Data-model consequences**: None.
- **Tests**: PDF extraction tests.
- **Acceptance criteria**: text-based PDF import works.
- **Risks**: Layout artifacts.

### M8: Multi-column & tabular PDF import parser
- **Goal**: Complex PDF support.
- **In Scope**: Column/table boundary detection.
- **Out of Scope**: OCR.
- **Dependencies**: M7.
- **Data-model consequences**: None.
- **Tests**: Layout boundary tests.
- **Acceptance criteria**: multi-column PDF import works correctly.
- **Risks**: Parsing failures on edge cases.

### M9: Optional OCR engine integration
- **Goal**: Support image PDFs.
- **In Scope**: OCR hook.
- **Out of Scope**: Bundling heavy OCR models.
- **Dependencies**: M8.
- **Data-model consequences**: None.
- **Tests**: OCR pipeline tests.
- **Acceptance criteria**: Image to text works.
- **Risks**: Library licensing/size.

### M10: Sync domain model & export format spec
- **Goal**: Define sync protocol.
- **In Scope**: Sync events, conflict rules.
- **Out of Scope**: Network transport.
- **Dependencies**: M6.
- **Data-model consequences**: Tombstones, UUIDs.
- **Tests**: Conflict resolution tests.
- **Acceptance criteria**: synchronization readiness verified locally.
- **Risks**: Clock skew.

### M11: Initial user-chosen sync transport
- **Goal**: Cloud sync.
- **In Scope**: File/WebDAV transport.
- **Out of Scope**: Real-time DB sync.
- **Dependencies**: M10.
- **Data-model consequences**: None.
- **Tests**: Network transport tests.
- **Acceptance criteria**: Sync between two instances works.
- **Risks**: Connection drops.

### M12: Linux desktop host feasibility study
- **Goal**: Evaluate Linux port.
- **In Scope**: Desktop shell evaluation.
- **Out of Scope**: Full release.
- **Dependencies**: None.
- **Data-model consequences**: None.
- **Tests**: Build checks.
- **Acceptance criteria**: Linux feasibility decision documented.
- **Risks**: UI incompatibility.

---

## 10. Acceptance Criteria Checklist

- [ ] **CSV/TSV import**: Parses term, POS, translation without errors.
- [ ] **text-based PDF import**: Extracts text accurately.
- [ ] **multi-column PDF import**: Detects and respects column boundaries.
- [ ] **multiword expressions**: Phrases like `"board game"` are preserved as single lexical items.
- [ ] **multiple parts of speech**: Abbreviations (`n`, `v`, `adj`) are stored as metadata.
- [ ] **identical spelling with different senses**: Homonyms can coexist and be learned independently.
- [ ] **global word knowledge versus sense knowledge**: Tracked and displayed distinctly.
- [ ] **sense-specific examples**: Examples are tied directly to their respective sense.
- [ ] **CEFR metadata**: Preserved correctly from source files.
- [ ] **idempotent re-import**: Re-importing a list updates gracefully without duplication.
- [ ] **preview correction**: User can modify or reject low-confidence items before saving.
- [ ] **no automatic learning of all translations**: Only user-confirmed meanings become active cards.
- [ ] **plural exceptions**: Fixed plurals (`trousers`) are not corrupted by singularization rules.
- [ ] **backup compatibility**: Data safety v1 contracts are maintained.
- [ ] **synchronization readiness**: Event models and IDs support future sync.
- [ ] **Linux feasibility decision**: Documented evaluation of Linux host frameworks.

---

## 11. Deferred Ideas

- Automatic AI sense selection without user confirmation.
- Web scrapers automatically pulling unverified example sentences.
- Full OCR integration in initial release.
- Bundling copyrighted Cambridge dictionary files.
- Central proprietary cloud server requirement.
- Real-time collaborative multi-user editing.
- Linux as a MAUI framework target.

---

## 12. Licensing & Source Attribution

Which source data may be stored locally, exported, backed up, or synchronized under applicable copyright law, source licenses, and provider terms?

- This document does not provide legal advice.
- Local storage, export, backup, and synchronization may be subject to different copyright, license, and provider-term requirements.
- Attribution and source metadata must be preserved.
- Proprietary vocabulary lists are not bundled with KnownFirst.
- The generic importer and the imported user content are separate concerns.
