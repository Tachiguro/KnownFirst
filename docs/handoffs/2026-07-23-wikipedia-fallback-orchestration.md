# Handoff: Wikipedia fallback orchestration

**Date:** 2026-07-23
**Branch:** `feature/wikipedia-fallback-orchestration`
**Base:** `639618ade38f3a252705085433c1cf6d36598806`
**Final branch head after this documentation commit:** `[Pending final commit SHA]`
**PR #11:** https://github.com/Tachiguro/KnownFirst/pull/11 (Open and unmerged)

## Scope and Architecture

The core architecture correctly places the Wikipedia fallback behind Wiktionary inside `LexicalEnrichmentService`. The fallback execution occurs as an explicitly chained request rather than a provider cycle. The fallback leverages the `WikipediaLookupProvider` which integrates the source-generated MediaWiki Action API JSON client.

**Fallback Decision Table:**

| Initial provider | Primary outcome | Mode | Wikipedia fallback |
| Wiktionary | Success | any | No |
| Wiktionary | final NotFound | Definition | Yes, once |
| Wiktionary | final NotFound | DefinitionAndTranslation | Yes, once |
| Wiktionary | final NotFound | Translation | No |
| Wiktionary | timeout | any | No |
| Wiktionary | rate limit | any | No |
| Wiktionary | network/transient failure | any | No |
| Wiktionary | ParseFailure | any | No |
| Wiktionary | PermanentFailure | any | No |
| Wiktionary | caller cancellation | any | No; cancellation propagates |
| Wikipedia | any | any | Explicit Wikipedia execution only; never route to Wiktionary |

**DefinitionAndTranslation contract:**
- Wikipedia does not act as a translation service.
- Lookups strictly in `LexicalLookupMode.Translation` do not perform network requests and immediately yield `NotFound`.
- In `LexicalLookupMode.DefinitionAndTranslation`, the `TargetTitleCandidate` from the language link is not saved as a translation.

## Changed Files
- `KnownFirst.Tests/LexicalEnrichmentRoutingTests.cs`
- `KnownFirst.Tests/StudyWorkflowServiceTests.cs`
- `CHANGELOG.md`
- `docs/CURRENT_WORK.md`
- `docs/INDEX.md`
- `docs/PROJECT_STATE.md`
- `docs/ROADMAP.md`
- `docs/architecture/wikipedia-json-client.md`
- `docs/architecture/wikipedia-lookup-provider.md`
- `docs/handoffs/2026-07-23-wikipedia-fallback-orchestration.md`

## Validation Results

- **Focused tests:** 35 passed, 0 failed, 0 skipped.
- **Complete suite:** 552 passed, 0 failed, 0 skipped.
- **Windows Debug:** 0 warnings, 0 errors.
- **Android Debug:** 0 warnings, 0 errors.
- **Android Release:** 0 warnings, 0 errors.
- **AOT warnings:** 0
- **Trimming warnings:** 0
- **Source-generation warnings:** 0

## Privacy and Execution Boundaries

- **Cache-isolation evidence:** Tests confirm Wikipedia results use the Wikipedia cache identity and do not bleed into Wiktionary's cache space.
- **PreparationService persistence/reload evidence:** Verifies that enriched data safely persists and reloads correctly without provider cycles.
- **Schema version:** Remains 7. No migration.
- **External behavior:** No live Wikimedia requests occurred. No device, emulator, ADB, logcat, APK, AAB, publish, signing, deployment, publication, or store work.
- **Exclusions:** No UI, Backup/Restore continuation, PDF/list import, or synchronization.

## Known Limitations

- Physical-device and visual validation remain unverified.

## Next Exact Action

1. Review Pull Request #11.
2. Manual merge decision by the user.
