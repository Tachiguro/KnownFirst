# KnownFirst Architecture

## 1. Status and purpose

This document is the binding long-term architecture and product specification for KnownFirst.

KnownFirst is an offline-first vocabulary-learning application for Windows and Android. Its purpose is to let a user import a text, remove vocabulary they already know, automatically prepare the most relevant unknown vocabulary, and learn it through active recall and spaced repetition.

Product promise:

> Skip what you know. Learn what matters.

This document defines durable product and engineering rules. It must not contain temporary branch names, one-time Codex instructions, or milestone-specific stop conditions.

---

## 2. Product principles

KnownFirst follows these principles:

1. **Known vocabulary is removed from the learning workload.**
2. **The original imported text is never modified.**
3. **Only vocabulary the user marks Unknown enters preparation and learning.**
4. **Vocabulary frequency determines preparation priority.**
5. **Automatic preparation is the normal workflow.**
6. **Manual preparation is an optional fallback.**
7. **Original context is shown during review and learning.**
8. **Recognition and active spelling are separate learning skills.**
9. **Learning data is local by default.**
10. **External dictionary requests transmit only the minimum required data.**
11. **A text with no open learning vocabulary is not retained.**
12. **A fully completed text is deleted together with its no-longer-required learning data.**
13. **Schema-13 Learn `AlreadyKnown` preserves semantic vocabulary, LearningCards, and factual learning state/history through a word-level clean control. Destructive minimal-marker semantics apply only to the explicitly scoped legacy or review/Preparation disposition contracts (sections 6.4 and 22–23).**
14. **The user interface is workflow-driven, not a collection of unrelated pages.**
15. **All long-running work must remain resumable and transactional.**

---

## 3. Technology and platforms

KnownFirst uses:

- C#
- .NET 10 LTS
- .NET MAUI Blazor Hybrid
- Windows
- Android
- SQLite for local persistent data
- shared application logic in platform-independent projects where practical
- English code, comments, logs, tests, documentation, and commit messages
- localized English, German, and Russian user-interface resources

The application must remain responsive on desktop and mobile.

Android requirements include:

- safe-area handling
- working system Back behavior
- no status-bar overlap
- touch-friendly controls
- clipboard paste and long-press text controls

Windows requirements include:

- normal text selection
- keyboard clipboard commands
- right-click context menus in editable controls
- responsive desktop layout

---

## 4. Localization and settings

The UI supports English, German, and Russian, plus an explicit System selection that follows the device language.

The UI language is a separate axis from the imported-text source language and from the translation target language (see section 15). A UI language is never used to build a lexical request or cache key.

Rules:

- the UI language choices are System, English, German, and Russian
- the explicit `system` selection is persisted and re-resolved from the device language on every start
- a missing, malformed, or unsupported stored language value is treated as System rather than failing
- unsupported device languages fall back to English
- an explicitly selected language persists
- language changes apply immediately
- application reset restores the System selection and reapplies the supported device language
- theme choices are System, Light, and Dark
- theme changes apply immediately and persist
- the preparation limit ("New words per day") defaults to 10
- the preparation limit may be set to 5, 10, 20, 30, or 50
- 50 is the hard maximum for newly admitted genuinely-new vocabulary per logical learning day
- due reviews and already-learned New sibling cards never count against the new-vocabulary limit
- the learning timezone mode may be set to System or Explicit (defaults to System, resolving to the OS-configured timezone)
- the learning day cutoff defaults to 00:00 (minute-of-day 0, configurable)
- card direction defaults to Both directions
- the learning mode may be set to Reading, Typing, or Automatic, and defaults to Automatic

The learning mode selects the *interaction mode* used while learning. It is a separate axis from card direction; see section 20.

The exact user-facing setting names are defined in `docs/MVP_WORKFLOW.md`.

---

## 5. Core domain separation

KnownFirst must keep these concepts separate:

### 5.1 Document

A document is an imported original text and its metadata.

### 5.2 Sentence span

A sentence span is a coordinate range into the unchanged original document.

### 5.3 Vocabulary identity

A vocabulary identity represents one reviewable word, acronym, abbreviation, or technical term.

### 5.4 Surface form

A surface form is an encountered spelling or capitalization variant.

### 5.5 Occurrence

An occurrence is one actual appearance of a vocabulary identity at an exact position in a document.

### 5.6 Context

A context is a readable original sentence containing an occurrence.

### 5.7 Lexical result

A lexical result is dictionary reference data such as a definition, translation, word class, usage label, example, or acronym expansion.

### 5.8 Prepared learning item

A prepared learning item is user-approved learning content created for one Unknown vocabulary identity.

### 5.9 Learning card

A learning card is one test direction for a prepared learning item.

### 5.10 Scheduling state

Scheduling state determines when a learning card is due again.

These concepts must not be merged into a single table or duplicated across competing models.

---

## 6. Knowledge and workflow states

Vocabulary knowledge state and card scheduling state are separate.

### 6.1 Vocabulary knowledge state

The durable user-facing knowledge states are:

- `Unknown`
- `PermanentlyKnown`

The existing `Ignored` state is the minimal exact-identity marker used for migration compatibility, invalid-token exclusion, legacy data, and the confirmed **Do not learn** preparation action. It is not a normal visible initial-review choice and must never exclude related identities by stemming.

### 6.2 Preparation state

An Unknown vocabulary item may be:

- `Unprepared`
- `Preparing`
- `Prepared`
- `PreparationFailed`

### 6.3 Card scheduling state

A Schema-13 FSRS learning card may be:

- `New`
- `Learning`
- `Review`
- `Relearning`

`Suspended` and `Retired` are legacy physical card states, not current FSRS scheduling states. Current learning eligibility is controlled separately by word-level `AlreadyKnown` and sense-level `StopLearning`.

### 6.4 Permanently known

In the current Schema-13 Learn workflow, “Mark permanently known” is a confirmed word-level `AlreadyKnown` decision. It prevents normal learning eligibility for all cards of the word and removes incomplete queue work while preserving semantic vocabulary data, LearningCards, FSRS state, and factual history. Section 22 defines this preservation contract.

Known decisions during initial vocabulary review or Preparation are separate disposition paths; their legacy marker and cleanup behavior must not be applied to the Learn action. The future Vocabulary reversal workflow remains open under `KF-VOCAB-005`.

KnownFirst must not automatically claim that a word is permanently learned after a fixed number of days.

---

## 7. Document ingestion and preflight

Text import must be analyzed through a temporary or transactional preflight result before permanent storage.

The binding deterministic rules for sentence segmentation, token decisions, candidate grouping, encountered forms, context selection, coordinate validation, and DEBUG explainability are defined in [`WORD_ANALYSIS.md`](WORD_ANALYSIS.md).

The original text must be preserved exactly as entered:

- no trimming
- no punctuation changes
- no capitalization changes
- no number removal
- no line-ending rewriting
- no spelling correction
- no translation

Cleaning and normalization apply only to derived analysis data.

### 7.1 Exact duplicate

Use a deterministic content fingerprint.

When the exact same text was already accepted:

- create no new document
- create no sentence spans
- create no occurrences
- create no review session
- update no counters
- show a localized duplicate message

### 7.2 No open learning vocabulary

When every extracted reviewable identity is already PermanentlyKnown or excluded by a retained legacy/exclusion marker:

- do not retain the document
- do not create a review session
- do not change frequency statistics
- discard the temporary analysis result
- tell the user that all words are already known or that no open learning words were found

### 7.3 New vocabulary exists

When at least one genuinely new reviewable identity exists:

- atomically save the document
- save sentence spans
- save candidates and occurrences
- create one active vocabulary-review session
- navigate directly to review

The document must not be tokenized a second time merely to persist the accepted result.

### 7.4 Review finishes with no Unknown vocabulary

When all newly reviewed candidates are marked Known:

- retain only minimal PermanentlyKnown markers
- delete the document content
- delete sentence spans
- delete occurrences
- delete temporary review data
- do not create learning content

This cleanup is transactional.

---

## 8. Sentence and occurrence coordinates

All coordinates refer to the original .NET string.

Required invariants:

```csharp
document.Content.Substring(sentence.StartPosition, sentence.Length)
```

returns the exact original sentence, and:

```csharp
document.Content.Substring(occurrence.StartPosition, occurrence.Length)
```

returns the exact original surface form.

A repeated word creates:

- one vocabulary candidate for review
- multiple occurrence rows
- an occurrence count equal to the actual number of appearances

Repeated occurrence rows are not duplicate vocabulary identities.

---

## 9. Tokenization and vocabulary identity

Tokenization must be deterministic, Unicode-safe, and platform-independent.

Preserve:

- German umlauts
- ß
- accented Latin characters
- Greek
- Cyrillic
- original capitalization
- technical punctuation when part of a recognized token

Exclude:

- whitespace
- punctuation-only values
- symbol-only values
- URLs
- email addresses
- standalone numbers

Retain examples such as:

- AI
- IT
- IP
- HTML
- OAuth2
- IPv6
- SHA-256
- CVE-2026-12345

Two explicit technical families are canonicalized during deterministic analysis:

- `CVE-YYYY-NNNN...` becomes the case-sensitive `CVE` acronym identity while the occurrence retains the complete identifier, year, identifier number, and exact coordinates.
- `SHA-1`, `SHA-224`, `SHA-256`, `SHA-384`, and `SHA-512` become the case-sensitive `SHA` acronym identity while each occurrence retains its variant and exact coordinates.

These are closed family rules, not generic suffix removal. `IPv6` and `OAuth2` remain unchanged unless a later explicit, tested rule is added. DEBUG analysis records a human-readable reason for each extraction and grouping decision.
The explicit bare acronyms `CVE` and `SHA` use the same identities as their supported family forms, so an imported family instance does not create a second learning identity beside its acronym.

Token kind participates in vocabulary identity:

- `Word`
- `Acronym`
- `Abbreviation`
- `TechnicalTerm`

These pairs remain distinct:

- IT / it
- US / us

Ordinary capitalization variants may be grouped conservatively:

- Network
- network
- NETWORK

One explicit source-language rule is supported for English imports: `I`, `me`, and `my` share the canonical `I` vocabulary identity. The rule is applied only when the imported-text language is English. Every encountered surface form and exact source coordinate remains unchanged.

Broad stemming is prohibited in the initial architecture. False merges are worse than temporary duplicates.

Do not automatically merge:

- network
- networking
- networked

unless a deterministic language rule is explicitly implemented and tested.

---

## 10. Review model

Vocabulary review asks only:

> Do you already know this word or acronym?

Visible actions:

- Known
- Unknown
- Undo previous decision

There is no visible Ignore action in the normal workflow.

Every decision is persisted immediately.

An unfinished review:

- is the only globally blocking workflow state
- blocks another import
- blocks preparation
- blocks learning
- remains resumable after navigation, application restart, Android backgrounding, and Windows restart
- allows Settings
- allows Discard import with destructive confirmation

After leaving Settings, workflow routing returns to the active review.

On narrow/mobile layouts, the app bar is the single page title and the duplicate page heading/back-to-home control is hidden. Review keeps progress, candidate, one highlighted context, context navigation, and the two decisions prominent. Token kind, encountered forms, and occurrence count are collapsed under Details; DEBUG analysis remains separate. The bottom workflow action area owns the primary Known/Unknown controls alongside the trailing destructive whole-import discard action, with guarded submission, a reserved saving state, reachable Undo, and matching bottom content padding. Discard invokes an explicit destructive confirmation that temporarily suppresses competing review actions within the workflow action region. The operation discards the entire unfinished active import without changing persisted review semantics, and narrow/mobile layouts stack the action structure responsively while preserving these semantic roles.

---

## 11. Frequency model

Frequency is the number of actual accepted occurrences of Unknown vocabulary.

Rules:

- each real occurrence counts
- duplicate sentence text does not reduce occurrence count
- exact duplicate imports change no counts
- changed or reordered texts containing no genuinely new vocabulary change no counts
- an accepted document containing at least one genuinely new vocabulary item may update counts for existing Unknown vocabulary found in that accepted document
- PermanentlyKnown vocabulary is not counted
- legacy ignored/excluded vocabulary is not counted

Preparation priority uses:

1. highest accepted occurrence count
2. earliest first-seen timestamp
3. canonical term alphabetically as a deterministic final tie-breaker

---

## 12. Representative contexts

During review, occurrences continue to reference the original document.

Before a vocabulary item becomes a prepared learning item, KnownFirst stores up to three independent context snapshots.

A context snapshot contains at least:

- learning-item ID
- exact original sentence text
- target start within the snapshot
- target length
- normalized comparison fingerprint
- optional source document title
- creation timestamp

Required invariant:

```csharp
snapshot.Text.Substring(snapshot.TargetStart, snapshot.TargetLength)
```

equals the displayed target surface form.

Imported content must never be rendered as unsafe raw HTML.

### 12.1 Duplicate context handling

Identical sentence contexts are retained only once per vocabulary item.

For comparison only:

- trim outer whitespace
- normalize line endings
- collapse repeated whitespace
- apply Unicode normalization without removing diacritics

Keep the first exact original sentence for display.

Example:

```text
Security is important.
Security is important.
Security protects information.
```

Expected:

- occurrence count: 3
- unique context snapshots: 2

---

## 13. Lexical-enrichment architecture

Automatic preparation uses a provider chain with authoritative fail-closed consent enforcement.

Interfaces:

```csharp
ILexicalEnrichmentService
IDictionaryLookupProvider
IAcronymExpansionProvider
ILexicalCacheRepository
IOnlineLookupAuthorizationGate
```

Provider priority:

1. explicit acronym expansion from the imported text
2. local lexical cache (checked before any network access or authorization check)
3. online Wiktionary provider (requires active online lookup authorization)

A lexical result is structured data, not one unstructured HTML or text blob.

It supports where available:

- source language
- lookup mode (`Definition`, `Translation`, or `DefinitionAndTranslation`)
- nullable target language
- canonical lookup term
- display term
- token kind
- provider
- part of speech
- acronym expansion
- definitions
- translations
- usage labels
- examples
- provider name
- source project
- source page title
- source revision ID
- attribution and license information
- lookup timestamp
- ranking or confidence metadata

The Import Text selector currently exposes only `Definition` or `Translation`. `DefinitionAndTranslation` remains a readable persisted and archived model value, so existing rows, preparation state, and portable archives that already use it continue to be processed unchanged. It is not a currently selectable import option.

### 13.1 Provider-supported form-to-lemma resolution

The provider parser separates direct lexical senses from grammatical form relations. When the originally queried page has at least one suitable direct sense, enrichment keeps the queried canonical term, ranks only the direct senses, and does not redirect merely because a form relation is also present. This keeps ordinary modern-English `data` on the `data` entry when that page supplies direct senses.

Only a form-only result may follow an explicit provider relation such as singular, plural, third-person singular, past tense, past participle, present participle, comparative, or superlative **of** a named base lemma. The base lemma is resolved through the same cache/provider chain and supplies the definition or translation used for learning. Thus form-only `systems`, `risks`, and `protects` may resolve to `system`, `risk`, and `protect` respectively.

The redirect chain has a visited-lemma set and a fixed maximum depth. A loop or depth overflow is a permanent lookup failure. No stemming or inferred suffix removal is permitted: for example, `risky`/`risk`, `protection`/`protect`, and `networking`/`network` remain separate without explicit provider evidence.

Prepared content stores the canonical learning term, encountered surface form, and grammatical relationship. Its context remains the unchanged original sentence with exact target coordinates.

Dictionary reference data and personal learning state remain separate.

### 13.2 Fail-closed online lookup consent architecture

KnownFirst implements a multi-layer fail-closed architecture to guarantee that external lexical network requests are strictly forbidden unless the user has explicitly granted online lookup consent:

1. **Persistent Authority:** `IAppSettingsService` (`AppSettingsService`) is the authoritative source for persisted consent state (`HasOnlineLookupConsent`), backed by device preferences. It emits `OnlineLookupConsentChanged` notifications with duplicate suppression upon grant, revocation, or full reset.
2. **Authorization Epoch Gate:** `IOnlineLookupAuthorizationGate` (`OnlineLookupAuthorizationGate`) manages authorization state and monotonic epochs. Granting consent opens a new authorization epoch with a fresh `CancellationTokenSource`. Revoking consent immediately cancels the active epoch's `CancellationTokenSource` and closes the gate. A subsequent re-grant begins a new epoch; cancelled tokens are never revived.
3. **Transport Authorization Gate:** `OnlineLookupAuthorizationHandler` is registered as a delegating handler in the `HttpClient` pipeline for lexical API clients (`IWikipediaApiClient`). Every outbound HTTP request invokes `EnsureAuthorized()` immediately before transmission, throwing `InvalidOperationException` if consent is absent or revoked.
4. **Service-Level Fail-Fast:** `LexicalEnrichmentService` checks the local lexical cache first. On a cache miss, it validates `EnsureAuthorized()` before initiating provider network queries, Wikipedia definition fallbacks, or lemma redirect lookups.
5. **Orchestration & Prefetch Protection:** `PreparationService` verifies authorization before starting an `AutomaticOnline` session, before executing candidate lookups, and before starting background prefetch. Active prefetch tasks are linked to the current authorization epoch token. When consent is revoked, in-flight prefetch is cancelled and any transient prefetched result from a prior epoch is discarded.
6. **UI Defense-in-Depth:** `PrepareWords.razor` disables the Automatic Online method card when consent is OFF, displays dedicated blocked-candidate notices for existing batches, and disables retry actions. The UI is a defense-in-depth presentation layer; security and privacy invariants are enforced at the transport and service boundaries.

### 13.3 Revocation and cancellation semantics

- **In-flight network requests:** Once bytes have been transmitted over the physical network, the server receives the request. However, revocation immediately signals cancellation to all in-flight authorized HTTP requests and background prefetch tasks via the epoch `CancellationTokenSource`.
- **Subsequent requests:** No new external lexical network request may begin after revocation occurs.
- **Epoch isolation:** Re-granting consent creates a brand-new authorization epoch. Previously cancelled cancellation tokens remain cancelled and cannot leak into or authorize new requests.
- **Failure classification:** Revocation-driven cancellation throws `OperationCanceledException` / `InvalidOperationException` and is handled cleanly as an intentional cancellation; it is never persisted to SQLite as a permanent candidate failure or reported as an ordinary provider network error.

### 13.4 Cache and local data integrity

- **Local cache availability:** Local SQLite lexical cache hits remain fully accessible and usable offline regardless of whether online lookup consent is enabled or disabled.
- **Non-destructive revocation:** Revoking online lookup consent does not delete or invalidate cached lexical entries, does not delete persisted preparation items or learning cards, and does not corrupt or delete existing preparation sessions.
- **Batch preservation:** An active `AutomaticOnline` session paused by consent revocation retains all completed, pending, and skipped candidate states. When consent is re-granted in Settings, the existing batch resumes safely.

### 13.5 Prefetch safety

- Prefetching does not start while online lookup is unauthorized.
- Active prefetch tasks link to the `CurrentEpochToken` of `IOnlineLookupAuthorizationGate`.
- Revoking consent cancels active prefetch tasks immediately.
- Transient prefetch results produced under a revoked epoch cannot be consumed under a new epoch.
- When consent is re-granted, fresh prefetch operations may proceed.
- Transient prefetch invalidation never deletes or mutates persisted candidate or session entities.

---

## 14. Acronym expansion

Before any network lookup, search the unchanged imported text for explicit patterns:

- `Long Form (ACRONYM)`
- `ACRONYM (Long Form)`

Examples:

- Information Technology (IT)
- Multi-Factor Authentication (MFA)
- ISMS (Information Security Management System)

Rules:

- acronym matching is case-sensitive
- preserve the original long-form spelling and punctuation
- prefer an explicit expansion from the imported text over an external result
- do not invent an expansion
- do not treat every uppercase word as a confirmed acronym
- an external provider may still supply definition and translation data

---

## 15. Wikimedia and Wiktionary integration

The first online provider uses read-only MediaWiki API requests against the relevant Wiktionary project.

Normal dictionary lookup must not require the user to create or paste an API key.

Requirements:

- use .NET `HttpClient`
- use a compliant, descriptive KnownFirst User-Agent
- transmit only the selected term and required language information
- never transmit the complete document
- never transmit context sentences
- never transmit learning history or analytics
- use at most two concurrent requests
- support cancellation and timeout
- respect `Retry-After`
- handle HTTP 429 and transient 5xx responses
- use limited retry with exponential backoff
- never retry indefinitely
- parse only the relevant language section
- use a maintained HTML parser rather than one large fragile regular expression
- retain source attribution and revision information
- never fabricate a missing definition or translation

Every request carries its lexical languages explicitly. `SourceLanguage` is the imported-text language. `Definition` requires a null target and requests a definition in the source language. `Translation` and `DefinitionAndTranslation` require a supported target language different from the source. UI culture is never consulted when building a lexical request or cache key. Ordinary English `Word` tokens use a lowercase canonical lookup term while their exact display/context forms remain unchanged; acronym and case-sensitive technical token kinds retain case (`IT` never becomes `it`).

Lookup results use the explicit outcomes `Success`, `NotFound`, `TransientFailure`, `PermanentFailure`, and `ParseFailure`. Retry is offered only for `TransientFailure` when online lookup consent is active. A successful result, a missing entry, a parse failure, a permanent failure, and lookup without active consent do not present a retry action.

Online lookup consent is governed by first-run onboarding (Step 4) and Settings. Prepare Words does not collect or activate consent contextually.

The disclosure states:

- no application data is sent to the KnownFirst developer
- the selected term and language information are sent directly to Wikimedia
- Wikimedia receives ordinary network metadata such as IP address and User-Agent
- retrieved lexical data and personal learning data are stored locally

After onboarding, Settings is the sole authority for granting or revoking online lookup consent.

The application must start and remain usable without network access.

---

## 16. Local lexical cache

Successful lexical results are cached in SQLite.

A stable cache key includes at least:

- source language
- normalized canonical lookup term
- lookup mode
- target language or an explicit null marker
- token kind where relevant
- provider
- provider schema version

The key format is versioned. Schema version 6 invalidates legacy lexical-cache rows whose keys omit request mode or target language, preventing old results from crossing language or lookup-mode boundaries.

The cache stores:

- structured definitions
- translations
- acronym expansions
- word class and labels
- examples
- source attribution
- source revision
- fetch timestamp

Rules:

- cache is checked before network
- cached results work offline
- identical results are not duplicated
- failed results do not create fabricated cache entries
- user learning status does not modify reference-data meaning
- unreferenced cache entries may be pruned by storage maintenance according to a future size policy

---

## 17. Preparation batches

Preparation selects only:

- Unknown
- Unprepared
- resolved review decisions
- vocabulary not already represented by a prepared learning item

Exclude:

- PermanentlyKnown
- legacy ignored/excluded
- already prepared
- unresolved active review candidates

The daily new-word target limits distinct genuinely-new Words admitted into Learning for the current learning day; Preparation itself is uncapped by this target. Users may prepare more vocabulary than can be admitted in a single day, and prepared items remain durably available in the backlog for future admission.

Recommended default for fresh installations: 5
Supported configured range: 1..50 (legacy grandfathered installations retain their established value, such as 10)

Due reviews, learned sibling cards, and Again repetitions never count against this limit. After a successful Preparation acceptance is durably committed, Preparation queries Learning readiness: if still-open genuinely-new daily demand is positive and fully satisfiable by eligible prepared backlog, Preparation automatically transitions to `/learn` without loading another candidate. When daily admission capacity is exhausted, readiness evaluates to false, allowing continued preparation and unblocked re-entry without redirect loops.

Preparation supports:

- Automatic online (recommended default; enabled when online lookup consent is granted; disabled with explanatory notice and Settings link when consent is OFF)
- Manual (always available)

Automatic is the primary workflow when consent is granted. Manual is the offline and custom-entry fallback.

When consent is OFF, starting a new Automatic online session is blocked. For existing Automatic online sessions paused while consent is OFF, unresolved candidates display a dedicated blocked-online candidate state offering Settings navigation and Manual entry while keeping local candidate dispositions (Mark known, Exclude from learning, Skip for now) and End preparation active.

For every automatic result, the user can:

- accept
- choose an alternative meaning
- edit
- retry only when the explicit lookup outcome is recoverable and consent is enabled
- switch to manual
- skip for now
- mark as known after confirmation
- exclude the exact identity with **Do not learn** after confirmation

Automatic preparation must never require typing when a usable result was found.

### 17.1 Manual preparation and acceptance architecture

Manual preparation accepts user-entered definitions or translations without requiring an online lookup result:

- **Candidate Lookup Context:** Context is authoritatively derived from the owning document of the candidate's first context occurrence (`CandidateLookupContext`), defining `LookupMode`, `TargetLanguage`, and `ExplanationLanguage`. If no document context exists, it falls back deterministically to Definition mode.
- **Manual Input Normalization:** Manual input is normalized strictly by lookup mode: Definition mode sets `Translation = null`; Translation mode sets `Definition = string.Empty`. Legacy `DefinitionAndTranslation` remains supported as a bounded compatibility mode.
- **Provenance Stripping:** Manual entries carry no fabricated provider data: `SelectedMeaningId`, provider name, source project, source page title, source revision, and attribution are cleared or empty. Candidate payload `ResolvedProviderMeaningIndexes` remains unchanged without claiming synthetic provider index resolution.
- **Exact Manual Semantic Reuse:** When accepting a manual input for a vocabulary identity with existing senses, `TryFindExactManualMeaning` compares candidate `ExactMeaningVariantIdentityPolicy` against existing manual meanings under matching `SemanticMeaningIdentity`. An exact match reuses the existing `Sense` and `Meaning` rather than allocating duplicate entities or duplicate `LearningCards`. Genuinely distinct manual meanings remain split under the split-not-guess principle.
- **Evidence Linkage:** Candidate frozen evidence and retained German derived whole-compound evidence link to the reused or newly created `Sense`/`Meaning` without fabricating occurrences.
- **Transactional Save and Progression Separation:** `AcceptSchema8` transactionally commits the Sense, Meaning, ContextSnapshot, LearningCard, and `Word.PreparationState = Prepared` state before next-item loading occurs. Next-candidate retrieval failures are isolated from acceptance, preventing false save failure reports and ensuring progression-only retry safety.

### 17.2 Preparation dispositions and batch lifecycle

Preparation disposition semantics are transactional:

- **Mark as known** (status `WordStatus.Known`) stores the minimal PermanentlyKnown marker, creates no learning cards, removes obsolete occurrence/context/frequency data, updates cleanup eligibility, and advances one candidate.
- **Do not learn** (status `WordStatus.Ignored`) stores the minimal exact `Ignored` marker, creates no learning cards, excludes no related identity, removes obsolete preparation data, and advances one candidate.
- **Skip for now** completes only the current batch candidate, leaves the word Unknown and Unprepared for later batches, and cannot cycle within the same session.

Back navigation, Home navigation, application suspension, and application restart pause an active preparation session so Home continues to offer **Continue preparation**. **End preparation** is a neutral/secondary workflow action in the bottom action bar: it marks the batch ended, keeps accepted prepared items and their learning cards, returns unresolved and skipped words to Unknown/Unprepared, clears transient result/error state, and leaves no active preparation method. While confirmation is open, competing disposition actions are suppressed. The next Prepare Words entry starts with the Automatic/Manual method choice, and later selection must not duplicate already prepared vocabulary.

The next active candidate is selected with a bounded ordered query. Accepting an already loaded result performs no lexical request. At most one following lexical result is prefetched; the prefetch is deduplicated, cancellable, and consumed only for its matching candidate.

The meaning chooser is an accessible bounded dialog/listbox rather than a native single-line select. Closed previews are limited to two visual lines and about 160 characters; alternative previews use about 240 characters with an accessible full-text expansion. Preview shortening never mutates persisted text. The picker constrains every child to the viewport, wraps unbroken text, respects safe areas, closes on Escape or Android Back, and restores focus to its invoking control.

---

## 18. Meaning ranking

The first version uses deterministic local ranking.

Ranking order:

1. explicit acronym expansion from imported text
2. exact token-kind match
3. part-of-speech match when available
4. overlap between normalized context words and dictionary definition/example words
5. provider order as final fallback

Do not claim perfect word-sense disambiguation.

When multiple meanings are similarly plausible, display a localized warning and let the user choose another meaning.

The complete context is not sent to an external AI service.

---

## 19. Prepared learning content

A prepared learning item stores at least:

- vocabulary identity
- selected meaning identity or stable snapshot
- source language
- explanation language
- displayed term
- encountered surface form when it differs from the learning term
- explicit grammatical relationship when lemma resolution occurred
- token kind
- acronym expansion, nullable
- selected translation, nullable
- selected definition, nullable
- optional dictionary example
- up to three context snapshots
- source attribution
- source project, page title, and revision ID
- prepared timestamp

For acronyms, answer order is:

1. acronym expansion
2. translation when relevant
3. definition

A prepared item must survive application restart.

Manual acceptance requires at least one useful answer: acronym expansion, translation, or definition. The canonical term and encountered form are read-only in the editor; note and accepted aliases remain optional. Acceptance is one transaction, rejects double submission, advances once, and restores focus/scroll position to the next item.

Normal preparation and learning cards show source metadata through a collapsed **Source details / Quelldetails** control. Expansion retains the provider/project, page title and supported link, revision ID, attribution, and license reference. Learning uses the compact form of the same control.

---

## 20. Learning-card directions and interaction modes

Card direction and interaction mode work together to govern card presentation, interaction flow, and progression. Direction determines *which* card exists, what serves as prompt versus answer, and how it is scheduled. Interaction mode decides *how* the user answers the card that is presented.

### 20.1 Card direction

Supported directions:

- `TermToMeaning`
- `MeaningToTerm`

Default setting:

- Both directions

One vocabulary identity counts as one new vocabulary item even when it generates two cards.

Each direction has independent scheduling state.

Direction-specific presentation and interaction semantics (KF-LEARN-003):

- `TermToMeaning`: The source term is presented on the front as the prompt, and the requested meaning content (definition, translation, example) is revealed on the back as the answer. `TermToMeaning` is always semantically a `Reading` (reveal-and-rate) interaction across both UI and backend; typing of definitions/translations is never required, even when configured `LearningMode` is `Typing` or Automatic progression is in the typing stage. Example sentence context targets remain unmasked (`HideTarget="false"`). Factual review history records `WasTypedAnswer = false`.
- `MeaningToTerm`: Meaning content is presented on the front as the prompt, and the source term is the answer. `MeaningToTerm` supports both `Reading` (reveal-and-rate, `WasTypedAnswer = false`) and `Typing` (spelling production of the source term, `WasTypedAnswer = true`). Example sentence context targets are masked (`HideTarget="true"`).

Context navigation belongs directly below the displayed context sentence for both directions; it is not placed after the complete answer and rating area.

### 20.2 Interaction mode

The resolved interaction mode is one of:

- `Reading` — the user reveals the answer and self-rates the recall
- `Typing` — the user types the expected answer and the application validates it locally

Resolution rules:

- for `TermToMeaning` cards, the interaction mode is always `Reading`, unconditionally overriding user or automatic typing modes to prevent requiring typing long definitions or translations
- for `MeaningToTerm` cards:
  - learning mode `Reading` resolves to the Reading interaction
  - learning mode `Typing` resolves to the Typing interaction
  - learning mode `Automatic` resolves from the card's own stored interaction progress

Schema-13 interaction progress is persisted per learning card and required answer variant, not as one vocabulary-wide state shared by both card directions. Each progress record holds the current interaction mode and interaction counters. Legacy mastery-review extension behavior is historical compatibility behavior, not current Schema-13 authority.

Every persisted rating durably records the interaction that was actually presented: whether the answer was typed and whether it was correct. After each persisted rating, the card's progress is recomputed from that card's complete stored review history. This recomputation happens regardless of whether the current setting is `Reading`, `Typing`, or `Automatic`. A fixed mode overrides only which interaction is presented to the user; it does not freeze or isolate the replay-owned progress. Selecting `Automatic` later may therefore resolve from reviews recorded while a fixed mode was selected. Changing the setting never rewrites already recorded review events.

Automatic transition rules apply to a `MeaningToTerm` card's required answer variant:

- progress starts in the Reading interaction
- in a resolved Reading interaction (KF-LEARN-004 / KF-LEARN-005), only `Good` and `Easy` count as successful recall to advance the progression counter toward Typing (two consecutive Good/Easy reviews advance to Typing); `Hard` indicates effortful recall and leaves the counter unchanged; `Again` resets the recall counter to zero.
- FSRS scheduling receives and persists all four ratings normally, completely distinct from interaction progression counters.
- after two consecutive successful recalls the progress switches to the Typing interaction and its typing-failure counter is reset
- in a resolved Typing interaction, a correct typed answer increases the typing-success counter and resets the typing-failure counter; an incorrect typed answer resets the typing-success counter and increases the typing-failure counter
- after two consecutive incorrect typed answers the progress returns to the Reading interaction and all three counters are reset
- all counters are bounded at two, so the state cannot grow without limit

The former 365-day mastery-review, automatic retirement, queue-pruning, and Sense mastery-rollup rules belong to the legacy scheduler model and are retained only as historical behavior. They are not binding Schema-13 outcomes; current scheduling and replay are governed by FSRS-6, while interaction progress remains separate. No replacement mastery semantics are inferred here.

Historical retirement was decided per card and could prune incomplete queue rows; those rules are not current Schema-13 authority. Current FSRS scheduling and factual review replay do not revive `Mastered`/`Retired` as authoritative learning outcomes.

Neither elapsed time nor interaction progress creates permanent knowledge. The explicit Schema-13 Learn permanent-known decision in section 22 is a separate, non-destructive clean control; it preserves the graph and factual history.

### 20.3 Reading interaction

Front:

- for `TermToMeaning`: source term, unmasked context sentence, and occurrence count where useful
- for `MeaningToTerm`: meaning prompt (translation/definition), masked context sentence

Back after reveal:

- for `TermToMeaning`: acronym expansion when applicable, translation, definition, optional example, source
- for `MeaningToTerm`: source term, full unmasked context, and pronunciation/details

The user self-rates the recall. An answer must be revealed before a rating is accepted.

### 20.4 Typing interaction (MeaningToTerm only)

Front:

- definition and/or translation
- optional context without revealing the target term

The user types the expected word or acronym.

The application compares the answer locally.

Comparison rules:

- Unicode normalization
- trim outer whitespace
- compare against canonical answer and accepted aliases
- preserve meaningful punctuation
- acronyms are case-sensitive
- German noun capitalization is meaningful
- case tolerance for ordinary words may be language-aware and tested
- display a readable character-level difference for incorrect spelling

A wrong typed answer is treated as `Again`.

A correct typed answer allows `Hard`, `Good`, or `Easy`.

Long free-text definitions are never graded by AI in the MVP.

---

## 21. Spaced-repetition architecture

### 21.0 Active scheduler and FSRS-6 foundation boundary

KnownFirst maintains a strict separation between the active production scheduler composition and the available algorithm foundations:

- **Current Active Production Scheduler:** The application runtime learning workflow uses `IFsrs6SchedulingService` / `Fsrs6SchedulingService` over Schema-13 persistence; `MauiProgram` registers the learning runtime through `AddKnownFirstLearningRuntime()`.
- **Available FSRS-6 Core Engine Foundation:** `KnownFirst.Core.Learning.Fsrs6` provides a pure, deterministic, platform-neutral FSRS-6 engine and replay foundation (`Fsrs6Scheduler`, `Fsrs6Replayer`, `Fsrs6Card`, `Fsrs6Parameters`, `Fsrs6ReviewEvent`, `Fsrs6CardState`), governed by [ADR-0008](decisions/ADR-0008-in-tree-fsrs6-core-scheduling-foundation.md). The Core engine is completely independent of MAUI, SQLite, DI, JSON, network, and platform APIs.
- **Completed Production Integration:** `KnownFirst` references `KnownFirst.Application`; `KF-FSRS-003` completed runtime composition over Schema-13 card projections and append-only factual review logs. Core value types and replay contracts remain free of database identity and persistence concepts. Fresh genuinely empty production databases bootstrap directly to Schema 13; existing Schema 1–12 databases fail closed without automatic migration, reset, or mutation.

### 21.1 Historical initial scheduler (superseded by FSRS-6)

The pre-cutover runtime used this abstraction:

```csharp
ISpacedRepetitionScheduler
```

Its production implementation was:

```csharp
SimpleSpacedRepetitionScheduler
```

That production authority has been replaced by `IFsrs6SchedulingService` / `Fsrs6SchedulingService`. The initial scheduler and interval/ease rules below are retained solely as historical architecture and compatibility context for later `KF-CLEANUP-001`; they do not govern Schema-13 scheduling.

It used an injectable clock.

Its per-card state included:

- card ID
- state
- due-at UTC
- interval in days
- ease factor
- successful review count
- lapse count
- last reviewed UTC
- last rating
- created UTC
- updated UTC

No Skip rating exists.

Available ratings:

- Again
- Hard
- Good
- Easy

German:

- Nochmal
- Schwer
- Gut
- Einfach

#### Historical interval/ease rules

Default ease factor: 2.5  
Minimum ease factor: 1.3

For a New card:

- Again: due in 10 minutes; state Learning
- Hard: due in 1 day; state Review
- Good: due in 3 days; state Review
- Easy: due in 7 days; state Review

For an existing Review card:

- Again:
  - lapse count +1
  - ease factor -0.20, minimum 1.3
  - due in 10 minutes
  - state Relearning
  - successful progression restarts after relearning
- Hard:
  - ease factor -0.15, minimum 1.3
  - interval = max(1 day, round(current interval × 1.2))
- Good:
  - interval = max(current interval + 1 day, round(current interval × ease factor))
- Easy:
  - ease factor +0.15
  - interval = max(current interval + 2 days, round(current interval × ease factor × 1.3))

Review intervals continue to grow. They do not end automatically after 7 or 14 days.

### 21.2 In-session Again repeat and queue semantics

1. **Scheduler contract:**
   - Rating a card `Again` computes and persists the FSRS-6 transition, its resulting due time/state, and the factual review event. Legacy interval/ease formulas do not govern this transition.
   - The scheduler-owned `DueAtUtc` is stored durably in `FsrsCardStates` and governs future session eligibility.

2. **Deterministic tail-repeat queue behavior:**
   - Every successfully committed explicit user `Again` rating appends exactly one new repeat queue row at the deterministic tail of the active learning-session queue (`IsAgainRepeat = true`).
   - Selecting `Again` on an existing `Again` repeat appends another repeat row to the queue tail.
   - There is no arbitrary one-repeat-per-card or one-repeat-per-session cap.
   - Repeats are strictly demand-driven: no repeat queue row is generated without an explicit committed user `Again` action.

3. **Active-session presentation exception:**
   - An incomplete queue row marked as an `Again` repeat (`IsAgainRepeat = true`) is eligible for presentation within its owning active session even when the card's persisted `DueAtUtc` is still in the future.
   - This presentation exception is strictly scoped to incomplete `Again` repeat queue rows within that active session.
   - Ordinary future-due non-repeat `Learning`, `Review`, or `Relearning` cards remain suppressed outside their due windows.

4. **Session progress counters:**
   - `CompletedCards` tracks the number of completed queue rows.
   - `TotalCards` reflects the total queue-row count of the session and increases dynamically whenever an `Again` action appends a new repeat row.
   - `AgainCount` tracks total committed `Again` ratings in the session.

5. **Daily-new budget independence:**
   - `Again` repeat rows belong to already-admitted vocabulary and never consume, recycle, replace, or reopen daily new-word admission grants ($N$).

6. **Durable session persistence and resume:**
   - Pending repeat queue rows are persisted transactionally as active-session state in the database.
   - Unfinished repeat rows survive leaving and re-entering the learning workflow, application restarts, and service recreation.

The FSRS-6 core in `KnownFirst.Core`, governed by [ADR-0008](decisions/ADR-0008-in-tree-fsrs6-core-scheduling-foundation.md), is consumed by the live production runtime. Schema-13 persistence, Archive V3 infrastructure, and runtime cutover are complete; Vocabulary workflows and later legacy cleanup retain separate backlog ownership.

### 21.3 Daily new-word budget, learning-day boundaries, and Bridge state

1. **Daily New-Word Budget ($N$):**
   - The daily new-word limit setting governs the daily new-word admission budget ($N \in \{1, 5, 10, 20, 30, 50\}$, recommended default 5, configurable range $1..50$, with legacy grandfathered installations retaining their established setting such as 10).
   - $N$ is enforced as a hard daily maximum of distinct genuinely-new `WordId`s per logical learning day. Preparation itself is uncapped by $N$.
   - Prospective fresh admission requires a distinct never-learned Word backed by at least one valid, queueable New LearningCard. Bare, unresolved, Ignored, or cardless Words never consume fresh daily grants.
   - One `WordId` consumes exactly one slot regardless of directions (`TermToMeaning`, `MeaningToTerm`), senses, meaning count, answer variants, or card count.
   - "Genuinely new" means no persisted genuine `LearningReview` / rating exists for any card of that `WordId`. Queueing, rendering, reveal, typing checks, and `LearningDayGrant` evidence do not count as learning.
   - Learning owns admission and readiness calculations. Readiness is non-admitting (performing no grant or session mutations) and evaluates whether still-open daily demand is satisfiable by eligible prepared backlog. Preparation consumes the readiness boolean post-commit to auto-transition to `/learn`, while zero remaining demand yields false, preserving intentional Preparation re-entry.
   - Admitted words receive immutable `SlotOrdinal` assignments ($0, 1, \dots, N-1$). Reducing $N$ preserves existing queue rows, grants, and order, but restricts presentation to items with `SlotOrdinal < N`. Deferred items remain durably persisted. Raising $N$ admits additional candidates into higher slot ordinals.
   - Same-day ratings or marking an admitted word Permanently Known never reopens or recycles a slot on the same day.

2. **Learning-Day Boundaries & ActiveDay Freeze:**
   - The learning time zone is resolved from `LearningTimezoneMode` (`System` or `Explicit`). `System` resolves to the device OS-configured timezone.
   - The learning day cutoff defaults to `00:00` (minute-of-day 0, configurable).
   - The active budget day freezes its effective timezone, cutoff, start timestamp, and end timestamp until transition. Configuration changes do not retroactively alter the active day.

3. **Bridge Phase:**
   - When the active day ends, the next regular boundary under the requested configuration is calculated.
   - Exact boundary equality transitions immediately to the next `ActiveBudgetDay` with no Bridge.
   - If the next regular boundary is in the future, the system enters `Bridge` phase.
   - Bridge grants 0 new-word budget and blocks genuinely-new cards from being presented, while due reviews and already-learned New sibling cards continue outside $N$.

4. **Active-Session Rollover Reconciliation:**
   - Active sessions surviving day rollover consume the new day's slots with carry-over genuinely-new words first.
   - If carry-over count $K < N$, remaining capacity admits fresh candidates. If $K \ge N$, no fresh candidates are admitted and excess carry-over grants remain durable but deferred.
   - Deduplication inspects incomplete queue representation rather than historical completed rows. Completed rows may recur when due again; incomplete ordinary/Again rows prevent duplicate appends.

---

## 22. Permanent-known action and learning completion

KnownFirst does not equate a fixed interval with permanent knowledge.

The current Schema-13 Learn action **Mark permanently known** requires explicit confirmation and persists a word-level `AlreadyKnown` decision. It is never inferred from an interval, interaction counter, or legacy mastery state.

The transaction:

- saves `WordLearningControls` and preserves the original decision timestamp on repeated markings;
- removes only incomplete `LearningSessionCards` for the word and normalizes affected session totals/status, removing an empty session only when it has neither queue rows nor reviews;
- prevents normal learning eligibility for every card of the word through the clean-control contract;
- preserves the semantic graph, definitions, translations, answer variants, contexts, and LearningCards;
- preserves completed queue history, factual learning/review history, and FSRS state/history;
- leaves legacy Word status and sense-level controls unchanged.

This behavior is implemented by `LearningService.MarkPermanentlyKnownSchema13` and covered by the existing Schema-13 preservation, rollback, and idempotency tests. No semantic-data, card, FSRS-state, or factual-history deletion is authorized by this action.

When a learning session completes and Unknown/Unprepared vocabulary remains, the summary reports the exact remaining count and offers **Prepare next words**, **Later**, and **Change daily limit**. It never forces navigation to preparation.

The historical Learn path deleted cards, prepared content, contexts, and history while retaining a minimal known marker; that destructive contract is superseded for Schema 13. Initial-review and Preparation dispositions remain separate paths, including their existing legacy status/cleanup semantics. They must not be silently equated with the Learn control. `KF-VOCAB-005` owns the open future user-facing/service reversal workflow; this section does not design it.

---

## 23. Document lifecycle and deletion

**Scope:** The cleanup rules below record the historical destructive document lifecycle and separate review/Preparation cleanup paths. They do not apply to the current Schema-13 Learn permanent-known action, which preserves the graph, cards, contexts, FSRS state, and factual history under section 22. In particular, the legacy permanent-known/retirement triggers below are superseded for Schema-13 Learn.

A document remains only while it supports unresolved learning work.

A document may be deleted when:

- no active vocabulary-review session references it
- no unresolved candidate references it
- no Unknown or Unprepared vocabulary relationship remains
- no active learning item or card requires it
- every reviewable vocabulary relationship is PermanentlyKnown or excluded legacy data
- all required cleanup operations can complete transactionally

For a fully completed document, delete:

- original content
- sentence spans
- occurrences
- candidate relationships
- document-vocabulary relationships
- context snapshots originating only from that document
- prepared learning content that is no longer active
- obsolete scheduling data
- document frequency data

Retain only minimal PermanentlyKnown identity markers.

When a document is complete, it is irrelevant which source text originally taught a word.

A non-blocking maintenance pass may verify cleanup eligibility after startup, but it must not delay first UI rendering.

Cleanup is also triggered immediately after:

- review completion
- a learning item becomes PermanentlyKnown
- a learning item is explicitly retired

All cleanup operations are transactional and idempotent.

---

## 24. Main navigation and workflow routing

Primary user navigation order:

1. Learn
2. Prepare Words
3. Import Text
4. Settings

Review remains an internal blocking workflow route. Prepare Words is a stateful primary destination: it is enabled for an active preparation or Unknown/Unprepared backlog, labelled **Continue preparation** while active, disabled with an explanation when empty, and blocked by active vocabulary review.

The workflow router evaluates the following priority:

1. active vocabulary review
2. active preparation session
3. active learning session
4. due learning cards
5. prepared new cards
6. unprepared Unknown vocabulary
7. no open work

Only active vocabulary review globally blocks unrelated work.

Detailed behavior is binding in `docs/MVP_WORKFLOW.md`.

---

## 25. Storage, transactions, and migrations

Use forward-only schema migrations.

Do not delete an existing user database merely because the schema changes.

Use transactions for:

- accepted import persistence
- review-session creation
- each review decision
- undo
- discard
- preparation acceptance
- preparation Mark as known and Do not learn cleanup
- learning-session creation
- every rating
- permanent-known cleanup
- document cleanup

A failed operation must not leave partial user-visible state.

Retry must not duplicate documents, candidates, occurrences, meanings, cards, or cache entries.

### 25.1 Portable data architecture

KnownFirst supports user-initiated portable data movement through a `.kfarchive` archive. The archive is a logical export format; it is not a copy of the SQLite file and does not make the database a public format.

**Archive format version and database schema version are separate axes.** They must never be collapsed into a single "version" concept.

- The archive reader accepts archive formats **v1** and **v2**.
- A Schema 7 database writes archive format **v1**.
- Schema 8, Schema 9, and Schema 10 databases write archive format **v2**.
- The current application runs database schema **10** and therefore exports archive format **v2**; neither the schema nor the archive format is incremented for Schema-10 Active learning-workflow portability.
- Database schema 10 is a separate version axis, governed by [`DATABASE_CONTRACT.md`](DATABASE_CONTRACT.md).

Import has two distinct target cases, and they are not interchangeable:

- **Restore into an empty installation** — strictly additive insertion into a verified-empty database.
- **Populated-target transactional merge** — non-destructive merge into an installation that already contains data. A format-v1 archive is upgraded in memory for this path; the external archive bytes are unchanged and format v1 is not redefined as a merge format.

Before either case mutates anything, the user sees a **read-only import preview**. The preview performs no database mutation, creates no safety copy, and invokes no writer. It distinguishes the restore case, the merge case, and the **no-change** case in which a repeated import would add nothing. No-change is a successful outcome, not a failure, and it presents no mutating action.

A confirmed populated-target merge re-validates independently and then runs validation, preflight planning, a validated safety copy, the transactional merge writer, and deterministic card-schedule replay, committing atomically or rolling back completely. Stale or non-executable plans are rejected. Repeated imports converge without duplicates.

Portable export must never damage an existing file. The two platforms achieve this differently, and the difference is architectural:

- **Windows** stages the archive to a same-directory temporary file, validates it through the production archive validator, and only then finalizes atomically — `File.Replace` for an existing destination, `File.Move` for a new one. A failure at any stage before finalization leaves an existing archive byte-for-byte unchanged.
- **Android** stages and strictly validates the archive privately *before* the destination picker is opened, so an invalid or failed archive never acquires or writes a destination. The chosen destination is then written through the content provider and reopened for verification. The provider boundary offers no universal atomic replacement guarantee, so Android must not be documented as providing the Windows atomic-finalization guarantee.

Archives are not encrypted and may contain personal imported text and learning history. The user is warned before export. Device preferences, online-lookup consent, the lexical cache, and diagnostic logs are outside the archive.

Workflow portability is version- and capability-aware:

- Historical format-v1 behavior and Schema-8/9 ordinary portable export remain Completed-only for learning workflows; source schema <=9 Active learning workflows are unsupported and rejected. Active `VocabularyReview` and `PreparationBatch` workflows remain outside portable archives and unsupported.
- For Schema-10 archive-V2 ordinary portable export, an Active `LearningSession` may be included with its durably committed queue state and associated committed `LearningReview` history. KF-BACKUP-005B supports restoring that workflow only into an empty Schema-10 installation, resuming from the last durably committed application/database state; transient or uncommitted UI state is not portable, and restore does not fabricate a Completed state.
- Against a populated target, a Schema-10 archive containing an Active learning workflow remains unsupported: preview and actual import fail closed with `BackupErrorCodes.ActiveWorkflowUnsupported`. Future populated-target Active convergence and conflict safety belong to KF-BACKUP-005C.

Schema-10 stable workflow identities are durable architecture intended for reuse by future synchronization, not backup-only identity. Network/cloud synchronization is not implemented.

The archive layout, validation rules, and resource limits are owned by [`architecture/backup-format-v1.md`](architecture/backup-format-v1.md). Merge identities, conflict policies, and safety-copy design are owned by [`architecture/backup-merge-v1-design.md`](architecture/backup-merge-v1-design.md). This section does not restate them.

---

## 26. Diagnostics

Diagnostics are developer-only.

Requirements:

- compile-time or equivalent Release exclusion
- direct diagnostic routes unavailable in Release
- no production database browser
- readable explanations for documents, sessions, sentence spans, candidates, occurrences, lexical cache, preparation, cards, and schedules
- monotonic preparation timing measurements for validation, the database transaction, prepared-meaning save, learning-card creation, session update, next-candidate query, context loading, UI transition, and network work
- raw IDs hidden by default
- database path copy action
- diagnostic report copy action
- no user secrets
- no API tokens because normal Wikimedia lookup uses no user API key

Preparation timing is DEBUG-only and bounded in memory. It must not expose diagnostics in Release. The UI delays its transition spinner until the wait is perceptible, and double submissions are rejected by both the component state and serialized service operations. The Release performance target remains a manually measured cached/preloaded Accept-to-next median of at most 300 ms; automated tests and Debug measurements do not prove that target.

---

## 27. Privacy and telemetry

KnownFirst does not send personal data to the KnownFirst developer.

The MVP has:

- no account
- no analytics
- no telemetry
- no advertising
- no payment flow
- no cloud synchronization
- no uploaded documents
- no uploaded context sentences

Online dictionary lookup is user-initiated and limited to the selected term and language parameters.

All personal learning state remains local.

---

## 28. Testing strategy

Automated tests must use:

- temporary SQLite databases
- fake clocks
- fake HTTP handlers
- captured representative response fixtures
- no live network dependency
- deterministic tokenization
- deterministic scheduling assertions

Test categories include:

- original-content invariants
- sentence and occurrence offsets
- vocabulary identity
- duplicate import behavior
- no-new-vocabulary behavior
- review persistence and undo
- Candidate versus Occurrence separation
- context deduplication
- acronym extraction
- dictionary parsing
- cache behavior
- preparation priority and limit
- typing comparison
- independent card directions
- scheduling
- session resume
- permanent-known cleanup
- document cleanup
- migration preservation
- Release exclusion of diagnostics where practical

---

## 29. Deferred features

The following are explicitly deferred unless a later milestone authorizes them:

- full Wiktionary dump processing
- Wiktextract pipeline
- Open English WordNet package
- OdeNet package
- FreeDict package
- downloadable offline dictionary packages
- GitHub Release package catalog
- local semantic embedding model
- ONNX Runtime
- local generative model
- PDF import
- EPUB import
- website import
- handwriting recognition
- speech recognition
- pronunciation scoring
- synchronization
- Google Drive
- accounts
- payments
- analytics

Portable `.kfarchive` export and import are implemented and are no longer deferred; see section 25.1. Synchronization remains deferred and is a separate capability from portable export and import.

Interfaces should allow future extension without speculative implementation now.

---

## 30. Non-negotiable invariants

1. Original imported text is unchanged.
2. Offsets always point to the original stored characters.
3. One vocabulary identity may have many occurrences.
4. Repeated words are reviewed once.
5. Identical contexts are displayed once but still count as multiple occurrences.
6. A text with no open learning vocabulary changes no statistics and is not stored.
7. Known vocabulary never enters automatic preparation.
8. Automatic preparation never requires manual typing when a usable result exists.
9. Complete documents and context sentences are never sent to Wikimedia.
10. Due reviews never count against the new-vocabulary limit.
11. Two card directions count as one new vocabulary item.
12. Card directions have independent scheduling.
13. No fixed 7-day or 14-day point automatically means permanently known.
14. Permanent knowledge requires the user's explicit decision.
15. Document cleanup follows the scoped contract in section 23; Schema-13 Learn permanent-known does not delete semantic vocabulary or factual history.
16. Schema-13 Learn persists the word-level AlreadyKnown control and preserves cards and FSRS state; legacy review/Preparation paths retain their separate marker semantics.
17. Active review is resumable and is the only globally blocking workflow.
18. Release builds do not expose developer diagnostics.
# Current Schema and scheduling authority

Current `master` production source uses Schema 13 and Archive V3, and production scheduling is owned by `IFsrs6SchedulingService` / `Fsrs6SchedulingService`. `MauiProgram` calls `AddKnownFirstLearningRuntime()`. Existing Schema 1–12 databases fail closed in the current production startup path; fresh databases bootstrap directly to Schema 13. Legacy scheduler code/columns may remain for later `KF-CLEANUP-001` work and do not make the legacy scheduler authoritative.
