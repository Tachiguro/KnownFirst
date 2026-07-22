using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KnownFirst.Core.Text;

public sealed partial class TextAnalyzer
{
    private static readonly HashSet<string> KnownAcronyms = new(StringComparer.Ordinal)
    {
        "AI",
        "CVE",
        "HTML",
        "IP",
        "IT",
        "SHA",
        "US"
    };

    private readonly ISentenceSegmenter _sentenceSegmenter;

    public TextAnalyzer()
        : this(new DeterministicSentenceSegmenter())
    {
    }

    public TextAnalyzer(ISentenceSegmenter sentenceSegmenter)
    {
        _sentenceSegmenter = sentenceSegmenter ?? throw new ArgumentNullException(nameof(sentenceSegmenter));
    }

    public TextAnalysisResult Analyze(string content, string? sourceLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var sentences = ExtractSentenceSpans(content);
        var extraction = ExtractOccurrences(content, sentences, sourceLanguage);
        var normalizedOccurrences = GermanVocabularyNormalizer.Normalize(
            content,
            sourceLanguage,
            extraction.Occurrences);
        var candidates = normalizedOccurrences
            .GroupBy(occurrence => occurrence.Identity, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedOccurrences = group.OrderBy(occurrence => occurrence.Order).ToArray();
                var surfaceForms = orderedOccurrences
                    .GroupBy(occurrence => occurrence.SurfaceForm, StringComparer.Ordinal)
                    .ToDictionary(
                        formGroup => formGroup.Key,
                        formGroup => formGroup.Count(),
                        StringComparer.Ordinal);
                var first = orderedOccurrences[0];

                return new VocabularyCandidate(
                    first.Identity,
                    first.CanonicalTerm ?? first.SurfaceForm,
                    first.Kind,
                    surfaceForms,
                    orderedOccurrences);
            })
            .OrderBy(candidate => candidate.Occurrences[0].Order)
            .ToArray();

        var result = new TextAnalysisResult(sentences, candidates, normalizedOccurrences.Count);

#if DEBUG
        var candidateGroups = candidates.Select(CreateCandidateGroupingAnalysis).ToArray();
        var contexts = candidates.SelectMany(candidate => ContextSelectionPolicy.Select(
                content,
                sentences,
                candidate.Occurrences,
                candidate.Identity))
            .ToArray();
        result = result with
        {
            Diagnostics = new TextAnalysisDiagnostics(
                extraction.Decisions,
                candidateGroups,
                contexts,
                AnalysisInvariantValidator.Validate(content, result))
        };
#endif

        return result;
    }

    public IReadOnlyList<TextSpan> ExtractSentenceSpans(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return _sentenceSegmenter.Segment(content);
    }

    private static ExtractionResult ExtractOccurrences(
        string content,
        IReadOnlyList<TextSpan> sentences,
        string? sourceLanguage)
    {
        var excludedRanges = CreateExcludedRanges(content);
        var occurrences = new List<TokenOccurrence>();
        var decisions = new List<TokenAnalysisDecision>();
        var excludedIndex = 0;
        var sentenceIndex = 0;
        var index = 0;

        while (index < content.Length)
        {
            while (excludedIndex < excludedRanges.Count && excludedRanges[excludedIndex].End <= index)
            {
                excludedIndex++;
            }

            if (excludedIndex < excludedRanges.Count && excludedRanges[excludedIndex].Start == index)
            {
                var excluded = excludedRanges[excludedIndex];
                var rawValue = content.Substring(excluded.Start, excluded.Length);
                decisions.Add(new TokenAnalysisDecision(
                    rawValue,
                    excluded.Start,
                    excluded.Length,
                    rawValue.Normalize(NormalizationForm.FormC),
                    null,
                    false,
                    excluded.ReasonCode,
                    excluded.Explanation,
                    FindSentenceOrder(sentences, excluded.Start, excluded.Length)));
                index = excluded.End;
                excludedIndex++;
                continue;
            }

            if (char.IsWhiteSpace(content[index]))
            {
                var whitespaceEnd = index + 1;
                while (whitespaceEnd < content.Length && char.IsWhiteSpace(content[whitespaceEnd]))
                {
                    whitespaceEnd++;
                }

                var rawWhitespace = content.Substring(index, whitespaceEnd - index);
                decisions.Add(new TokenAnalysisDecision(
                    rawWhitespace,
                    index,
                    rawWhitespace.Length,
                    string.Empty,
                    null,
                    false,
                    AnalysisReasonCodes.ExcludedWhitespace,
                    "Whitespace separates tokens and is not reviewable vocabulary.",
                    null));
                index = whitespaceEnd;
                continue;
            }

            if (!TryReadRune(content, index, out var rune, out var runeLength)
                || !IsLetterOrDigit(rune))
            {
                var excludedEnd = index + runeLength;
                while (excludedEnd < content.Length
                       && (excludedIndex >= excludedRanges.Count
                           || excludedRanges[excludedIndex].Start != excludedEnd)
                       && TryReadRune(content, excludedEnd, out var next, out var nextLength)
                       && !IsLetterOrDigit(next)
                       && !Rune.IsWhiteSpace(next))
                {
                    excludedEnd += nextLength;
                }

                var rawValue = content.Substring(index, excludedEnd - index);
                var isPunctuationOnly = rawValue.EnumerateRunes().All(IsPunctuation);
                decisions.Add(new TokenAnalysisDecision(
                    rawValue,
                    index,
                    rawValue.Length,
                    rawValue.Normalize(NormalizationForm.FormC),
                    null,
                    false,
                    isPunctuationOnly
                        ? AnalysisReasonCodes.ExcludedPunctuationOnly
                        : AnalysisReasonCodes.ExcludedSymbolOnly,
                    isPunctuationOnly
                        ? "Punctuation-only text is not reviewable vocabulary."
                        : "Symbol-only text is not reviewable vocabulary.",
                    FindSentenceOrder(sentences, index, rawValue.Length)));
                index = excludedEnd;
                continue;
            }

            var tokenStart = index;
            var tokenEnd = index;
            var hasLetter = false;
            var hasDigit = false;
            var hasInternalPeriod = false;
            var hasHyphen = false;

            while (tokenEnd < content.Length)
            {
                if (!TryReadRune(content, tokenEnd, out var current, out var currentLength))
                {
                    break;
                }

                if (IsLetter(current))
                {
                    hasLetter = true;
                    tokenEnd += currentLength;
                    continue;
                }

                if (Rune.IsDigit(current))
                {
                    hasDigit = true;
                    tokenEnd += currentLength;
                    continue;
                }

                if (IsMark(current))
                {
                    tokenEnd += currentLength;
                    continue;
                }

                if (IsConnector(current)
                    && TryReadRune(content, tokenEnd + currentLength, out var connectorNext, out _)
                    && IsLetterOrDigit(connectorNext))
                {
                    hasInternalPeriod |= current.Value == '.';
                    hasHyphen |= IsHyphen(current);
                    tokenEnd += currentLength;
                    continue;
                }

                break;
            }

            var tokenWithoutTrailingPeriod = content.Substring(tokenStart, tokenEnd - tokenStart);
            if (tokenEnd < content.Length
                && content[tokenEnd] == '.'
                && (hasInternalPeriod || AbbreviationPolicy.IsKnownStem(tokenWithoutTrailingPeriod)))
            {
                tokenEnd++;
            }

            var surfaceForm = content.Substring(tokenStart, tokenEnd - tokenStart);
            if (!hasLetter)
            {
                decisions.Add(new TokenAnalysisDecision(
                    surfaceForm,
                    tokenStart,
                    surfaceForm.Length,
                    surfaceForm.Normalize(NormalizationForm.FormC),
                    null,
                    false,
                    AnalysisReasonCodes.ExcludedStandaloneNumber,
                    "Standalone numbers are not reviewable vocabulary.",
                    FindSentenceOrder(sentences, tokenStart, surfaceForm.Length)));
                index = Math.Max(tokenEnd, index + runeLength);
                continue;
            }

            var technicalFamily = TechnicalTokenFamilyPolicy.Resolve(surfaceForm);
            var kind = technicalFamily?.TokenKind
                ?? Classify(surfaceForm, hasDigit, hasHyphen, hasInternalPeriod);
            var identityResolution = VocabularyIdentityPolicy.Resolve(surfaceForm, kind, sourceLanguage);
            var identity = technicalFamily?.Identity ?? identityResolution.Identity;
            var canonicalTerm = technicalFamily?.CanonicalTerm ?? identityResolution.CanonicalTerm;

            while (sentenceIndex < sentences.Count && tokenStart >= sentences[sentenceIndex].EndPosition)
            {
                sentenceIndex++;
            }

            int? containingSentenceOrder = null;
            if (sentenceIndex < sentences.Count
                && tokenStart >= sentences[sentenceIndex].StartPosition
                && tokenEnd <= sentences[sentenceIndex].EndPosition)
            {
                containingSentenceOrder = sentences[sentenceIndex].Order;
                occurrences.Add(new TokenOccurrence(
                    surfaceForm,
                    identity,
                    kind,
                    tokenStart,
                    tokenEnd - tokenStart,
                    occurrences.Count,
                    containingSentenceOrder.Value,
                    canonicalTerm,
                    technicalFamily?.Family ?? TechnicalTokenFamily.None,
                    technicalFamily?.InstanceYear,
                    technicalFamily?.InstanceIdentifier,
                    technicalFamily?.Variant));
            }

            decisions.Add(new TokenAnalysisDecision(
                surfaceForm,
                tokenStart,
                surfaceForm.Length,
                EncounteredFormPolicy.CreateComparisonKey(kind, surfaceForm),
                kind,
                true,
                technicalFamily?.ReasonCode ?? GetInclusionReasonCode(kind),
                technicalFamily?.Explanation ?? GetInclusionExplanation(kind),
                containingSentenceOrder));
            index = tokenEnd;
        }

        return new ExtractionResult(occurrences, decisions);
    }

    private static IReadOnlyList<ExcludedRange> CreateExcludedRanges(string content)
    {
        var candidates = UrlRegex().Matches(content)
            .Select(match => new ExcludedRange(
                match.Index,
                match.Length,
                AnalysisReasonCodes.ExcludedUrl,
                "URLs are excluded from reviewable vocabulary."))
            .Concat(EmailAddressRegex().Matches(content).Select(match => new ExcludedRange(
                match.Index,
                match.Length,
                AnalysisReasonCodes.ExcludedEmailAddress,
                "Email addresses are excluded from reviewable vocabulary.")))
            .OrderBy(range => range.Start)
            .ThenByDescending(range => range.Length)
            .ToArray();
        var ranges = new List<ExcludedRange>();

        foreach (var candidate in candidates)
        {
            if (ranges.Count == 0 || candidate.Start >= ranges[^1].End)
            {
                ranges.Add(candidate);
            }
        }

        return ranges;
    }

    private static CandidateGroupingAnalysis CreateCandidateGroupingAnalysis(VocabularyCandidate candidate)
    {
        var formsBefore = candidate.Occurrences
            .OrderBy(occurrence => occurrence.Order)
            .Select(occurrence => occurrence.SurfaceForm)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var formsAfter = candidate.EncounteredForms;
        string reasonCode;
        string explanation;

        if (candidate.Occurrences.Any(occurrence =>
                occurrence.TechnicalFamily != TechnicalTokenFamily.None))
        {
            reasonCode = AnalysisReasonCodes.TechnicalFamilyGrouping;
            explanation = $"Grouped explicit technical forms with the canonical `{candidate.CanonicalTerm}` acronym identity.";
        }
        else if (candidate.Kind == TokenKind.Word
                 && candidate.Occurrences.Any(occurrence =>
                     !string.Equals(
                         occurrence.Identity,
                         VocabularyIdentityPolicy.Resolve(
                             occurrence.SurfaceForm,
                             occurrence.Kind).Identity,
                         StringComparison.Ordinal)))
        {
            reasonCode = AnalysisReasonCodes.ExplicitLanguageRuleGrouping;
            explanation = $"Grouped by an explicit source-language rule under the canonical `{candidate.CanonicalTerm}` identity.";
        }
        else if (candidate.Kind == TokenKind.Word && formsAfter.Count < formsBefore.Length)
        {
            reasonCode = AnalysisReasonCodes.OrdinaryWordCaseGrouping;
            explanation = "Grouped because ordinary words differ only by capitalization.";
        }
        else if (candidate.Occurrences.Count > 1)
        {
            reasonCode = AnalysisReasonCodes.RepeatedIdentity;
            explanation = "Grouped because repeated occurrences have the same deterministic vocabulary identity.";
        }
        else
        {
            reasonCode = AnalysisReasonCodes.FirstIdentityOccurrence;
            explanation = "Created as the first occurrence of this deterministic vocabulary identity.";
        }

        return new CandidateGroupingAnalysis(
            candidate.Identity,
            candidate.CanonicalTerm,
            candidate.Kind,
            formsBefore,
            formsAfter,
            candidate.Occurrences.Count,
            reasonCode,
            explanation);
    }

    private static TokenKind Classify(
        string surfaceForm,
        bool hasDigit,
        bool hasHyphen,
        bool hasInternalPeriod)
    {
        if (hasDigit || hasHyphen)
        {
            return TokenKind.TechnicalTerm;
        }

        if (hasInternalPeriod || AbbreviationPolicy.IsKnown(surfaceForm))
        {
            return TokenKind.Abbreviation;
        }

        return KnownAcronyms.Contains(surfaceForm)
            ? TokenKind.Acronym
            : TokenKind.Word;
    }

    private static int? FindSentenceOrder(
        IReadOnlyList<TextSpan> sentences,
        int start,
        int length) => sentences
        .Where(sentence => start >= sentence.StartPosition && start + length <= sentence.EndPosition)
        .Select(sentence => (int?)sentence.Order)
        .SingleOrDefault();

    private static string GetInclusionReasonCode(TokenKind kind) => kind switch
    {
        TokenKind.Acronym => AnalysisReasonCodes.IncludedAcronymPattern,
        TokenKind.Abbreviation => AnalysisReasonCodes.IncludedAbbreviationPattern,
        TokenKind.TechnicalTerm => AnalysisReasonCodes.IncludedTechnicalTokenPattern,
        _ => AnalysisReasonCodes.IncludedUnicodeWord
    };

    private static string GetInclusionExplanation(TokenKind kind) => kind switch
    {
        TokenKind.Acronym => "Included because the value matches a supported case-sensitive acronym identity.",
        TokenKind.Abbreviation => "Included because the value matches an explicit abbreviation rule.",
        TokenKind.TechnicalTerm => "Included because letters with technical digits or hyphens form one supported token.",
        _ => "Included because the value is a Unicode word."
    };

    private static bool TryReadRune(string content, int index, out Rune rune, out int length)
    {
        if (index >= content.Length)
        {
            rune = default;
            length = 0;
            return false;
        }

        var status = Rune.DecodeFromUtf16(content.AsSpan(index), out rune, out length);
        if (status == System.Buffers.OperationStatus.Done)
        {
            return true;
        }

        rune = Rune.ReplacementChar;
        length = 1;
        return true;
    }

    private static bool IsLetterOrDigit(Rune rune) => IsLetter(rune) || Rune.IsDigit(rune);

    private static bool IsLetter(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or
        UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter;

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or
        UnicodeCategory.EnclosingMark;

    private static bool IsPunctuation(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.ConnectorPunctuation or
        UnicodeCategory.DashPunctuation or
        UnicodeCategory.OpenPunctuation or
        UnicodeCategory.ClosePunctuation or
        UnicodeCategory.InitialQuotePunctuation or
        UnicodeCategory.FinalQuotePunctuation or
        UnicodeCategory.OtherPunctuation;

    private static bool IsConnector(Rune rune) =>
        rune.Value is '.' or '\'' or '\u2019' or '-' or '\u2010' or '\u2011';

    private static bool IsHyphen(Rune rune) => rune.Value is '-' or '\u2010' or '\u2011';

    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>()]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(
        @"[\p{L}\p{M}\p{N}.!#$%&'*+/=?^_`{|}~-]+@[\p{L}\p{M}\p{N}-]+(?:\.[\p{L}\p{M}\p{N}-]+)+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EmailAddressRegex();

    private sealed record ExtractionResult(
        IReadOnlyList<TokenOccurrence> Occurrences,
        IReadOnlyList<TokenAnalysisDecision> Decisions);

    private sealed record ExcludedRange(
        int Start,
        int Length,
        string ReasonCode,
        string Explanation)
    {
        public int End => Start + Length;
    }
}
