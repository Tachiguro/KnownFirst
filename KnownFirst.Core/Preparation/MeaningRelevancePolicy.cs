namespace KnownFirst.Core.Preparation;

public enum MeaningRelevanceTier
{
    Standard = 0,
    Demoted = 1
}

public readonly record struct MeaningRelevanceClassification(
    MeaningRelevanceTier Tier,
    string? TriggeringLabel = null);

public static class MeaningRelevancePolicy
{
    private static readonly HashSet<string> RecognizedDemotionLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // English strong-demotion concepts
        "obsolete",
        "archaic",
        "historical",
        "dated",

        // German strong-demotion concepts
        "veraltet",
        "historisch",
        "obsolet",
        "veraltend"
    };

    private static readonly char[] PunctuationTrimChars =
    [
        ' ', '\t', '\r', '\n',
        '.', ',', ';', ':',
        '(', ')', '[', ']', '{', '}',
        '"', '\'', '`', '´',
        '-', '–', '—', '_', '/'
    ];

    public static MeaningRelevanceClassification Classify(LexicalMeaning meaning)
    {
        ArgumentNullException.ThrowIfNull(meaning);

        if (meaning.UsageLabels is null || meaning.UsageLabels.Count == 0)
        {
            return new MeaningRelevanceClassification(MeaningRelevanceTier.Standard);
        }

        foreach (var rawLabel in meaning.UsageLabels)
        {
            if (string.IsNullOrWhiteSpace(rawLabel))
            {
                continue;
            }

            var normalized = NormalizeLabel(rawLabel);
            if (RecognizedDemotionLabels.Contains(normalized))
            {
                return new MeaningRelevanceClassification(MeaningRelevanceTier.Demoted, normalized);
            }
        }

        return new MeaningRelevanceClassification(MeaningRelevanceTier.Standard);
    }

    public static string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        return label.Trim().Trim(PunctuationTrimChars).ToLowerInvariant();
    }

    public static IReadOnlyList<int> RankIndices(IReadOnlyList<LexicalMeaning> meanings)
    {
        ArgumentNullException.ThrowIfNull(meanings);

        return meanings
            .Select((meaning, index) => (Index: index, Meaning: meaning, Classification: Classify(meaning)))
            .OrderBy(item => item.Classification.Tier)
            .ThenBy(item => item.Index)
            .Select(item => item.Index)
            .ToArray();
    }

    public static IReadOnlyList<LexicalMeaning> Rank(IReadOnlyList<LexicalMeaning> meanings)
    {
        ArgumentNullException.ThrowIfNull(meanings);

        return RankIndices(meanings)
            .Select(index => meanings[index])
            .ToArray();
    }
}
