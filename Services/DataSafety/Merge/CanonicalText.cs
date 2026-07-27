namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Shared canonical null/empty and language-code handling for merge-identity fields. These rules are
/// deliberately narrow: most string fields keep null and empty string distinct
/// (see <see cref="CanonicalFingerprintBuilder.WriteNullableString"/>); the helpers here are for the
/// specific identity components whose design definition explicitly collapses null/empty, or that need
/// a canonical language-code form.
/// </summary>
public static class CanonicalText
{
    /// <summary>
    /// Canonical form for optional free-text identity components where null and empty are documented
    /// as indistinguishable (e.g. Meaning.Definition/Translation, per
    /// docs/architecture/backup-merge-v1-design.md §4.2): null collapses to <see cref="string.Empty"/>,
    /// non-null values are trimmed. Do not use this for fields where null/empty must remain distinct.
    /// </summary>
    public static string NormalizeOptional(string? value) => value is null ? string.Empty : value.Trim();

    /// <summary>
    /// Canonical language-code form for merge identity comparison: trimmed, culture-invariant
    /// lowercase, null treated as empty. This is intentionally distinct from
    /// KnownFirst.Core.Language.LanguagePreferencePolicy, which is scoped to UI language selection and
    /// silently collapses any unrecognized code to "en" — merge identity must never coerce an
    /// unrecognized language code into a different one, since doing so could silently merge two
    /// genuinely distinct languages.
    /// </summary>
    public static string CanonicalLanguageCode(string? value) =>
        value is null ? string.Empty : value.Trim().ToLowerInvariant();
}
