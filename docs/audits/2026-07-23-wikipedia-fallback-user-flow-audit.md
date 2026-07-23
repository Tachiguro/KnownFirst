# Wikipedia Fallback User-Flow Audit

**Date:** 2026-07-23
**Audited master commit:** `d33cd80633f1ad1c25f76567136c642c419a23af`
**Scope:** Post-merge synchronization, canonical-state correction, and user-flow audit for the merged KnownFirst Wikipedia fallback (PR #11).

## PR #11 Merge Confirmation
PR #11 was successfully merged and the local `master` branch is synchronized with `origin/master`. The feature head `9aa4ef7` matches the merge commit `d33cd80` exactly in tree state (Tree ID: `97c1c25`).

## Files and Symbols Inspected
- `KnownFirst.Core/Preparation/SourceReferencePolicy.cs` (`CreatePageUri`)
- `Components/Pages/PrepareWords.razor` (Translation handling, Network error rendering, Provider and Meaning display)
- `Components/Shared/SourceDetails.razor` (`ProviderName`, `SourceProject`, `PageTitle`, `RevisionId`, `Attribution` rendering)
- `Resources/Localization/SharedResource.resx` (`Settings_OnlineConsentGranted`, `Source_Project`, `Prepare_NetworkFailure`, etc.)
- `Services/Lexical/LexicalEnrichmentService.cs` (Fallback execution, cache reloading)

## Answers to Audit Questions

1. **Is the merged automatic Wikipedia fallback already reachable through the existing user workflow?**
   Yes. `LexicalEnrichmentService.EnrichAsync` seamlessly triggers the fallback when Wiktionary yields a final `NotFound` outcome.
2. **Will a successful Wikipedia definition already appear in the existing preparation UI?**
   Yes. `PrepareWords.razor` binds to the returned definition seamlessly.
3. **Does the current online-lookup consent explicitly or implicitly authorize contacting Wikipedia?**
   Yes. The privacy consent explicitly names "Wikimedia" as the remote destination for dictionary lookups, covering both Wiktionary and Wikipedia.
4. **Does any existing consent or privacy text incorrectly mention only Wiktionary?**
   No. The privacy wording says "Wikimedia" and "Online dictionary" without isolating Wiktionary. 
5. **Are Wikipedia provider name, source project, page title, attribution, or revision information visible to the user?**
   Yes. `SourceDetails.razor` renders all of these values if they are present.
6. **Is the legally or operationally required attribution sufficiently represented?**
   **No.** `SourceReferencePolicy.CreatePageUri` explicitly requires `.EndsWith(".wiktionary.org")`. Consequently, `en.wikipedia.org` generates a null URI. `SourceDetails.razor` falls back to rendering the Wikipedia page title as plain text rather than a required clickable hyperlink.
7. **Can Wikipedia ever appear as a fabricated translation in the UI?**
   No. The schema-neutral Wikipedia result does not populate the translation field.
8. **How does DefinitionAndTranslation behave when Wikipedia returns a definition but no translation?**
   `PrepareWords.razor` renders the definition and displays an empty input box for the translation, expecting manual entry.
9. **Are fallback NotFound, transient, parse, and permanent outcomes presented correctly?**
   Yes. Wiktionary's transient/permanent errors propagate up to generic UI states (e.g., `Prepare_NetworkFailure`), avoiding leaking confusing provider-specific diagnostic noise.
10. **Does cached Wikipedia content reload through the complete UI-facing flow?**
    Yes. `LexicalEnrichmentService.EnrichAsync` requests the cache using the provider name passed in the fallback execution (`Wikipedia`), successfully retrieving any cached Wikipedia data.
11. **Is provider-selection UI actually necessary for the automatic fallback?**
    No. The automatic fallback runs behind the scenes and requires no dedicated UI toggle.
12. **Which exact user-facing gaps remain?**
    The only significant gap is the missing hyperlink generation for Wikipedia attribution.
13. **Which gaps are:**
    - **Must fix before calling the fallback user-ready:** Hyperlink generation for Wikipedia attribution.
    - **Should fix:** None.
    - **Deferred or unnecessary:** Provider-selection UI.
14. **What is the smallest safe implementation package that should follow this audit?**
    Modify `SourceReferencePolicy.CreatePageUri` to accept `.wikipedia.org` (and broadly `.wikimedia.org`) to generate valid `wiki/` hyperlinks for the attribution UI.
15. **Which exact production files, localization resources, and tests would that package likely modify?**
    - `KnownFirst.Core/Preparation/SourceReferencePolicy.cs`
    - `KnownFirst.Tests/SourceReferencePolicyTests.cs` (if it exists or similar unit tests)

## Findings Classification
- **Must:** Fix `SourceReferencePolicy.CreatePageUri` to support `.wikipedia.org` / `.wikimedia.org` attribution links.
- **Should:** N/A.
- **Deferred:** Dedicated provider-selection UI configuration is unnecessary for an automated fallback flow.

## Exact Evidence for Every Finding
- **Consent evidence:** `SharedResource.resx` line 640 uses "Wikimedia" and "Online dictionary lookup".
- **Attribution evidence:** `SourceReferencePolicy.cs` line 12 strictly requires `!sourceProject.EndsWith(".wiktionary.org")`.
- **UI render evidence:** `SourceDetails.razor` line 24 conditionally renders `<a href="@sourceUri">` only if `sourceUri` is not null.

## Boundaries and Constraints
- Schema version remains 7.
- No migration was created.
- No production code was modified in this audit package.
- No live Wikimedia requests were made.
- No device, emulator, or visual validations were performed.
- Unresolved manual validation requirements remain for the eventual physical-device checks.
