# Definition and Translation Learning-Target Semantics

> **Proposed / unresolved. Product decision required. Not an implementation authorization. Not a schema decision. Not an accepted ADR.**

## Problem

KnownFirst must support a single real semantic Sense retaining a definition and one or more translations. For example, German **Haus** may have the semantic Sense “building in which people live”; a German definition may be retained and learned, while English **house** is also retained and learned. These must not silently collapse into an indistinguishable learning target when the user intends to learn both. Conversely, definition versus translation must not automatically fabricate different semantic Senses when they describe the same real Sense. Different real Senses must not silently merge.

The current card identity based on Sense and direction does not settle whether definition and translation content are independent learning targets. `ExplanationLanguage` participates in semantic matching and must not be changed casually by documentation.

## Terminology and constraints

- **Word:** the lexical surface and language-scoped vocabulary identity.
- **Sense:** one semantic interpretation of a Word; distinct real Senses must remain distinct.
- **Meaning:** exact source/provider content or a user-authored content variant describing a Sense.
- **AnswerVariant:** an answer wording associated with a Sense and language; wording alternatives for the same answer target are not automatically independent learning objects.
- **Learning intention/target:** what the user intends to recall or recognize, potentially involving definition content, translation content, or both. Its semantics are unresolved here.
- **Learning Card:** the review-facing object whose direction and scheduling/progress identity are distinct from content and wording.
- **Scheduling/progress identity:** the identity used by the learning scheduler and persisted progress; no change is selected here.

Existing meaning-centric architecture and preparation/vocabulary owners remain relevant: `KF-LEARN-003`, `KF-LEARN-004`, `KF-VOCAB-003`, `KF-VOCAB-004`, and `KF-PREP-002`. Existing Schema-13 persistence, archive handling, and FSRS runtime are current foundations, not a decision about this requirement.

## Solution dimensions to decide later

Later product design may need to decide, independently:

1. whether definition and translation are one combined target, independently selectable targets, or a user-configurable choice;
2. whether target selection is per Sense, per direction, per Meaning/content variant, or another bounded scope;
3. how multiple translations and wording alternatives relate to one target;
4. how target visibility and answer acceptance work in Reading and Typing;
5. how target intention maps to existing card and scheduling/progress identity;
6. how preparation, Vocabulary editing, import, archive, and replay expose the distinction.

These are materially different options, not recommendations. No option is accepted here, and no entity, schema version, card key, migration, archive revision, or scheduler design is prescribed.

## Data-integrity risks

- silently merging different Senses;
- silently splitting one Sense because its definition and translation differ in form;
- losing exact Meaning/source content or provenance;
- turning wording alternatives into duplicate learning objects;
- assigning progress to the wrong target or resetting existing progress;
- producing ambiguous or non-deterministic archive/import merges;
- changing `ExplanationLanguage` matching semantics unintentionally;
- making a target appear learned when only another target was learned.

## Questions requiring explicit product decision

- Should a user be able to learn both the Haus definition and the English translation independently?
- What is the minimum user-visible distinction between a semantic Sense, its content, and its learning target?
- Which target choices are available in preparation and later Vocabulary editing?
- How should existing cards and progress behave when content contains both forms?
- What answer variants count as equivalent wording for one target?

## Later PLAN_ONLY impact

After `KF-LEARN-010` is explicitly resolved, a separate PLAN_ONLY package (`KF-LEARN-011`) must define bounded implementation work across preparation, Vocabulary, learning interaction/progression, persistence/archive contracts if needed, and scheduling/progress mapping. That plan must preserve data integrity and fail-closed behavior and must not assume that a product decision implicitly authorizes schema or migration work.
