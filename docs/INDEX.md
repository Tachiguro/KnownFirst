# KnownFirst Documentation Index

This document is the canonical task router for KnownFirst. Coding agents use this index to locate only the specific documentation required for their active task.

## Baseline Reading Rules

- **Always read:** [AGENTS.md](../AGENTS.md) (universal repository and safety rules).
- **Prompt formulation & task isolation:** Consult [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md).
- **Read [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) when:** writing or modifying repository files.
- **Read [docs/TESTING.md](TESTING.md) for:** test scopes, failure policies, and execution rules.
- **Read [docs/CURRENT_WORK.md](CURRENT_WORK.md) only when:** continuing the active package, reviewing the active branch, changing operational task state, or explicitly requested by the task.
- **Read [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) only after:** an explicit build, configuration, packaging, signing, APK, AAB, release, artifact-reconstruction, or store request.
- **Read [docs/BETA_TESTING.md](BETA_TESTING.md) for:** manual Android device testing procedures.
- **Read [docs/GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md) for:** manual cross-platform GUI testing.
- **Do NOT read routine status or historical docs** ([PROJECT_STATE.md](PROJECT_STATE.md), [ROADMAP.md](ROADMAP.md), [CHANGELOG.md](../CHANGELOG.md), audits, handoffs, backup plans) unless directly required by the task category below.

## Task-Based Reading Matrix

### 0. Prompt Creation and Task Orchestration
- **Required reading:** [AGENTS.md](../AGENTS.md), [PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md), [INDEX.md](INDEX.md), and only the task-specific contracts needed to formulate the next prompt.
- **Normally NOT required:** Full project state, roadmap, changelog, historical handoffs, unrelated architecture, source code outside the requested task.

### 1. Text Analysis and Import
- **Required reading:** [WORD_ANALYSIS.md](WORD_ANALYSIS.md), relevant sections of [MVP_WORKFLOW.md](MVP_WORKFLOW.md), relevant accepted ADRs ([ADR-0002](decisions/ADR-0002-separate-analysis-preparation-and-learning.md), [ADR-0003](decisions/ADR-0003-frequency-prioritizes-never-filters.md), [ADR-0004](decisions/ADR-0004-known-vocabulary-across-texts.md)), affected code and tests.
- **Normally NOT required:** Database audit, online provider specs, build/release guides.

### 2. Vocabulary Review, Preparation, and Lexical Lookup
- **Required reading:** Relevant sections of [MVP_WORKFLOW.md](MVP_WORKFLOW.md), relevant high-level sections of [KNOWNFIRST_ARCHITECTURE.md](KNOWNFIRST_ARCHITECTURE.md), Wikipedia integration specs when applicable ([architecture/wikipedia-json-client.md](architecture/wikipedia-json-client.md), [architecture/wikipedia-lookup-provider.md](architecture/wikipedia-lookup-provider.md)), [ADR-0005](decisions/ADR-0005-source-generated-json-for-android-aot.md) (when JSON/AOT/serialization is affected), [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md) (only if cache or persisted data changes), affected code and tests.
- **Normally NOT required:** Backup plans, database audit, build/release guides.

### 3. Learning and Scheduling
- **Required reading:** Relevant sections of [MVP_WORKFLOW.md](MVP_WORKFLOW.md), [REQUIREMENTS_DELTA_LEARNING_AND_NORMALIZATION.md](REQUIREMENTS_DELTA_LEARNING_AND_NORMALIZATION.md) (when its historical checkpoint is relevant), [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md) (only if persistence changes), affected code and tests.
- **Normally NOT required:** Text analysis internals, backup plans, build/release guides.

### 4. UI and Localization
- **Required reading:** [UI_UX_ACCEPTANCE.md](UI_UX_ACCEPTANCE.md), relevant sections of [MVP_WORKFLOW.md](MVP_WORKFLOW.md), affected Razor/CSS/localization files and tests.
- **Normally NOT required:** Database audit, text analysis internals, build/release guides.

### 5. Database and Migration
- **Required reading:** [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md), [architecture/database-audit.md](architecture/database-audit.md), relevant accepted ADRs ([ADR-0001](decisions/ADR-0001-local-sqlite-storage.md), [ADR-0004](decisions/ADR-0004-known-vocabulary-across-texts.md)), migration and compatibility tests.
- **Normally NOT required:** UI design docs, online lookup provider specs, build/release guides.

### 6. Backup and Restore
- **Required reading:** [architecture/backup-format-v1.md](architecture/backup-format-v1.md), [plans/backup-restore-v1-implementation-plan.md](plans/backup-restore-v1-implementation-plan.md), [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md), [architecture/database-audit.md](architecture/database-audit.md), affected Data Safety code and tests.
- **Normally NOT required:** UI design specs, online lookup provider specs, build/release guides.

### 7. Test Routing and Execution
Refer to [TESTING.md](TESTING.md) for full scope definitions and failure policies:
- **Focused automated tests:** [TESTING.md](TESTING.md) (FOCUSED_AUTOMATED scope), affected code and test classes.
- **All automated tests:** [TESTING.md](TESTING.md) (ALL_AUTOMATED scope).
- **Automated UI contract checks:** [TESTING.md](TESTING.md) (UI_CONTRACT_AUTOMATED scope), `UiWorkflowContractTests.cs`.
- **Windows smoke verification:** [TESTING.md](TESTING.md) (WINDOWS_SMOKE scope), `scripts/smoke-test-windows.ps1`.
- **Manual Windows GUI testing:** [TESTING.md](TESTING.md) (MANUAL_WINDOWS_GUI scope), [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- **Manual Android GUI testing:** [TESTING.md](TESTING.md) (MANUAL_ANDROID_GUI scope), [BETA_TESTING.md](BETA_TESTING.md).
- **Full validation:** [TESTING.md](TESTING.md) (FULL_VALIDATION scope with explicit list of included scopes).

### 8. Build, Versioning, Packaging, and Release
Refer to [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) for full commands and isolation boundaries:
- **Build / configuration verification:** [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md), [VERSIONING.md](VERSIONING.md), affected project files, scripts, and configuration tests.
- **APK / AAB / package generation:** [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md), [VERSIONING.md](VERSIONING.md), relevant current scripts, and applicable release evidence.
- **Manual Android device validation:** [BETA_TESTING.md](BETA_TESTING.md) in addition to [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
- **Historical artifact reconstruction:** [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) plus exact release/tag evidence.

### 9. Diagnostics
- **Required reading:** [development/DEBUG_ARTIFACT_POLICY.md](development/DEBUG_ARTIFACT_POLICY.md), relevant diagnostic service code and tests.
- **Normally NOT required:** Backup plans, text analysis internals, release packaging.

### 10. Governance and Documentation
- **Required reading:** [AGENTS.md](../AGENTS.md), [PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md), [INDEX.md](INDEX.md), [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md), [TESTING.md](TESTING.md), only the specific contracts being updated.
- **Normally NOT required:** Unaffected domain code, test suites, build scripts.

### 11. Historical Investigation
- **Required reading:** Only the specific historical document needed for investigation ([audits/](audits/), [handoffs/](handoffs/), [archive/](archive/), historical test plans).
- **Normally NOT required:** All active implementation contracts not under investigation.
