using System.Text;

namespace KnownFirst.Core.Text;

public sealed record VocabularyIdentityResolution(
    string Identity,
    string CanonicalTerm,
    bool AppliedLanguageRule);

public static class VocabularyIdentityPolicy
{
    public static VocabularyIdentityResolution Resolve(
        string surfaceForm,
        TokenKind tokenKind,
        string? sourceLanguage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceForm);

        var normalized = surfaceForm.Normalize(NormalizationForm.FormC);
        if (tokenKind == TokenKind.Word
            && IsEnglish(sourceLanguage)
            && normalized.ToLowerInvariant() is "i" or "me" or "my")
        {
            return new VocabularyIdentityResolution("W:i", "I", AppliedLanguageRule: true);
        }

        var identity = tokenKind switch
        {
            TokenKind.Word => $"W:{normalized.ToLowerInvariant()}",
            TokenKind.Acronym => $"A:{normalized}",
            TokenKind.Abbreviation => $"B:{normalized}",
            TokenKind.TechnicalTerm => $"T:{normalized}",
            _ => throw new ArgumentOutOfRangeException(nameof(tokenKind))
        };

        return new VocabularyIdentityResolution(identity, normalized, AppliedLanguageRule: false);
    }

    private static bool IsEnglish(string? sourceLanguage)
    {
        if (string.IsNullOrWhiteSpace(sourceLanguage))
        {
            return false;
        }

        var languageFamily = sourceLanguage.Trim()
            .Replace('_', '-')
            .Split('-', 2, StringSplitOptions.TrimEntries)[0];
        return string.Equals(languageFamily, "en", StringComparison.OrdinalIgnoreCase);
    }
}
