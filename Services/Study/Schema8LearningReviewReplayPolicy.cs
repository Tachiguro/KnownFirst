using KnownFirst.Core.Learning;
using KnownFirst.Core.Settings;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Schema8;
using KnownFirst.Services.DataSafety.Merge;

namespace KnownFirst.Services.Study;

/// <summary>
/// Deterministic local replay of a card's durable review history into per-<c>(CardId, AnswerVariantId)</c>
/// progress (KF-MEANING-001 Slice 4). Pure: no connection, no clock, no ambient state.
/// <para>
/// Binding properties. (1) Review events are the only source of truth; <c>AnswerVariantProgress</c> is a
/// rebuildable cache. (2) Logical identity is the fingerprint below — never the physical
/// <c>LearningReviews.Id</c>; among logical duplicates the lowest physical Id survives, and survivors replay
/// ordered by <c>ReviewedAtUtc</c> then physical Id. (3) Replay owns only currently
/// <see cref="AnswerVariantRequirement.Required"/> assignments and consumes only events at or after that
/// assignment's <c>RequiredSinceUtc</c>, so reviews from an earlier AcceptedOnly period can never
/// retroactively grant Required progress or mastery. (4) A persisted row belongs to the current Required
/// epoch only when its <c>CreatedAtUtc</c> is tick-equal to <c>RequiredSinceUtc</c>; otherwise nothing is
/// inherited from it. (5) Every produced timestamp is derived from review/boundary data, never from a wall
/// clock, so replay is reproducible across restarts and an unchanged second run writes nothing.
/// </para>
/// </summary>
public static class Schema8LearningReviewReplayPolicy
{
    /// <summary>Fingerprint domain. Distinct from the merge planner's <c>KnownFirst.Merge.LearningReview.v1</c>.</summary>
    public const string FingerprintDomain = "KnownFirst.LocalLearningReviewReplay.v1";

    /// <summary>Current replay algorithm version. A stored row below this is rebuilt; above this fails closed.</summary>
    public const int ReplayVersion = 1;

    public const string DiagnosticDuplicateFingerprint = "replay-duplicate-fingerprint";
    public const string DiagnosticUnattributedReview = "replay-unattributed-review";

    // ---- Logical identity ----

    /// <summary>
    /// The exact 12-field canonical fingerprint. Each nullable variant reference contributes an explicit
    /// presence marker followed by its value, so <see langword="null"/>, <c>0</c> and any other id are
    /// mutually distinguishable.
    /// </summary>
    public static string ComputeFingerprint(Schema8ReplayReviewEvent reviewEvent)
    {
        ArgumentNullException.ThrowIfNull(reviewEvent);

        return new CanonicalFingerprintBuilder(FingerprintDomain)
            .WriteInt32(reviewEvent.CardId)
            .WriteUtcTimestamp(Schema8Utc.Normalize(reviewEvent.ReviewedAtUtc))
            .WriteEnum(reviewEvent.Rating)
            .WriteBoolean(reviewEvent.WasTypedAnswer)
            .WriteBoolean(reviewEvent.WasCorrect)
            .WriteUtcTimestamp(Schema8Utc.Normalize(reviewEvent.DueAtUtc))
            .WriteInt32(reviewEvent.IntervalDays)
            .WriteDouble(reviewEvent.EaseFactor)
            .WriteBoolean(reviewEvent.TargetAnswerVariantId.HasValue)
            .WriteNullableInt32(reviewEvent.TargetAnswerVariantId)
            .WriteBoolean(reviewEvent.MatchedAnswerVariantId.HasValue)
            .WriteNullableInt32(reviewEvent.MatchedAnswerVariantId)
            .ComputeSha256Hex();
    }

    /// <summary>
    /// Deduplicates by logical fingerprint (lowest physical Id survives) and returns survivors ordered by
    /// <c>ReviewedAtUtc</c> ascending, then physical Id ascending.
    /// </summary>
    public static IReadOnlyList<Schema8ReplaySurvivor> SelectSurvivors(
        IEnumerable<Schema8ReplayReviewEvent> events,
        ICollection<Schema8ReplayDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        var byFingerprint = new Dictionary<string, Schema8ReplaySurvivor>(StringComparer.Ordinal);
        foreach (var reviewEvent in events.OrderBy(e => e.ReviewId))
        {
            var fingerprint = ComputeFingerprint(reviewEvent);
            if (byFingerprint.TryGetValue(fingerprint, out var existing))
            {
                diagnostics?.Add(new Schema8ReplayDiagnostic(
                    DiagnosticDuplicateFingerprint,
                    $"Review {reviewEvent.ReviewId} is a logical duplicate of review {existing.Event.ReviewId}; the lowest physical Id survives."));
                continue;
            }

            byFingerprint[fingerprint] = new Schema8ReplaySurvivor(reviewEvent, fingerprint);
        }

        return byFingerprint.Values
            .OrderBy(survivor => Schema8Utc.Normalize(survivor.Event.ReviewedAtUtc).Ticks)
            .ThenBy(survivor => survivor.Event.ReviewId)
            .ToList();
    }

    // ---- Historical schedule reconstruction ----

    /// <summary>
    /// The pre-event schedule of the first survivor: <c>CardSchedule.New(card.CreatedAtUtc)</c>. Because that
    /// state is <see cref="CardState.New"/> with a zero interval, the first event can never be a mastery
    /// review.
    /// </summary>
    public static CardSchedule FirstPreEventSchedule(DateTime cardCreatedAtUtc) =>
        CardSchedule.New(Schema8Utc.Normalize(cardCreatedAtUtc));

    /// <summary>
    /// The pre-event schedule of a later survivor, derived exclusively from the previous survivor's persisted
    /// post-review values. <c>SuccessfulReviewCount</c> and <c>LapseCount</c> are not recorded on a review and
    /// are irrelevant to <see cref="AutomaticLearningPolicy.IsMasteryReview"/>, so they stay zero. The card's
    /// current state is never used as historical state, and Learning vs. Relearning is never fabricated.
    /// </summary>
    public static CardSchedule NextPreEventSchedule(Schema8ReplayReviewEvent previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return new CardSchedule(
            previous.IntervalDays > 0 ? CardState.Review : CardState.Learning,
            Schema8Utc.Normalize(previous.DueAtUtc),
            previous.IntervalDays,
            previous.EaseFactor,
            0,
            0,
            Schema8Utc.Normalize(previous.ReviewedAtUtc),
            previous.Rating);
    }

    // ---- Attribution ----

    /// <summary>
    /// How one event relates to one answer variant. A matched different variant credits only that variant; the
    /// targeted variant is untouched — neither advanced, reset, nor failed.
    /// </summary>
    public static Schema8EventAttribution Classify(Schema8ReplayReviewEvent reviewEvent, int answerVariantId)
    {
        ArgumentNullException.ThrowIfNull(reviewEvent);

        if (reviewEvent.MatchedAnswerVariantId == answerVariantId)
        {
            return Schema8EventAttribution.Success;
        }

        if (reviewEvent.TargetAnswerVariantId != answerVariantId)
        {
            return Schema8EventAttribution.NotAttributed;
        }

        // Targeted variant with a different matched variant: explicitly untouched.
        if (reviewEvent.MatchedAnswerVariantId.HasValue)
        {
            return Schema8EventAttribution.NotAttributed;
        }

        return reviewEvent.WasCorrect ? Schema8EventAttribution.Success : Schema8EventAttribution.Failure;
    }

    // ---- Epoch identity ----

    /// <summary>
    /// Whether a persisted row is current-Required-epoch progress: exact tick equality of
    /// <c>progress.CreatedAtUtc</c> and <c>assignment.RequiredSinceUtc</c> after UTC normalization. No
    /// tolerance, never a raw SQL comparison.
    /// </summary>
    public static bool BelongsToCurrentEpoch(AnswerVariantProgressRow? persisted, DateTime requiredSinceUtc) =>
        persisted is not null && Schema8Utc.AreSameInstant(persisted.CreatedAtUtc, requiredSinceUtc);

    /// <summary>The deterministic zero baseline of a freshly started Required epoch.</summary>
    public static AnswerVariantProgressRow CreateEpochBaseline(int cardId, int answerVariantId, DateTime requiredSinceUtc)
    {
        var boundary = Schema8Utc.Normalize(requiredSinceUtc);
        return new AnswerVariantProgressRow
        {
            CardId = cardId,
            AnswerVariantId = answerVariantId,
            InteractionMode = LearningInteractionMode.Reading,
            ConsecutiveReadingSuccessCount = 0,
            ConsecutiveTypingSuccessCount = 0,
            ConsecutiveTypingFailureCount = 0,
            LastAssessedAtUtc = null,
            MasteryReviewExtensionScheduled = false,
            IsMastered = false,
            ReplayVersion = ReplayVersion,
            CreatedAtUtc = boundary,
            UpdatedAtUtc = boundary
        };
    }

    // ---- Replay ----

    /// <summary>
    /// Replays the card's survivors into one outcome per currently Required assignment.
    /// <paramref name="persistedProgress"/> contributes exactly one thing: a valid current-epoch row's
    /// <c>IsMastered = true</c> is preserved (mastery is monotonic <em>within one unchanged epoch</em>, which
    /// is what keeps a migrated, already-retired legacy card mastered). Everything else is recomputed.
    /// </summary>
    public static Schema8ReplayResult Replay(
        Schema8CardRow card,
        IReadOnlyList<Schema8AttributionCandidateRow> assignments,
        IReadOnlyList<Schema8ReplayReviewEvent> events,
        IReadOnlyList<AnswerVariantProgressRow> persistedProgress)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(persistedProgress);

        var diagnostics = new List<Schema8ReplayDiagnostic>();
        var survivors = SelectSurvivors(events, diagnostics);
        var outcomes = new List<Schema8ReplayVariantOutcome>();

        foreach (var assignment in assignments.Where(a => a.IsRequired).OrderBy(a => a.AnswerVariantId))
        {
            if (!assignment.RequiredSinceUtc.HasValue)
            {
                throw Schema8LearningDataException.Create(
                    Schema8LearningDataErrorCode.RequirementBoundaryViolation,
                    $"Assignment {assignment.AssignmentId} is Required but carries no RequiredSinceUtc.");
            }

            var boundary = Schema8Utc.Normalize(assignment.RequiredSinceUtc.Value);
            var persisted = persistedProgress
                .FirstOrDefault(row => row.AnswerVariantId == assignment.AnswerVariantId);
            if (persisted is not null && persisted.ReplayVersion > ReplayVersion)
            {
                throw Schema8LearningDataException.Create(
                    Schema8LearningDataErrorCode.ReplayVersionUnsupported,
                    $"Progress row for card {card.Id}/variant {assignment.AnswerVariantId} declares ReplayVersion " +
                    $"{persisted.ReplayVersion}, which is newer than this build's {ReplayVersion}.");
            }

            var inheritedMastery = BelongsToCurrentEpoch(persisted, boundary) && persisted!.IsMastered;
            outcomes.Add(ReplayVariant(card, assignment.AnswerVariantId, boundary, survivors, inheritedMastery));
        }

        return new Schema8ReplayResult(survivors, outcomes, diagnostics);
    }

    private static Schema8ReplayVariantOutcome ReplayVariant(
        Schema8CardRow card,
        int answerVariantId,
        DateTime boundary,
        IReadOnlyList<Schema8ReplaySurvivor> survivors,
        bool inheritedMastery)
    {
        var state = AutomaticLearningState.Initial;
        var isMastered = inheritedMastery;
        DateTime? lastAssessedAtUtc = null;
        var consumed = 0;

        Schema8ReplayReviewEvent? previous = null;
        foreach (var survivor in survivors)
        {
            var reviewEvent = survivor.Event;
            var preEventSchedule = previous is null
                ? FirstPreEventSchedule(card.CreatedAtUtc)
                : NextPreEventSchedule(previous);
            previous = reviewEvent;

            var attribution = Classify(reviewEvent, answerVariantId);
            if (attribution == Schema8EventAttribution.NotAttributed)
            {
                continue;
            }

            // Boundary filter: an event before this Required epoch started can never contribute.
            if (Schema8Utc.Normalize(reviewEvent.ReviewedAtUtc).Ticks < boundary.Ticks)
            {
                continue;
            }

            var (nextState, mastered) = ApplyEvent(
                state, preEventSchedule, reviewEvent, attribution == Schema8EventAttribution.Success);
            state = nextState;
            isMastered = isMastered || mastered;
            lastAssessedAtUtc = Schema8Utc.Normalize(reviewEvent.ReviewedAtUtc);
            consumed++;
        }

        return new Schema8ReplayVariantOutcome(
            card.Id, answerVariantId, boundary, state, isMastered, lastAssessedAtUtc, consumed);
    }

    /// <summary>
    /// Applies one attributed, eligible event: reconstructs whether it was a mastery review from the
    /// pre-event schedule, records the reading or typing assessment (the durable <c>WasTypedAnswer</c> flag
    /// decides which), determines mastery, and reproduces the extension-state behaviour.
    /// </summary>
    public static (AutomaticLearningState State, bool MasteryAchieved) ApplyEvent(
        AutomaticLearningState state,
        CardSchedule preEventSchedule,
        Schema8ReplayReviewEvent reviewEvent,
        bool success)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(preEventSchedule);
        ArgumentNullException.ThrowIfNull(reviewEvent);

        var isMasteryReview = AutomaticLearningPolicy.IsMasteryReview(preEventSchedule);
        var next = reviewEvent.WasTypedAnswer
            ? AutomaticLearningPolicy.RecordTypingAssessment(state, success)
            : AutomaticLearningPolicy.RecordRecallAssessment(state, reviewEvent.Rating != ReviewRating.Again);

        var masteryAchieved = isMasteryReview
            && reviewEvent.WasTypedAnswer
            && success
            && AutomaticLearningPolicy.HasTypingMastery(next);

        if (isMasteryReview
            && reviewEvent.Rating != ReviewRating.Again
            && !masteryAchieved
            && !next.MasteryReviewExtensionScheduled)
        {
            next = next with { MasteryReviewExtensionScheduled = true };
        }

        return (next, masteryAchieved);
    }

    // ---- Progress replacement planning ----

    /// <summary>
    /// Computes the complete expected progress replacement before any mutation.
    /// <para>
    /// Only currently Required, replay-owned rows are inserted or updated. Rows of AcceptedOnly assignments
    /// are preserved byte-for-byte: they are never inserted, updated, version-upgraded, or deleted. A
    /// persisted row whose variant no longer has any assignment for this card's direction is deleted, because
    /// it can no longer be attributed. A row is written only when at least one replay-owned field differs, so
    /// an unchanged second replay yields <see cref="Schema8ProgressReplacementPlan.IsEmpty"/>.
    /// </para>
    /// </summary>
    public static Schema8ProgressReplacementPlan PlanProgressReplacement(
        IReadOnlyList<Schema8AttributionCandidateRow> assignments,
        IReadOnlyList<AnswerVariantProgressRow> persistedProgress,
        Schema8ReplayResult replayResult)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(persistedProgress);
        ArgumentNullException.ThrowIfNull(replayResult);

        var inserts = new List<AnswerVariantProgressRow>();
        var updates = new List<AnswerVariantProgressRow>();
        var deletes = new List<int>();

        var assignedVariantIds = assignments.Select(a => a.AnswerVariantId).ToHashSet();
        var acceptedOnlyVariantIds = assignments
            .Where(a => !a.IsRequired)
            .Select(a => a.AnswerVariantId)
            .ToHashSet();

        foreach (var outcome in replayResult.Outcomes.OrderBy(o => o.AnswerVariantId))
        {
            var expected = outcome.ToRow();
            var persisted = persistedProgress.FirstOrDefault(row => row.AnswerVariantId == outcome.AnswerVariantId);
            if (persisted is null)
            {
                inserts.Add(expected);
                continue;
            }

            if (!AreReplayOwnedFieldsEqual(persisted, expected))
            {
                updates.Add(expected);
            }
        }

        foreach (var persisted in persistedProgress.OrderBy(row => row.AnswerVariantId))
        {
            if (acceptedOnlyVariantIds.Contains(persisted.AnswerVariantId))
            {
                continue; // preserved byte-for-byte while AcceptedOnly
            }

            if (!assignedVariantIds.Contains(persisted.AnswerVariantId))
            {
                deletes.Add(persisted.AnswerVariantId);
            }
        }

        return new Schema8ProgressReplacementPlan(inserts, updates, deletes);
    }

    /// <summary>Field-by-field equality over every replay-owned column; timestamps compared by exact ticks.</summary>
    public static bool AreReplayOwnedFieldsEqual(AnswerVariantProgressRow persisted, AnswerVariantProgressRow expected)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(expected);

        return persisted.InteractionMode == expected.InteractionMode
            && persisted.ConsecutiveReadingSuccessCount == expected.ConsecutiveReadingSuccessCount
            && persisted.ConsecutiveTypingSuccessCount == expected.ConsecutiveTypingSuccessCount
            && persisted.ConsecutiveTypingFailureCount == expected.ConsecutiveTypingFailureCount
            && Schema8Utc.AreSameInstant(persisted.LastAssessedAtUtc, expected.LastAssessedAtUtc)
            && persisted.MasteryReviewExtensionScheduled == expected.MasteryReviewExtensionScheduled
            && persisted.IsMastered == expected.IsMastered
            && persisted.ReplayVersion == expected.ReplayVersion
            && Schema8Utc.AreSameInstant(persisted.CreatedAtUtc, expected.CreatedAtUtc)
            && Schema8Utc.AreSameInstant(persisted.UpdatedAtUtc, expected.UpdatedAtUtc);
    }

    /// <summary>Applies an already-planned replacement. Ordered deterministically: deletes, inserts, updates.</summary>
    public static void ApplyProgressPlan(SQLite.SQLiteConnection connection, int cardId, Schema8ProgressReplacementPlan plan)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var variantId in plan.DeletedAnswerVariantIds)
        {
            Schema8LearningRepository.DeleteProgress(connection, cardId, variantId);
        }

        foreach (var row in plan.Inserts)
        {
            Schema8LearningRepository.InsertProgress(connection, row);
        }

        foreach (var row in plan.Updates)
        {
            Schema8LearningRepository.UpdateProgress(connection, row);
        }
    }

    // ---- Projection helper ----

    public static Schema8ReplayReviewEvent ToReplayEvent(Schema8ReviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new Schema8ReplayReviewEvent(
            row.Id, row.CardId, row.Rating, row.WasTypedAnswer, row.WasCorrect,
            Schema8Utc.Normalize(row.ReviewedAtUtc), Schema8Utc.Normalize(row.DueAtUtc),
            row.IntervalDays, row.EaseFactor, row.TargetAnswerVariantId, row.MatchedAnswerVariantId);
    }

    /// <summary>
    /// Resolves the interaction mode for the current Required target from its freshly replayed current-epoch
    /// state. When no app-settings service is available, <see cref="LearningMode.Automatic"/> is used, so the
    /// replayed state itself decides (a fresh epoch therefore starts in Reading).
    /// </summary>
    public static LearningInteractionMode ResolveInteraction(
        LearningMode? learningMode,
        Schema8ReplayVariantOutcome targetOutcome,
        CardDirection direction = CardDirection.MeaningToTerm)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);
        if (direction == CardDirection.TermToMeaning)
        {
            return LearningInteractionMode.Reading;
        }

        return AutomaticLearningPolicy.ResolveInteraction(
            learningMode ?? LearningMode.Automatic,
            targetOutcome.State,
            direction);
    }
}
