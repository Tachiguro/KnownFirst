# KnownFirst Word Analysis

## Purpose

This document is the binding specification for text analysis, vocabulary-candidate creation, occurrence storage, sentence segmentation, context selection, and DEBUG-only explainability in KnownFirst.

The analysis must answer deterministically:

1. Which character ranges are sentences?
2. Which ranges are reviewable tokens?
3. Why was a token included or excluded?
4. Which occurrences belong to one vocabulary candidate?
5. Which exact single-sentence contexts are shown, and why?

## Original-text invariant

The accepted document is stored unchanged. Analysis must not alter capitalization, punctuation, whitespace, line endings, quotation marks, citations, hyphens, apostrophes, Unicode characters, or spelling.

All derived coordinates use .NET UTF-16 indices.

```csharp
document.Content.Substring(sentence.StartPosition, sentence.Length)
```

must equal the exact sentence, and:

```csharp
document.Content.Substring(occurrence.StartPosition, occurrence.Length)
```

must equal the exact surface form.

## Analysis pipeline

1. Preserve original input.
2. Calculate document fingerprint.
3. Segment exact sentence spans.
4. Detect excluded ranges such as URLs and emails.
5. Tokenize each sentence.
6. Classify tokens.
7. Include or exclude each token with a reason code.
8. Create exact occurrences.
9. Group compatible occurrences into candidates.
10. Deduplicate encountered forms.
11. Select unique single-sentence contexts.
12. Validate coordinate invariants.
13. Persist transactionally.
14. Produce DEBUG-only explanation data.

Do not tokenize an accepted document twice merely for persistence.

## Sentence segmentation

### One context equals one sentence

A review or learning context must contain exactly one stored sentence span. Several sentences may be available as separate contexts, but only one is displayed at a time.

### Boundaries

Initial terminators:

- `.`
- `!`
- `?`

Trailing closing quotes, apostrophes, parentheses, square brackets, and citation markers remain attached to the sentence.

A boundary exists after terminal punctuation followed by optional citation groups and then whitespace or end-of-document.

Example:

```text
It is part of information risk management.[1] It typically involves preventing unauthorized access.[2]
```

Expected spans:

```text
It is part of information risk management.[1]
```

```text
It typically involves preventing unauthorized access.[2]
```

### Abbreviation safeguards

Do not split inside:

- `e.g.`
- `i.e.`
- `etc.`
- `Mr.`
- `Mrs.`
- `Ms.`
- `Dr.`
- `Prof.`
- `No.`
- `U.S.`
- `U.K.`
- decimal values such as `3.14`

Abbreviation handling must be explicit and tested, not hidden in one opaque regular expression.

Any final non-empty remainder becomes a sentence.

A grammatically long sentence remains one sentence. Never concatenate neighboring sentences and never silently truncate the stored span.

## Token detection

Supported categories:

- Word
- Acronym
- Abbreviation
- TechnicalTerm

Examples that remain one token:

- `information`
- `Informationssicherheit`
- `IT`
- `MFA`
- `OAuth2`
- `IPv6`
- `SHA-256`
- `CVE-2026-12345`

Explicit conservative technical families are resolved during analysis:

- a valid `CVE-YYYY-NNNN...` occurrence maps to canonical learning identity `CVE` with `TokenKind.Acronym`; the occurrence retains the complete surface form, year, identifier, and exact coordinates
- `SHA-1`, `SHA-224`, `SHA-256`, `SHA-384`, and `SHA-512` map to canonical learning identity `SHA` with `TokenKind.Acronym`; each occurrence retains its variant and exact coordinates

Several valid SHA variants therefore share one review identity. The complete CVE instance does not create a separate learning card by default. These are closed pattern families, not generic suffix rules: `IPv6` and `OAuth2` remain unchanged.
The explicitly recognized bare acronyms `CVE` and `SHA` share those same family identities.

Preserve Unicode letters, combining marks, German umlauts, and `ß`.

Exclude with explicit reasons:

- whitespace
- punctuation-only values
- symbol-only values
- standalone numbers
- URLs
- email addresses

Uppercase alone must not permanently confirm an acronym.

Example reason codes:

- `IncludedUnicodeWord`
- `IncludedAcronymPattern`
- `IncludedTechnicalTokenPattern`
- `IncludedCveFamilyPattern`
- `IncludedShaFamilyPattern`
- `ExcludedUrl`
- `ExcludedEmailAddress`
- `ExcludedStandaloneNumber`
- `ExcludedPunctuationOnly`
- `ExcludedSymbolOnly`

## Candidate identity

A candidate is one review decision. An occurrence is one exact appearance.

Repeated words create one candidate and many occurrences.

For `TokenKind.Word`, case-only variants normally share one candidate:

```text
Information
information
INFORMATION
```

Case-sensitive identities remain separate:

```text
IT != it
US != us
```

Do not use broad stemming. Do not merge merely by suffix removal:

- `risk` and `risky`
- `protect` and `protection`
- `network` and `networking`

Provider-confirmed lemma resolution is a later lexical-enrichment step. It follows only explicit plural, third-person singular, past tense, past participle, present participle, comparative, or superlative relations naming a base lemma. The base is looked up through the existing cache/provider chain with a visited set and fixed redirect-depth limit. The prepared item stores the base learning term, encountered surface form, and relationship while its context stays unchanged.

This does not weaken the no-stemming rule. `risky`/`risk`, `protection`/`protect`, and `networking`/`network` remain separate without explicit provider evidence.

### German coordinated compounds

For hyphenated process coordinations like:

```text
Arbeits-, Qualitäts-, Sicherheits- und Datenschutzprozesse
```

The canonical vocabulary identities are normalized to their full forms:

- `Arbeitsprozess`
- `Qualitätsprozess`
- `Sicherheitsprozess`
- `Datenschutzprozess`

Crucially, the **original surface forms and coordinates** remain unchanged and fully traceable. For example, the `TokenOccurrence` for the first token retains `SurfaceForm = "Arbeits-"` (including the hyphen) and points to its exact original length, ensuring the original text invariant is strictly preserved.

## Conservative German derived compound candidates

This section defines the binding contract for optional, lexicon-backed decomposition of German compound words into derived vocabulary candidates.

### Preconditions (all must hold)

1. Source text language is German.
2. The feature is explicitly enabled via `EnhancedTermRecognitionEnabled` (default OFF).
3. An `IGermanLexicon` instance is supplied; without a lexicon the feature is inactive and existing behavior is unchanged.

### Decomposition rule

The decomposer attempts to split a German compound into an ordered sequence of **2 to 4 components**, each fully confirmed by the lexicon. Standalone tokens outside a recognized compound are never affected — this rule applies only while decomposing a candidate compound word.

- Every candidate component span must be at least 2 characters long before any fallback interpretation is considered.
- Every component except the final (head) one may resolve as any supported lexicon category (`Noun`, `Verb`, `Adjective`); the final component must resolve specifically as `GermanLexemeCategory.Noun`.
- For each component span, **literal lexicon resolution is attempted first** (the exact substring, or its first-letter upper-cased form). A successful literal match wins outright for that span; no fallback interpretation is even considered once literal resolution succeeds.
- Only when literal resolution fails may a bounded, lexicon-confirmed **fallback** be considered: stripping one candidate suffix from the end of the span and re-resolving the remainder (see "Shipped fallback suffixes" below). Fallback resolution is single-step — a stripped remainder is never itself stripped again — and every fallback result must independently resolve through the same `IGermanLexicon` contract as a literal match. This is not general stemming or heuristic morphology: only the explicitly shipped, bounded suffix set is ever tried.
- A word that itself resolves as a single valid lexicon entry, with no genuine split, is **not** treated as a one-component "decomposition" of itself — a decomposition always requires splitting into at least 2 components.
- **Ambiguity fails closed** in every one of these cases, without any preference/tie-break heuristic:
  - zero complete valid partitions of the whole word exist;
  - more than one complete valid partition exists (whether differing in split position, component count, or fallback interpretation);
  - more than one genuinely distinct, independently lexicon-confirmed fallback interpretation exists for the same component span.

### Whole-compound behavior

The source compound word always remains a **Direct** vocabulary candidate with its full original identity and all its `TokenOccurrence` rows. Decomposition never removes or demotes it.

### Derived candidate contract

For each accepted decomposition the decomposer produces one derived candidate for every ordered component (2 to 4, per the bound above):

- **Provenance kind:** `CandidateProvenanceKind.DerivedFromCompound`.
- **Identity:** the canonical lexicon lemma for that component (e.g. `schreiben`, `Maschine`, `Arbeit`, `Griff`).
- **No fabricated literal `TokenOccurrence` rows:** derived candidates do not insert synthetic occurrence rows into the database. Their evidence is recorded through `DerivedTermEvidence`.
- **`DerivedTermEvidence` fields retained (unchanged by multi-component/fallback support):**
  - source compound identity (the whole compound's learning term);
  - exact source surface form as it appears in the original text;
  - whole-compound start position and length within the document — always the **complete source-compound occurrence**, never a sub-span of it, regardless of which component or how many components were derived;
  - sentence order (to preserve derivation context);
  - the component's literal or fallback-resolved form (`ComponentForm`). This field carries **no independent source-coordinate semantics** — it is informational text only, never used to compute or validate a position/length in the document; the source-position truth is always the whole-compound occurrence above.

### Candidate ordering and collision

- Derived candidates are **appended** deterministically after all Direct candidates; existing Direct order is unchanged.
- If a derived component identity collides with an existing Direct identity, the **Direct candidate wins** and the derived candidate is suppressed.
- If multiple occurrences of the same source compound produce equivalent derivation groups, they are **grouped by identity** with multiple `DerivedTermEvidence` entries (one per occurrence), mirroring how Direct candidates accumulate multiple `TokenOccurrence` rows.

### Shipped fallback suffixes

The fallback mechanism is **one unified, closed suffix-stripping path** — it does not run two independently executed mechanisms for "linking elements" and "de-inflection." The currently shipped candidate-suffix set is exactly:

- `s`
- `es`
- `e`

Each shipped suffix is backed by a concrete, lexicon-confirmed example (see "Examples" below). Other suffixes from the broader German linking/inflection inventory (`n`, `en`, `er`) were evaluated during planning but are **not** shipped in this package: no sufficiently conservative, lexicon-backed justification was established for them. A compound that would only decompose via one of these unshipped suffixes fails closed rather than guessing.

### Scope boundary — what the decomposer does NOT do

- No broad stemming or general suffix removal — only the small, fixed, shipped suffix set (`s`, `es`, `e`) is ever tried, and only as a fallback after literal resolution fails.
- No morphological guessing beyond that fixed set.
- No network, provider, or online lookup.
- No `n`, `en`, or `er` linking/inflection suffixes (evaluated but intentionally not shipped).
- No umlaut-mutating fallback forms.
- No component span shorter than 2 characters.
- No more than 4 components in one decomposition.
- No stacking of more than one fallback interpretation on the same component (a stripped remainder is never stripped again).
- No multi-word or phrase decomposition.
- No standalone-token normalization — this mechanism only ever applies while decomposing a recognized compound word, never to an ordinary reviewed token.
- No decomposition when the lexicon is absent or the feature is disabled.

### Examples

**Accepted (literal resolution only):**
- `Schreibmaschine` → `schreiben` (lexicon: verb infinitive lemma) + `Maschine` (lexicon: noun)
- `Waschmaschine` → `waschen` + `Maschine`

**Accepted (via the shipped `s`/`es` linking-element fallback):**
- `Arbeitszimmer` → `Arbeit` + `Zimmer` (linking `s` stripped from `Arbeits-`)
- `Sicherheitsmanagement` → `Sicherheit` + `Management` (linking `s` stripped from `Sicherheits-`)
- `Bundesland` → `Bund` + `Land` (linking `es` stripped from `Bundes-`)

**Accepted (via the shipped `e` de-inflection fallback):**
- `Fenstergriffe` → `Fenster` + `Griff` (plural surface form `griffe` normalizes to the lexicon-confirmed singular lemma `Griff` by stripping `e`)

**Fail-closed:**
- A compound that would only decompose via an unshipped `n`/`en`/`er` linking or de-inflection suffix.
- A word that itself resolves as a single lexicon entry, with no genuine multi-component split (e.g. `Zimmer` alone).
- A compound admitting more than one complete valid partition, or a component span admitting more than one genuinely distinct fallback interpretation.

### Production lexicon and application-integration status

A production, offline `IGermanLexicon` implementation (`GeneratedGermanLexicon`, backed by `KnownFirst.Core/Text/German/Assets/german-lexicon.v2.kfgl`) exists and is wired into the application's `TextReviewService` analysis path, gated behind the persisted `EnhancedTermRecognitionEnabled` setting exposed in Settings. This decomposer contract — including the 2–4 component bound and the shipped `s`/`es`/`e` fallback set described above — is merged to `master` via PR #134 (merge commit `6c7a89ed6b4b0fc7701fdca8ec85a38b91bbeeb5`); see [docs/PROJECT_STATE.md](PROJECT_STATE.md) for exact provenance and counts. It is not yet packaged into a shipped Windows/Android build. Every rule above remains a description of the decomposer's contract against *any* `IGermanLexicon` instance, not a claim that this production lexicon is active in a release build.

The post-review-completion lifecycle semantics in "Post-review derived-evidence context lifecycle" below (German Enhanced Term Recognition Package 5A) are independently reviewed and approved (0 BLOCKER / 0 MAJOR / 0 MINOR) but still an **uncommitted working-tree candidate** on branch `fix/german-derived-lifecycle-integrity-v1`; see [docs/CURRENT_WORK.md](CURRENT_WORK.md) for exact current lifecycle status.

### Post-review derived-evidence context lifecycle

This subsection extends the derived-candidate contract above with what happens after a review decision, once a derived candidate is decided Unknown.

- A derived candidate still never receives a fabricated `TokenOccurrence`, before or after review completion.
- Once a derived candidate is decided Unknown, its context source for Preparation is the retained `DerivedTermEvidence` — not a synthetic occurrence. The required `ReviewCandidate` and `DerivedTermEvidence` may survive normal review-session completion specifically so this context remains recoverable; see [docs/DATABASE_CONTRACT.md](DATABASE_CONTRACT.md) "Schema-11 Derived-Term Evidence Contract" for the persistence-level lifecycle.
- The target/source coordinates used for that context remain the complete real source-compound occurrence — never a fabricated sub-span for the derived component — exactly as the pre-review-completion contract already requires.
- Preparation prefers ordinary occurrence-based context when at least one valid occurrence context exists; it falls back to derived evidence only when zero valid occurrence contexts remain. This applies identically to the review-words display path and the Accept path.
- Invalid coordinates or a substring mismatch against the real document/sentence text fail closed (no context produced from that evidence row), consistent with the existing coordinate-validation invariants above.
- Direct-vs-Derived identity resolution and prior-Known suppression are unchanged by this lifecycle: a permanently Known identity (whether established directly or through a prior derived occurrence) still suppresses a later derived candidate for the same identity.
- When the retaining word later leaves the Unknown lifecycle through MarkKnown or Exclude, its retained evidence is cleaned up and no longer contributes context.

## Encountered forms

Encountered forms show genuinely distinct variants, not case duplicates.

For ordinary words:

1. Unicode-normalize comparison values.
2. Compare case-insensitively.
3. Keep one representative for case-only variants.
4. Prefer lowercase when available.
5. Otherwise keep the first original form.
6. Preserve deterministic first-seen order.

Example:

```text
Information information INFORMATION
```

Displayed:

```text
information
```

For acronyms, abbreviations, and case-sensitive technical identities, preserve meaningful case distinctions.

## Occurrences

Every occurrence stores:

- document ID
- candidate ID
- sentence ID
- absolute start
- length
- exact surface form or verifiable reference
- occurrence order
- explicit technical family and CVE year/identifier or SHA variant when applicable

Occurrence count equals actual appearances. Context deduplication never reduces occurrence count.

## Context selection

The context is the exact sentence span containing the occurrence. Never collect surrounding sentences after sentence segmentation.

The UI receives:

- one current sentence
- Previous context availability
- `Context X of Y`
- Next context availability

Deduplicate contexts per candidate by a comparison fingerprint:

1. trim outer whitespace for comparison only
2. normalize line endings
3. collapse repeated whitespace
4. Unicode-normalize
5. preserve diacritics
6. retain the first exact original sentence for display

Select at most three unique contexts in document order.

For each context retain diagnostics for:

- sentence ID/order
- sentence start/length
- occurrence start/length
- target start relative to sentence
- exact sentence
- exact target substring
- selected/rejected reason
- duplicate fingerprint

## DEBUG-only explainability

Detailed analysis diagnostics are populated and surfaced only in DEBUG builds.

On Review Words, show:

- `Analysis details`
- German: `Analysedetails`

The button opens candidate details without changing review progress.

Provide a document-level DEBUG analysis view containing:

### Document summary

- title
- language
- character length
- fingerprint
- sentence count
- included token count
- excluded token count
- candidate count
- occurrence count

### Sentence spans

- order
- start
- length
- exact text
- boundary reason
- substring-invariant status

### Token decisions

- raw surface form
- start
- length
- normalized value
- token kind
- included/excluded
- reason code
- human-readable explanation
- sentence ID/order

### Candidate grouping

- display term
- comparison key
- token kind
- occurrence count
- forms before deduplication
- forms after deduplication
- grouping reason

### Context selection

- all occurrence sentences
- selected contexts
- rejected duplicates
- fingerprints
- coordinates
- target substring
- selection/rejection reason

Explanations must be human-readable, for example:

```text
Grouped with candidate "information" because both tokens are ordinary words and differ only by capitalization.
```

```text
Sentence boundary created after "." followed by citation "[1]" and whitespace.
```

```text
Context rejected because its whitespace-normalized sentence matches the first retained context.
```

Provide:

- `Copy analysis report`
- German: `Analysebericht kopieren`

Release builds must not expose the routes, buttons, raw coordinates, or report.

## Validation invariants

Persistence fails transactionally if any condition is false:

1. sentence ranges lie inside the document
2. occurrence ranges lie inside the document
3. every occurrence belongs to exactly one sentence
4. occurrence substring equals surface form
5. selected context equals exactly one sentence span
6. target range lies inside the context
7. target substring equals displayed occurrence
8. occurrence count equals persisted rows
9. encountered forms contain no comparison duplicates
10. selected contexts contain no normalized duplicates

Diagnostics identify the failed invariant.

## Required examples

### Citation-separated sentences

```text
It is part of information risk management.[1] It typically involves preventing unauthorized access.[2]
```

Expected: two spans and two separately selectable contexts.

### Multiple cited sentences

```text
Protected information may take any form.[2] Information security protects confidentiality.[3] It also supports availability.[4]
```

Expected: three spans; never one combined context.

### Encountered forms

```text
Information security protects information. INFORMATION remains available.
```

Expected candidate: `information`; displayed encountered forms: `information`; occurrences: 3.

### Case-sensitive distinction

```text
IT protects systems. it remains available.
```

Expected separate candidates `IT` and `it`.

### Duplicate contexts

```text
Security protects data. Security protects data. Security protects networks.
```

Expected: occurrence count 3; unique contexts 2.

## Required automated tests

Sentence segmentation:

- `.`, `!`, `?`
- `[1]`
- `[2][3]`
- closing quotes and parentheses
- `e.g.`, `i.e.`, `U.S.`
- `3.14`
- final sentence without punctuation

Coordinates:

- sentence substring
- occurrence substring
- target within context
- Unicode/umlaut offsets
- technical-token offsets

Candidate identity:

- case-only ordinary words group
- Information/information/INFORMATION displays once
- IT/it and US/us remain separate
- no broad stemming
- CVE identifiers map to `CVE`
- supported SHA variants map to `SHA`
- `IPv6` and `OAuth2` remain unchanged

Contexts:

- one context equals one sentence
- cited adjacent sentences remain separate
- duplicate sentences deduplicate
- occurrence count remains unchanged
- maximum three contexts
- deterministic order

Diagnostics:

- sentence boundary has a reason
- included/excluded token has a reason
- grouping has a reason
- each CVE/SHA extraction has a human-readable family reason
- duplicate rejection has a reason
- DEBUG-only UI unavailable in Release where practical

Conservative German compound decomposition:

- feature inactive when disabled or no lexicon supplied; existing behavior unchanged
- unambiguous literal split accepted (e.g. `Schreibmaschine` → `schreiben` + `Maschine`)
- unambiguous split via the shipped `s`/`es` linking-element fallback accepted (e.g. `Arbeitszimmer` → `Arbeit` + `Zimmer`, `Sicherheitsmanagement` → `Sicherheit` + `Management`, `Bundesland` → `Bund` + `Land`)
- unambiguous split via the shipped `e` de-inflection fallback accepted (e.g. `Fenstergriffe` → `Fenster` + `Griff`)
- literal resolution wins outright even when a fallback interpretation would also independently resolve for the same span
- valid unique 3-component and 4-component decompositions accepted in order
- a decomposition requiring more than 4 components fails closed
- a component span shorter than 2 characters is never attempted
- the final/head component must resolve as `GermanLexemeCategory.Noun`; a compound whose only possible final component resolves as a different category fails closed
- a word that itself resolves as a single lexicon entry, with no genuine split, is not accepted as a one-component decomposition
- ambiguous split (multiple valid complete partitions, of the same or different component counts) produces no derived candidates
- more than one genuinely distinct fallback interpretation for the same component span produces no derived candidates for that compound
- an unshipped linking/de-inflection suffix (`n`, `en`, `er`) never causes a match; the compound fails closed
- source compound remains a Direct candidate with unchanged identity and occurrences
- derived components carry `CandidateProvenanceKind.DerivedFromCompound`
- derived candidates have no fabricated literal `TokenOccurrence` rows
- `DerivedTermEvidence` retains source compound identity, surface form, start/length, sentence order, and component form, and always points to the complete source-compound occurrence regardless of component count
- multiple occurrences of the same compound group by identity with multiple evidence entries
- Direct identity wins over colliding Derived identity
- derived candidates appended after all Direct candidates; Direct order unchanged

## Scope boundary

This document covers text analysis and context selection. It does not define dictionary ranking, source-attribution presentation, learning-mode selection, spelling rendering, or spaced-repetition intervals.
