using System.Text;

namespace KnownFirst.Core.Text;

public static class AbbreviationPolicy
{
    private static readonly HashSet<string> KnownAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dr.",
        "e.g.",
        "i.e.",
        "etc.",
        "Jr.",
        "Mr.",
        "Mrs.",
        "Ms.",
        "No.",
        "Prof.",
        "Sr.",
        "U.K.",
        "U.S.",
        "bzw.",
        "ca.",
        "usw.",
        "z.B."
    };

    public static bool IsKnown(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return KnownAbbreviations.Contains(value.Normalize(NormalizationForm.FormC));
    }

    public static bool IsKnownStem(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return IsKnown(value.EndsWith(".", StringComparison.Ordinal) ? value : $"{value}.");
    }
}
