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

- **Dependency Injection**: The provider is explicitly registered as an `ILexicalLookupProvider` singleton via standard DI mechanisms without reflection or assembly scanning, guaranteeing AOT and trimming safety.
- **Routing**: The provider is queried only when the incoming request explicitly specifies `Provider = "Wikipedia"`.
- **Fallback Orchestration**: The provider orchestration explicitly chains requests in a schema-neutral manner. If a `Wiktionary` lookup yields `NotFound` and meets eligibility conditions (e.g., `Definition` or `DefinitionAndTranslation` mode), the orchestration attempts a single `Wikipedia` fallback using the final effective term. The returned result maintains `ProviderName = "Wikipedia"` to ensure cache isolation and accurate provenance.
- **Cache**: Caching relies on the existing `LexicalCacheRepository`. The provider name is embedded in cache keys to ensure isolation.
- **UI & Flow**: No UI or database migrations are included in this foundational provider implementation. The database schema version remains `7`.
- **Backup**: No changes to the backup mechanism are applied.
- **Device Checks**: The execution requires no physical device validations.
