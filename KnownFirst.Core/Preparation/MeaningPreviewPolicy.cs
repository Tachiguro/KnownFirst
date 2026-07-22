using System.Text;

namespace KnownFirst.Core.Preparation;

public readonly record struct SelectableMeaning(
    int OriginalIndex,
    string PrimaryText,
    string? SecondaryText,
    bool IsTruncated,
    string FullText);

public static class MeaningPreviewPolicy
{
    public const int ClosedPreviewLength = 160;
    public const int AlternativePreviewLength = 240;

    public static string CreateClosedPreview(string value) => CreatePreview(value, ClosedPreviewLength);

    public static string CreateAlternativePreview(string value) => CreatePreview(value, AlternativePreviewLength);

    public static bool IsAlternativeTruncated(string value) => CountRunes(value) > AlternativePreviewLength;

    public static string CreatePreview(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        if (CountRunes(value) <= maximumLength)
        {
            return value;
        }

        var builder = new StringBuilder();
        foreach (var rune in value.EnumerateRunes().Take(maximumLength))
        {
            builder.Append(rune);
        }

        return $"{builder.ToString().TrimEnd()}…";
    }

    private static int CountRunes(string value) => value.EnumerateRunes().Count();

    public static IReadOnlyList<SelectableMeaning> GetSelectableMeanings(
        IReadOnlyList<LexicalMeaning> meanings,
        LexicalLookupMode? lookupMode = null)
    {
        ArgumentNullException.ThrowIfNull(meanings);

        var result = new List<SelectableMeaning>(meanings.Count);
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < meanings.Count; i++)
        {
            var meaning = meanings[i];
            
            var hasDefinition = !string.IsNullOrWhiteSpace(meaning.Definition);
            var hasTranslation = !string.IsNullOrWhiteSpace(meaning.Translation);

            string? primaryText = null;

            if (lookupMode == LexicalLookupMode.Translation && hasTranslation)
            {
                primaryText = meaning.Translation;
            }
            else if (lookupMode == LexicalLookupMode.Definition && hasDefinition)
            {
                primaryText = meaning.Definition;
            }
            else if (lookupMode == LexicalLookupMode.DefinitionAndTranslation || lookupMode == null)
            {
                if (hasDefinition && hasTranslation)
                {
                    primaryText = $"{meaning.Definition} ({meaning.Translation})";
                }
                else if (hasDefinition)
                {
                    primaryText = meaning.Definition;
                }
                else if (hasTranslation)
                {
                    primaryText = meaning.Translation;
                }
            }
            else
            {
                if (hasDefinition) primaryText = meaning.Definition;
                else if (hasTranslation) primaryText = meaning.Translation;
            }

            if (string.IsNullOrWhiteSpace(primaryText))
            {
                continue;
            }
            
            primaryText = primaryText.Trim();

            var secondaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(meaning.PartOfSpeech))
            {
                secondaryParts.Add(meaning.PartOfSpeech.Trim());
            }

            if (meaning.UsageLabels != null)
            {
                foreach (var label in meaning.UsageLabels)
                {
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        secondaryParts.Add(label.Trim());
                    }
                }
            }

            string? secondaryText = secondaryParts.Count > 0 
                ? string.Join(" · ", secondaryParts) 
                : null;

            var signature = $"{primaryText}|{secondaryText ?? string.Empty}";
            if (seenSignatures.Add(signature))
            {
                var fullText = primaryText;
                var preview = CreatePreview(primaryText, AlternativePreviewLength);
                result.Add(new SelectableMeaning(
                    OriginalIndex: i,
                    PrimaryText: preview,
                    SecondaryText: secondaryText,
                    IsTruncated: IsAlternativeTruncated(primaryText),
                    FullText: fullText
                ));
            }
        }

        return result;
    }

    public static string GetPrimaryTextForMode(string definition, string translation, LexicalLookupMode? lookupMode)
    {
        var hasDefinition = !string.IsNullOrWhiteSpace(definition);
        var hasTranslation = !string.IsNullOrWhiteSpace(translation);

        if (lookupMode == LexicalLookupMode.Translation && hasTranslation) return translation.Trim();
        if (lookupMode == LexicalLookupMode.Definition && hasDefinition) return definition.Trim();
        if (hasDefinition && hasTranslation) return $"{definition.Trim()} ({translation.Trim()})";
        if (hasDefinition) return definition.Trim();
        if (hasTranslation) return translation.Trim();
        return string.Empty;
    }
}
