namespace KnownFirst.Services.DataSafety.Merge;

// Immutable, content-derived stable identities for cross-device merge matching. Each wraps an
// uppercase SHA-256 hex digest produced by CanonicalFingerprintBuilder with the entity's own
// version/domain discriminator. They are distinct struct types (not plain strings) specifically so
// that, for example, a VocabularyIdentity can never be accidentally compared against a
// MeaningIdentity even though both happen to be 64-character hex strings.

public readonly record struct SourceMaterialIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct VocabularyIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct MeaningIdentity(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// A LearningCard's physical matching identity: (stable VocabularyIdentity, Direction). This is
/// deliberately narrower than "everything that identifies a card" — per
/// docs/architecture/backup-merge-v1-design.md §4.3, MeaningId is not part of this key, because the
/// schema's real DB-enforced unique index is (WordId, Direction), not (MeaningId, Direction); only one
/// card can ever exist per matching identity regardless of how many distinct Meanings exist for the
/// word.
/// </summary>
public readonly record struct LearningCardMatchIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ReviewSessionIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ReviewCandidateIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PreparationSessionIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PreparationCandidateIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LearningSessionIdentity(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LearningSessionCardIdentity(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// A LearningReview event's content fingerprint (§6): identifies a review event by its complete
/// immutable field set plus the stable LearningCard identity — never the archive-local integer CardId.
/// </summary>
public readonly record struct LearningReviewFingerprint(string Value)
{
    public override string ToString() => Value;
}
