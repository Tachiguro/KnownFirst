Read and follow:

docs/KNOWNFIRST_ARCHITECTURE.md

The document is the master architecture and long-term product specification.

Do not implement the complete document in this task.

Implement only the first functional vertical slice described below.

Create and switch to this branch:

feature/text-review-vertical-slice

Do not commit or push automatically.

# Goal

Implement one complete and manually testable KnownFirst workflow:

Paste English or German text
→ preserve and save the original text
→ extract sentences and vocabulary candidates
→ show each candidate in its original highlighted context
→ classify it as Known, Unknown, or Ignored
→ persist every decision
→ resume an unfinished review
→ block another import and the Learn page until review is complete

This task must produce working application behavior, not only interfaces,
architecture, placeholder pages, or database entities.

# Preserve the existing stable foundation

Do not regress:

- Windows and Android builds
- responsive design
- Android safe areas
- Android Back navigation
- immediate English/German UI switching
- persisted UI language
- System, Light, and Dark appearance modes
- Settings behavior
- Reset automatic scrolling
- Support and Bug Report placeholders
- Tachiguro attribution
- existing tests
- README
- current stable database behavior

Do not redesign the current UI.

Use the existing colors, spacing, cards, buttons, navigation, responsive
breakpoints, Light mode, and Dark mode.

# Explicitly excluded from this task

Do not implement:

- real dictionary downloads
- GitHub Release package installation
- full Wiktionary import
- Wiktextract import
- Open English WordNet import
- OdeNet import
- FreeDict import
- DictionaryBuilder project
- ONNX Runtime
- local semantic AI
- generative AI
- actual learning cards
- translations
- backup
- export
- synchronization
- Google Drive
- PDF or file import
- statistics expansion

Do not create incomplete production integrations for these features.

# 1. Import Text

Make the existing Import Text page functional.

Include:

- required document title
- large multiline paste field
- document language:
  - English
  - German
- explanation language:
  - English
  - German
- Save and analyze button
- localized validation messages
- progress indication
- protection against double submission

The original pasted text must be stored exactly as entered.

Do not:

- shorten it
- remove numbers from it
- correct it
- normalize it
- translate it
- change capitalization
- change punctuation

Cleaning applies only to extracted vocabulary candidates.

# 2. Only one active import

Allow only one unfinished import/review session.

When an unfinished session exists:

- block another text import
- show Continue review
- show reviewed count and total count
- provide Discard import with destructive confirmation
- block Prepare Words
- block Learn
- enforce blocking even when the route is opened directly

Discard must remove only data created exclusively for that unfinished import.

It must not remove existing Known, Unknown, Ignored, or future Mastered user
state.

# 3. Sentence coordinates

Store sentence boundaries as coordinates into the unchanged original text.

Use an entity such as:

SentenceSpanEntity
- Id
- DocumentId
- StartPosition
- Length
- Order

Sentence extraction must initially support English and German punctuation and
paragraph boundaries conservatively.

All offsets must refer to the original .NET string.

The following invariant must always hold:

document.Content.Substring(startPosition, length)

must return the original stored sentence or word surface form.

Do not duplicate the full sentence for every word.

# 4. Tokenization

Implement deterministic, platform-independent tokenization in KnownFirst.Core.

The tokenizer must be Unicode-safe.

Preserve:

- German umlauts
- ß
- accented Latin characters
- Greek characters
- Cyrillic characters
- original capitalization

Ignore as vocabulary candidates:

- whitespace
- punctuation-only values
- symbol-only values
- URLs
- email addresses
- standalone numbers

Retain:

- ordinary words
- short words
- acronyms
- abbreviations
- technical terms containing numbers or punctuation where recognized

Examples to retain:

- AI
- IT
- IP
- HTML
- OAuth2
- IPv6
- SHA-256
- CVE-2026-12345

For this first vertical slice, complex multi-token technical terms such as
“ISO 27001” and “TLS 1.3” may remain separate tokens.

Record that limitation clearly.

# 5. Capitalization and identity

Do not use lowercase alone as the identity of every candidate.

These must remain distinct:

- IT and it
- US and us

Repeated ordinary capitalization variants may be grouped conservatively:

- Network
- network
- NETWORK

But preserve every encountered surface form and its occurrence count.

Use token kind as part of candidate identity:

- Word
- Acronym
- Abbreviation
- TechnicalTerm

Do not classify an all-uppercase sentence word automatically as an acronym when
the evidence is insufficient.

# 6. Conservative word-family handling

Do not implement broad stemming.

Do not automatically merge:

- network
- networking
- networked

unless an existing deterministic rule is explicitly reliable.

For this first vertical slice:

- exact equivalent candidates may be grouped
- capitalization variants may be grouped conservatively
- a very small explicit set of English and German irregular forms may be
  supported only when covered by tests
- uncertain forms remain separate candidates

It is preferable to show two related candidates than to merge unrelated words
incorrectly.

Prepare interfaces for future language packages, but do not build or download
real packages in this task.

# 7. Occurrences and context

For every reviewable candidate, retain its occurrences during the active review.

Store:

- DocumentId
- SentenceSpanId
- StartPosition
- Length
- SurfaceForm
- candidate or lexical-item ID
- occurrence order

Display the original sentence containing the selected occurrence.

Render safely as:

- text before target
- highlighted target
- text after target

Use `<mark>` or an equivalent accessible highlight.

Never inject the original sentence as raw HTML.

Show up to three different contexts for a candidate when available.

Include:

- Previous context
- Context 1 of 3
- Next context

At this stage, do not automatically claim that all contexts represent the same
dictionary meaning.

Display this localized notice when several different contexts exist:

English:
This word appears in multiple contexts. Review the examples before deciding.

German:
Dieses Wort kommt in mehreren Zusammenhängen vor. Prüfe die Beispiele vor
deiner Entscheidung.

Meaning separation with dictionaries and semantic AI is deferred to a later
phase.

# 8. Review Words

After analysis, navigate directly to Review Words.

Show one unique candidate at a time.

Display:

- candidate
- token kind
- encountered surface forms
- occurrence count
- highlighted context
- context navigation
- progress, for example 12 / 84

Question:

English:
Do you already know this word or acronym?

German:
Kennst du dieses Wort oder Akronym bereits?

Actions:

- Known / Bekannt
- Unknown / Unbekannt
- Ignore / Ignorieren
- Undo previous decision / Letzte Entscheidung rückgängig machen

Persist every decision immediately.

Prevent accidental double-click decisions.

Undo must restore the previous candidate and its previous persisted status.

# 9. Resume behavior

Persist enough review-session state to resume after:

- navigating away
- closing the application
- restarting Windows
- restarting Android
- Android background and resume

Resume at the first unresolved candidate.

Do not repeat completed decisions.

On Home, show a prominent Continue review card when an unfinished review
exists.

# 10. Completion and gating

A review is complete only when every candidate has one of:

- Known
- Unknown
- Ignored

After completion:

- mark the session Completed
- show a localized summary
- remove the unfinished-review restriction
- update Home counters
- allow the next workflow stage to become available

Do not implement learning cards in this task.

The Learn page may show:

English:
Word learning will be implemented in the next milestone.

German:
Das Lernen der Wörter wird im nächsten Meilenstein implementiert.

# 11. User-data storage rules

Dictionary reference data does not exist yet and must not be simulated as
personal user data.

For Known:

Keep a minimal user marker containing:

- language
- canonical candidate identity
- token kind
- status
- required surface aliases

Remove or avoid retaining after review completion:

- occurrence coordinates
- context records
- document frequency
- copied sentence data

Future confident matches should be skipped.

For Ignored:

Keep a minimal marker so KnownFirst does not ask again.

For Unknown:

Keep:

- candidate identity
- occurrence count
- encountered forms
- up to three representative context coordinates
- status
- data required for the next learning milestone

Do not delete the original imported document because retained Unknown contexts
refer to it.

Do not implement Mastered behavior yet, but preserve existing enum/schema support
when already present.

# 12. Database and transactions

Reuse existing entities and migrations wherever practical.

Do not create duplicate representations of documents, words, occurrences,
sessions, or meanings.

Use a forward migration.

Do not delete the existing user database.

Use transactions for:

- document creation
- sentence creation
- candidate and occurrence creation
- review-session creation
- individual review decisions
- discard

A failed analysis must not leave a document without a usable review session.

Retry must not duplicate data.

# 13. DEBUG diagnostics

Add or extend a DEBUG-only diagnostics page.

Show read-only:

- database path
- documents
- original text character count
- sentence spans
- extracted candidates
- token kinds
- surface forms
- occurrence counts
- stored offsets
- statuses
- active session
- reviewed and total counts

Include Refresh.

Do not expose this page in Release navigation.

# 14. Tests

Add focused tests for:

- original document remains unchanged
- sentence offsets retrieve the correct original text
- token offsets retrieve the exact original surface form
- German umlauts and ß preserve correct offsets
- accented Latin, Greek, and Cyrillic text does not corrupt tokenization
- URLs are excluded
- email addresses are excluded
- standalone numbers are excluded
- OAuth2, IPv6, SHA-256, and CVE identifiers are retained
- IT and it remain distinct
- US and us remain distinct
- exact duplicates are reviewed once and counted correctly
- uncertain related forms are not forcibly merged
- each decision persists
- Undo restores the previous decision
- unfinished review resumes
- second import is blocked
- direct Learn navigation is blocked
- completed review removes the block
- Known retains only minimal user state
- Ignored retains a minimal marker
- Unknown retains at most three contexts
- discard removes only import-specific data
- migration preserves existing settings

Use temporary SQLite databases for integration tests where practical.

# 15. Manual validation corpus

English:

Information technology (IT) is the study or use of computers.
It is useful.
Several networks were connected.
She is networking with security professionals.
HTML, OAuth2, IPv6 and SHA-256 are technical terms.
Contact test@example.com or visit https://example.com.
The values are 42 and 2026.

German:

Die IT schützt das Netzwerk.
Es ist mit mehreren Netzwerken verbunden.
Die Häuser stehen neben dem alten Haus.
Das berufliche Netzwerken ist wichtig.
OAuth2 und SHA-256 werden verwendet.

Verify:

- original text is unchanged
- IT and it remain distinct
- target highlighting is exact
- URL, email, and standalone numbers are excluded
- technical terms remain
- contexts can be switched
- interruption resumes correctly
- another import is blocked
- Learn is blocked until completion

# 16. Validation and stop condition

After implementation:

1. Run all tests.
2. Build Windows.
3. Build Android.
4. Fix compilation and test failures.
5. Do not commit or push.

Report:

- entities reused
- schema changes
- files created
- files modified
- tokenizer rules
- known word-family limitations
- test totals
- Windows build result
- Android build result
- exact manual test checklist
- remaining work for real dictionaries and semantic AI

Stop after this vertical slice.

Do not begin dictionary downloading, Wiktionary import, WordNet import, FreeDict
import, or ONNX integration.