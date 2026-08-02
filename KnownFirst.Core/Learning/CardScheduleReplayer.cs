namespace KnownFirst.Core.Learning;

/// <summary>
/// Deterministic replay of a card's complete review history through the ordinary spaced-repetition
/// scheduler (KF-MEANING-001 Slice 8 correction). Pure: no database access, no wall-clock time, no second
/// scheduling algorithm — every transition is produced by the same <see cref="ISpacedRepetitionScheduler"/>
/// an ordinary application review uses. Used by the populated-target merge writer to recompute a matched
/// card's derived scheduling fields whenever its surviving review-event set changes: review history is
/// authoritative, the card's scheduling fields are derived from it, never carried over unchanged from
/// either side's stale snapshot.
/// </summary>
public static class CardScheduleReplayer
{
    /// <summary>
    /// One review event as replay needs it: the scheduling inputs plus a stable, content-derived
    /// fingerprint. The fingerprint deduplicates logically-identical events and breaks a same-
    /// <see cref="ReviewedAtUtc"/> tie deterministically; its exact value is opaque here — callers supply
    /// whichever existing fingerprint contract already defines "the same event" for their event set.
    /// </summary>
    public readonly record struct ReviewEvent(DateTime ReviewedAtUtc, ReviewRating Rating, string FingerprintHex);

    /// <summary>
    /// Deduplicates <paramref name="events"/> by <see cref="ReviewEvent.FingerprintHex"/> (first survivor
    /// per fingerprint — duplicates are byte-identical by definition, so which copy survives cannot affect
    /// the result), orders the survivors by <see cref="ReviewEvent.ReviewedAtUtc"/> then by
    /// <see cref="ReviewEvent.FingerprintHex"/> (stable ordinal comparison — the hex fingerprint's ordinal
    /// order is exactly its underlying byte order) as the fixed tie-break, and replays them from
    /// <paramref name="initial"/> through <paramref name="scheduler"/>. Deterministic for identical inputs
    /// regardless of the enumeration order of <paramref name="events"/>.
    /// </summary>
    public static CardSchedule Replay(
        CardSchedule initial,
        IEnumerable<ReviewEvent> events,
        ISpacedRepetitionScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(scheduler);

        var survivors = events
            .GroupBy(reviewEvent => reviewEvent.FingerprintHex, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(reviewEvent => reviewEvent.ReviewedAtUtc)
            .ThenBy(reviewEvent => reviewEvent.FingerprintHex, StringComparer.Ordinal)
            .ToList();

        var schedule = initial;
        foreach (var reviewEvent in survivors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            schedule = scheduler.Schedule(schedule, reviewEvent.Rating, reviewEvent.ReviewedAtUtc);
        }

        return schedule;
    }
}
