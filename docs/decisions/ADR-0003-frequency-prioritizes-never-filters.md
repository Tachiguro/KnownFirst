# ADR-0003: Frequency prioritizes but never filters vocabulary

**Status:** Accepted
**Decision date:** 2026-07-22 (records an established implementation decision)

## Context

Frequency is useful for deciding which Unknown words to prepare first, but a
low-frequency word may still be important to the user. Filtering candidates by
frequency would silently discard valid vocabulary and make repeated-context
deduplication corrupt learning priority.

## Decision

Every supported reviewable vocabulary identity remains eligible regardless of
frequency, including identities with one occurrence. Frequency equals the
number of accepted real occurrences and is used only for deterministic
priority:

1. highest accepted occurrence count;
2. earliest first-seen time; and
3. canonical term as the final alphabetical tie-breaker.

Context deduplication affects display snapshots, never occurrence count.

## Consequences

- Users decide whether vocabulary is Known or Unknown.
- Rare but meaningful terms remain visible.
- Preparation focuses on higher-value repeated vocabulary without losing data.
- Exact duplicate imports and rejected/no-open-vocabulary preflight outcomes
  must not alter counts.
- Tests must distinguish candidate count, occurrence count, and unique context
  count.

## Alternatives

- Dropping single-occurrence words was rejected because frequency is not a
  validity signal.
- Ranking by dictionary availability was rejected because provider results
  must not redefine imported vocabulary.
- Counting unique contexts instead of occurrences was rejected because
  repeated appearances are real frequency evidence.
