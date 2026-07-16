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

Provider-confirmed lemma resolution is a later lexical-enrichment step.

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
- duplicate rejection has a reason
- DEBUG-only UI unavailable in Release where practical

## Scope boundary

This document covers text analysis and context selection. It does not define dictionary ranking, source-attribution presentation, learning-mode selection, spelling rendering, or spaced-repetition intervals.
