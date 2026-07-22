using System.Text.RegularExpressions;

namespace KnownFirst.Core.Text;

public enum TechnicalTokenFamily
{
    None = 0,
    Cve = 1,
    Sha = 2
}

public sealed record TechnicalTokenResolution(
    string CanonicalTerm,
    string Identity,
    TokenKind TokenKind,
    TechnicalTokenFamily Family,
    int? InstanceYear,
    string? InstanceIdentifier,
    string? Variant,
    string ReasonCode,
    string Explanation);

public static partial class TechnicalTokenFamilyPolicy
{
    private static readonly HashSet<string> SupportedShaVariants = new(StringComparer.Ordinal)
    {
        "1",
        "224",
        "256",
        "384",
        "512"
    };

    public static TechnicalTokenResolution? Resolve(string surfaceForm)
    {
        ArgumentNullException.ThrowIfNull(surfaceForm);

        var cve = CvePattern().Match(surfaceForm);
        if (cve.Success)
        {
            return new TechnicalTokenResolution(
                "CVE",
                "A:CVE",
                TokenKind.Acronym,
                TechnicalTokenFamily.Cve,
                int.Parse(cve.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture),
                cve.Groups["identifier"].Value,
                null,
                AnalysisReasonCodes.IncludedCveFamilyPattern,
                $"Grouped `{surfaceForm}` with the CVE acronym because it matches the explicit CVE identifier pattern.");
        }

        var sha = ShaPattern().Match(surfaceForm);
        if (!sha.Success || !SupportedShaVariants.Contains(sha.Groups["variant"].Value))
        {
            return null;
        }

        var variant = sha.Groups["variant"].Value;
        return new TechnicalTokenResolution(
            "SHA",
            "A:SHA",
            TokenKind.Acronym,
            TechnicalTokenFamily.Sha,
            null,
            null,
            variant,
            AnalysisReasonCodes.IncludedShaFamilyPattern,
            $"Grouped `{surfaceForm}` with the SHA acronym because `{variant}` is an explicitly supported SHA variant.");
    }

    [GeneratedRegex(@"^CVE-(?<year>\d{4})-(?<identifier>\d{4,})$", RegexOptions.CultureInvariant)]
    private static partial Regex CvePattern();

    [GeneratedRegex(@"^SHA-(?<variant>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ShaPattern();
}
