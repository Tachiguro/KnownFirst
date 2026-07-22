using SQLite;

namespace KnownFirst.Data.Entities;

[Table("ContextSnapshots")]
public sealed class ContextSnapshotEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_ContextSnapshots_Meaning_Fingerprint", 1, Unique = true)]
    public int MeaningId { get; set; }

    [Indexed]
    public int WordId { get; set; }

    [Indexed]
    public int SourceDocumentId { get; set; }

    public string SourceDocumentTitle { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public int TargetStart { get; set; }

    public int TargetLength { get; set; }

    [Indexed("IX_ContextSnapshots_Meaning_Fingerprint", 2, Unique = true)]
    public string NormalizedFingerprint { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
