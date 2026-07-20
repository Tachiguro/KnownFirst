namespace KnownFirst.Core.Preparation;

public static class LexicalLookupOutcomePolicy
{
    public static bool CanRetry(
        LexicalLookupStatus status,
        string? errorCode = null) =>
        status == LexicalLookupStatus.TransientFailure
        || string.Equals(errorCode, "translation-not-found", StringComparison.Ordinal);
}
