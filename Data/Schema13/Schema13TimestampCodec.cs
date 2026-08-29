using System.Globalization;

namespace KnownFirst.Data.Schema13;

internal static class Schema13TimestampCodec
{
    private const string StandardUtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private static readonly string[] AllowedUtcFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.fffffffK",
        "yyyy-MM-dd'T'HH:mm:ss.ffffffK",
        "yyyy-MM-dd'T'HH:mm:ss.fffffK",
        "yyyy-MM-dd'T'HH:mm:ss.ffffK",
        "yyyy-MM-dd'T'HH:mm:ss.fffK",
        "yyyy-MM-dd'T'HH:mm:ss.ffK",
        "yyyy-MM-dd'T'HH:mm:ss.fK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "O"
    ];

    public static string FormatUtc(DateTime dateTime)
    {
        if (dateTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", nameof(dateTime));
        }

        return dateTime.ToString(StandardUtcFormat, CultureInfo.InvariantCulture);
    }

    public static string FormatUtc(DateTimeOffset dateTimeOffset)
    {
        if (dateTimeOffset.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset zero).", nameof(dateTimeOffset));
        }

        return dateTimeOffset.ToString(StandardUtcFormat, CultureInfo.InvariantCulture);
    }

    public static DateTime ParseUtcDateTime(string? text)
    {
        var dto = ParseUtcDateTimeOffset(text);
        return new DateTime(dto.UtcTicks, DateTimeKind.Utc);
    }

    public static DateTimeOffset ParseUtcDateTimeOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Timestamp string cannot be null, empty, or whitespace.");
        }

        if (!text.EndsWith('Z') && !text.EndsWith('z'))
        {
            throw new FormatException($"Timestamp '{text}' must explicitly designate UTC with 'Z'.");
        }

        if (!DateTimeOffset.TryParseExact(
                text,
                AllowedUtcFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            throw new FormatException($"Timestamp '{text}' is not a valid ISO-8601 UTC timestamp.");
        }

        if (dto.Offset != TimeSpan.Zero)
        {
            throw new FormatException($"Timestamp '{text}' has non-zero offset.");
        }

        return dto;
    }
}
