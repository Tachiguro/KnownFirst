namespace KnownFirst.Core.Settings;

/// <summary>
/// Single source of truth for interpreting an optional Display Name.
/// <para>
/// The Display Name is deliberately minimal: it is a local, optional label the user may set for
/// themselves. There is no account, profile, or cloud identity behind it, so there is nothing to
/// validate beyond "is there meaningful text here at all". Normalization therefore answers exactly
/// one question — absent, or this exact trimmed text — and every caller routes through it so a
/// blank name can never be persisted as an empty-but-present value.
/// </para>
/// </summary>
public static class DisplayNamePolicy
{
    /// <summary>
    /// Returns the trimmed Display Name, or <see langword="null"/> when the input is
    /// <see langword="null"/>, empty, or whitespace-only. Inner spacing is preserved.
    /// </summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
