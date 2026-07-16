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
13. **Only a minimal marker for permanently known vocabulary remains.**
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
- localized English and German user interface resources

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

The UI supports English and German.

Rules:

- first launch uses the supported device language
- unsupported device languages fall back to English
- a user-selected language persists
- language changes apply immediately
- application reset removes the saved language preference and reapplies the supported device language
- theme choices are System, Light, and Dark
- theme changes apply immediately and persist
- the preparation limit defaults to 10
- the preparation limit may be set to 5, 10, 20, 30, or 50
- 50 is the hard maximum for newly prepared vocabulary in one daily batch
- due reviews never count against the new-vocabulary limit
- card direction defaults to Both directions

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

The existing `Ignored` state may remain internally for migration compatibility, invalid-token exclusion, or legacy data. It is not a normal visible review choice.

### 6.2 Preparation state

An Unknown vocabulary item may be:

- `Unprepared`
- `Preparing`
- `Prepared`
- `PreparationFailed`

### 6.3 Card scheduling state

A learning card may be:

- `New`
- `Learning`
- `Review`
- `Relearning`
- `Suspended`
- `Retired`

### 6.4 Permanently known

`PermanentlyKnown` means:

- the vocabulary is never shown in normal review again
- it has no future learning schedule
- personal definition, translation, context, frequency, and learning-history data may be deleted
- a minimal identity marker remains so future imports skip it confidently

A vocabulary item becomes PermanentlyKnown in either of these ways:

1. the user selects Known during the initial vocabulary review, or
2. the user explicitly selects “Mark permanently known” later in learning and confirms the destructive cleanup

KnownFirst must not automatically claim that a word is permanently learned after a fixed number of days.

---

## 7. Document ingestion and preflight

Text import must be analyzed through a temporary or transactional preflight result before permanent storage.

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

Automatic preparation uses a provider chain.

Suggested interfaces:

```csharp
ILexicalEnrichmentService
IDictionaryLookupProvider
IAcronymExpansionProvider
ILexicalCacheRepository
```

Provider priority:

1. explicit acronym expansion from the imported text
2. local lexical cache
3. online Wiktionary provider

A lexical result is structured data, not one unstructured HTML or text blob.

It supports where available:

- source language
- explanation language
- queried lemma
- display term
- token kind
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

Dictionary reference data and personal learning state remain separate.

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

Before the first online lookup, the user sees an explicit privacy disclosure and chooses whether to continue.

The disclosure states:

- no application data is sent to the KnownFirst developer
- the selected term and language information are sent directly to Wikimedia
- Wikimedia receives ordinary network metadata such as IP address and User-Agent
- retrieved lexical data and personal learning data are stored locally

A saved local consent may be revoked in Settings.

The application must start and remain usable without network access.

---

## 16. Local lexical cache

Successful lexical results are cached in SQLite.

A stable cache key includes at least:

- source language
- normalized lemma
- token kind where relevant
- explanation language
- provider
- provider schema version

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

The configured limit applies to unique newly prepared vocabulary items, not generated card directions.

Default: 10  
Maximum: 50

Due reviews never count against this limit.

Preparation supports:

- Automatic online
- Manual

Automatic is the primary workflow. Manual is a fallback.

For every automatic result, the user can:

- accept
- choose an alternative meaning
- edit
- retry
- switch to manual
- skip for now

Automatic preparation must never require typing when a usable result was found.

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
- token kind
- acronym expansion, nullable
- selected translation, nullable
- selected definition
- optional dictionary example
- up to three context snapshots
- source attribution
- prepared timestamp

For acronyms, answer order is:

1. acronym expansion
2. translation when relevant
3. definition

A prepared item must survive application restart.

---

## 20. Learning-card directions

Supported directions:

- `TermToMeaning`
- `MeaningToTerm`

Default setting:

- Both directions

One vocabulary identity counts as one new vocabulary item even when it generates two cards.

Each direction has independent scheduling state.

### 20.1 Term to meaning

Front:

- term
- original highlighted context
- occurrence count where useful

Back after reveal:

- acronym expansion when applicable
- translation
- definition
- optional example
- source

The user self-rates the recall.

### 20.2 Meaning to term

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

Use an abstraction:

```csharp
ISpacedRepetitionScheduler
```

The initial implementation is:

```csharp
SimpleSpacedRepetitionScheduler
```

It must be isolated so a future FSRS implementation can replace it without changing page logic, learning history, or card models.

Use an injectable clock.

Store per card:

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

### 21.1 Initial deterministic scheduling rules

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

A card rated Again may appear once again at the end of the current session. Repeated Again ratings must not create an endless session.

A future milestone may implement FSRS with a configurable desired retention. The current data model must not prevent that migration.

---

## 22. Permanent-known action and learning completion

KnownFirst does not equate a fixed interval with permanent knowledge.

A learning item remains scheduled until the user explicitly chooses:

- Mark permanently known

This action requires confirmation and explains that:

- future reviews stop
- prepared definitions, translations, contexts, card schedules, and learning history may be deleted
- the minimal known-vocabulary marker remains

The application may suggest this action after both card directions have reached long review intervals, but it must not activate it automatically.

When one vocabulary identity becomes PermanentlyKnown:

- delete all of its active learning cards
- delete its personal prepared-learning content
- delete its personal context snapshots
- delete its occurrence and frequency data when no unresolved dependency remains
- retain the minimal known marker
- update all related documents
- trigger document-cleanup eligibility checks

---

## 23. Document lifecycle and deletion

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
2. Import Text
3. Settings

Review and preparation are workflow routes, not permanent primary-navigation destinations.

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
- learning-session creation
- every rating
- permanent-known cleanup
- document cleanup

A failed operation must not leave partial user-visible state.

Retry must not duplicate documents, candidates, occurrences, meanings, cards, or cache entries.

---

## 26. Diagnostics

Diagnostics are developer-only.

Requirements:

- compile-time or equivalent Release exclusion
- direct diagnostic routes unavailable in Release
- no production database browser
- readable explanations for documents, sessions, sentence spans, candidates, occurrences, lexical cache, preparation, cards, and schedules
- raw IDs hidden by default
- database path copy action
- diagnostic report copy action
- no user secrets
- no API tokens because normal Wikimedia lookup uses no user API key

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
- backup
- export
- synchronization
- Google Drive
- accounts
- payments
- analytics

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
15. Fully completed documents and no-longer-needed learning data are deleted.
16. Minimal known-vocabulary markers remain.
17. Active review is resumable and is the only globally blocking workflow.
18. Release builds do not expose developer diagnostics.
