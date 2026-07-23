# Structured Vocabulary Import and Sense-Level Learning Architecture Plan

## Document Status

- **Type**: Binding Architecture & Initiative Plan
- **Created**: 2026-07-23
- **Status**: Draft / Proposal — Pending Data-Model Decision
- **Target Target Scope**: Structured List/PDF Import, Sense-Level Learning, Sync Preparation, Linux Feasibility
- **Database Schema Constraint**: Preserves SQLite `PRAGMA user_version = 7` (No schema migration in this package)

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
25. Which original source data can be stored locally under fair use vs copyright limits?
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
- **Description**: Retain `WordEntity` and `MeaningEntity`, but lift the single-meaning assumption in `StudyWorkflowService`. Add fields for `PartOfSpeech`, `CEFRLevel`, `SenseKey`, and update `LearningCardEntity` unique index to `(MeaningId, Direction)`.
- **Pros**: Minimal schema shift from Schema 7; reuses existing table structures.
- **Cons**: `MeaningEntity` is currently overloaded with both provider raw data and user sense selection.
- **SRS Impact**: Cards become meaning-addressed.
- **Sync Impact**: Sync requires tracking `MeaningEntity` mutations.

### Option B: Dedicated `SenseEntity` alongside `WordEntity` and `MeaningEntity`
- **Description**: Introduce `SenseEntity` (representing a distinct semantic definition) and treat `MeaningEntity` as raw provider/source lookup data.
- **Pros**: Clean separation between raw dictionary lookups and curated user senses.
- **Cons**: Introduces a 3-tier entity hierarchy (`Word` -> `Sense` -> `Meaning/Source`).
- **SRS Impact**: Cards reference `SenseId`.

### Option C: Explicit Lexeme-Sense-Source Domain Model
- **Description**: Full domain refactoring into `LexemeEntity`, `SenseEntity`, `SenseKnowledgeEntity`, `SourceEntryEntity`, and `LearningCardEntity`.
- **Pros**: Complete alignment with linguistic standards and multiword expressions.
- **Cons**: High migration complexity; breaking change for existing persistence layer.

### Provisional Recommendation
> **Provisional recommendation — requires dedicated data-model decision**: Option A or Option B are preferred over Option C to preserve stability and local data continuity. No schema version upgrade is authorized until a dedicated decision package is approved.

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

- **M0**: Data-model decision & architecture sign-off (Documentation & Schema proposal).
- **M1**: Structured CSV/TSV list import engine.
- **M2**: Import staging preview & interactive correction UI.
- **M3**: Curriculum & CEFR metadata tagging.
- **M4**: Multi-meaning & Part-of-Speech domain support.
- **M5**: Sense-level knowledge tracking.
- **M6**: Sense-addressed learning cards & example sentence management.
- **M7**: Single-column text PDF import parser.
- **M8**: Multi-column & tabular PDF import parser.
- **M9**: Optional OCR engine integration (Scanned PDFs).
- **M10**: Sync domain model & export format spec.
- **M11**: Initial user-chosen sync transport (Cloud File / WebDAV).
- **M12**: Linux desktop host feasibility study.

---

## 10. Acceptance Criteria Checklist

- [ ] **CSV/TSV Import**: Parses term, POS, definition, translation, and CEFR without errors.
- [ ] **MWE Handling**: Phrases like `"board game"` are preserved as single lexical items.
- [ ] **POS Filtering**: Abbreviations (`n`, `v`, `adj`) are stored as metadata, not words.
- [ ] **Homonyms**: Multiple senses for the same term can coexist and be learned independently.
- [ ] **Preview & Edit**: User can modify or reject low-confidence items before saving.
- [ ] **Plural Exceptions**: Fixed plurals (`trousers`) are not corrupted by singularization rules.
- [ ] **Schema Safety**: Database contract remains at `PRAGMA user_version = 7` until explicit migration package.

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

1. **Content Independence**: Import tools are generic utilities. Users bear responsibility for the copyright status of imported files.
2. **Attribution Preservation**: Imported dictionary definitions and example sentences retain source name, license tag, and attribution string.
3. **Export Integrity**: Export and sync operations preserve attribution metadata.
