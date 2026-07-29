using System.Text.Json.Serialization;
using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Study;

/// <summary>
/// The frozen four-field evidence for one processed context (KF-MEANING-001 Slice 3): the document it
/// came from, its normalized-context fingerprint, and the exact target span. Frozen once written — later
/// re-processing of the same document must never mutate an already-recorded entry; it can only add new
/// entries for genuinely new evidence (see <see cref="PreparationCandidatePayloadCodec"/>).
/// </summary>
public sealed record PreparationCandidateEvidence(
    int SourceDocumentId,
    string NormalizedFingerprint,
    int TargetStart,
    int TargetLength);

/// <summary>
/// Versioned Schema-8 envelope persisted in the existing <c>PreparationCandidates.ResultJson</c> column
/// (KF-MEANING-001 Slice 3). Wraps the same <see cref="LexicalResult"/> shape Schema-7 already persists
/// unwrapped, adding the explicit <see cref="ResolvedProviderMeaningIndexes"/> ledger (which provider
/// meaning indexes have already been resolved into a Sense — accepted, matched, or dismissed) and the
/// frozen evidence baseline. The wire discriminator is exactly <c>"payloadVersion"</c>
/// (<see cref="PreparationCandidatePayloadCodec"/> depends on this exact name).
/// </summary>
public sealed record PreparationCandidatePayloadV1(
    [property: JsonPropertyName("payloadVersion")] int PayloadVersion,
    LexicalResult? Result,
    IReadOnlyList<int> ResolvedProviderMeaningIndexes,
    IReadOnlyList<PreparationCandidateEvidence> FrozenEvidence)
{
    public const int CurrentVersion = 1;

    public static PreparationCandidatePayloadV1 Create(
        LexicalResult? result,
        IReadOnlyList<int>? resolvedProviderMeaningIndexes = null,
        IReadOnlyList<PreparationCandidateEvidence>? frozenEvidence = null) =>
        new(CurrentVersion, result, resolvedProviderMeaningIndexes ?? [], frozenEvidence ?? []);

    /// <summary>A Pending candidate with genuinely new evidence already frozen but no provider lookup yet.</summary>
    public static PreparationCandidatePayloadV1 CreatePending(
        IReadOnlyList<PreparationCandidateEvidence> frozenEvidence) =>
        new(CurrentVersion, null, [], frozenEvidence);
}

/// <summary>
/// Thrown by <see cref="PreparationCandidatePayloadCodec.Write"/> for every rejected write — never thrown
/// by <see cref="PreparationCandidatePayloadCodec.Read"/>, which reports failure via
/// <see cref="PreparationCandidatePayloadKind.Malformed"/> instead so a corrupt persisted row can be
/// classified without an exception-driven control flow.
/// </summary>
public sealed class PreparationPayloadException : Exception
{
    public PreparationPayloadException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PreparationCandidatePayloadV1))]
[JsonSerializable(typeof(PreparationCandidateEvidence))]
[JsonSerializable(typeof(LexicalResult))]
internal sealed partial class PreparationCandidatePayloadJsonSerializerContext : JsonSerializerContext
{
}
