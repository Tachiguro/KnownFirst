using System.Buffers;
using System.Collections.ObjectModel;
using System.Text;

namespace KnownFirst.Core.Text;

public enum VocabularySearchQueryStatus
{
    Active = 0,
    EmptyQuery = 1,
    InvalidSourceLanguage = 2,
    QueryTooLong = 3,
    InvalidUnicode = 4
}

public sealed record VocabularySearchKeyRange(
    TokenKind TokenKind,
    string InclusiveLowerBound,
    string ExclusiveUpperBound)
{
    public string PrefixKey => InclusiveLowerBound;

    public string LowerBound => InclusiveLowerBound;

    public string UpperBound => ExclusiveUpperBound;

    public bool MatchesUtf8(string candidateIdentity) =>
        VocabularySearchKeyPolicy.IsWithinUtf8Range(
            candidateIdentity,
            InclusiveLowerBound,
            ExclusiveUpperBound);
}

public sealed record VocabularySearchPlan(
    VocabularySearchQueryStatus Status,
    string SourceLanguage,
    string NormalizedQuery,
    int ScalarCount,
    IReadOnlyList<VocabularySearchKeyRange> KeyRanges,
    IReadOnlyList<string> ExactAliases)
{
    public bool IsValid =>
        Status is VocabularySearchQueryStatus.Active or VocabularySearchQueryStatus.EmptyQuery;

    public bool IsSearchActive => Status == VocabularySearchQueryStatus.Active;

    public bool UseRecentOverview => Status == VocabularySearchQueryStatus.EmptyQuery;

    public VocabularySearchKeyRange GetRange(TokenKind tokenKind) =>
        KeyRanges.Single(range => range.TokenKind == tokenKind);

    public bool MatchesUtf8(string candidateIdentity)
    {
        if (!IsSearchActive
            || string.IsNullOrEmpty(candidateIdentity)
            || !VocabularySearchKeyPolicy.IsValidUnicode(candidateIdentity))
        {
            return false;
        }

        for (var i = 0; i < ExactAliases.Count; i++)
        {
            if (string.Equals(ExactAliases[i], candidateIdentity, StringComparison.Ordinal))
            {
                return true;
            }
        }

        for (var i = 0; i < KeyRanges.Count; i++)
        {
            if (KeyRanges[i].MatchesUtf8(candidateIdentity))
            {
                return true;
            }
        }

        return false;
    }
}

public static class VocabularySearchKeyPolicy
{
    public const int MaxQueryScalarCount = 80;

    public static IReadOnlyList<string> SupportedSourceLanguages { get; } =
        Array.AsReadOnly(["en", "de"]);

    public static IReadOnlyList<TokenKind> OrderedTokenKinds { get; } =
        Array.AsReadOnly([
            TokenKind.Word,
            TokenKind.Acronym,
            TokenKind.Abbreviation,
            TokenKind.TechnicalTerm
        ]);

    private static readonly ReadOnlyCollection<VocabularySearchKeyRange> EmptyRanges =
        Array.AsReadOnly(Array.Empty<VocabularySearchKeyRange>());

    private static readonly ReadOnlyCollection<string> EmptyAliases =
        Array.AsReadOnly(Array.Empty<string>());

    private static readonly ReadOnlyCollection<string> EnglishPronounAliases =
        Array.AsReadOnly(["W:i"]);

    public static VocabularySearchPlan CreatePlan(string? sourceLanguage, string? rawQuery)
    {
        if (!TryNormalizeSourceLanguage(sourceLanguage, out var normalizedLanguage))
        {
            return new VocabularySearchPlan(
                VocabularySearchQueryStatus.InvalidSourceLanguage,
                string.Empty,
                string.Empty,
                0,
                EmptyRanges,
                EmptyAliases);
        }

        if (rawQuery is null)
        {
            return CreateEmptyQueryPlan(normalizedLanguage);
        }

        if (!IsValidUnicode(rawQuery))
        {
            return new VocabularySearchPlan(
                VocabularySearchQueryStatus.InvalidUnicode,
                normalizedLanguage,
                string.Empty,
                0,
                EmptyRanges,
                EmptyAliases);
        }

        var trimmed = rawQuery.Trim();
        if (trimmed.Length == 0)
        {
            return CreateEmptyQueryPlan(normalizedLanguage);
        }

        string normalizedQuery;
        try
        {
            normalizedQuery = trimmed.Normalize(NormalizationForm.FormC).Trim();
        }
        catch (ArgumentException)
        {
            return new VocabularySearchPlan(
                VocabularySearchQueryStatus.InvalidUnicode,
                normalizedLanguage,
                string.Empty,
                0,
                EmptyRanges,
                EmptyAliases);
        }

        if (normalizedQuery.Length == 0)
        {
            return CreateEmptyQueryPlan(normalizedLanguage);
        }

        if (!TryCountUnicodeScalars(normalizedQuery, out var scalarCount))
        {
            return new VocabularySearchPlan(
                VocabularySearchQueryStatus.InvalidUnicode,
                normalizedLanguage,
                string.Empty,
                0,
                EmptyRanges,
                EmptyAliases);
        }

        if (scalarCount > MaxQueryScalarCount)
        {
            return new VocabularySearchPlan(
                VocabularySearchQueryStatus.QueryTooLong,
                normalizedLanguage,
                string.Empty,
                0,
                EmptyRanges,
                EmptyAliases);
        }

        var ranges = new VocabularySearchKeyRange[OrderedTokenKinds.Count];
        for (var i = 0; i < OrderedTokenKinds.Count; i++)
        {
            ranges[i] = CreateKeyRange(normalizedQuery, OrderedTokenKinds[i]);
        }

        var exactAliases = IsEnglishFirstPersonSingularPronounQuery(normalizedLanguage, normalizedQuery)
            ? EnglishPronounAliases
            : EmptyAliases;

        return new VocabularySearchPlan(
            VocabularySearchQueryStatus.Active,
            normalizedLanguage,
            normalizedQuery,
            scalarCount,
            Array.AsReadOnly(ranges),
            exactAliases);
    }

    public static bool TryCreatePlan(
        string? sourceLanguage,
        string? rawQuery,
        out VocabularySearchPlan plan)
    {
        plan = CreatePlan(sourceLanguage, rawQuery);
        return plan.IsSearchActive;
    }

    public static bool TryNormalizeSourceLanguage(
        string? sourceLanguage,
        out string normalizedSourceLanguage)
    {
        if (string.IsNullOrWhiteSpace(sourceLanguage) || !IsValidUnicode(sourceLanguage))
        {
            normalizedSourceLanguage = string.Empty;
            return false;
        }

        var candidate = sourceLanguage.Trim();
        if (string.Equals(candidate, "en", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSourceLanguage = "en";
            return true;
        }

        if (string.Equals(candidate, "de", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSourceLanguage = "de";
            return true;
        }

        normalizedSourceLanguage = string.Empty;
        return false;
    }

    public static string CreatePrefixKey(string normalizedQuery, TokenKind tokenKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedQuery);
        if (!IsValidUnicode(normalizedQuery))
        {
            throw new ArgumentException("The search query contains invalid Unicode characters.", nameof(normalizedQuery));
        }

        var nfc = normalizedQuery.Trim().Normalize(NormalizationForm.FormC);
        return tokenKind switch
        {
            TokenKind.Word => $"W:{nfc.ToLowerInvariant()}",
            TokenKind.Acronym => $"A:{nfc}",
            TokenKind.Abbreviation => $"B:{nfc}",
            TokenKind.TechnicalTerm => $"T:{nfc}",
            _ => throw new ArgumentOutOfRangeException(nameof(tokenKind))
        };
    }

    public static VocabularySearchKeyRange CreateKeyRange(
        string normalizedQuery,
        TokenKind tokenKind)
    {
        var lowerBound = CreatePrefixKey(normalizedQuery, tokenKind);
        if (!TryComputeExclusiveUpperBound(lowerBound, out var upperBound))
        {
            throw new InvalidOperationException(
                "Failed to compute an exclusive UTF-8 prefix upper bound.");
        }

        return new VocabularySearchKeyRange(tokenKind, lowerBound, upperBound);
    }

    public static bool TryComputeExclusiveUpperBound(
        string inclusiveLowerBound,
        out string exclusiveUpperBound)
    {
        if (string.IsNullOrEmpty(inclusiveLowerBound) || !IsValidUnicode(inclusiveLowerBound))
        {
            exclusiveUpperBound = string.Empty;
            return false;
        }

        var runes = new List<Rune>(inclusiveLowerBound.Length);
        foreach (var rune in inclusiveLowerBound.EnumerateRunes())
        {
            runes.Add(rune);
        }

        for (var i = runes.Count - 1; i >= 0; i--)
        {
            var currentValue = runes[i].Value;
            if (currentValue >= 0x10FFFF)
            {
                continue;
            }

            var nextScalarValue = currentValue == 0xD7FF ? 0xE000 : currentValue + 1;
            var nextRune = new Rune(nextScalarValue);

            var builder = new StringBuilder(inclusiveLowerBound.Length + 2);
            for (var j = 0; j < i; j++)
            {
                builder.Append(runes[j].ToString());
            }

            builder.Append(nextRune.ToString());
            exclusiveUpperBound = builder.ToString();
            return true;
        }

        exclusiveUpperBound = string.Empty;
        return false;
    }

    public static int CompareUtf8Ordinal(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
    }

    public static bool IsWithinUtf8Range(
        string candidate,
        string inclusiveLowerBound,
        string exclusiveUpperBound)
    {
        if (string.IsNullOrEmpty(candidate)
            || string.IsNullOrEmpty(inclusiveLowerBound)
            || string.IsNullOrEmpty(exclusiveUpperBound)
            || !IsValidUnicode(candidate)
            || !IsValidUnicode(inclusiveLowerBound)
            || !IsValidUnicode(exclusiveUpperBound))
        {
            return false;
        }

        return CompareUtf8Ordinal(candidate, inclusiveLowerBound) >= 0
            && CompareUtf8Ordinal(candidate, exclusiveUpperBound) < 0;
    }

    public static bool IsValidUnicode(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var span = value.AsSpan();
        while (!span.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(span, out _, out var charsConsumed);
            if (status != OperationStatus.Done || charsConsumed <= 0)
            {
                return false;
            }

            span = span[charsConsumed..];
        }

        return true;
    }

    private static bool TryCountUnicodeScalars(string value, out int scalarCount)
    {
        scalarCount = 0;
        var span = value.AsSpan();
        while (!span.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(span, out _, out var charsConsumed);
            if (status != OperationStatus.Done || charsConsumed <= 0)
            {
                scalarCount = 0;
                return false;
            }

            scalarCount++;
            span = span[charsConsumed..];
        }

        return true;
    }

    private static bool IsEnglishFirstPersonSingularPronounQuery(
        string normalizedLanguage,
        string normalizedQuery) =>
        string.Equals(normalizedLanguage, "en", StringComparison.Ordinal)
        && normalizedQuery.ToLowerInvariant() is "i" or "me" or "my";

    private static VocabularySearchPlan CreateEmptyQueryPlan(string normalizedLanguage) =>
        new(
            VocabularySearchQueryStatus.EmptyQuery,
            normalizedLanguage,
            string.Empty,
            0,
            EmptyRanges,
            EmptyAliases);
}
