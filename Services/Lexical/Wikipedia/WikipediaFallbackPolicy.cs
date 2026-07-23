using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Lexical.Wikipedia;

/// <summary>
/// Orchestrates the explicit, schema-neutral fallback policy from Wiktionary to Wikipedia.
/// </summary>
public static class WikipediaFallbackPolicy
{
    /// <summary>
    /// Determines whether the specified request and its resulting status are eligible for Wikipedia fallback.
    /// </summary>
    public static bool IsEligibleForFallback(LexicalLookupRequest originalRequest, LexicalResult originalResult)
    {
        // The originally requested provider must be Wiktionary.
        if (!string.Equals(originalRequest.Provider, "Wiktionary", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The final effective Wiktionary attempt must have returned NotFound.
        if (originalResult.Status != LexicalLookupStatus.NotFound)
        {
            return false;
        }

        // Translation-only requests must never trigger Wikipedia fallback.
        if (originalRequest.LookupMode != LexicalLookupMode.Definition 
            && originalRequest.LookupMode != LexicalLookupMode.DefinitionAndTranslation)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates the fallback request targeting Wikipedia while preserving the exact effective terms.
    /// </summary>
    public static LexicalLookupRequest CreateFallbackRequest(LexicalLookupRequest effectiveRequest)
    {
        return new LexicalLookupRequest(
            effectiveRequest.SourceLanguage,
            effectiveRequest.LookupMode,
            effectiveRequest.TargetLanguage,
            effectiveRequest.CanonicalLookupTerm,
            effectiveRequest.TokenKind,
            "Wikipedia",
            effectiveRequest.DisplayedSurfaceForm,
            effectiveRequest.VocabularyCanonicalTerm);
    }
}
