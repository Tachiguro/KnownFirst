# KnownFirst Documentation Index

This document is the canonical task router for KnownFirst. Coding agents use this index to locate only the specific documentation required for their active task.

## Baseline Reading Rules

- **Always read:** [AGENTS.md](../AGENTS.md) (universal repository and safety rules).
- **Read [docs/CURRENT_WORK.md](CURRENT_WORK.md) when:** continuing active implementation, updating operational task state, reviewing active branch status, or when the task explicitly references current work.
- **Do NOT read routine status/historical docs** ([PROJECT_STATE.md](PROJECT_STATE.md), [ROADMAP.md](ROADMAP.md), [CHANGELOG.md](../CHANGELOG.md), audits, handoffs, backup plans, build guides) unless directly required by the task category below.

## Task-Based Reading Matrix

### 1. Text Analysis and Import
- **Required reading:** [WORD_ANALYSIS.md](WORD_ANALYSIS.md), relevant sections of [product/IMPORT_AND_REVIEW.md](product/IMPORT_AND_REVIEW.md), relevant accepted ADRs ([ADR-0002](decisions/ADR-0002-separate-analysis-preparation-and-learning.md), [ADR-0003](decisions/ADR-0003-frequency-prioritizes-never-filters.md)), affected code and tests.
- **Normally NOT required:** Database audit, online provider docs, build/release guides.

### 2. Vocabulary Review, Preparation, and Lexical Lookup
- **Required reading:** [product/PREPARATION.md](product/PREPARATION.md), [product/PREPARATION_RESULTS.md](product/PREPARATION_RESULTS.md), [engineering/LEXICAL_LOOKUP.md](engineering/LEXICAL_LOOKUP.md), provider architecture when applicable ([architecture/wikipedia-json-client.md](architecture/wikipedia-json-client.md), [architecture/wikipedia-lookup-provider.md](architecture/wikipedia-lookup-provider.md)), affected code and tests.
- **Normally NOT required:** Backup plans, database audit, build/release guides.

### 3. Learning and Scheduling
- **Required reading:** [product/LEARNING.md](product/LEARNING.md), [REQUIREMENTS_DELTA_LEARNING_AND_NORMALIZATION.md](REQUIREMENTS_DELTA_LEARNING_AND_NORMALIZATION.md), [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md) (only if persistence changes), affected code and tests.
- **Normally NOT required:** Text analysis internals, backup plans, build/release guides.

### 4. UI and Localization
- **Required reading:** [UI_UX_ACCEPTANCE.md](UI_UX_ACCEPTANCE.md), relevant design specs ([design/IMPORT_AND_REVIEW.md](design/IMPORT_AND_REVIEW.md), [design/PREPARATION.md](design/PREPARATION.md), [design/LEARNING.md](design/LEARNING.md), [design/SETTINGS_AND_NAVIGATION.md](design/SETTINGS_AND_NAVIGATION.md)), affected Razor/CSS/localization files and tests.
- **Normally NOT required:** Database audit, text analysis internals, build/release guides.

### 5. Database and Migration
- **Required reading:** [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md), [architecture/database-audit.md](architecture/database-audit.md), relevant accepted ADRs ([ADR-0001](decisions/ADR-0001-local-sqlite-storage.md), [ADR-0004](decisions/ADR-0004-known-vocabulary-across-texts.md)), migration and compatibility tests.
- **Normally NOT required:** UI design docs, online lookup provider specs, build/release guides.

### 6. Backup and Restore
- **Required reading:** [architecture/backup-format-v1.md](architecture/backup-format-v1.md), [plans/backup-restore-v1-implementation-plan.md](plans/backup-restore-v1-implementation-plan.md), [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md), [architecture/database-audit.md](architecture/database-audit.md), affected Data Safety code and tests.
- **Normally NOT required:** UI design specs, online lookup provider specs, build/release guides.

### 7. Build, Versioning, Packaging, APK, AAB, and Release
- **Required reading:** [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md), [VERSIONING.md](VERSIONING.md), [BETA_TESTING.md](BETA_TESTING.md), relevant release evidence ([releases/1.0.0-beta.8.md](releases/1.0.0-beta.8.md)), current build scripts and tests.
- **Normally NOT required:** Text analysis internals, UI acceptance criteria, database audit.

### 8. Diagnostics
- **Required reading:** [development/DEBUG_ARTIFACT_POLICY.md](development/DEBUG_ARTIFACT_POLICY.md), relevant diagnostic service code and tests.
- **Normally NOT required:** Backup plans, text analysis internals, release packaging.

### 9. Governance and Documentation
- **Required reading:** [AGENTS.md](../AGENTS.md), [INDEX.md](INDEX.md), [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md), only the specific contracts or documents being updated.
- **Normally NOT required:** Unaffected domain code, test suites, build scripts.

### 10. Historical Investigation
- **Required reading:** Only the specific historical document needed for investigation ([audits/](audits/), [handoffs/](handoffs/), [archive/](archive/), historical test plans).
- **Normally NOT required:** All active implementation contracts not under investigation.
