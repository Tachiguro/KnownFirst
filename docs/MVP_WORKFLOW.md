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
2. **Import Text / Text importieren**
3. **Settings / Einstellungen**

The application may retain a Home dashboard, but the principal action priority remains Learn first.

Do not show these as permanent primary-navigation items:

- Review Words
- Prepare Words

They remain internal workflow routes.

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
- explanation language: English or German
- Save and analyze

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

Preparation is a batch workflow, not a permanent primary-navigation page.

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

Batch size uses the configured new-vocabulary limit.

Default: 10  
Hard maximum: 50

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
4. parse structured meanings
5. rank possible meanings
6. show the best result for confirmation

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
- source

Actions:

- Accept and continue / Übernehmen und weiter
- Choose another meaning / Andere Bedeutung wählen
- Edit / Bearbeiten
- Retry / Erneut versuchen
- Manual entry / Manuell eingeben
- Skip for now / Später
- Cancel preparation / Vorbereitung beenden

Normal automatic preparation requires no typing when a usable result exists.

### 11.1 Several plausible meanings

English:

> Several meanings may fit this context.

German:

> Mehrere Bedeutungen könnten zu diesem Zusammenhang passen.

The user chooses an alternative or accepts the suggested result.

### 11.2 No result

English:

> No suitable dictionary result was found.

German:

> Es wurde kein passender Wörterbucheintrag gefunden.

Actions:

- Retry
- Manual entry
- Skip for now

Do not fabricate an answer.

A failure for one word does not block the remaining batch.

---

## 12. Manual preparation

Manual entry is optional fallback behavior.

Fields:

- acronym expansion, optional
- translation, optional when definition is available
- definition, required
- additional note, optional
- accepted answer aliases, optional

Show original contexts while editing.

Actions:

- Save and continue
- Skip for now
- Cancel

Do not allow an empty learning item.

---

## 13. Preparation limit reached

When the configured batch limit is reached:

English:

> You prepared 10 new words. Learn these words before adding more, or change the limit in Settings.

German:

> Du hast 10 neue Wörter vorbereitet. Lerne diese Wörter zuerst oder ändere das Limit in den Einstellungen.

Actions:

- Start learning / Lernen starten
- Change limit / Limit ändern
- Return Home / Zur Startseite

The displayed number uses the configured limit.

The user may increase the limit up to 50.

Due reviews never count against this limit.

---

## 14. Learning-session selection

When a learning session starts:

1. include all due cards, oldest due first
2. include prepared new vocabulary for the current batch
3. order new vocabulary by accepted frequency descending
4. generate enabled card directions
5. avoid duplicate cards in the initial queue

An active session is persisted and resumable.

An active learning session does not block import.

A card rated Again may reappear once at the end of the current session.

Repeated Again ratings must not create an endless session.

---

## 15. Term-to-meaning card

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

Ratings:

- Again / Nochmal
- Hard / Schwer
- Good / Gut
- Easy / Einfach

There is no Skip rating.

The user may leave the session and continue later.

---

## 16. Meaning-to-term spelling card

Front:

- definition and/or translation
- optional context with the target hidden
- editable answer field
- Check answer

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
- allow one same-session repetition

### 16.2 Correct answer

Allow:

- Hard
- Good
- Easy

The spelling direction has its own schedule, independent from recognition.

Do not use AI to grade long free-text definitions.

---

## 17. Ratings

### Again / Nochmal

Meaning:

- not recalled
- or typed incorrectly

Behavior:

- schedule in 10 minutes
- allow one reappearance at the end of the current session
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

> All current reviews are complete. More unknown words are waiting for preparation.

German:

> Alle aktuellen Wiederholungen sind abgeschlossen. Weitere unbekannte Wörter warten auf die Vorbereitung.

Actions:

- Prepare next frequent words
- Later
- Change limit

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

- English
- German

### Appearance

- System
- Light
- Dark

### New vocabulary per preparation batch

- 5
- 10
- 20
- 30
- 50

Default: 10  
Maximum: 50

### Card direction

- Term to meaning
- Meaning to term
- Both directions

Default: Both directions

### Online dictionary lookup

- saved consent status
- revoke consent

Do not request or store a Wikimedia API key.

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

1. prepare one vocabulary item
2. learn Term to meaning
3. reveal answer and rate
4. learn Meaning to term
5. enter one wrong letter
6. verify readable correction
7. verify the wrong answer is Again
8. enter the correct answer later
9. verify the card directions retain independent schedules

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
