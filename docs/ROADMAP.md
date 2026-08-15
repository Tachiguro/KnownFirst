# KnownFirst Roadmap

**Prioritization date:** 2026-08-15

This roadmap records intended sequence and priority. Verified current implementation state belongs in [PROJECT_STATE.md](PROJECT_STATE.md); active operational task state belongs in [CURRENT_WORK.md](CURRENT_WORK.md).

## Status Definitions

- **Committed:** Merged and verified on `master`.
- **Current:** Active scoped work on a branch under review/verification.
- **Planned:** Accepted priority sequence, implementation not started.
- **Deferred:** Intentionally outside the current sequence.
- **Public-release blocker:** Must be resolved before any public Google Play promotion.

## Prioritized Milestones

| Priority | Milestone | Status | Required outcome |
| ---: | --- | --- | --- |
| 1 | Beta 10 Internal Testing | Committed | Portable `.kfarchive` export/import for empty targets, native file pickers, one-time What's New notice, Beta 10 identity (PR #18). |
| 2 | Russian UI & Translation Target | Committed | Russian UI localization (PR #20), Learn repeat/direction clarity (PR #21), Beta 11 identity (PR #22), Beta 12 Russian translation target fix (PR #23/PR #32); distributed via Google Play Internal Testing. |
| 3 | Meaning & Backup Foundations | Committed | Backup Merge Slices 1–3 (PRs #26–#28), Meaning Slices 0–3 (PRs #29–#33); dormant Schema-8 foundations merged to `master`. |
| 4 | Tooling Infrastructure & Safeguards | Committed | Windows StartupSmoke GUI launcher (PR #35), New-Chat Bootstrap Protocol (PR #36), Google Play packaging safeguards (PR #37). |
| 5 | Meaning Slices 4–5 | Committed | Slice 4 direction-specific progress replay (PR #40); Slice 5 Sense-addressed cards and queues (PR #41). |
| 6 | Schema 8 Activation | Committed | Schema 8 activation and live user-data migration engine (PR #42). |
| 7 | Schema-8 MergePreflight | Committed | Merge preflight planning and preview adaptation (PR #43). |
| 8 | Populated Target Merge Writer | Committed | Transactional populated-target merge writer and Import routing (PR #44). |
| 9 | Import UI & Localization | Committed | Import preview UI, EN/DE/RU localization, convergence validation (PR #45), Windows export atomic replacement (PR #48). |
| 10 | Documentation Governance & Export Staging | Committed | Release-readiness rules (PR #49), Android export staging/validation (PR #50). |
| 11 | Schema-9 History & Package A | Committed | Schema-9 review-session history storage (PR #51), Package A completed-review convergence (PR #52). |
| 12 | Authoritative Documentation Reconciliation | Committed | Documentation governance packages D1–D5 (PRs #53–#63). |
| 13 | Schema-9 Convergence (Packages B & C) | Committed | Package B writer evidence (PR #65), Package C convergence hardening (PR #68). |
| 14 | Public-Release Support Surface | Committed | 14A placeholder removal (PR #71), 14B reopenable release-note history (PR #73), functional Report a bug email composer (PR #98, PR #99). |
| 15 | Portable Merge Integrity Hardening | Committed | Schema-10 stable identity (PR #79), empty-target Active workflow restore (PR #81), populated-target Active convergence (PR #83), canonical export orderings (PR #85, PR #87), Occurrence action-key correction (PR #89). |
| 16 | Automated GUI Validation | Current (P16-A Committed) | P16-A Android automation foundation merged (PR #91). P16-B (Android matrix mapping) and P16-C (Windows launcher integration) pending. |
| 17 | Public-Release Readiness | Planned — blocker | Privacy disclosures, Wikimedia attribution review, website, and store materials. |
| 18 | Russian Source-Text Support | Deferred | Cyrillic tokenization/normalization, Russian Wiktionary parsing, Wikipedia fallback. |

## Cleanup & Release Sequence

1. **Repository Cleanup Program:**
   - **Package 1 (Safe Artifact Cleanup):** Merged via PR #110 (`Clean` / `Clean -Deep` launcher actions, log retention pruning, artifact safety).
   - **Package 2 (Script Organization & Path Portability):** Merged via PR #111 (`scripts/packaging/`, `scripts/validation/`, `scripts/tools/` layout; `$PSScriptRoot` / `__file__` dynamic root resolution).
   - **Package 3 (Documentation Hygiene & Path Governance):** Active on `docs/documentation-hygiene-and-path-governance-v1` (status compaction, obsolete doc deletions, ADR-0007 configurable checkout path governance).
   - **Future Cleanup Steps:** External local checkout cleanup remains separately authorized.

2. **Windows Distribution Packaging Validation:**
   - Source-controlled infrastructure merged via PR #107 (`publish-windows-portable.ps1`, `publish-windows-msix.ps1`).
   - Execution of real portable ZIP packaging, clean-PC portable validation, MSIX signing, and Store onboarding follow cleanup milestones.

3. **Beta-13 Pre-AAB Release-Readiness Gate:**
   - Source version `1.0.0-beta.13` (build 13) merged on `master` via PR #92.
   - Exact-candidate Pre-AAB Release-Readiness Gate ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) remains pending on a post-merge candidate HEAD; no release AAB is authorized or created.

## Public-Release Blockers

Before any public Google Play release (beyond Internal Testing):
1. **Unfinished Control and Debug UI Gating:** Verified absence of unfinished controls or debug overlays in Release builds (owned by Pre-AAB Gate).
2. **Attribution & Licenses:** Wikimedia attribution and license metadata verification.
3. **Automated GUI Matrix Coverage:** Deterministic automation of critical matrix scenarios (Priority 16).
4. **Store & Privacy Documentation:** Privacy policy, store listings, and distribution terms.
