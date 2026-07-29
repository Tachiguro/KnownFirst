namespace KnownFirst.Services.Study;

/// <summary>
/// Thrown whenever a Schema-8 preparation operation encounters candidate/envelope history it cannot
/// safely operate on — an unsupported envelope version, malformed JSON, or (for the evidence ledger) a
/// Word whose candidate history cannot be classified — always before any session/candidate/Sense/Meaning
/// mutation (KF-MEANING-001 Slice 3). Stable, preparation-specific; never a generic
/// <see cref="InvalidOperationException"/> for these specific failure classes so callers can distinguish
/// "corrupt history" from ordinary workflow-state errors.
/// </summary>
public sealed class PreparationCandidateStateException : Exception
{
    public PreparationCandidateStateException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
