# KnownFirst MVP Workflow

## 1. Purpose

This document defines the binding user workflow for the first usable KnownFirst MVP.

The MVP must let a user:

1. paste a text
2. review only genuinely new vocabulary
3. choose Known or Unknown
4. automatically prepare the most frequent Unknown vocabulary
5. confirm or correct dictionary results
6. learn in original context
7. practise recognition and spelling
8. review due cards through spaced repetition
9. permanently remove vocabulary from learning when the user is fully confident
10. automatically delete completed source texts and obsolete learning data

The workflow must be understandable without technical database knowledge.

---

## 2. Primary navigation

Primary navigation order:

1. **Learn / Lernen**
2. **Prepare Words / Wörter vorbereiten**
3. **Import Text / Text importieren**
4. **Settings / Einstellungen**

The application may retain a Home dashboard, but the principal action priority remains Learn first.

Do not show these as permanent primary-navigation items:

- Review Words

Review remains an internal workflow route. Prepare Words is always represented in the ordered navigation but is stateful: it is enabled for an active preparation or Unknown/Unprepared backlog, labelled **Continue preparation / Vorbereitung fortsetzen** while active, disabled with a concise reason when empty, and blocked by active vocabulary review.

### 2.1 Learn availability

Learn is enabled when at least one of these exists:

- an active learning session
- at least one due card
- at least one prepared new card

Otherwise Learn is disabled and explains:

English:

> No words are ready to learn yet.

German:

> Es sind noch keine Wörter zum Lernen vorbereitet.

### 2.2 Import availability

Import is disabled only while an unfinished vocabulary review exists.

Due cards, prepared cards, an unfinished learning session, or an unfinished preparation session do not permanently block import.

### 2.3 Settings availability

Settings remains available in every state.

When Settings was opened from a blocking vocabulary review, leaving Settings returns to that review.

---

## 3. Global workflow priority

KnownFirst determines the highest-priority open state.

### Priority 1: Active vocabulary review

Behavior:

- navigate to Review automatically
- block another import
- block preparation
- block learning
- allow Settings
- allow Discard import
- show review progress
- resume at the first unresolved candidate

This is the only globally blocking state.

### Priority 2: Active preparation session

Behavior:

- offer Continue preparation
- allow learning already prepared or due cards
- allow Settings
- allow import when no vocabulary review is active

### Priority 3: Active learning session

Behavior:

- show Continue learning as the primary action
- preserve the exact current queue and revealed state where practical
- allow Settings
- allow import when no vocabulary review is active

### Priority 4: Due cards

Behavior:

- show Learn now as the primary action
- due cards come before newly prepared cards

### Priority 5: Prepared new cards

Behavior:

- show Start learning

### Priority 6: Unprepared Unknown vocabulary

When no due or prepared cards exist:

- show Prepare words as the primary action
- entering preparation may happen automatically after review completion

When due or prepared cards exist:

- show the backlog count
- offer preparation after the current learning session

### Priority 7: No open work

Behavior:

- Learn is disabled
- Import Text is the primary action
- Settings remains available

---

## 4. Import Text

Fields:

- required document title
- large multiline editable text field
- source language: English or German
- lookup mode: Definition or Translation
- target language: English, German, or Russian, shown only for translation mode and excluding the selected source language
- Save and analyze

Definition mode has no target language. Translation mode requires a target language different from the source language.

Source language, translation target language, and UI language are three independent axes. The lexical languages are saved with the import and are never derived from the System/English/German/Russian UI language.

Russian is a translation target only. Russian source-text import and analysis remain deferred, so the source-language choice stays English or German.

`DefinitionAndTranslation` is not a currently selectable import option. It remains a readable persisted and archived model value, so existing rows, preparation state, and portable archives that already use it continue to be processed unchanged.

Deterministic vocabulary normalization uses the imported-text language. English `I`, `me`, and `my` are one canonical `I` vocabulary identity; their original forms and coordinates remain unchanged. This closed rule does not enable broad stemming.

The multiline field must support:

Windows:

- right-click context menu
- Paste
- Copy
- Cut
- Ctrl+V
- Shift+Insert
- normal text selection

Android:

- long-press selection
- clipboard paste
- normal touch editing

The original text is stored exactly as entered only after preflight accepts it.

Text-analysis behavior and DEBUG explainability follow the binding specification in [`WORD_ANALYSIS.md`](WORD_ANALYSIS.md).

Show progress and prevent double submission.

---

## 5. Import preflight outcomes

### 5.1 Exact duplicate

Message:

English:

> This text was already imported. Nothing was saved and no statistics were changed.

German:

> Dieser Text wurde bereits importiert. Es wurde nichts gespeichert und keine Statistik verändert.

Actions:

- Return Home
- Import another text

### 5.2 All words already known

Message:

English:

> All words are already known. The text was not saved.

German:

> Alle Wörter sind bereits bekannt. Der Text wurde nicht gespeichert.

This means the user can read the text without an open vocabulary-learning requirement.

No document, counters, occurrences, or session are retained.

### 5.3 No open learning words

Use this when all reviewable items are either permanently known or excluded legacy data.

English:

> There are no open learning words. The text was not saved.

German:

> Es gibt keine offenen Lernwörter. Der Text wurde nicht gespeichert.

### 5.4 New vocabulary found

Behavior:

- save accepted analysis transactionally
- create one review session
- navigate directly to vocabulary review

---

## 6. Vocabulary Review

Question:

English:

> Do you already know this word or acronym?

German:

> Kennst du dieses Wort oder Akronym bereits?

Visible actions:

- Known / Bekannt
- Unknown / Unbekannt
- Undo previous decision / Letzte Entscheidung rückgängig machen

Do not show Ignore as a normal action.

For every candidate show:

- candidate
- token kind
- encountered forms
- actual occurrence count
- highlighted original context
- context navigation
- progress

When several contexts exist, show:

English:

> This word appears in multiple contexts. Review the examples before deciding.

German:

> Dieses Wort kommt in mehreren Zusammenhängen vor. Prüfe die Beispiele vor deiner Entscheidung.

Persist each decision immediately.

Prevent double submission.

### 6.1 Known

When Known is selected:

- create or update the minimal permanently-known marker
- do not prepare the word
- do not schedule it
- remove unnecessary occurrence and frequency data after review cleanup

### 6.2 Unknown

When Unknown is selected:

- keep the vocabulary identity
- keep actual accepted occurrence count
- keep encountered forms
- keep up to three representative context references until preparation
- add the item to the unprepared backlog

### 6.3 Undo

Undo restores:

- the previous candidate
- its previous persisted status
- session progress
- related temporary state

### 6.4 Narrow/mobile review layout

Use the mobile app bar as the single page title and hide duplicate page headings and prominent Back-to-home controls. Home shows the KnownFirst name once in its hero.

Keep progress, candidate, one highlighted context, context navigation, Known, and Unknown prominent. Put token kind, encountered forms, and occurrence count in a collapsed Details section; DEBUG Analysis details remains separate.

Known and Unknown remain prominent primary review actions side by side in the bottom workflow action bar above the bottom safe area, with a reserved concise saving-status line, disabled actions while saving, reachable Undo, and sufficient page padding so content is never hidden. `Discard import` is positioned as the destructive trailing/end action within that workflow action bar rather than in ordinary page flow. Opening discard confirmation suppresses competing review actions and displays an explicit destructive confirmation in the action area to discard the entire unfinished active import (not merely the current vocabulary item). On narrow layouts, the action area stacks responsively while keeping all controls reachable without horizontal overflow.

---

## 7. Review completion

### 7.1 No Unknown vocabulary remains

Behavior:

- complete the review
- delete the document and all temporary analysis data
- retain minimal permanently-known markers
- return to Home

Message:

English:

> You know all words in this text. The text was not saved.

German:

> Du kennst alle Wörter in diesem Text. Der Text wurde nicht gespeichert.

### 7.2 Unknown vocabulary remains and no cards are ready

Behavior:

- complete the review
- navigate to Prepare words

### 7.3 Unknown vocabulary remains and learning work already exists

Behavior:

- complete the review
- return to the dashboard
- keep the Unknown items in the preparation backlog
- prioritize due reviews
- show the backlog count

---

## 8. Prepare Words

User-facing title:

English:

> Prepare words

German:

> Wörter vorbereiten

Preparation remains a resumable batch workflow even though its stateful entry is visible in primary navigation.

Select the next batch in this order:

1. highest accepted occurrence count
2. earliest first-seen timestamp
3. canonical term alphabetically

Select only:

- Unknown
- Unprepared
- resolved review items

Exclude:

- PermanentlyKnown
- already prepared
- unresolved review candidates
- legacy ignored/excluded data

Preparation is uncapped by the daily target; users may prepare more vocabulary than can be admitted in a single day. The configured daily new-word limit governs Learning admission (recommended default 5 for fresh installations, configurable range 1..50; grandfathered installations preserve their established limit).

Two generated card directions still count as one newly prepared vocabulary item.

---

## 9. Preparation method choice

Before preparation begins, show:

English:

> How should these words be prepared?

German:

> Wie sollen diese Wörter vorbereitet werden?

Actions:

- Automatic online / Automatisch online
- Manual / Manuell
- Cancel / Abbrechen

Automatic online is the recommended default.

---

## 10. First online lookup disclosure

Before the first online lookup, show:

English:

> KnownFirst does not send your documents, example sentences, learning history, or personal data to the KnownFirst developer. Only the selected term and the selected language information are sent directly to Wikimedia for dictionary lookup. Wikimedia receives normal network information such as your IP address and the KnownFirst User-Agent. Retrieved dictionary content and your personal learning data are stored locally on this device.

German:

> KnownFirst sendet keine Dokumente, Beispielsätze, Lernhistorie oder persönlichen Daten an den Entwickler von KnownFirst. Für die Wörterbuchabfrage werden ausschließlich der ausgewählte Begriff und die gewählten Sprachinformationen direkt an Wikimedia übertragen. Wikimedia erhält dabei übliche Netzwerkdaten wie deine IP-Adresse und den KnownFirst-User-Agent. Abgerufene Wörterbuchinhalte und deine persönlichen Lerndaten werden lokal auf diesem Gerät gespeichert.

Actions:

- Start online lookup / Online-Abfrage starten
- Prepare manually / Manuell vorbereiten
- Cancel / Abbrechen

Do not request an API key.

The user may revoke saved online-lookup consent in Settings.

---

## 11. Automatic preparation

For every selected vocabulary item:

1. detect an explicit acronym expansion in the original text
2. check the local lexical cache
3. query the relevant Wiktionary provider when needed
4. parse direct lexical senses separately from grammatical form relations
5. keep the queried term when at least one suitable direct sense exists
6. only for a form-only entry, follow an explicit provider relation to a base lemma through the same cache/provider chain when supported
7. rank possible meanings
8. show the best result for confirmation

Supported relations are explicit singular, plural, third-person singular, past tense, past participle, present participle, comparative, and superlative forms. Direct senses outrank grammatical descriptions, so `data` remains `data` when a direct sense exists; form-only `systems`, `risks`, and `protects` may resolve to their provider-supplied base lemmas. Store the canonical learning term, encountered surface form, and grammatical relationship while keeping the original context unchanged. Use a visited set and fixed redirect-depth limit. Never infer a lemma with broad stemming: `risky`/`risk`, `protection`/`protect`, and `networking`/`network` remain separate without provider evidence.

Ordinary English words use the lowercase canonical lookup term (`Contact` -> `contact`, `Information` -> `information`, ordinary `NETWORK` -> `network`) while the exact displayed surface and coordinates remain unchanged. Acronyms and case-sensitive technical tokens retain their case (`IT` remains `IT`).

Progress example:

English:

> Preparing vocabulary — 7 of 10

German:

> Wörter werden vorbereitet — 7 von 10

Display:

- term
- token kind
- original highlighted context
- context navigation
- occurrence count
- acronym expansion where available
- selected translation
- selected definition
- alternative meanings
- a collapsed Source details / Quelldetails control retaining project, page/link, revision, attribution, and license metadata

Actions:

- Accept and continue / Übernehmen und weiter
- Choose another meaning / Andere Bedeutung wählen
- Edit / Bearbeiten
- Try again / Erneut versuchen only for a recoverable lookup outcome
- Manual entry / Manuell eingeben
- Mark as known / Als bekannt markieren
- Exclude from learning / Vom Lernen ausschließen
- Skip for now / Später
- End preparation / Vorbereitung beenden

Normal automatic preparation requires no typing when a usable result exists.

### 11.1 Several plausible meanings

English:

> Several meanings may fit this context.

German:

> Mehrere Bedeutungen könnten zu diesem Zusammenhang passen.

The user chooses an alternative or accepts the suggested result.

Use a bounded accessible dialog/listbox or mobile sheet, not a native single-line select. The closed value is at most two visual lines and approximately 160 characters. Alternative previews are approximately 240 characters and offer accessible full-text expansion. Full selected text remains in the definition card and persistence is never truncated. All children use bounded widths, wrapping, and `min-width: 0`; long unbroken values wrap anywhere. Escape and Android Back close the picker, focus returns to its invoking button, and safe areas are respected.

### 11.2 No result

English:

> No suitable dictionary result was found.

German:

> Es wurde kein passender Wörterbucheintrag gefunden.

Actions:

- Manual entry
- Mark as known
- Exclude from learning
- Skip for now

Do not fabricate an answer.

A failure for one word does not block the remaining batch.

In DEBUG only, a NotFound details section may show the displayed surface, vocabulary canonical term, normalized lookup term, source, mode, target, versioned cache key, provider request, and provider outcome. Release remains concise.

### 11.3 Lookup outcomes and retry

The explicit outcomes are `Success`, `NotFound`, `TransientFailure`, `PermanentFailure`, and `ParseFailure`. Try again is shown only for `TransientFailure`, including offline/timeout, HTTP 429, and transient HTTP 5xx. It is not shown for Success, NotFound, ParseFailure, or PermanentFailure. Do not show another-source actions until another provider actually exists.

### 11.4 Preparation dispositions and transition performance

- **Mark as known** (status `WordStatus.Known`) requires confirmation, stores the minimal PermanentlyKnown marker, means the vocabulary item is known and should not be learned/reviewed normally (distinct from exclusion), creates no cards, removes obsolete preparation/context/frequency data transactionally, updates document-cleanup eligibility, and advances exactly once.
- **Exclude from learning** (status `WordStatus.Ignored`) requires a scope explanation, stores a minimal exact exclusion marker that is not Known, creates no cards, excludes no related identity, removes obsolete preparation data, and advances exactly once.
- **Skip for now** removes the candidate only from the current batch, leaves it Unknown and Unprepared for future batches, and cannot repeat within the same session even when every item is skipped.

Back or Home pauses the active batch and preserves its method and current candidate. **End preparation** ends the batch: it is a neutral/secondary workflow action located in the bottom action bar. While its confirmation panel is open, competing disposition actions are suppressed. Confirmation ends the batch and its resumability: accepted items remain prepared and lasting Known/Ignored decisions are preserved, while unresolved and skipped items return to the Unknown/Unprepared backlog. It does not delete accepted learning content. The next entry shows the Automatic/Manual choice and creates no duplicate candidate for accepted vocabulary.

Accepting a loaded result performs no network request. Query only the current/next required state, prefetch at most one matching next lexical result with cancellation and deduplication, reject double submission, and delay the spinner until the transition is perceptible.

**Save-versus-progression recovery:** Successful acceptance is transactionally committed before retrieving the next candidate. If retrieving or loading the next candidate fails, the UI reports that the item was saved but the next item could not be loaded, and Retry executes progression/loading only without repeating acceptance.

DEBUG diagnostics measure validation, database transaction, prepared-meaning save, card creation, session update, next-candidate query, context loading, UI transition, and network work with a monotonic timer.

---

## 12. Manual preparation

Manual entry is the fallback preparation mode when no dictionary result is available or when the user chooses manual editing.

Display:
- candidate term and encountered surface form / token kind metadata, read-only
- original contexts while editing

Primary fields:
- **Normal Definition mode:** one primary multiline Definition field using shared `.text-area` styling.
- **Normal Translation mode:** one primary multiline Translation field using shared `.text-area` styling.
- **Legacy combined mode:** bounded two-field compatibility exception only.

The normal editor removes redundant form controls for canonical term, encountered form, and Additional Note.

Advanced options (collapsed by default):
- Acronym expansion (optional, shown when applicable)
- Accepted spelling aliases (optional, affects accepted typed answers; aliases are not extra cards)

Actions:
- Save and continue / Übernehmen und weiter
- Skip for now / Später
- Cancel manual entry / Abbrechen (returns to lookup result or lookup state for the current candidate)

Validation & error behavior:
- Empty Definition in Definition mode receives a dedicated localized error (`Please enter a definition.`).
- Empty Translation in Translation mode receives a dedicated localized error (`Please enter a translation.`).
- Input validation failures focus and reveal the relevant invalid field.
- Unexpected errors remain safely logged without exposing raw exception details.
- Save commits transactionally. If next-item loading fails after save, the UI provides progression-only Retry.

---

## 13. Daily new-word target and Preparation-to-Learning transition

Preparation is not capped by the daily new-word admission target. Users may continue preparing vocabulary beyond that day's remaining admission capacity, and prepared items remain available in the backlog for future admission.

The configured daily target governs only distinct genuinely-new Words admitted into Learning for the current learning day (default 5 for fresh installations, configurable range 1..50; grandfathered installations preserve their established limit).

Workflow rules:

- After each successful Preparation acceptance is durably committed, Preparation queries Learning readiness.
- When still-open genuinely-new demand is positive and eligible prepared backlog can fully satisfy it, Preparation automatically transitions to `/learn` without loading another candidate.
- When eligible prepared backlog is below open demand, normal Preparation candidate progression continues.
- When daily genuinely-new admission capacity is already exhausted, readiness evaluates to false; Preparation progression continues normally, allowing the user to prepare more words or return later without an automatic redirect loop.
- Dispositions (**Skip for now**, **Mark as known**, **Exclude from learning**) and **End preparation** do not query readiness or trigger automatic Learning transition.
- Extra prepared vocabulary exceeding daily admission capacity remains safely preserved in the backlog for future learning days.
- Due reviews, learned sibling cards, and Again repetitions never count against the daily new-word admission target.

---

## 14. Learning-session selection

When a learning session starts:

1. include all due cards, oldest due first
2. include admitted prepared new vocabulary up to the remaining daily new-word budget (prioritized by frequency)
3. order admitted new vocabulary by accepted frequency descending
4. generate enabled card directions for admitted vocabulary
5. avoid duplicate cards in the initial queue

An active session is persisted and resumable.

An active learning session does not block import.

Every successfully committed explicit user Again action appends exactly one repeat attempt at the deterministic tail of the current active learning-session queue, regardless of whether the source attempt was an ordinary card or an existing Again repeat. There is no arbitrary one-repeat-per-card or one-repeat-per-session cap, but repeats are never generated automatically: each additional repeat requires another explicit committed user Again rating. Existing queued cards remain ahead in deterministic queue order without reordering. While the scheduler persists the normal future due time and Learning/Relearning state for subsequent sessions, the incomplete repeat row remains active and presentable within the current session until its turn is reached. Session completion occurs only when no incomplete queue row remains.

### 14.1 Card direction and interaction mode

Card direction and interaction mode are independent axes.

**Card direction** decides which cards exist and how they are scheduled:

- Term to meaning
- Meaning to term
- Both directions (default)

Each direction keeps its own schedule. Two generated directions still count as one newly prepared vocabulary item. A direction does not by itself decide how the user answers: Meaning to term is not intrinsically a typed card, and Term to meaning is not intrinsically a reveal-and-rate card.

**Interaction mode** decides how the user answers the card that is presented:

- Reading — reveal the answer and self-rate the recall (section 15)
- Typing — type the expected answer and have it validated locally (section 16)

The interaction mode is resolved from the Learning mode setting (section 22):

- Reading resolves every card to the Reading interaction
- Typing resolves every card to the Typing interaction
- Automatic resolves each card from that card's own stored interaction progress

Interaction progress belongs to the individual card and its required accepted answers. Both directions of the same vocabulary item do not share one progress counter: each card keeps its own schedule and its own interaction progress.

Reading or Typing fixes only the interaction shown for the card in front of the user. The saved review still records what actually happened — whether the answer was typed and whether it was correct — and progress is recomputed from the card's complete review history after every rating. Selecting Automatic later may therefore resolve from reviews recorded while Reading or Typing was fixed. Changing the setting never rewrites past reviews.

Automatic transition behavior, as implemented for one card:

- progress starts in the Reading interaction
- in a Reading interaction, any rating other than Again counts as one successful recall; Again resets the recall counter
- after two consecutive successful recalls, that card's progress switches to the Typing interaction
- in a Typing interaction, a correct typed answer increases its typing-success counter and clears its failure counter; an incorrect typed answer clears the success counter and increases the failure counter
- after two consecutive incorrect typed answers, that card's progress returns to the Reading interaction and its counters are reset

A review card that has reached the 365-day maximum interval is a mastery review. Rated better than Again without achieving mastery, its next due date is extended once to that maximum. Mastery requires a correct typed answer on a mastery review that brings that answer to two consecutive typing successes. When all required answers of the current card satisfy the mastery rule, that card is retired. The other card direction remains independent and is not retired automatically. KnownFirst never claims mastery from elapsed time alone, and mastery does not replace the explicit permanent-known decision in section 18.

---

## 15. Reading interaction card

Shown when the resolved interaction mode is Reading.

Front:

- term
- original sentence
- target term highlighted
- context navigation where available
- Reveal answer

Before reveal, do not show:

- translation
- definition
- acronym expansion

Back:

1. acronym expansion when applicable
2. translation
3. definition
4. optional dictionary example
5. source

Context Previous/position/Next controls appear directly below the context sentence, not below the complete answer and rating area. Source metadata uses the compact collapsed Source details control.

Ratings:

- Again / Nochmal
- Hard / Schwer
- Good / Gut
- Easy / Einfach

There is no Skip rating.

The user may leave the session and continue later.

---

## 16. Typing interaction card

Shown when the resolved interaction mode is Typing.

Front:

- definition and/or translation
- optional context with the target hidden
- editable answer field
- Check answer

Context navigation, when present, also appears directly below this context sentence.

The expected response is:

- the canonical term
- or one explicitly accepted alias

For acronyms, the expected acronym may be required and is case-sensitive.

After submission show:

- entered answer
- correct answer
- readable character-level difference
- accepted aliases where relevant

### 16.1 Incorrect answer

Behavior:

- treat as Again
- persist immediately
- show the correct spelling
- schedule according to Again
- append one repeat attempt at the deterministic tail of the active session queue

### 16.2 Correct answer

Allow:

- Hard
- Good
- Easy

Each card direction keeps its own schedule, independent from the other direction and independent from the resolved interaction mode.

Do not use AI to grade long free-text definitions.

---

## 17. Ratings

### Again / Nochmal

Meaning:

- not recalled
- or typed incorrectly

Behavior:

- schedule in 10 minutes
- append one repeat attempt at the deterministic tail of the current active learning-session queue (including for repeated Again actions on existing repeats, without an arbitrary repeat cap)
- enter Learning or Relearning
- record a lapse where applicable

### Hard / Schwer

Meaning:

- recalled correctly with substantial effort

Behavior:

- schedule a short interval
- reduce ease where applicable

Hard must not be used for a failed recall.

### Good / Gut

Meaning:

- recalled correctly with normal effort

Behavior:

- schedule the normal interval

### Easy / Einfach

Meaning:

- recalled immediately and confidently

Behavior:

- schedule a longer interval

Intervals continue to grow. They do not end automatically after 7 or 14 days.

---

## 18. Permanent-known action

A card menu provides:

English:

> Mark permanently known

German:

> Dauerhaft als bekannt markieren

Confirmation:

English:

> Future reviews for this word will stop. Its personal definition, translation, contexts, frequency data, card schedules, and learning history may be deleted. A minimal known-word marker will remain so KnownFirst does not ask again.

German:

> Zukünftige Wiederholungen für dieses Wort werden beendet. Persönliche Definitionen, Übersetzungen, Kontexte, Häufigkeitsdaten, Kartenpläne und Lernhistorie können gelöscht werden. Ein minimaler Marker bleibt erhalten, damit KnownFirst nicht erneut danach fragt.

Actions:

- Mark permanently known
- Cancel

After confirmation:

- stop all card directions
- delete their scheduling state
- delete personal prepared content and context snapshots
- retain the minimal known marker
- update all affected documents
- trigger document cleanup

KnownFirst may suggest this action after long successful intervals, but never performs it automatically.

---

## 19. Learning-session completion

Show:

- cards reviewed
- Again count
- Hard count
- Good count
- Easy count
- next due review
- remaining unprepared Unknown vocabulary

Actions depend on state.

### 19.1 More unprepared vocabulary exists

Message:

English:

> All current reviews are complete. {count} unknown words are waiting for preparation.

German:

> Alle aktuellen Wiederholungen sind abgeschlossen. {count} unbekannte Wörter warten auf die Vorbereitung.

Actions:

- Prepare next words / Nächste Wörter vorbereiten
- Later / Später
- Change daily limit / Tageslimit ändern

Do not force navigation after the learning summary.

### 19.2 Nothing else is open

English:

> No more words are due right now.

German:

> Aktuell sind keine weiteren Wörter fällig.

Actions:

- Return Home
- Import Text

---

## 20. Document progress

For retained documents show understandable progress:

- title
- total unique reviewable vocabulary
- permanently known
- in learning
- unprepared Unknown
- completion percentage

Do not expose raw database IDs in normal UI.

A document is complete only when:

- no active review remains
- no unprepared Unknown relationship remains
- no active learning card remains
- every vocabulary relationship is PermanentlyKnown or excluded legacy data

---

## 21. Fully completed text

When the document is complete:

- delete the full original text
- delete sentence spans
- delete occurrences
- delete document-vocabulary relationships
- delete context snapshots
- delete obsolete prepared learning content
- delete obsolete schedule and frequency data
- retain only minimal permanently-known vocabulary markers

No context snapshot is retained for a fully completed text because no active word from that text still needs learning context.

The deletion is transactional.

---

## 22. Settings

Required settings:

### UI language

- System
- English
- German
- Russian

System follows the supported device language and falls back to English for an unsupported device language. The UI language is independent from the imported-text source language and from the translation target language.

### Appearance

- System
- Light
- Dark

### New words per day / Neue Wörter pro Tag

- 5 Recommended
- 1
- 10
- Custom (1..50)

Recommended default for fresh installations: 5 (legacy grandfathered installations retain their established value, such as 10)
Configurable range: 1..50

Help text explains that the setting limits new words admitted into daily learning so study remains manageable, while due reviews and preparation volume are not restricted by this limit.

### Card direction

- Term to meaning
- Meaning to term
- Both directions

Default: Both directions

Card direction is independent from Learning mode; see section 14.1.

### Learning mode

- Reading
- Typing
- Automatic

Default: Automatic

Reading resolves every card to the reveal-and-self-rate interaction. Typing resolves every card to the typed-answer interaction. Automatic resolves each card from that card's own stored interaction progress. Progress is recomputed from each card's complete review history after every rating whichever mode is selected; a fixed mode changes only the interaction that is shown. The transition rules are binding in section 14.1.

### Online dictionary lookup

- saved consent status
- when consent exists: revoke consent
- when consent is absent: the binding online-lookup disclosure and an explicit action to activate online dictionary lookup

Do not request or store a Wikimedia API key.

Portable archives never carry online-lookup consent. Importing an archive or resetting local data neither grants nor restores consent; the user must grant it again explicitly.

### Portable data

Export writes a portable `.kfarchive` archive to a destination the user chooses.

Before export, warn that an archive is not encrypted and may contain personal imported text and learning history.

Import selects a `.kfarchive` archive and always shows a read-only preview before anything is changed. The preview performs no mutation, creates no safety copy, and invokes no writer. It distinguishes:

- **restore** — the installation is empty and the archive would be restored into it
- **merge** — the installation already contains data and the archive would be merged non-destructively, with the counts of new, enriched, preserved-variant, and skipped items shown together with the explanation that local data is preserved and a validated safety copy is created before any change
- **no-change** — the archive would add nothing, for example on a repeated import; this is a successful outcome, not a failure, and it offers no mutating action

From the preview the user either confirms with an action labelled for the restore or merge case, or cancels. Cancelling changes nothing.

After a confirmed import, show the outcome: restored, merge applied with its counts and the safety-copy notice, or no change.

Required failure and refusal behavior:

- an archive that fails validation is refused and nothing is changed
- an unsupported target state is refused with a clear reason
- an active vocabulary review, preparation, or learning workflow blocks import
- a merge plan that is stale or no longer executable is rejected
- cancellation at any point leaves the installation unchanged
- a failure during the merge rolls back completely and never presents a false success

### Reset application data

Reset:

- user database
- settings
- learning state
- cache according to reset semantics
- language preference, then reapply supported device language
- theme to System
- preparation limit to default
- card direction to Both
- learning mode to Automatic

---

## 23. Home dashboard

Without a broad redesign, show:

- due cards
- prepared cards
- unprepared Unknown vocabulary
- active review progress where applicable
- active preparation progress where applicable
- active learning progress where applicable

Primary action follows the global workflow priority.

Do not make the user choose a route that the workflow can determine automatically.

---

## 24. Required user-facing error behavior

### Offline during automatic preparation

English:

> The dictionary service is unavailable. Cached results remain available.

German:

> Der Wörterbuchdienst ist nicht erreichbar. Bereits gespeicherte Ergebnisse bleiben verfügbar.

Actions:

- Retry
- Prepare manually
- Continue with remaining words
- Cancel

### Rate limited

English:

> The dictionary service asked KnownFirst to wait. Please try again shortly.

German:

> Der Wörterbuchdienst hat KnownFirst gebeten zu warten. Versuche es gleich noch einmal.

Respect the server-provided retry delay.

### Missing source context

Learning must not crash.

Show the term and prepared answer without the missing context.

### Database failure

Do not display a false success state.

Preserve the last committed state and offer Retry where safe.

---

## 25. Manual acceptance scenarios

### Scenario A: All vocabulary already known

1. import a text containing only PermanentlyKnown vocabulary
2. analyze
3. verify no review starts
4. verify no document remains
5. verify no counts change
6. verify the all-words-known message

### Scenario B: Review finishes with all Known

1. import a text with new candidate identities
2. mark every candidate Known
3. complete review
4. verify document and temporary analysis data are deleted
5. verify minimal known markers remain

### Scenario C: Automatic acronym preparation

Use:

```text
Information Technology (IT) protects information systems.
Multi-Factor Authentication (MFA) reduces authentication risk.
```

1. mark IT and MFA Unknown
2. start automatic preparation
3. verify long forms come from the imported text
4. verify dictionary definition or translation is added where available
5. verify no required manual typing
6. accept the result

### Scenario D: Frequency priority

Use a text where `network` appears five times and `encryption` once.

1. mark both Unknown
2. set preparation limit to 1
3. verify network is prepared first

### Scenario E: Duplicate contexts

Use:

```text
Security is important.
Security is important.
Security protects information.
```

Verify:

- occurrence count is 3
- unique displayed contexts are 2

### Scenario F: Recognition and spelling

1. prepare one vocabulary item with Both directions enabled
2. set Learning mode to Reading, then learn one card by revealing the answer and rating it
3. set Learning mode to Typing, then learn the remaining card
4. enter one wrong letter
5. verify readable correction
6. verify the wrong answer is Again
7. enter the correct answer later
8. verify the card directions retain independent schedules, independently of the resolved interaction mode

### Scenario G: Session resume

1. start a learning session
2. complete part of it
3. close the application
4. reopen
5. continue at the correct card without duplicating ratings

### Scenario H: Permanently known and document deletion

1. retain a document with one active learning word
2. mark that word permanently known
3. confirm cleanup
4. verify no future card remains
5. verify the completed document and context snapshots are deleted
6. verify the minimal known marker remains
7. reimport a text containing that word
8. verify KnownFirst does not ask again

---

## 26. MVP boundaries

The MVP does not require:

- API-key entry
- account creation
- synchronization
- analytics
- advertisements
- PDF import
- EPUB import
- website import
- handwriting recognition
- speech recognition
- AI grading of definitions
- local language model
- full offline dictionary packages
- FSRS implementation

The initial scheduler remains replaceable by FSRS later.
