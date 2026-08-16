using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Builds an unambiguous canonical UTF-8 byte sequence for merge-identity/fingerprint hashing and
/// reduces it to an uppercase SHA-256 hex digest. Every field is encoded as an explicit null/non-null
/// marker byte, followed (for non-null values) by a fixed-width 4-byte big-endian UTF-8 byte-length
/// prefix and the raw payload bytes. This makes null and empty string distinguishable, and makes the
/// byte stream immune to delimiter-collision ambiguity, because every field's boundary is determined
/// by its own explicit length rather than by scanning for a separator character.
///
/// Every fingerprint family must start with a version/domain discriminator string (the constructor's
/// <c>domain</c> parameter, e.g. "KnownFirst.Merge.SourceMaterial.v1") so that two
/// structurally similar identity definitions never collide on the same hash space.
/// </summary>
public sealed class CanonicalFingerprintBuilder
{
    private const byte NullMarker = 0;
    private const byte NonNullMarker = 1;

    private readonly List<byte> _buffer = [];

    public CanonicalFingerprintBuilder(string domain)
    {
        ArgumentException.ThrowIfNullOrEmpty(domain);
        WriteString(domain);
    }

    public CanonicalFingerprintBuilder WriteNullableString(string? value)
    {
        if (value is null)
        {
            _buffer.Add(NullMarker);
            return this;
        }

        _buffer.Add(NonNullMarker);
        var payload = Encoding.UTF8.GetBytes(value);
        Span<byte> lengthPrefix = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, payload.Length);
        _buffer.AddRange(lengthPrefix.ToArray());
        _buffer.AddRange(payload);
        return this;
    }

    public CanonicalFingerprintBuilder WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return WriteNullableString(value);
    }

    public CanonicalFingerprintBuilder WriteBoolean(bool value) => WriteString(value ? "true" : "false");

    public CanonicalFingerprintBuilder WriteInt32(int value) => WriteString(value.ToString(CultureInfo.InvariantCulture));

    public CanonicalFingerprintBuilder WriteNullableInt32(int? value) =>
        WriteNullableString(value?.ToString(CultureInfo.InvariantCulture));

    public CanonicalFingerprintBuilder WriteInt64(long value) => WriteString(value.ToString(CultureInfo.InvariantCulture));

    public CanonicalFingerprintBuilder WriteNullableInt64(long? value) =>
        WriteNullableString(value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Documented exact precision: "G17", invariant culture — the round-trippable representation for
    /// <see cref="double"/> that reproduces the exact original bit pattern on parse-back, regardless
    /// of locale. <see cref="double.NaN"/> and positive/negative infinity are rejected rather than
    /// encoded: every current caller (e.g. scheduler EaseFactor) is a bounded, finite domain value
    /// (<c>KnownFirst.Core.Learning.SimpleSpacedRepetitionScheduler.MinimumEaseFactor</c> is 1.3), so a
    /// NaN/infinite value can only originate from a corrupt or manipulated archive. "G17" would still
    /// format such values deterministically ("NaN"/"Infinity"/"-Infinity"), but silently accepting them
    /// would let a non-domain-valid value flow into identity computation instead of failing closed.
    /// </summary>
    public CanonicalFingerprintBuilder WriteDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a domain-valid scheduler value; NaN and infinite values are rejected rather than encoded.",
                nameof(value));
        }

        return WriteString(value.ToString("G17", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Normalizes to UTC and formats with the invariant-culture round-trip ("O") representation.
    /// <see cref="DateTimeKind.Local"/> is rejected rather than silently converted, because that
    /// conversion would depend on the executing machine's time zone — a pure, deterministic function
    /// must not depend on environment state. Callers must supply values already in UTC, or
    /// <see cref="DateTimeKind.Unspecified"/> values that are already UTC-semantic (the convention
    /// used throughout this codebase's *Utc-suffixed fields).
    /// </summary>
    public CanonicalFingerprintBuilder WriteUtcTimestamp(DateTime value) => WriteString(FormatUtc(value));

    public CanonicalFingerprintBuilder WriteNullableUtcTimestamp(DateTime? value) =>
        WriteNullableString(value.HasValue ? FormatUtc(value.Value) : null);

    /// <summary>Symbolic enum name (never the numeric ordinal), so renumbering an enum cannot silently change a fingerprint.</summary>
    public CanonicalFingerprintBuilder WriteEnum<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = Enum.GetName(value)
            ?? throw new ArgumentOutOfRangeException(nameof(value), value, $"Undefined {typeof(TEnum).Name} value.");
        return WriteString(name);
    }

    public CanonicalFingerprintBuilder WriteNullableEnum<TEnum>(TEnum? value) where TEnum : struct, Enum
    {
        if (!value.HasValue)
        {
            _buffer.Add(NullMarker);
            return this;
        }

        return WriteEnum(value.Value);
    }

    public byte[] ToByteArray() => [.. _buffer];

    /// <summary>Uppercase SHA-256 hex digest over the complete canonical byte sequence.</summary>
    public string ComputeSha256Hex() => Convert.ToHexString(SHA256.HashData([.. _buffer]));

    private static string FormatUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeKind.Local => throw new ArgumentException(
                "Local DateTime values are not supported by canonical encoding, because converting them " +
                "would depend on the executing machine's time zone. Supply a Utc or Unspecified-treated-as-Utc value.",
                nameof(value)),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };

        return utc.ToString("O", CultureInfo.InvariantCulture);
    }
}
