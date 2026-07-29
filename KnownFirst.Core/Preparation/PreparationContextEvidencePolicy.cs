using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KnownFirst.Core.Preparation;

/// <summary>
/// The canonical four-field identity of one piece of context evidence (KF-MEANING-001 Slice 3):
/// the document it came from, its normalized-text fingerprint, and the exact target span within that
/// normalized context. Two occurrences that produce the same key are the same evidence, regardless of
/// how many times each is independently discovered.
/// </summary>
public readonly record struct ContextEvidenceKey(
    int SourceDocumentId,
    string NormalizedFingerprint,
    int TargetStart,
    int TargetLength);

/// <summary>
/// Pure, database-independent context normalization/fingerprinting/key policy (KF-MEANING-001 Slice 3),
/// extracted so the existing Schema-7 context path (<c>PreparationService</c>) and the Schema-8 evidence
/// scanner share exactly one implementation instead of two independently-maintained copies. Every method
/// here is a pure function of its inputs — no SQLite, no entities, no clock.
/// </summary>
public static partial class PreparationContextEvidencePolicy
{
    /// <summary>Collapses line-ending variants and internal whitespace runs to a single space, trims, and
    /// applies Unicode Normalization Form C — the exact pre-Slice-3 <c>NormalizeContext</c> algorithm.</summary>
    public static string NormalizeText(string value) =>
        WhitespaceRegex().Replace(value.Replace("\r\n", "\n").Replace('\r', '\n').Trim(), " ")
            .Normalize(NormalizationForm.FormC);

    /// <summary>SHA-256 of the UTF-8 bytes of an already-normalized string, as uppercase hex.</summary>
    public static string CreateFingerprint(string normalizedText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText)));

    /// <summary>Normalizes <paramref name="rawText"/> and fingerprints the result in one step.</summary>
    public static string Fingerprint(string rawText) => CreateFingerprint(NormalizeText(rawText));

    /// <summary>Builds the canonical four-field key for one context occurrence.</summary>
    public static ContextEvidenceKey CreateKey(
        int sourceDocumentId, string rawText, int targetStart, int targetLength) =>
        new(sourceDocumentId, Fingerprint(rawText), targetStart, targetLength);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
