namespace KnownFirst.Services.DataSafety.Merge;

public enum FixtureRecordClassification
{
    New,
    ExactDuplicate
}

public readonly record struct FixtureClassificationResult<TItem>(TItem Item, FixtureRecordClassification Classification);

/// <summary>
/// Pure, database-independent identity-set utilities used only to prove merge-identity and
/// convergence behavior against fixture data (design §11). This is not the future SQLite merge writer
/// (slice 5): it assigns no database IDs, never touches storage, and its "union" result is a plain
/// in-memory list, not a persisted write plan.
/// </summary>
public static class MergeFixtureSet
{
    /// <summary>Classifies each archive-side fixture item as New (no matching identity in target) or ExactDuplicate.</summary>
    public static IReadOnlyList<FixtureClassificationResult<TItem>> Classify<TItem>(
        IReadOnlyCollection<TItem> targetItems,
        IReadOnlyCollection<TItem> archiveItems,
        Func<TItem, string> identityKey)
    {
        ArgumentNullException.ThrowIfNull(targetItems);
        ArgumentNullException.ThrowIfNull(archiveItems);
        ArgumentNullException.ThrowIfNull(identityKey);

        var targetKeys = new HashSet<string>(targetItems.Select(identityKey), StringComparer.Ordinal);

        return archiveItems
            .Select(item => new FixtureClassificationResult<TItem>(
                item,
                targetKeys.Contains(identityKey(item)) ? FixtureRecordClassification.ExactDuplicate : FixtureRecordClassification.New))
            .ToList();
    }

    /// <summary>
    /// Deduplicated union of target and archive fixture items by stable identity: target-only and
    /// archive-only items both survive, distinct-identity variants are both preserved, and identity
    /// collisions keep the target's own copy (never a silent overwrite). Result order is deterministic
    /// (ordinal identity-key sort), independent of input enumeration order.
    /// </summary>
    public static IReadOnlyList<TItem> DeduplicatedUnion<TItem>(
        IReadOnlyCollection<TItem> targetItems,
        IReadOnlyCollection<TItem> archiveItems,
        Func<TItem, string> identityKey)
    {
        ArgumentNullException.ThrowIfNull(targetItems);
        ArgumentNullException.ThrowIfNull(archiveItems);
        ArgumentNullException.ThrowIfNull(identityKey);

        var byIdentity = new Dictionary<string, TItem>(StringComparer.Ordinal);
        foreach (var item in targetItems)
        {
            byIdentity[identityKey(item)] = item;
        }

        foreach (var item in archiveItems)
        {
            byIdentity.TryAdd(identityKey(item), item);
        }

        return byIdentity
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Value)
            .ToList();
    }
}
