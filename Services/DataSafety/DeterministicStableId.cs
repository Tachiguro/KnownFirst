using System.Security.Cryptography;
using System.Text;

namespace KnownFirst.Services.DataSafety;

/// <summary>
/// Deterministic, repeatable StableId generation for the archive-format v1→v2 in-memory upgrade
/// (KF-MEANING-001 Slice 2). Never <see cref="Guid.NewGuid"/> — the same seed always produces the same
/// StableId, so reading the same valid v1 archive twice produces a byte-identical upgraded
/// <c>BackupPayloadV2</c>. Uses a fixed KnownFirst namespace GUID plus a SHA-256-based UUIDv5-style
/// construction (RFC 4122 version/variant bits set on a SHA-256 digest rather than the RFC's own SHA-1,
/// which is immaterial here — only determinism and collision resistance matter, not RFC compliance).
/// </summary>
internal static class DeterministicStableId
{
    // Fixed, permanent constant — must never change, or every previously-upgraded archive's StableIds
    // would silently shift.
    private static readonly Guid Namespace = new("6f2a9d3c-6b3d-4e1a-9c5f-2d8b7a6e4f10");

    public static string Generate(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var namespaceBytes = Namespace.ToByteArray();
        var seedBytes = Encoding.UTF8.GetBytes(seed);
        var combined = new byte[namespaceBytes.Length + seedBytes.Length];
        namespaceBytes.CopyTo(combined, 0);
        seedBytes.CopyTo(combined, namespaceBytes.Length);

        var hash = SHA256.HashData(combined);
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5 (name-based)
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(guidBytes).ToString("N");
    }
}
