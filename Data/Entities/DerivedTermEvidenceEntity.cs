using SQLite;

namespace KnownFirst.Data.Entities;

[Table("DerivedTermEvidenceEntries")]
public sealed class DerivedTermEvidenceEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ReviewCandidateId { get; set; }

    public string SourceIdentity { get; set; } = string.Empty;

    public string SourceSurfaceForm { get; set; } = string.Empty;

    public int SourceStartPosition { get; set; }

    public int SourceLength { get; set; }

    public int SourceSentenceOrder { get; set; }

    public string ComponentForm { get; set; } = string.Empty;
}
