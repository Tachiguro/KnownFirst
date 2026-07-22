# ADR-0002: Separate analysis, preparation, and learning

**Status:** Accepted
**Decision date:** 2026-07-22 (records an established implementation decision)

## Context

An occurrence in imported text, a vocabulary identity, dictionary reference
data, user-confirmed learning content, and a scheduled card have different
lifecycles. Combining them would make cleanup unsafe, duplicate data, and allow
dictionary or scheduling concerns to corrupt exact text coordinates.

## Decision

Keep these stages and models separate:

1. word analysis creates exact sentence spans, candidates, forms, and
   occurrences from unchanged source text;
2. preparation enriches only user-confirmed Unknown vocabulary and creates
   user-approved meanings and context snapshots; and
3. learning creates independently scheduled card directions from prepared
   content.

Services may pass stable identities between stages but must not collapse the
models into one table or competing representations.

## Consequences

- Original coordinates and occurrence truth remain independent from dictionary
  quality and learning outcomes.
- Preparation and learning can resume or fail without re-tokenizing documents.
- Cleanup must account for cross-stage dependencies explicitly.
- More entities and transactional boundaries are required.
- Tests must cover transitions between stages, not only each stage in
  isolation.

## Alternatives

- One vocabulary/card record containing source, dictionary, and schedule data
  was rejected because the lifecycles and deletion rules conflict.
- Preparing every extracted token before user review was rejected because it
  wastes work and processes known vocabulary.
- Deriving learning state directly from text analysis was rejected because
  analysis must not decide what the user knows.
