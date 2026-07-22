using System.Text;
using System.Text.RegularExpressions;

namespace KnownFirst.Core.Preparation;

public enum GrammaticalRelationKind
{
    Plural = 0,
    Singular = 1,
    ThirdPersonSingular = 2,
    PastTense = 3,
    PastParticiple = 4,
    PresentParticiple = 5,
    Comparative = 6,
    Superlative = 7
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
            ?? Match(value, SingularPattern(), GrammaticalRelationKind.Singular, "singular of")
            ?? Match(value, ThirdPersonPattern(), GrammaticalRelationKind.ThirdPersonSingular, "third-person singular of")
            ?? Match(value, CombinedPastPattern(), GrammaticalRelationKind.PastTense, "past tense and past participle of")
            ?? Match(value, PastTensePattern(), GrammaticalRelationKind.PastTense, "past tense of")
            ?? Match(value, PastParticiplePattern(), GrammaticalRelationKind.PastParticiple, "past participle of")
            ?? Match(value, PresentParticiplePattern(), GrammaticalRelationKind.PresentParticiple, "present participle of")
            ?? Match(value, ComparativePattern(), GrammaticalRelationKind.Comparative, "comparative of")
            ?? Match(value, SuperlativePattern(), GrammaticalRelationKind.Superlative, "superlative of")
            ?? Match(value, GermanPluralPattern(), GrammaticalRelationKind.Plural, "plural of")
            ?? Match(value, GermanSingularPattern(), GrammaticalRelationKind.Singular, "singular of")
            ?? Match(value, GermanThirdPersonPattern(), GrammaticalRelationKind.ThirdPersonSingular, "third-person singular of")
            ?? Match(value, GermanPastTensePattern(), GrammaticalRelationKind.PastTense, "past tense of")
            ?? Match(value, GermanPastParticiplePattern(), GrammaticalRelationKind.PastParticiple, "past participle of")
            ?? Match(value, GermanPresentParticiplePattern(), GrammaticalRelationKind.PresentParticiple, "present participle of")
            ?? Match(value, GermanComparativePattern(), GrammaticalRelationKind.Comparative, "comparative of")
            ?? Match(value, GermanSuperlativePattern(), GrammaticalRelationKind.Superlative, "superlative of");
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

    [GeneratedRegex(@"^(?:the\s+)?singular(?:\s+form)?\s+of\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SingularPattern();

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

    [GeneratedRegex(@"^(?:(?:nominativ|genitiv|dativ|akkusativ)\s+)?plural\s+des\s+(?:substantivs|nomens)\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanPluralPattern();

    [GeneratedRegex(@"^(?:(?:nominativ|genitiv|dativ|akkusativ)\s+)?singular\s+des\s+(?:substantivs|nomens)\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanSingularPattern();

    [GeneratedRegex(@"^(?:\d+\.|erste|zweite|dritte)\s+person\s+singular(?:\s+[\p{L}\p{M}-]+){0,5}\s+(?:des\s+verbs|von)\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanThirdPersonPattern();

    [GeneratedRegex(@"^(?:präteritum|imperfekt)(?:\s+[\p{L}\p{M}-]+){0,4}\s+(?:des\s+verbs|von)\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanPastTensePattern();

    [GeneratedRegex(@"^(?:partizip\s+(?:ii|2)|partizip\s+perfekt)(?:\s+[\p{L}\p{M}-]+){0,4}\s+(?:des\s+verbs|von)\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanPastParticiplePattern();

    [GeneratedRegex(@"^(?:partizip\s+(?:i|1)|partizip\s+präsens)(?:\s+[\p{L}\p{M}-]+){0,4}\s+(?:des\s+verbs|von)\s+" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanPresentParticiplePattern();

    [GeneratedRegex(@"^komparativ(?:form)?\s+(?:(?:des\s+adjektivs|von)\s+)?" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanComparativePattern();

    [GeneratedRegex(@"^superlativ(?:form)?\s+(?:(?:des\s+adjektivs|von)\s+)?" + LemmaCapture + @"(?:[\s.,;:]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GermanSuperlativePattern();
}
