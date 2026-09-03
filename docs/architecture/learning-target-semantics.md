# Definition and Translation Learning-Target Semantics

> **Partially resolved product semantics; scheduling and persistent card identity remain open. Not an implementation authorization. Not a schema decision. Not an accepted ADR.**

## Problem

KnownFirst must support a single real semantic Sense retaining a definition and one or more translations. For example, German **Haus** may have the semantic Sense “building in which people live”; a German definition may be retained and learned, while English **house** is also retained and learned. These must not silently collapse into an indistinguishable learning target when the user intends to learn both. Conversely, definition versus translation must not automatically fabricate different semantic Senses when they describe the same real Sense. Different real Senses must not silently merge.

The current card identity based on Sense and direction does not settle whether definition and translation content are independent learning targets. `ExplanationLanguage` participates in semantic matching and must not be changed casually by documentation.

## Terminology and constraints

- **Word:** the lexical surface and language-scoped vocabulary identity.
- **Sense:** one semantic interpretation of a Word; distinct real Senses must remain distinct.
- **Meaning:** exact source/provider content or a user-authored content variant describing a Sense.
- **AnswerVariant:** an answer wording associated with a Sense and language; wording alternatives for the same answer target are not automatically independent learning objects.
- **Learning intention/target:** what the user intends to recall or recognize (e.g., German definition, English translation, French translation).
- **Learning Card:** the review-facing object whose direction and scheduling/progress identity are distinct from content and wording.
- **Scheduling/progress identity:** the identity used by the learning scheduler and persisted progress; no change is selected here.

Existing meaning-centric architecture and preparation/vocabulary owners remain relevant: `KF-LEARN-003`, `KF-LEARN-004`, `KF-VOCAB-003`, `KF-VOCAB-004`, and `KF-PREP-002`. Existing Schema-13 persistence, archive handling, and FSRS runtime are current foundations, not a decision about this requirement.

## Accepted Product Semantics (Resolved under KF-LEARN-010)

The following core domain and user-facing semantics are durably accepted:

1. **Word identity preservation:** A Word is not duplicated merely because the user requests another Definition or Translation learning intention.
2. **Sense integrity:** One real semantic Sense remains one Sense when its definition and translations describe that same real Sense. Different real Senses must never silently merge.
3. **No artificial Senses:** Definition versus Translation must never create artificial Senses merely to distinguish content form.
4. **Target independence:** Definition and Translation are distinct explicitly requested learning intentions/targets under that Sense.
5. **Language specificity:** Translation targets are target-language-specific.
   - Example: German Word **Haus** $\to$ one real Sense ("building in which people live") $\to$ German Definition target: *"Gebäude, in dem Menschen wohnen"*; English Translation target: *"house"*; French Translation target: *"maison"* (only when French was explicitly requested).
6. **No unrequested target generation:** KnownFirst must not automatically generate or store all possible target languages. Only the definition/translation target explicitly requested by the user's text/preparation workflow is created and retained for learning.
7. **UI language independence:** The active UI language must never implicitly determine source lexical language, definition language, or translation target language.
8. **Wording variants vs Targets:** Equivalent wording alternatives for the same requested target are AnswerVariants, not independent learning targets.
9. **No spurious mastery transfer:** Learning one requested target must not make another requested target appear mastered merely because they share a Sense.
10. **Unambiguous prompts:** Learning presentation must make target type and language unambiguous (e.g. definition request vs *"How do you say Haus in English?"*).

## Open Decision: Scheduling, Card Identity, and Persistence

The following material design decision remains explicitly **OPEN** and is not decided:

- **Independent vs Shared Scheduling:**
  - Whether each Definition/Translation target must have a completely independent `LearningCard` / FSRS schedule and due date;
  - or whether another scheduling relationship can safely preserve independently learnable target state without cross-target schedule interference.
- **Concrete consequence:** With a shared Sense schedule, an `Easy` review on an English *"house"* target would postpone a difficult German definition target even though the definition is not yet mastered. Fully independent schedules avoid this issue, but may require new persistent card/target identities, database schema evolution, and archive transport changes.

### Explicit Non-Decisions & Boundaries

- No database schema version (e.g., Schema 14) is selected.
- No archive format (e.g., Archive V4) is selected.
- No storage layout (e.g., dedicated `LearningTargets` table or new card columns) is prescribed.
- No acronym expansion as an independent learning target kind is created.
- `KF-LEARN-011` remains blocked until this remaining scheduling/identity decision is resolved.

## Data-integrity risks

- silently merging different Senses;
- silently splitting one Sense because its definition and translation differ in form;
- losing exact Meaning/source content or provenance;
- turning wording alternatives into duplicate learning objects;
- assigning progress to the wrong target or resetting existing progress;
- producing ambiguous or non-deterministic archive/import merges;
- changing `ExplanationLanguage` matching semantics unintentionally;
- making a target appear learned when only another target was learned.

## Later PLAN_ONLY impact

After the remaining scheduling and persistent identity decision of `KF-LEARN-010` is explicitly resolved, a separate PLAN_ONLY package (`KF-LEARN-011`) must define bounded implementation work across preparation, Vocabulary, learning interaction/progression, persistence/archive contracts if needed, and scheduling/progress mapping. That plan must preserve data integrity and fail-closed behavior and must not assume that a product decision implicitly authorizes schema or migration work.
