namespace KnownFirst.Services.DataSafety.Merge;

/// <summary>
/// Narrow test-only seam for exercising failure paths in the safety-copy write/validate/finalize
/// pipeline (archive-write failure, staged/final validation failure, metadata-write failure,
/// metadata validation failure, cancellation after staging begins). Production code always uses
/// <see cref="None"/>; every hook is a no-op there, and the real filesystem and the real
/// <see cref="BackupArchiveReader"/> perform every check. This is not a general-purpose filesystem
/// abstraction — it only marks the specific points a test may need to intervene at.
/// </summary>
public class MergeSafetyCopyFailureInjector
{
    public static readonly MergeSafetyCopyFailureInjector None = new();

    public virtual void BeforeArchiveWrite()
    {
    }

    public virtual void AfterArchiveWritten(string stagingArchivePath)
    {
    }

    public virtual void AfterArchiveMoved(string finalArchivePath)
    {
    }

    public virtual void BeforeMetadataWrite()
    {
    }

    public virtual void AfterMetadataWritten(string stagingMetadataPath)
    {
    }

    /// <summary>
    /// Fires once the new archive/metadata pair has been reopened, validated, and cross-checked —
    /// the explicit commit point — immediately before best-effort old-copy cleanup runs. Intended
    /// only for proving cancellation-token behavior at/after the commit point (the committed pair
    /// must survive regardless); it is not a general failure-injection point for cleanup itself,
    /// since old-copy cleanup is already internally non-fatal by construction.
    /// </summary>
    public virtual void AfterCommit()
    {
    }
}
