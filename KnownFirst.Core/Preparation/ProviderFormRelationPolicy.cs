using System.Text;
using System.Text.RegularExpressions;

namespace KnownFirst.Core.Preparation;

public enum GrammaticalRelationKind
{
    Plural = 0,
    ThirdPersonSingular = 1,
    PastTense = 2,
    PastParticiple = 3,
    PresentParticiple = 4,
    Comparative = 5,
    Superlative = 6
}

public sealed record ProviderFormRelation(
    GrammaticalRelationKind Kind,
    string BaseLemma,
    string Relationship);

public static partial class ProviderFormRelationPolicy
{
    public static ProviderFormRelation? Resolve(IEnumerable<LexicalMeaning> meanings)
    {
        ArgumentNullException.ThrowIfNull(meanings);

        foreach (var meaning in meanings)
        {
            var relation = Resolve(meaning.Definition);
            if (relation is not null)
            {
                return relation;
            }
        }

        return null;
    }

    public static ProviderFormRelation? Resolve(string providerDefinition)
    {
        ArgumentNullException.ThrowIfNull(providerDefinition);
        var value = providerDefinition.Trim().Normalize(NormalizationForm.FormC);

        return Match(value, PluralPattern(), GrammaticalRelationKind.Plural, "plural of")
            ?? Match(value, ThirdPersonPattern(), GrammaticalRelationKind.ThirdPersonSingular, "third-person singular of")
            ?? Match(value, CombinedPastPattern(), GrammaticalRelationKind.PastTense, "past tense and past participle of")
            ?? Match(value, PastTensePattern(), GrammaticalRelationKind.PastTense, "past tense of")
            ?? Match(value, PastParticiplePattern(), GrammaticalRelationKind.PastParticiple, "past participle of")
            ?? Match(value, PresentParticiplePattern(), GrammaticalRelationKind.PresentParticiple, "present participle of")
            ?? Match(value, ComparativePattern(), GrammaticalRelationKind.Comparative, "comparative of")
            ?? Match(value, SuperlativePattern(), GrammaticalRelationKind.Superlative, "superlative of");
    }

    private static ProviderFormRelation? Match(
        string value,
        Regex pattern,
        GrammaticalRelationKind kind,
        string relationship)
    {
        var match = pattern.Match(value);
        return match.Success
            ? new ProviderFormRelation(kind, match.Groups["lemma"].Value, relationship)
            : null;
    }

    private const string LemmaCapture = @"(?<lemma>[\p{L}\p{M}][\p{L}\p{M}'’.-]*)";

    [GeneratedRegex(@"^(?:the\s+)?plural(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PluralPattern();

    [GeneratedRegex(@"^(?:the\s+)?third-person\s+singular(?:\s+simple\s+present(?:\s+indicative)?)?(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThirdPersonPattern();

    [GeneratedRegex(@"^(?:the\s+)?(?:simple\s+)?past(?:\s+tense)?\s+and\s+past\s+participle\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CombinedPastPattern();

    [GeneratedRegex(@"^(?:the\s+)?(?:simple\s+)?past\s+tense(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PastTensePattern();

    [GeneratedRegex(@"^(?:the\s+)?past\s+participle(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PastParticiplePattern();

    [GeneratedRegex(@"^(?:the\s+)?present\s+participle(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PresentParticiplePattern();

    [GeneratedRegex(@"^(?:the\s+)?comparative(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComparativePattern();

    [GeneratedRegex(@"^(?:the\s+)?superlative(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuperlativePattern();
}
