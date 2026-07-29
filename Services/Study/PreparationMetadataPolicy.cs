using System.Text;

namespace KnownFirst.Services.Study;

/// <summary>
/// Thrown for every rejected TopicOrDomain/PartOfSpeech value (KF-MEANING-001 Slice 3) — always before any
/// Sense/Meaning/Card mutation. Stable, preparation-specific error codes.
/// </summary>
public sealed class PreparationMetadataValidationException : Exception
{
    public PreparationMetadataValidationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

/// <summary>
/// Normalization/validation/precedence policy for the two Sense-level metadata fields introduced by
/// KF-MEANING-001 Slice 3 preparation: <c>TopicOrDomain</c> and <c>PartOfSpeech</c>. Both fields are
/// trimmed, Unicode-normalized (Form C), empty-collapsed to <see cref="string.Empty"/>, and bounded by a
/// maximum UTF-8 byte length; an over-limit value is rejected before any mutation via
/// <see cref="PreparationMetadataValidationException"/>.
/// </summary>
public static class PreparationMetadataPolicy
{
    public const int MaxTopicOrDomainUtf8Bytes = 256;
    public const int MaxPartOfSpeechUtf8Bytes = 128;

    /// <summary>Never inferred from labels, definitions, translations, or notes — explicit input only.</summary>
    public static string NormalizeTopicOrDomain(string? value) =>
        Normalize(value, MaxTopicOrDomainUtf8Bytes, "preparation-topic-or-domain-too-long");

    public static string NormalizePartOfSpeech(string? value) =>
        Normalize(value, MaxPartOfSpeechUtf8Bytes, "preparation-part-of-speech-too-long");

    /// <summary>
    /// Resolution order: (1) explicit, non-empty <c>PreparedMeaningInput.PartOfSpeech</c>; (2) the selected
    /// provider <c>LexicalMeaning.PartOfSpeech</c> from the persisted envelope Result; (3) <see cref="string.Empty"/>.
    /// </summary>
    public static string ResolvePartOfSpeech(string? explicitInput, string? providerPartOfSpeech)
    {
        var normalizedExplicit = NormalizePartOfSpeech(explicitInput);
        return normalizedExplicit.Length > 0 ? normalizedExplicit : NormalizePartOfSpeech(providerPartOfSpeech);
    }

    private static string Normalize(string? value, int maxUtf8Bytes, string errorCode)
    {
        var trimmed = (value ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormC);
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var byteCount = Encoding.UTF8.GetByteCount(trimmed);
        if (byteCount > maxUtf8Bytes)
        {
            throw new PreparationMetadataValidationException(
                errorCode,
                $"Value is {byteCount} UTF-8 bytes, exceeding the {maxUtf8Bytes}-byte limit.");
        }

        return trimmed;
    }
}
