# Wikipedia Fallback User-Flow Audit

**Date:** 2026-07-23
**Audited master commit:** `d33cd80633f1ad1c25f76567136c642c419a23af`
**Scope:** Post-merge synchronization, canonical-state correction, and user-flow audit for the merged KnownFirst Wikipedia fallback (PR #11).

## PR #11 Merge Confirmation
PR #11 was successfully merged and the local `master` branch is synchronized with `origin/master`. The feature head `9aa4ef7` matches the merge commit `d33cd80` exactly in tree state (Tree ID: `97c1c25`).

## Files and Symbols Inspected
- `KnownFirst.Core/Preparation/SourceReferencePolicy.cs` (`CreatePageUri`, `GetLicenseReference`)
- `Components/Pages/PrepareWords.razor` (`StartAutomaticAsync`, `ConfirmOnlineLookupAsync`, `privacy disclosure rendering`, `automatic/manual flow`, `DefinitionAndTranslation behavior`, `empty translation behavior`, `alternative-meaning selection`, `retry and manual-entry behavior`, `NotFound/transient/parse/permanent/cancellation states`)
- `Components/Shared/SourceDetails.razor` (`ProviderName`, `SourceProject`, `PageTitle`, `RevisionId`, `Attribution` rendering)
- `Resources/Localization/SharedResource.resx` and `SharedResource.de.resx` (`Settings_OnlineConsentGranted`, `Source_Project`, `Prepare_NetworkFailure`, `Prepare_OnlineDisclosure`)
- `Services/Lexical/LexicalEnrichmentService.cs` (`ExecuteSingleProviderAsync`, `LookupOneAsync`, `EnrichAsync`, fallback execution, provider identity checks)
- `Services/Lexical/Wikipedia/WikipediaFallbackPolicy.cs`
- `Services/Lexical/Wikipedia/WikipediaLookupProvider.cs`
- `Services/Lexical/Wikipedia/WikipediaApiClient.cs`
- `Services/Study/PreparationService.cs` (`LookupCurrentAsync`, `GetCurrentAsync`, `AcceptAsync`)
- `KnownFirst.Tests/LexicalEnrichmentRoutingTests.cs` (fallback routing tests, cache isolation tests)
- `KnownFirst.Tests/StudyWorkflowServiceTests.cs` (preparation persistence/reload tests)
- `KnownFirst.Tests/UiWorkflowContractTests.cs` (UI workflow contract tests)
- `KnownFirst.Tests/LocalizationResourceTests.cs` (localization tests)

## Answers to Audit Questions

1. **Is the merged automatic Wikipedia fallback already reachable through the existing user workflow?**
   Yes. `LexicalEnrichmentService.EnrichAsync` seamlessly triggers the fallback when Wiktionary yields a final `NotFound` outcome.
2. **Will a successful Wikipedia definition already appear in the existing preparation UI?**
   Yes. `PrepareWords.razor` binds to the returned definition seamlessly and renders the content without further interaction.
3. **Does the current online-lookup consent explicitly or implicitly authorize contacting Wikipedia?**
   The destination is accurately described as Wikimedia. However, the purpose is inaccurately limited to "dictionary lookup" and "dictionary content". The disclosure does not fully describe Wikipedia's encyclopedic fallback context.
4. **Does any existing consent or privacy text incorrectly mention only Wiktionary?**
   No, it says "Wikimedia" and "Online dictionary" (or "Online-Wörterbuchabfrage"). The gap is that the purpose only mentions dictionary context, not encyclopedic.
5. **Are Wikipedia provider name, source project, page title, attribution, or revision information visible to the user?**
   Yes. `SourceDetails.razor` renders all of these values if they are present.
6. **Is the legally or operationally required attribution sufficiently represented?**
   **No. Attribution sufficiency is unresolved.** 
   The current UI displays provider, project, page title, revision, attribution text, and a plain license name, but:
   - Wikipedia page link is absent; `SourceReferencePolicy.CreatePageUri` explicitly requires `.EndsWith(".wiktionary.org")`.
   - License URI is absent; `SourceReferencePolicy.GetLicenseReference` returns only a plain text license name, and `SourceDetails.razor` renders it as plain text.
   - Exact reuse and modification notice requirements have not been fully assessed.
7. **Can Wikipedia ever appear as a fabricated translation in the UI?**
   No. The schema-neutral Wikipedia result does not populate the translation field.
8. **How does DefinitionAndTranslation behave when Wikipedia returns a definition but no translation?**
   `PrepareWords.razor` renders the definition and displays an empty input box for the translation, expecting manual entry.
9. **Are fallback NotFound, transient, parse, and permanent outcomes presented correctly?**
   Yes, they are mapped securely to generic UI messages in `PrepareWords.razor` `GetLookupFailureMessage()`:
   - `NotFound` -> `Prepare_DictionaryEntryNotFound` or `Prepare_DefinitionNotFound` or `Prepare_TranslationNotFound` or `Prepare_LanguageSectionNotFound`. **Retry**: No.
   - `TransientFailure` -> `Prepare_NetworkFailure` (for "network-unavailable", "timeout", "transient-server-error") or `Prepare_TransientFailure`. **Retry**: Yes.
   - `ParseFailure` -> `Prepare_ResponseParseFailure` or `Prepare_ParseFailure`. **Retry**: No.
   - `PermanentFailure` -> `Prepare_PermanentFailure` (e.g. for unknown provider). **Retry**: No.
   - `cancellation` -> Swallowed/Handled seamlessly.
   - Wikipedia fallback failure after primary NotFound -> Bubbly correctly (e.g., transient Wikipedia error renders as `Prepare_NetworkFailure`).
   *(Visually unverified: physical UI rendering of these messages remains unverified.)*
10. **Does cached Wikipedia content reload through the complete UI-facing flow?**
    Yes. `PreparationService.LookupCurrentAsync` calls `LexicalEnrichmentService.EnrichAsync`. `EnrichAsync` executes the fallback, calling `ExecuteSingleProviderAsync` and then `LookupOneAsync`. `LookupOneAsync` correctly checks `LexicalCacheRepository.GetAsync` using the fallback provider name (`Wikipedia`). On cache hit, `LexicalEnrichmentService` returns it. `PreparationService` maps it, and `PrepareWords.razor` renders it smoothly. If the item was already saved to the database (`PreparationItems` table via `AcceptAsync`), it is loaded directly from DB via `PreparationService.GetCurrentAsync` without hitting the lexical cache.
11. **Is provider-selection UI actually necessary for the automatic fallback?**
    No. The automatic fallback runs behind the scenes and requires no dedicated UI toggle.
12. **Which exact user-facing gaps remain?**
    Wikipedia page link rendering, license URI rendering, precise English and German online-lookup disclosure text for encyclopedic fallback context, and remaining visual device validation.
13. **Which gaps are:**
    **Must:**
    - Wikipedia source-page link support in `SourceReferencePolicy.cs`.
    - Concrete license-reference/link handling.
    - Accurate English and German online-lookup disclosure covering Wikipedia context.
    - Automated regression coverage for attribution and consent behavior.
    **Should:**
    - Review whether UI wording should distinguish dictionary definitions from encyclopedic context.
    - Verify responsive and accessible rendering of long source, attribution, and license values.
    - Consider a clearly localized provider display name rather than raw provider identifiers, only if existing behavior is user-hostile.
    **Deferred:**
    - Manual provider-selection UI.
    - Broad Wikimedia project support beyond Wiktionary and Wikipedia.
    - Device and visual validation until the bounded implementation is complete.
14. **What is the smallest safe implementation package that should follow this audit?**
    Modify `SourceReferencePolicy.CreatePageUri` to explicitly allowlist `.wiktionary.org` and `.wikipedia.org`. It must require valid DNS hostname syntax, HTTPS only, escaped article title, no arbitrary URL supplied by provider data, and NO general `.wikimedia.org` acceptance without a separate approved use case. Fix license handling and update English and German disclosure strings in `SharedResource.resx` and `SharedResource.de.resx` to mention encyclopedic context, Wikipedia, normal network metadata, and local storage. Add robust tests for `SourceReferencePolicy` and resource localization.
15. **Which exact production files, localization resources, and tests would that package likely modify?**
    - `KnownFirst.Core/Preparation/SourceReferencePolicy.cs`
    - `Components/Shared/SourceDetails.razor`
    - `Resources/Localization/SharedResource.resx`
    - `Resources/Localization/SharedResource.de.resx`
    - `Services/Lexical/Wikipedia/WikipediaApiClient.cs` (potential provider change only if necessary after compliance review)
    - `KnownFirst.Tests/SourceReferencePolicyTests.cs` (new test file)
    - `KnownFirst.Tests/SourceDetailsTests.cs` or UI contract tests
    - `KnownFirst.Tests/LocalizationResourceTests.cs`

## Legal Disclaimer
This audit is not legal advice. The implementation must provide a traceable attribution and licensing representation consistent with the applicable Wikimedia and CC BY-SA reuse requirements. See https://foundation.wikimedia.org/wiki/Policy:Terms_of_Use/en and https://creativecommons.org/licenses/by-sa/4.0/legalcode. Final compliance confirmation regarding contributor attribution, concrete license identification, license URI/hyperlink, and 1200-character extract modification notice remains unresolved and requires deliberate review.

## Boundaries and Constraints
- Schema version remains 7.
- No migration was created.
- No production code was modified in this audit package.
- No live Wikimedia requests were made.
- No device, emulator, or visual validations were performed.
- Unresolved manual validation requirements remain for the eventual physical-device checks.
