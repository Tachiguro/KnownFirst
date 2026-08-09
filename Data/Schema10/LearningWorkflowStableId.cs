namespace KnownFirst.Data.Schema10;

/// <summary>
/// The canonical shape rules for a Schema-10 learning-workflow <c>StableId</c> (KF-BACKUP-005A), plus the
/// single generator for a fresh one.
///
/// <para>Exactly two accepted shapes exist, and both are canonical lowercase hexadecimal:
/// <list type="bullet">
/// <item><description>32 characters — a fresh identity, <c>Guid.NewGuid().ToString("N")</c>. Assigned to
/// every row created on Schema 10, and to every legacy <b>Active</b> workflow row the migration finds
/// (an Active workflow was never portable, so there is no cross-installation identity to
/// reconstruct).</description></item>
/// <item><description>64 characters — a deterministic SHA-256 digest produced by
/// <c>LearningWorkflowStableIdBootstrapPolicy</c> for a legacy <b>Completed</b> workflow row, whose
/// history <em>was</em> portable and therefore may already exist on another installation.</description></item>
/// </list>
/// The digest is never truncated to 32: a shortened SHA-256 would be indistinguishable from a GUID-form
/// id, which would erase exactly the distinction this type exists to keep visible.</para>
///
/// <para>Uppercase is deliberately rejected rather than normalized. <see cref="Guid.ToString(string)"/>
/// with "N" already emits lowercase, and the bootstrap policy lowercases its digest once, at the source —
/// so an uppercase value reaching here means some path bypassed both, which is a defect to surface, not
/// to silently repair.</para>
/// </summary>
public static class LearningWorkflowStableId
{
    /// <summary>Length of the fresh GUID-form ("N") identity.</summary>
    public const int GuidFormLength = 32;

    /// <summary>Length of the deterministic lowercase SHA-256 hex digest identity.</summary>
    public const int Sha256FormLength = 64;

    /// <summary>A fresh, never-before-used identity for a row that has no portable history to reconstruct.</summary>
    public static string NewGuidForm() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// True when <paramref name="value"/> is non-null, is exactly 32 or 64 characters long, and consists
    /// only of canonical lowercase hex digits.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (value is null || (value.Length != GuidFormLength && value.Length != Sha256FormLength))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Fail-closed guard for a value that must already be a valid StableId.</summary>
    public static string Require(string? value, string context) =>
        IsValid(value)
            ? value!
            : throw new ArgumentException(
                $"'{value ?? "<null>"}' is not a valid learning-workflow StableId ({context}); " +
                $"expected {GuidFormLength} or {Sha256FormLength} lowercase hex characters.",
                nameof(value));
}
