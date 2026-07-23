# Wikipedia Lookup Provider Architecture

## Overview

The `WikipediaLookupProvider` is a concrete implementation of `ILexicalLookupProvider` for the KnownFirst application. It leverages the existing `IWikipediaApiClient` to perform lookups against the Wikipedia Action API.

## Design Boundaries

- **Provider Name**: `Wikipedia`
- **Schema Version**: `1`
- **Contracts**:
  - Implements `ILexicalLookupProvider`.
  - Does **not** implement `IDictionaryLookupProvider`. It only provides encyclopedic context and does not serve as a primary dictionary.
- **Client Usage**: Uses the existing low-level JSON API client (`IWikipediaApiClient`).

## Data Mapping

- **Definitions**: 
  - An article extract is mapped exactly to `LexicalMeaning.Definition`.
  - A definition is required for a `Success` status. If an article is returned but the extract is empty or whitespace, the result is treated as `NotFound` (`no-usable-content`).
- **Translations**:
  - Wikipedia does not act as a translation service.
  - Lookups strictly in `LexicalLookupMode.Translation` do not perform network requests and immediately yield `NotFound` (`translation-not-supported`).
  - In `LexicalLookupMode.DefinitionAndTranslation`, the `TargetTitleCandidate` from the language link is not saved as a translation. The `TargetTitleCandidate` is merely an unverified hint to the topic in another language, not an acceptable vocabulary translation.
- **Status Mapping**:
  - API `Success` -> `Success`
  - API `NotFound`, `Disambiguation`, `NoUsableContent` -> `NotFound`
  - API `RateLimited`, `TimedOut`, `TransientFailure` -> `TransientFailure`
  - API `PermanentFailure` -> `PermanentFailure`
  - API `ParseFailure` -> `ParseFailure`
- **Metadata**:
  - `Attribution` and `SourceProject` strictly mirror the values provided by the `IWikipediaApiClient` for successful lookups.
  - In error scenarios or when no API lookup occurs, metadata remains deterministically empty or `null` rather than using fabricated values like `"Wikimedia Foundation"`.
- **Identity**:
  - `MeaningId` is deterministic. It prefers the stable positive `PageId` prefixed by the source project (`wp_{SourceProject}_{PageId}`).
  - If a `PageId` is unavailable, a SHA-256 hash derived from the source project and canonical title is used.
  - `RevisionId` does not alter the `MeaningId`. Different articles have different IDs.

## Privacy & Security

- **Logging**: The provider respects data privacy. It logs operation durations, lengths of lookup terms, and request context (e.g., source language and lookup mode), but never full user search terms, extracts, or raw Wikipedia URLs in plain text.
- **Exceptions**: Unhandled exceptions are logged only by their type name. The full exception object is not passed to the logger to avoid accidental spillage of sensitive user input embedded in exception messages.

## Execution Constraints

- **Dependency Injection**: The provider is explicitly registered as an `ILexicalLookupProvider` singleton via standard DI mechanisms without reflection or assembly scanning, guaranteeing AOT, trimming safety, and source-generated JSON compatibility. Privacy-safe diagnostics remain mandatory.
- **Routing**: The provider can be reached through:
  - an explicit `Wikipedia` request; or
  - one internally created fallback request after the complete `Wiktionary` execution returns deterministic `NotFound`.

### Fallback Orchestration Decision Table

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

**Explicit constraints:**
- One fallback attempt maximum.
- No provider cycles.
- No route from Wikipedia back to Wiktionary.
- No result merging.
- Genuine Wikipedia `ProviderName`, `SourceProject`, `PageTitle`, `RevisionId`, `Attribution`, and `MeaningId` remain intact.
- Wikipedia never fabricates translations.
- `DefinitionAndTranslation` follows the existing definition-or-translation success contract.
- Cache keys include provider identity and provider schema version.
- Schema remains 7.
- No migration, UI, Backup/Restore, PDF/list import, or synchronization is included.
