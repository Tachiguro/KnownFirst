# Documentation audit

**Audit date:** 2026-07-22
**Source baseline:** `origin/master` at
`956e71895cf141805c8c24f7d32691075d439730`

## Follow-up audit: canonical handoff and consolidation

The follow-up audit on 2026-07-22 verified `master` at
`8dafcea161350432da47d97bfb5ac1397f5d3f5e`, one registered worktree at
`C:\Dev\KnownFirst`, and a clean status before the documentation branch.
`docs/INDEX.md`, `docs/CURRENT_WORK.md`, and
`docs/development/DEBUG_ARTIFACT_POLICY.md` are now the canonical navigation,
handoff, and disposable-diagnostics documents. The single-worktree cleanup is
recorded in the dated handoff under `docs/handoffs/`.

Historical branch/worktree inventories remain retained as historical evidence;
they are not current instructions. No duplicate current-work file, absolute
removed-worktree link, or `KnownFirst-diagnostics` reference was introduced.

The platform-target follow-up on `build/remove-apple-targets` removed iOS and
Mac Catalyst from the active project, platform folders, diagnostics branches,
tests, scripts, and current product documentation. Remaining Apple mentions
are historical context or explicit statements that Apple support is removed.

## Audit method

The audit covered every tracked Markdown document, the absent/present top-level
project entry points, the complete `docs/` tree, the `.github/` tree,
schema sources, release scripts, test inventory, recent history, branches,
tags, and worktrees. Product claims were checked against the release tree,
current code, automated tests, or explicit release evidence.

Authority is classified as:

- **Binding:** current rules or contracts that implementation must follow.
- **Operational:** repeatable development, test, or release procedure.
- **Informative:** state, evidence, history, or explanation.
- **Historical:** preserved context that must not drive current work.

## Pre-audit inventory

| Path | Purpose | Authority | Last prior update | Audit finding and action |
| --- | --- | --- | --- | --- |
| `AGENTS.md` | Repository instructions | Binding | `fb4d759`, 2026-07-16 | Correct but too small: no reading order, worktree governance, documentation roles, PR rules, or Definition of Done. Replaced with the binding entry point. |
| `README.md` | Project introduction and local workflow | Informative | `7392527`, 2026-07-17 | Product overview was broadly current; documentation map and release-state pointers were missing. Added them and clarified online lookup. |
| `docs/AGENT_WORKFLOW.md` | Detailed engineering work-package flow | Binding operational detail | `724add5`, 2026-07-21 | Current and useful. Retained; `AGENTS.md` now owns entry-point and governance rules. |
| `docs/ANDROID_ONLINE_LOOKUP_CRASH_TEST.md` | Manual Android lookup regression plan | Operational | `ae8408b`, 2026-07-20 | Originating issue was fixed, but status was not recorded and the diagnostics case was ambiguous for Release. Added a regression-status notice. |
| `docs/BETA_TESTING.md` | Android package and manual test guide | Operational | `dab3912`, 2026-07-17 | Claimed Beta 3 identities while the project is Beta 8; direct APK script is actually hard-coded to Beta 6. Updated current identity and separated the valid AAB path from the legacy helper limitation. |
| `docs/CODEX_IMPLEMENTATION_PROMPT.md` | Original automatic-learning implementation prompt | Historical | `fb4d759`, 2026-07-16 | Completed prompt looked like live instructions. Added a historical-status banner and current entry-point links. |
| `docs/GUI_TEST_MATRIX.md` | Repeatable Beta 7 visual/workflow matrix | Operational | `3a29480`, 2026-07-21 | Detailed current regression baseline, but contains no completed results. Retained without converting test definitions into evidence. |
| `docs/KNOWNFIRST_ARCHITECTURE.md` | Long-term product and architecture contract | Binding | `6f80d00`, 2026-07-17 | Broadly current and consistent with the release. It correctly keeps backup deferred. Retained as authoritative. |
| `docs/KnownFirst_Windows_GUI_Testplan.md` | Windows manual regression corpus | Operational/historical | `fb4d759`, 2026-07-16 | Still named the resolved Android crash as an open blocker. Marked the plan historical and the blocker resolved while preserving the regression scenario. |
| `docs/MVP_WORKFLOW.md` | Binding user-workflow contract | Binding | `6f80d00`, 2026-07-17 | Current and consistent with implemented workflow. Retained. |
| `docs/REQUIREMENTS_DELTA_LEARNING_AND_NORMALIZATION.md` | Learning/normalization verification record | Historical | `8a98eb1`, 2026-07-20 | The recorded 265-test result was valid for that checkpoint but stale as a current total. Added commit/date scope and a current-state link. |
| `docs/UI_UX_ACCEPTANCE.md` | Beta 7 UI/UX acceptance baseline | Binding | `724add5`, 2026-07-21 | Current design baseline; it correctly separates automated contracts from visual acceptance. Retained. |
| `docs/WORD_ANALYSIS.md` | Text-analysis and coordinate contract | Binding | `13d119f`, 2026-07-20 | Current and consistent with architecture/tests. Retained. |
| `docs/archive/TEXT_REVIEW_VERTICAL_SLICE_PROMPT.md` | First vertical-slice prompt | Historical | `fb4d759`, 2026-07-16 | Correctly archived and intentionally superseded by current architecture/workflow. Retained unchanged. |

There was no `CHANGELOG.md` and no `.github/` directory before the audit.

## Gaps closed

The audit added:

- `CHANGELOG.md` for user-visible release effects;
- `docs/PROJECT_STATE.md` for verified current whole-project state;
- `docs/ROADMAP.md` for ordered future work;
- `docs/DATABASE_CONTRACT.md` for persisted-data and migration rules;
- `docs/releases/1.0.0-beta.8.md` for the released version;
- `docs/handoffs/2026-07-22-beta-8-release.md` for technical transfer;
- `docs/decisions/README.md` and six accepted ADRs;
- `docs/maintenance/branch-and-worktree-inventory.md`;
- this documentation audit; and
- `.github/pull_request_template.md`.

## Intentional overlap

- `AGENTS.md` is the mandatory index and safety contract;
  `docs/AGENT_WORKFLOW.md` gives deeper work-package execution detail.
- `KNOWNFIRST_ARCHITECTURE.md` defines durable domain rules;
  `MVP_WORKFLOW.md` defines user-visible sequence; `WORD_ANALYSIS.md`
  specializes exact analysis behavior.
- `DATABASE_CONTRACT.md` specializes persisted compatibility without
  duplicating every domain rule.
- UI acceptance documents define criteria and scenarios. They are not proof
  that a manual run occurred.
- `PROJECT_STATE.md` owns current facts, `ROADMAP.md` owns future order,
  `CHANGELOG.md` owns user effects, and release notes own one published
  version.

## Contradictions resolved

1. Beta 3 identity claims were replaced with the verified Beta 8 identity.
2. The direct APK helper's Beta 6 metadata is now identified as a tooling
   limitation rather than a current release path.
3. The Android crash is no longer documented as an open blocker.
4. The 265-test historical checkpoint no longer appears to be the current
   suite total.
5. Completed agent prompts are explicitly historical, so they cannot override
   current governance.

## Evidence deliberately not invented

The repository does not contain:

- the released AAB or its checksum;
- physical-device model and Android version;
- screenshots or completed GUI-matrix results;
- a supported backup/restore format; or
- proof of Apple support; Apple targets are intentionally removed from the
  current project and no Apple build or device validation is claimed.

Those gaps are reported as limitations, not filled with assumptions.
