using System.Text;
using System.Text.Json;
using KnownFirst.Core.Preparation;

namespace KnownFirst.Services.Study;

/// <summary>
/// The discriminated shape a <c>PreparationCandidates.ResultJson</c> value can hold, as classified by
/// <see cref="PreparationCandidatePayloadCodec.Read"/>. Never inferred from a try/catch around a single
/// deserialization call — each case is a distinct, deliberate branch (KF-MEANING-001 Slice 3).
/// </summary>
public enum PreparationCandidatePayloadKind
{
    /// <summary>The column is empty or whitespace-only (no lookup has run yet).</summary>
    Empty,

    /// <summary>A well-formed, fully validated Schema-8 <see cref="PreparationCandidatePayloadV1"/> envelope.</summary>
    EnvelopeV1,

    /// <summary>
    /// A raw Schema-7-shaped <see cref="LexicalResult"/> with no canonical <c>payloadVersion</c>
    /// discriminator at the top level — the historical, still-supported shape.
    /// </summary>
    LegacyLexicalResult,

    /// <summary>
    /// A well-formed canonical envelope whose <c>payloadVersion</c> is a valid integer other than 1. Never
    /// falls back to legacy parsing — an unrecognized future envelope must never be silently misread as a
    /// raw <see cref="LexicalResult"/>.
    /// </summary>
    UnsupportedEnvelopeVersion,

    /// <summary>
    /// The JSON is invalid, the canonical discriminator is duplicated or not an integer, the envelope
    /// exceeds <see cref="PreparationCandidatePayloadCodec.MaxEnvelopeBytes"/>, or a validated EnvelopeV1
    /// failed a graph-invariant check.
    /// </summary>
    Malformed
}

/// <summary>
/// The result of <see cref="PreparationCandidatePayloadCodec.Read"/> — exactly one of
/// <see cref="Envelope"/>, <see cref="LegacyResult"/>, or <see cref="UnsupportedVersion"/>/
/// <see cref="FailureDetail"/> is populated, matching <see cref="Kind"/>.
/// </summary>
public sealed record PreparationCandidatePayloadReadResult
{
    private PreparationCandidatePayloadReadResult(
        PreparationCandidatePayloadKind kind,
        PreparationCandidatePayloadV1? envelope,
        LexicalResult? legacyResult,
        int? unsupportedVersion,
        string? failureDetail)
    {
        Kind = kind;
        Envelope = envelope;
        LegacyResult = legacyResult;
        UnsupportedVersion = unsupportedVersion;
        FailureDetail = failureDetail;
    }

    public PreparationCandidatePayloadKind Kind { get; }

    public PreparationCandidatePayloadV1? Envelope { get; }

    public LexicalResult? LegacyResult { get; }

    public int? UnsupportedVersion { get; }

    public string? FailureDetail { get; }

    /// <summary>The underlying provider lookup regardless of shape (<see langword="null"/> for Empty/Unsupported/Malformed).</summary>
    public LexicalResult? AnyResult => Envelope?.Result ?? LegacyResult;

    public static readonly PreparationCandidatePayloadReadResult Empty =
        new(PreparationCandidatePayloadKind.Empty, null, null, null, null);

    public static PreparationCandidatePayloadReadResult AsEnvelope(PreparationCandidatePayloadV1 envelope) =>
        new(PreparationCandidatePayloadKind.EnvelopeV1, envelope, null, null, null);

    public static PreparationCandidatePayloadReadResult AsLegacy(LexicalResult result) =>
        new(PreparationCandidatePayloadKind.LegacyLexicalResult, null, result, null, null);

    public static PreparationCandidatePayloadReadResult AsUnsupported(int version) =>
        new(PreparationCandidatePayloadKind.UnsupportedEnvelopeVersion, null, null, version, null);

    public static PreparationCandidatePayloadReadResult AsMalformed(string detail) =>
        new(PreparationCandidatePayloadKind.Malformed, null, null, null, detail);
}

/// <summary>
/// The discriminated reader/writer for the Schema-8 <see cref="PreparationCandidatePayloadV1"/> envelope
/// persisted in the existing <c>PreparationCandidates.ResultJson</c> column (KF-MEANING-001 Slice 3).
/// Source-generated JSON only — no reflection-based fallback anywhere in this type.
/// </summary>
public static class PreparationCandidatePayloadCodec
{
    /// <summary>
    /// The envelope byte limit. Applies only when the canonical <c>payloadVersion</c> discriminator is
    /// present in the input (Read) and always to serialized output (Write) — a historical raw
    /// <see cref="LexicalResult"/> is never rejected by <see cref="Read"/> merely for exceeding this size.
    /// </summary>
    public const int MaxEnvelopeBytes = 256 * 1024;

    private static ReadOnlySpan<byte> DiscriminatorPropertyName => "payloadVersion"u8;

    public static PreparationCandidatePayloadReadResult Read(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return PreparationCandidatePayloadReadResult.Empty;
        }

        var utf8 = Encoding.UTF8.GetBytes(resultJson);

        if (!TryPreScanDiscriminator(utf8, out var present, out var version, out var preScanFailure))
        {
            return PreparationCandidatePayloadReadResult.AsMalformed(preScanFailure!);
        }

        if (!present)
        {
            return ReadLegacy(utf8);
        }

        // The canonical discriminator is present: the envelope byte limit applies from here on, even if
        // the version turns out to be unsupported.
        if (utf8.Length > MaxEnvelopeBytes)
        {
            return PreparationCandidatePayloadReadResult.AsMalformed(
                $"Envelope JSON ({utf8.Length} bytes) exceeds the {MaxEnvelopeBytes}-byte limit.");
        }

        if (version != PreparationCandidatePayloadV1.CurrentVersion)
        {
            return PreparationCandidatePayloadReadResult.AsUnsupported(version);
        }

        return ReadEnvelopeV1(utf8);
    }

    /// <summary>
    /// Validates, sorts (ascending, without <c>Distinct()</c> — duplicates are a rejection, never a
    /// silent dedup), serializes through the source-generated context, and enforces the envelope byte
    /// limit. Throws <see cref="PreparationPayloadException"/> on any violation; never writes a partially
    /// invalid envelope.
    /// </summary>
    public static string Write(PreparationCandidatePayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateForWrite(payload);

        var sorted = payload.ResolvedProviderMeaningIndexes.OrderBy(index => index).ToArray();
        var normalized = payload with { ResolvedProviderMeaningIndexes = sorted };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            normalized,
            PreparationCandidatePayloadJsonSerializerContext.Default.PreparationCandidatePayloadV1);
        if (bytes.Length > MaxEnvelopeBytes)
        {
            throw new PreparationPayloadException(
                "envelope-too-large",
                $"Envelope JSON ({bytes.Length} bytes) exceeds the {MaxEnvelopeBytes}-byte limit.");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static PreparationCandidatePayloadReadResult ReadLegacy(byte[] utf8)
    {
        try
        {
            var legacy = JsonSerializer.Deserialize(utf8, LexicalJsonSerializerContext.Default.LexicalResult);
            return legacy is null
                ? PreparationCandidatePayloadReadResult.AsMalformed("Legacy LexicalResult JSON deserialized to null.")
                : PreparationCandidatePayloadReadResult.AsLegacy(legacy);
        }
        catch (JsonException ex)
        {
            return PreparationCandidatePayloadReadResult.AsMalformed($"Legacy LexicalResult JSON is invalid: {ex.Message}");
        }
    }

    private static PreparationCandidatePayloadReadResult ReadEnvelopeV1(byte[] utf8)
    {
        PreparationCandidatePayloadV1? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(
                utf8,
                PreparationCandidatePayloadJsonSerializerContext.Default.PreparationCandidatePayloadV1);
        }
        catch (JsonException ex)
        {
            return PreparationCandidatePayloadReadResult.AsMalformed($"EnvelopeV1 JSON is invalid: {ex.Message}");
        }

        if (envelope is null)
        {
            return PreparationCandidatePayloadReadResult.AsMalformed("EnvelopeV1 JSON deserialized to null.");
        }

        var failure = ValidateForRead(envelope);
        return failure is null
            ? PreparationCandidatePayloadReadResult.AsEnvelope(envelope)
            : PreparationCandidatePayloadReadResult.AsMalformed(failure);
    }

    private static string? ValidateForRead(PreparationCandidatePayloadV1 envelope)
    {
        if (envelope.PayloadVersion != PreparationCandidatePayloadV1.CurrentVersion)
        {
            return $"EnvelopeV1 PayloadVersion {envelope.PayloadVersion} does not match the expected value {PreparationCandidatePayloadV1.CurrentVersion}.";
        }

        if (envelope.Result is null)
        {
            return envelope.ResolvedProviderMeaningIndexes.Count > 0
                ? "EnvelopeV1 has ResolvedProviderMeaningIndexes but no Result to resolve them against."
                : ValidateEvidence(envelope.FrozenEvidence);
        }

        var seen = new HashSet<int>();
        var previous = int.MinValue;
        var first = true;
        foreach (var index in envelope.ResolvedProviderMeaningIndexes)
        {
            if (index < 0 || index >= envelope.Result.Meanings.Count)
            {
                return $"ResolvedProviderMeaningIndexes contains out-of-range index {index}.";
            }

            if (!seen.Add(index))
            {
                return $"ResolvedProviderMeaningIndexes contains duplicate index {index}.";
            }

            if (!first && index < previous)
            {
                return "ResolvedProviderMeaningIndexes is not sorted ascending.";
            }

            previous = index;
            first = false;
        }

        return ValidateEvidence(envelope.FrozenEvidence);
    }

    private static void ValidateForWrite(PreparationCandidatePayloadV1 payload)
    {
        if (payload.PayloadVersion != PreparationCandidatePayloadV1.CurrentVersion)
        {
            throw new PreparationPayloadException(
                "invalid-payload-version",
                $"PayloadVersion {payload.PayloadVersion} does not match the expected value {PreparationCandidatePayloadV1.CurrentVersion}.");
        }

        var evidenceFailure = ValidateEvidence(payload.FrozenEvidence);
        if (evidenceFailure is not null)
        {
            throw new PreparationPayloadException("invalid-evidence", evidenceFailure);
        }

        if (payload.Result is null)
        {
            if (payload.ResolvedProviderMeaningIndexes.Count > 0)
            {
                throw new PreparationPayloadException(
                    "resolved-index-without-result",
                    "ResolvedProviderMeaningIndexes is non-empty but the envelope has no Result to resolve them against.");
            }

            return;
        }

        var seen = new HashSet<int>();
        foreach (var index in payload.ResolvedProviderMeaningIndexes)
        {
            if (index < 0 || index >= payload.Result.Meanings.Count)
            {
                throw new PreparationPayloadException(
                    "resolved-index-out-of-range",
                    $"Resolved provider meaning index {index} is out of range for {payload.Result.Meanings.Count} meaning(s).");
            }

            if (!seen.Add(index))
            {
                throw new PreparationPayloadException(
                    "duplicate-resolved-index",
                    $"Duplicate resolved provider meaning index {index}.");
            }
        }
    }

    private static string? ValidateEvidence(IReadOnlyList<PreparationCandidateEvidence> evidence)
    {
        foreach (var entry in evidence)
        {
            if (entry.SourceDocumentId <= 0
                || entry.TargetStart < 0
                || entry.TargetLength <= 0
                || string.IsNullOrWhiteSpace(entry.NormalizedFingerprint))
            {
                return $"FrozenEvidence contains an invalid entry (SourceDocumentId={entry.SourceDocumentId}, TargetStart={entry.TargetStart}, TargetLength={entry.TargetLength}).";
            }
        }

        return null;
    }

    /// <summary>
    /// Bounded top-level pre-scan: walks only the root object's immediate properties (skipping every
    /// nested value without descending into it) looking for the canonical <c>payloadVersion</c>
    /// discriminator. A differently-cased property (e.g. <c>"PayloadVersion"</c>) is never treated as the
    /// discriminator — it is skipped like any other property, so a document carrying only that stray
    /// property is reported as having no canonical discriminator (legacy path).
    /// </summary>
    private static bool TryPreScanDiscriminator(
        byte[] utf8Json, out bool present, out int version, out string? failureDetail)
    {
        present = false;
        version = 0;
        failureDetail = null;

        try
        {
            var reader = new Utf8JsonReader(utf8Json, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                // Not a top-level JSON object (bare array/number/string/literal). Treated as "no
                // canonical discriminator" — the subsequent legacy-parse attempt reports Malformed with a
                // proper JsonException-derived message for genuinely invalid JSON.
                return true;
            }

            var foundCount = 0;
            while (true)
            {
                if (!reader.Read())
                {
                    failureDetail = "Truncated JSON while scanning for the payloadVersion discriminator.";
                    return false;
                }

                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    failureDetail = "Unexpected token while scanning for the payloadVersion discriminator.";
                    return false;
                }

                var isDiscriminator = reader.ValueTextEquals(DiscriminatorPropertyName);
                if (!reader.Read())
                {
                    failureDetail = "Truncated JSON while scanning for the payloadVersion discriminator.";
                    return false;
                }

                if (!isDiscriminator)
                {
                    reader.Skip();
                    continue;
                }

                foundCount++;
                if (foundCount > 1)
                {
                    failureDetail = "Duplicate 'payloadVersion' property.";
                    return false;
                }

                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out version))
                {
                    failureDetail = "'payloadVersion' is not a JSON integer.";
                    return false;
                }
            }

            present = foundCount == 1;
            return true;
        }
        catch (JsonException)
        {
            // Genuinely malformed JSON (e.g. an unquoted property name). Reported as "no canonical
            // discriminator found" so the caller's legacy-parse attempt runs next and reports Malformed
            // with a proper JsonException-derived message — a single, consistent failure path for
            // invalid JSON regardless of whether it happens to resemble an envelope.
            present = false;
            version = 0;
            return true;
        }
    }
}
