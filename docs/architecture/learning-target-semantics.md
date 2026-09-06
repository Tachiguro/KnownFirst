# Definition and Translation Learning-Target Semantics

> **Authoritative product and architecture contract (resolved under KF-LEARN-010; implementation owned by KF-LEARN-011).**

## Problem

KnownFirst must support a single real semantic Sense retaining a definition and one or more translations. For example, German **Haus** may have the semantic Sense “building in which people live”; a German definition may be retained and learned, while English **house** is also retained and learned. These must not silently collapse into an indistinguishable learning target when the user intends to learn both. Conversely, definition versus translation must not automatically fabricate different semantic Senses when they describe the same real Sense. Different real Senses must not silently merge.

Under KF-LEARN-010, the scheduling and persistent identity relationship is fully resolved to independent, target-centric learning cards under one unified semantic Sense.

## Terminology and Constraints

- **Word:** the lexical surface and language-scoped vocabulary identity. A Word is not duplicated for additional Definition or Translation learning intentions.
- **Sense:** one semantic interpretation of a Word; distinct real Senses must remain distinct. Definition versus Translation must never fabricate artificial Senses.
- **Meaning:** exact source/provider content or a user-authored content variant describing a Sense.
- **AnswerVariant:** an answer wording associated with a Sense and language; alternative valid answer wordings for the same target remain AnswerVariants, not independent learning objects.
- **LearningTarget:** an explicitly requested Definition or Translation learning intention (e.g., German definition, English translation, French translation).
- **LearningCard:** the review-facing scheduling entity. Exactly: LearningTarget -> one LearningCard -> one FSRS-6 schedule.
- **Scheduling/progress identity:** each target owns its independent Stability, Difficulty, DueAtUtc, factual FSRS review history, and interaction/progression projection. Reviewing another target under the same Sense must not mutate this target.

## Accepted Product Contract (Resolved under KF-LEARN-010)

The following core domain, scheduling, and user-facing semantics are durably accepted:

### A. Word / Sense Semantics
1. **Word identity preservation:** A Word is not duplicated merely because the user requests another Definition or Translation learning intention.
2. **Sense integrity:** One real semantic Sense remains one Sense when its definition and translations describe that same real Sense. Different real Senses must never silently merge.
3. **No artificial Senses:** Definition versus Translation must never create artificial Senses merely to distinguish content form.
4. **AnswerVariant vs Target:** Equivalent valid answer wordings for the same requested target are AnswerVariants, not independent learning objects.

### B. LearningTarget Identity
Every explicitly requested Definition or Translation intention is an independent LearningTarget:
- Examples under one real German **Haus** Sense:
  - Definition in German (*"Gebäude, in dem Menschen wohnen"*);
  - Definition in English (*"a building in which people live"*);
  - Translation German $\to$ English (*"house"*);
  - Translation German $\to$ French (*"maison"*);
  - Translation German $\to$ Spanish (*"casa"*).
- Only explicitly requested targets are created. KnownFirst never automatically generates or stores unrequested languages.

### C. Explicit Semantic Languages
- Translation targets strictly distinguish source and target language: German $\to$ English is not English $\to$ German.
- Definition targets explicitly identify the word's lexical language and definition language.
- Active UI locale is never semantic language truth.

### D. Scheduling Identity
Exactly:
\text{LearningTarget} \longrightarrow \text{one LearningCard} \longrightarrow \text{one FSRS-6 schedule}

- Each target owns independent:
  - Memory Stability;
  - Difficulty;
  - DueAtUtc;
  - Factual FSRS review history;
  - Interaction / progression projection.
- Reviewing or mastering one target under a Sense must never mutate or postpone another target under that same Sense.
- Learning presentation makes target kind and language unambiguous (e.g. definition prompt vs *"How do you say Haus in English?"*).

### E. Interaction is Not Scheduling Identity
- Forward/reverse presentation, Reading, recall, Typing, and Automatic stages must not create separate FSRS schedules.
- CardDirection may exist temporarily in current code during package cutover, but it is no longer part of future scheduling identity.

### F. Automatic Typing Qualification Lifecycle
Typing is a temporary qualification mechanism, not permanent routine maintenance.

Accepted progression semantics:
1. **Lower-friction recall/Reading first:** Fresh cards start in low-friction Reading mode.
2. **Good and Easy advance recall qualification:** Qualifying scheduled reviews with Good or Easy advance the recall counter.
3. **Hard holds:** A rating of Hard holds the current recall qualification counter without advancement or reset.
4. **Again resets recall qualification:** A rating of Again resets the relevant recall qualification counter to zero.
5. **Typing qualification entry:** After two qualifying scheduled recall successes, the target enters Typing qualification.
6. **Typing qualification:** Two successful typing checks on distinct scheduled review events qualify typing.
7. **Return to low-friction maintenance:** Once qualified, routine Automatic interaction returns to low-friction non-typing maintenance (Reading).
8. **Indefinite FSRS:** FSRS scheduled reviews continue indefinitely in low-friction maintenance.
9. **One-check re-arm on lapse:** A later genuine scheduled recall lapse (Again) re-arms exactly one typing re-check.
10. **Re-check restoration:** One successful typing re-check restores the qualified low-friction state; a failed re-check lapses out of qualified maintenance back to initial recall qualification.
11. **Session repeat isolation:** Same-session Again tail repeats must not count as distinct scheduled qualification reviews.
12. **Single FSRS event:** One user interaction must never cause multiple FSRS review events.
13. **Per-target typing opt-out:** Per-target typing opt-out prevents Automatic from entering typing qualification without altering FSRS identity.
14. **FSRS identity independence:** Typing opt-out does not alter the target's FSRS card identity or scheduling.

### G. Existing Again Queue Binding Invariant
- Every committed Again appends exactly one repeat to the active-session tail.
- Existing incomplete work remains ahead of the repeat.
- Repeats may chain without an arbitrary cap.
- Active-session repetition remains distinct from scheduler DueAtUtc.

### H. Factual Replay-Safe Progression Requirement
- The authoritative Automatic progression must be derivable from factual committed interaction events.
- The pure Core replay input must represent at least: event order, ReviewRating, whether the interaction was typing, typing correctness, and whether the event represents a normal scheduled review versus a same-session Again repeat.
- The Core policy must not depend on SQLite, LearningSessionCardEntity, legacy Schema row types, UI state, or mutable database projections.
- Any future persisted progression row is a reconstructible projection/cache, not irreplaceable authority.

### I. Pre-Release Persistence and Migration Policy
- No production user dataset must be migrated for this redesign.
- No Schema-13 $\to$ future-schema migration is required.
- Old development databases may fail closed as unsupported once the clean target-centric schema lands.
- Historical archive compatibility is not required solely for development data.
- Data integrity for the new current format remains mandatory.

## Implementation Boundaries & Package Status (KF-LEARN-011)

The accepted product contract is fully implemented and technically accepted across all six ordered slices under `KF-LEARN-011` on branch `feature/learning-targets-definition-translation-v1`:
1. **Slice 1 (1/6): Core Targets & Governance** (`feat(core): implement pure domain LearningTarget and Automatic progression policy`): Pure Core domain types (`LearningTargetKind`, `LearningTargetIdentity`, `LearningTarget`, `TargetAnswerVariant`, `TargetAnswerRequirement`, `TargetInteractionEvent`, `TargetAutomaticProgressionState`, `TargetAutomaticProgressionPolicy`) and durable contract governance.
2. **Slice 2 (2/6): Persistence Foundation** (`feat(data): implement Schema 14 target persistence, bootstrap, and repositories`): Schema 14 target/answer-variant/FSRS persistence DDL, shape validator, `Schema14CleanBootstrap`, repositories (`LearningTargetRepository`, `TargetAnswerVariantRepository`, `TargetFsrsStateRepository`, `TargetFsrsReviewHistoryRepository`, `TargetReviewRepository`), and unit/integration test coverage.
3. **Slice 3 (3/6): Preparation Target Cutover** (`feat(preparation): cut over preparation pipeline and staging to LearningTargets`): Preparation pipeline and interactive staging cut over to produce clean `LearningTarget` and `TargetAnswerVariant` entities (with add-later target flow under existing Senses).
4. **Slice 4 (4/6): Learning Runtime Cutover** (`feat(learning): cut over LearningService, session queue, and review runtime to LearningTargets`): `LearningService`, active session queue, and review persistence cut over to target-centric authority with 14-point Automatic typing qualification and non-destructive `AlreadyKnown` controls.
5. **Slice 5 (5/6): Backup Current Format Cutover** (`feat(backup): cut over portable backup export, restore, and merge to Archive V4`): `.kfarchive` format V4 (`BackupPayloadV4`), clean restore-into-empty, target-aware causal prefix merge (`CausalHistoryConflict`), and V4 merge safety copies.
6. **Slice 6 (6/6): Integration & Legacy Decommissioning** (`feat(data): complete Schema 14 integration and legacy decommissioning`): Decommissioned legacy `LearningCards` scheduler and interaction paths, clean Schema 14 database lifecycle (`PRAGMA user_version = 14`, `PRAGMA foreign_keys = ON`), fail-closed legacy database protection (`UnsupportedOlderVersion`), and consolidated end-to-end verification.
