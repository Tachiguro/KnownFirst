namespace KnownFirst.Core.Preparation;

public static class LexicalLookupOutcomePolicy
{
    public static bool CanRetry(LexicalLookupStatus status) => status is
        LexicalLookupStatus.TransientFailure or
        LexicalLookupStatus.ParseFailure;
}
