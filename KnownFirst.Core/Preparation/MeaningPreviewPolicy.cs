using System.Text;

namespace KnownFirst.Core.Preparation;

public static class MeaningPreviewPolicy
{
    public const int ClosedPreviewLength = 160;
    public const int AlternativePreviewLength = 240;

    public static string CreateClosedPreview(string value) => CreatePreview(value, ClosedPreviewLength);

    public static string CreateAlternativePreview(string value) => CreatePreview(value, AlternativePreviewLength);

    public static bool IsAlternativeTruncated(string value) => CountRunes(value) > AlternativePreviewLength;

    public static string CreatePreview(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        if (CountRunes(value) <= maximumLength)
        {
            return value;
        }

        var builder = new StringBuilder();
        foreach (var rune in value.EnumerateRunes().Take(maximumLength))
        {
            builder.Append(rune);
        }

        return $"{builder.ToString().TrimEnd()}…";
    }

    private static int CountRunes(string value) => value.EnumerateRunes().Count();
}
