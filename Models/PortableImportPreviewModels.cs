namespace KnownFirst.Models.Backup;

/// <summary>
/// UI-facing classification of a read-only portable-import preview (KF-MEANING-001 Slice 9). Mirrors the
/// eventual <see cref="PortableImportDisposition"/> plus the pre-mutation outcomes a preview alone can
/// determine (<see cref="Blocked"/>, <see cref="ValidationFailed"/>, <see cref="Cancelled"/>,
/// <see cref="Failed"/>) that <see cref="PortableImportDisposition"/> never needs, since it is populated
/// only for a successful import.
/// </summary>
public enum PortableImportPreviewDisposition
{
    RestoreIntoEmpty,
    MergeChanges,
    MergeNoChange,
    Blocked,
    ValidationFailed,
    Cancelled,
    Failed
}

/// <summary>
/// Deterministic, UI-safe read-only preview of what a subsequent <c>ImportPortableArchiveAsync</c> call
/// would do against the target's current state — never a raw exception, a database integer id, the
/// complete archive payload, or the complete internal merge plan. Computed with no target mutation, no
/// safety copy, and no writer invocation; the actual import independently re-validates and re-evaluates
/// the operation immediately before any mutation, so this preview can never weaken those guards.
/// </summary>
public sealed record PortableImportPreview(
    PortableImportPreviewDisposition Disposition,
    bool CanConfirm,
    bool WillMutate,
    bool RequiresSafetyCopy,
    BackupPortableArchiveSummary? ArchiveSummary,
    int InsertedCount,
    int EnrichedCount,
    int PreservedVariantCount,
    int SkippedCount,
    IReadOnlyList<string> WarningCodes,
    string? ErrorCode)
{
    public static PortableImportPreview ForRestoreIntoEmpty(BackupPortableArchiveSummary archiveSummary) =>
        new(PortableImportPreviewDisposition.RestoreIntoEmpty, true, true, false, archiveSummary, 0, 0, 0, 0, [], null);

    public static PortableImportPreview ForMergeChanges(
        BackupPortableArchiveSummary archiveSummary,
        int insertedCount,
        int enrichedCount,
        int preservedVariantCount,
        int skippedCount,
        IReadOnlyList<string> warningCodes) =>
        new(PortableImportPreviewDisposition.MergeChanges, true, true, true, archiveSummary,
            insertedCount, enrichedCount, preservedVariantCount, skippedCount, warningCodes, null);

    public static PortableImportPreview ForMergeNoChange(
        BackupPortableArchiveSummary archiveSummary,
        int skippedCount,
        IReadOnlyList<string> warningCodes) =>
        new(PortableImportPreviewDisposition.MergeNoChange, false, false, false, archiveSummary,
            0, 0, 0, skippedCount, warningCodes, null);

    public static PortableImportPreview ForBlocked(
        PortableImportPreviewDisposition disposition,
        string? errorCode,
        BackupPortableArchiveSummary? archiveSummary = null,
        IReadOnlyList<string>? warningCodes = null) =>
        new(disposition, false, false, false, archiveSummary, 0, 0, 0, 0, warningCodes ?? [], errorCode);
}

/// <summary>
/// Stable error codes specific to the preview surface that have no existing <see cref="Services.DataSafety.BackupErrorCodes"/>
/// or <see cref="Services.DataSafety.Merge.MergePreflightErrorCodes"/> equivalent — a required merge
/// decision or blocking prerequisite carries no <c>ErrorCode</c> on the underlying plan (it is not itself
/// a failure, only a reason confirmation cannot proceed yet), so the preview surface needs its own stable
/// code to report the blocked state without ever surfacing raw internal detail.
/// </summary>
public static class PortableImportPreviewErrorCodes
{
    public const string MergeRequiresUserDecision = "merge-requires-user-decision";
    public const string MergeBlockedByPrerequisite = "merge-blocked-by-prerequisite";
}
