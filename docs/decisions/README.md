# Architecture decision records

Architecture decision records (ADRs) preserve the reason for durable decisions
that already govern KnownFirst. Roadmap ideas are not ADRs until a decision has
actually been accepted.

## Naming

Use:

```text
ADR-0001-short-kebab-case-title.md
```

Numbers are never reused. A later decision that replaces an earlier one gets a
new number and updates the earlier ADR's status to `Superseded` with a link.

## Status values

- `Proposed`
- `Accepted`
- `Superseded`
- `Deprecated`

## Required sections

Every ADR contains:

- **Context**
- **Decision**
- **Consequences**
- **Alternatives**

Include status and decision date near the top. Consequences must include costs
and limitations, not only benefits.

## Workflow

1. Confirm that the topic is a durable architecture decision rather than an
   implementation detail or future idea.
2. Create a `Proposed` ADR on the feature branch.
3. Review it with the affected code, data contract, tests, and documentation.
4. Mark it `Accepted` only when the project has actually adopted the
   decision.
5. Update `AGENTS.md`, `PROJECT_STATE.md`, `DATABASE_CONTRACT.md`, or
   other binding documents when the decision changes their contract.

Do not create a backup-format ADR until Data Safety v1 has produced and accepted
that decision.

## Current accepted ADRs

- [ADR-0001: Local SQLite storage](ADR-0001-local-sqlite-storage.md)
- [ADR-0002: Separate analysis, preparation, and learning](ADR-0002-separate-analysis-preparation-and-learning.md)
- [ADR-0003: Frequency prioritizes but never filters vocabulary](ADR-0003-frequency-prioritizes-never-filters.md)
- [ADR-0004: Known vocabulary is stored across texts](ADR-0004-known-vocabulary-across-texts.md)
- [ADR-0005: Source-generated JSON metadata for Android AOT](ADR-0005-source-generated-json-for-android-aot.md)
- [ADR-0006: Git worktrees isolate feature development](ADR-0006-git-worktrees-for-isolated-development.md) *(Superseded by ADR-0007)*
- [ADR-0007: Single canonical working directory](ADR-0007-single-canonical-working-directory.md)
