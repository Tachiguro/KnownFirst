# ADR-0004: Known vocabulary is stored across texts

**Status:** Accepted
**Decision date:** 2026-07-22 (records an established implementation decision)

## Context

KnownFirst should skip vocabulary the user has already classified as known when
it appears in later texts. Retaining every original document merely to remember
that decision would conflict with local data minimization and completed-text
cleanup.

## Decision

Persist a minimal language-scoped vocabulary marker for words the user marks
Known or later marks permanently known. Apply that marker across subsequent
text imports using the binding vocabulary-identity policy.

Delete document-specific content, contexts, frequency, prepared content,
schedules, and obsolete history when lifecycle rules allow it. Do not infer
known status from frequency, review interval, stemming, or a related identity.

## Consequences

- Future texts skip confidently known vocabulary without retaining completed
  source documents.
- Identity and normalization rules become compatibility-sensitive.
- Permanently-known cleanup can minimize personal data.
- A false identity merge could hide vocabulary, so normalization must remain
  conservative and tested.

## Alternatives

- Document-scoped known status was rejected because users would repeatedly
  classify the same vocabulary.
- Keeping full documents and contexts forever was rejected because it violates
  the completed-document lifecycle.
- Automatically marking long-interval cards known was rejected because
  permanent knowledge requires an explicit user decision.
