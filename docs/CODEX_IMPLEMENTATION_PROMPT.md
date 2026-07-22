Read completely before making changes:

- AGENTS.md
- docs/KNOWNFIRST_ARCHITECTURE.md
- docs/MVP_WORKFLOW.md

Treat both specification documents as binding.

The current text-import and vocabulary-review implementation is the stable
foundation. Do not rebuild it from scratch.

Create and switch to:

feature/automatic-dictionary-learning-mvp

Create the branch from the current checked-out checkpoint.

Do not work directly on master.
Do not merge.
Do not commit or push automatically.

# Objective

Implement the first usable automatic dictionary and learning MVP described in
the two specification documents.

The complete usable workflow must be:

Paste text
→ analyze
→ review each unique new candidate with Known or Unknown
→ automatically select the most frequent unprepared Unknown vocabulary
→ automatically retrieve acronym expansions, translations, and definitions
→ let the user confirm or correct every prepared item
→ create recognition and spelling cards
→ learn due and new cards with spaced repetition
→ resume interrupted work
→ allow explicit Mark permanently known
→ delete a fully completed source text and obsolete personal learning data

# Scope required in this task

Implement all of the following:

1. Remove the visible Ignore action from normal vocabulary review.
   Keep legacy schema compatibility where necessary.

2. Implement workflow routing and gating:
   - active vocabulary review is the only globally blocking state
   - Settings remains available
   - Learn is the first primary navigation action
   - Review and Prepare are workflow routes, not permanent primary navigation
   - disabled actions explain why they are unavailable

3. Preserve and complete import preflight:
   - exact duplicate creates no data and changes no counts
   - text with no open learning vocabulary creates no permanent document
   - completed review with no Unknown items deletes temporary document data

4. Implement automatic preparation batches:
   - only Unknown and Unprepared vocabulary
   - highest accepted occurrence frequency first
   - earliest first-seen tie-breaker
   - canonical alphabetical final tie-breaker
   - configured batch size
   - default 10
   - hard maximum 50
   - due cards do not count against the new-item limit

5. Implement preparation method selection:
   - Automatic online
   - Manual
   - automatic is the normal recommended path
   - manual is optional fallback

6. Implement the first-online-lookup privacy disclosure exactly according to
   docs/MVP_WORKFLOW.md.
   Persist consent locally and allow revocation in Settings.

7. Do not request, accept, or store a Wikimedia API key.

8. Implement the lexical provider chain:
   - explicit acronym expansion from the imported text
   - local SQLite lexical cache
   - read-only Wiktionary lookup through the MediaWiki Action API

9. For the Wikimedia provider:
   - use .NET HttpClient
   - use a descriptive KnownFirst User-Agent
   - at most two concurrent requests
   - cancellation and timeout
   - limited transient retry
   - respect Retry-After
   - handle 429 and transient 5xx
   - parse the correct language section with a maintained HTML parser such as
     AngleSharp
   - retain page title, source project, revision ID, attribution, and lookup
     timestamp
   - never send the complete document, context sentence, learning history, or
     analytics
   - never fabricate missing data

10. Support:
    - English definition
    - German definition
    - English term with German explanation/translation
    - German term with English explanation/translation

11. Detect acronym patterns before network lookup:
    - Long Form (ACRONYM)
    - ACRONYM (Long Form)
    Imported-text expansion has priority.

12. Implement preparation confirmation:
    - Accept and continue
    - Choose another meaning
    - Edit
    - Retry
    - Manual entry
    - Skip for now
    Automatic results must not require typing when usable data exists.

13. Deduplicate representative context snapshots without reducing actual
    occurrence counts.
    Keep at most three unique context snapshots per prepared vocabulary item.

14. Implement prepared learning content and two independent card directions:
    - TermToMeaning
    - MeaningToTerm
    Default setting: Both directions.
    One vocabulary item counts as one new item even when two cards are created.

15. Implement spelling testing for MeaningToTerm:
    - local deterministic comparison
    - Unicode normalization
    - canonical answer and accepted aliases
    - acronym case sensitivity
    - German noun capitalization remains meaningful
    - readable character-level difference
    - incorrect typed answer records Again
    - correct typed answer allows Hard, Good, or Easy
    - no AI grading

16. Implement ISpacedRepetitionScheduler and
    SimpleSpacedRepetitionScheduler with the exact first-version scheduling
    rules in docs/KNOWNFIRST_ARCHITECTURE.md.

17. Implement resumable learning sessions:
    - due cards first
    - new cards ordered by frequency
    - persist every rating immediately
    - prevent double submission
    - one same-session Again repetition maximum per card
    - no endless session
    - no Skip rating

18. Implement Mark permanently known:
    - explicit destructive confirmation
    - stop both card directions
    - delete personal prepared content, context snapshots, scheduling state,
      frequency, and obsolete learning history
    - retain only the minimal known-vocabulary marker
    - update every related document
    - trigger cleanup eligibility

19. Implement complete-document cleanup:
    - fully completed text has no remaining context snapshots
    - delete original content, sentence spans, occurrences, document links,
      obsolete learning content, and obsolete schedules
    - retain minimal permanently-known vocabulary markers
    - cleanup is transactional and idempotent
    - startup maintenance is non-blocking

20. Extend DEBUG-only diagnostics for:
    - lexical cache
    - preparation state
    - selected and alternative meanings
    - prepared items
    - card directions
    - due dates
    - intervals
    - ease factors
    - ratings
    - active learning session
    - document cleanup eligibility

# Explicit exclusions

Do not implement:

- full Wiktionary dumps
- Wiktextract
- WordNet or OdeNet package building
- FreeDict package building
- downloadable offline dictionary packages
- GitHub Release package hosting
- local LLM
- ONNX
- semantic embeddings
- PDF, EPUB, or website import
- handwriting recognition
- speech recognition
- backup
- export
- synchronization
- accounts
- analytics
- advertisements
- payments
- FSRS in this milestone

Do not refactor unrelated stable code.

# Required automated tests

Do not use live network calls.

Use fake HTTP handlers, response fixtures, a fake clock, and temporary SQLite
databases.

Add or update tests for:

## Review and workflow

- visible review offers Known and Unknown but not Ignore
- legacy Ignored data remains readable
- active review blocks Import, Prepare, and Learn
- Settings remains available
- leaving Settings returns to active review
- Learn enablement depends on active/due/prepared cards
- Review and Prepare are not permanent primary-navigation items

## Import and frequency

- exact duplicate persists nothing and changes no counts
- no-open-vocabulary text persists nothing and changes no counts
- all-Known completed review deletes temporary document data
- accepted document updates actual Unknown occurrence counts once
- repeated occurrences create one candidate and many occurrences
- duplicate sentence contexts preserve occurrence count

## Acronyms

- Information Technology (IT)
- IT (Information Technology)
- Multi-Factor Authentication (MFA)
- imported expansion outranks external expansion
- ordinary uppercase words are not blindly treated as confirmed acronyms

## Dictionary and cache

- English definition parsing
- German definition parsing
- English-to-German result
- German-to-English result
- correct language section
- missing page
- malformed response
- timeout
- 429 with Retry-After
- transient 5xx retry limit
- successful cache write
- cache hit without network
- duplicate cache prevention
- attribution and revision retained
- no complete document or context is sent externally

## Preparation

- only Unknown and Unprepared selected
- frequency ordering
- deterministic tie-breakers
- configured limit
- hard maximum 50
- due cards excluded from new-item limit
- automatic result accepted without typing
- alternative meaning selection
- failed lookup does not block remaining items
- interrupted preparation resumes
- context snapshots deduplicate by normalized sentence
- at most three context snapshots

## Learning and spelling

- answer hidden before reveal
- acronym expansion displayed first
- only prepared items enter learning
- two directions count as one new vocabulary item
- direction schedules are independent
- exact spelling accepted
- accepted alias accepted
- wrong spelling shows a readable difference and records Again
- acronym case error is rejected
- German noun capitalization error is rejected
- every rating persists
- Again reappears once at most in the session
- session cannot become endless
- due cards ordered before new cards
- new cards ordered by frequency
- interrupted session resumes
- summary counts are correct

## Scheduler

- New Again = 10 minutes
- New Hard = 1 day
- New Good = 3 days
- New Easy = 7 days
- Review Again enters Relearning and lowers ease
- Hard, Good, Easy interval formulas are deterministic
- minimum ease is 1.3
- intervals continue beyond 7 and 14 days
- no fixed interval automatically marks PermanentlyKnown

## Permanent known and cleanup

- explicit confirmation required
- both card directions removed
- scheduling and personal prepared content removed
- minimal known marker retained
- related documents updated
- fully completed document deletes all source text and context snapshots
- reimport does not ask for the permanently-known word
- cleanup is idempotent
- startup maintenance does not block initial UI rendering

All existing stable tests must continue to pass.

# Validation

Run only:

1. all automated unit and integration tests
2. Windows Debug build

Do not run:

- GUI automation
- browser automation
- Windows Release build
- Android Debug build
- Android Release build
- Android emulator tests

The user will manually validate Windows and Android UI behavior.

Fix concrete test and Windows Debug build failures.
Do not broaden scope to unrelated refactoring.

# Stop condition

Stop only when the automatic dictionary preparation and first complete learning
workflow are usable from end to end on the Windows Debug build.

Do not commit or push.

Report:

- branch name
- architecture reused
- entities reused
- schema and migration changes
- files created and modified
- provider chain
- exact Wikimedia request data
- privacy and consent behavior
- preparation selection behavior
- cache behavior
- card-direction behavior
- spelling-comparison behavior
- scheduling behavior
- permanent-known cleanup
- document cleanup
- test totals
- Windows Debug build result
- exact manual Windows validation steps
- exact manual Android validation steps
- known limitations
- remaining work for offline dictionary packages and FSRS
