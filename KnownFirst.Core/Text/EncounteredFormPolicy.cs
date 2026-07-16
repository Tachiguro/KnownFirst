using System.Globalization;
using System.Text;

namespace KnownFirst.Core.Text;

public static class EncounteredFormPolicy
{
    public static IReadOnlyList<string> Deduplicate(TokenKind kind, IEnumerable<string> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);

        var groups = new List<FormGroup>();
        var groupsByKey = new Dictionary<string, FormGroup>(StringComparer.Ordinal);

        foreach (var form in forms)
        {
            ArgumentNullException.ThrowIfNull(form);
            var comparisonKey = CreateComparisonKey(kind, form);
            if (!groupsByKey.TryGetValue(comparisonKey, out var group))
            {
                group = new FormGroup(form, IsLowercaseWord(kind, form));
                groupsByKey.Add(comparisonKey, group);
                groups.Add(group);
                continue;
            }

            if (!group.HasLowercaseRepresentative && IsLowercaseWord(kind, form))
            {
                group.Representative = form;
                group.HasLowercaseRepresentative = true;
            }
        }

        return groups.Select(group => group.Representative).ToArray();
    }

    public static string CreateComparisonKey(TokenKind kind, string form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var normalized = form.Normalize(NormalizationForm.FormC);
        return kind == TokenKind.Word ? normalized.ToLowerInvariant() : normalized;
    }

    private static bool IsLowercaseWord(TokenKind kind, string form)
    {
        if (kind != TokenKind.Word)
        {
            return false;
        }

        var normalized = form.Normalize(NormalizationForm.FormC);
        return normalized.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is
                   UnicodeCategory.UppercaseLetter or
                   UnicodeCategory.LowercaseLetter or
                   UnicodeCategory.TitlecaseLetter or
                   UnicodeCategory.ModifierLetter or
                   UnicodeCategory.OtherLetter)
            && string.Equals(normalized, normalized.ToLowerInvariant(), StringComparison.Ordinal);
    }

    private sealed class FormGroup(string representative, bool hasLowercaseRepresentative)
    {
        public string Representative { get; set; } = representative;

        public bool HasLowercaseRepresentative { get; set; } = hasLowercaseRepresentative;
    }
}
