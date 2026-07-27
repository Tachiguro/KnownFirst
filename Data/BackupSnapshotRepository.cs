using KnownFirst.Data.Entities;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data;

public enum PortableSnapshotCaptureStatus
{
    Success,
    BlockedByActiveWorkflow
}

/// <summary>
/// Result of a race-free "no active workflow, then capture" check performed inside a single
/// <see cref="IKnownFirstDatabase.ExecuteSnapshotAsync{T}"/> callback. Slice 2 (merge safety copy)
/// only guarantees that the safety-copy snapshot itself was captured with no active workflow at
/// that instant; a future merge writer must re-check the condition again immediately before mutation.
/// </summary>
public sealed record PortableSnapshotCaptureResult(
    PortableSnapshotCaptureStatus Status,
    BackupSnapshot? Snapshot);

public static class BackupSnapshotRepository
{
    /// <summary>
    /// Race-free precondition for merge safety copies: checks for any active
    /// ReviewSession/PreparationSession/LearningSession and captures the portable snapshot in the
    /// same transaction/connection when none exists, so no time-of-check/time-of-use gap can let an
    /// active workflow slip into a safety copy. Does not alter <see cref="CapturePortableSnapshot"/>,
    /// which remains the unchanged path for ordinary user export.
    /// </summary>
    public static PortableSnapshotCaptureResult CapturePortableSnapshotForMergeSafetyCopy(
        SQLiteConnection connection)
    {
        // ToList() forces every stored row to be classified now, so an unrecognized status value
        // aborts here (fail-closed) before any directory creation, timestamp generation, filename
        // generation, or file write is ever reached.
        var hasActiveReview = connection.Table<ReviewSessionEntity>().ToList()
            .Select(session => IsBlockingReviewStatus(session.Status))
            .ToList()
            .Any(isBlocking => isBlocking);
        var hasActivePreparation = connection.Table<PreparationSessionEntity>().ToList()
            .Select(session => IsBlockingPreparationStatus(session.Status))
            .ToList()
            .Any(isBlocking => isBlocking);
        var hasActiveLearning = connection.Table<LearningSessionEntity>().ToList()
            .Select(session => IsBlockingLearningStatus(session.Status))
            .ToList()
            .Any(isBlocking => isBlocking);

        if (hasActiveReview || hasActivePreparation || hasActiveLearning)
        {
            return new PortableSnapshotCaptureResult(
                PortableSnapshotCaptureStatus.BlockedByActiveWorkflow,
                null);
        }

        return new PortableSnapshotCaptureResult(
            PortableSnapshotCaptureStatus.Success,
            CapturePortableSnapshot(connection));
    }

    /// <summary>
    /// Fail-closed classification: any value not explicitly recognized as blocking or terminal
    /// throws, rather than being silently treated as terminal (which would let an unrecognized
    /// future status proceed as if safe).
    /// </summary>
    private static bool IsBlockingReviewStatus(KnownFirst.Models.ReviewSessionStatus status) =>
        status switch
        {
            KnownFirst.Models.ReviewSessionStatus.Active => true,
            KnownFirst.Models.ReviewSessionStatus.Completed => false,
            _ => throw new InvalidOperationException(
                $"Unrecognized ReviewSessionStatus value '{status}' encountered during merge safety copy classification.")
        };

    private static bool IsBlockingPreparationStatus(KnownFirst.Models.PreparationSessionStatus status) =>
        status switch
        {
            KnownFirst.Models.PreparationSessionStatus.Active => true,
            KnownFirst.Models.PreparationSessionStatus.Completed => false,
            KnownFirst.Models.PreparationSessionStatus.Cancelled => false,
            _ => throw new InvalidOperationException(
                $"Unrecognized PreparationSessionStatus value '{status}' encountered during merge safety copy classification.")
        };

    private static bool IsBlockingLearningStatus(KnownFirst.Models.LearningSessionStatus status) =>
        status switch
        {
            KnownFirst.Models.LearningSessionStatus.Active => true,
            KnownFirst.Models.LearningSessionStatus.Completed => false,
            _ => throw new InvalidOperationException(
                $"Unrecognized LearningSessionStatus value '{status}' encountered during merge safety copy classification.")
        };

    public static BackupSnapshot CapturePortableSnapshot(SQLiteConnection connection)
    {
        var snapshot = CaptureSnapshot(connection);

        var reviewSessions = snapshot.ReviewSessions
            .Where(session => session.Status == KnownFirst.Models.ReviewSessionStatus.Completed)
            .ToList();
        var reviewSessionIds = reviewSessions.Select(session => session.Id).ToHashSet();

        var preparationSessions = snapshot.PreparationSessions
            .Where(session => session.Status != KnownFirst.Models.PreparationSessionStatus.Active)
            .ToList();
        var preparationSessionIds = preparationSessions.Select(session => session.Id).ToHashSet();

        var learningSessions = snapshot.LearningSessions
            .Where(session => session.Status == KnownFirst.Models.LearningSessionStatus.Completed)
            .ToList();
        var learningSessionIds = learningSessions.Select(session => session.Id).ToHashSet();

        return snapshot with
        {
            ReviewSessions = reviewSessions,
            ReviewCandidates = snapshot.ReviewCandidates
                .Where(candidate => reviewSessionIds.Contains(candidate.SessionId))
                .ToList(),
            PreparationSessions = preparationSessions,
            PreparationCandidates = snapshot.PreparationCandidates
                .Where(candidate => preparationSessionIds.Contains(candidate.SessionId))
                .ToList(),
            LearningReviews = snapshot.LearningReviews
                .Where(review => learningSessionIds.Contains(review.SessionId))
                .ToList(),
            LearningSessions = learningSessions,
            LearningSessionCards = snapshot.LearningSessionCards
                .Where(item => learningSessionIds.Contains(item.SessionId))
                .ToList()
        };
    }

    public static BackupSnapshot CaptureSnapshot(SQLiteConnection connection)
    {
        var userVersion = connection.ExecuteScalar<int>("PRAGMA user_version");
        if (userVersion > DatabaseSchema.CurrentVersion)
        {
            throw new DatabaseSchemaCompatibilityException(userVersion, DatabaseSchema.CurrentVersion);
        }

        var docs = GetBoundedTable<DocumentEntity>(connection, BackupFormatLimits.MaxSourceMaterials);
        var words = GetBoundedTable<WordEntity>(connection, BackupFormatLimits.MaxVocabularyItems);
        var wordForms = GetBoundedTable<WordFormEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var sentences = GetBoundedTable<SentenceSpanEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var occurrences = GetBoundedTable<WordOccurrenceEntity>(connection, BackupFormatLimits.MaxOccurrences);
        var meanings = GetBoundedTable<MeaningEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var reviewStates = GetBoundedTable<ReviewStateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var reviewSessions = GetBoundedTable<ReviewSessionEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var reviewCandidates = GetBoundedTable<ReviewCandidateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var prepSessions = GetBoundedTable<PreparationSessionEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var prepCandidates = GetBoundedTable<PreparationCandidateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var contextSnapshots = GetBoundedTable<ContextSnapshotEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var cards = GetBoundedTable<LearningCardEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var reviews = GetBoundedTable<LearningReviewEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var learningSessions = GetBoundedTable<LearningSessionEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var queueItems = GetBoundedTable<LearningSessionCardEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);

        long otherRecords = (long)wordForms.Count + sentences.Count + meanings.Count + reviewStates.Count
            + reviewSessions.Count + reviewCandidates.Count + prepSessions.Count + prepCandidates.Count
            + contextSnapshots.Count + cards.Count + reviews.Count + learningSessions.Count + queueItems.Count;

        if (otherRecords > BackupFormatLimits.MaxOtherCountedRecords)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }

        return new BackupSnapshot(
            docs, words, wordForms, sentences, occurrences, meanings,
            reviewStates, reviewSessions, reviewCandidates, prepSessions, prepCandidates,
            contextSnapshots, cards, reviews, learningSessions, queueItems);
    }

    private static List<T> GetBoundedTable<T>(SQLiteConnection connection, int limit) where T : new()
    {
        var count = connection.Table<T>().Count();
        if (count > limit)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }
        return connection.Table<T>().ToList();
    }
}
