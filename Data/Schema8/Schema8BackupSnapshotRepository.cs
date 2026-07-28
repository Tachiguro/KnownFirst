using KnownFirst.Data.Entities;
using KnownFirst.Models;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data.Schema8;

/// <summary>Schema-8 counterpart of <see cref="BackupSnapshotRepository"/> (KF-MEANING-001 Slice 2). Every
/// method here requires the caller to have already resolved a <see cref="ValidatedSchema8Capability"/> —
/// callers reach these methods only after <c>BackupSchemaCapability.Resolve</c> confirmed the connection is
/// a shape-valid Schema-8 database.</summary>
public static class Schema8BackupSnapshotRepository
{
    /// <summary>Race-free counterpart of <see cref="BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy"/>:
    /// checks for any active workflow and captures the Schema-8 snapshot in the same connection/callback
    /// when none exists.</summary>
    public static Schema8PortableSnapshotCaptureResult CapturePortableSnapshotForMergeSafetyCopy(SQLiteConnection connection)
    {
        var hasActiveReview = connection.Table<ReviewSessionEntity>().ToList()
            .Select(session => IsBlockingReviewStatus(session.Status)).ToList().Any(isBlocking => isBlocking);
        var hasActivePreparation = connection.Table<PreparationSessionEntity>().ToList()
            .Select(session => IsBlockingPreparationStatus(session.Status)).ToList().Any(isBlocking => isBlocking);
        var hasActiveLearning = connection.Table<LearningSessionEntity>().ToList()
            .Select(session => IsBlockingLearningStatus(session.Status)).ToList().Any(isBlocking => isBlocking);

        if (hasActiveReview || hasActivePreparation || hasActiveLearning)
        {
            return new Schema8PortableSnapshotCaptureResult(PortableSnapshotCaptureStatus.BlockedByActiveWorkflow, null);
        }

        return new Schema8PortableSnapshotCaptureResult(PortableSnapshotCaptureStatus.Success, CapturePortableSnapshot(connection));
    }

    private static bool IsBlockingReviewStatus(ReviewSessionStatus status) => status switch
    {
        ReviewSessionStatus.Active => true,
        ReviewSessionStatus.Completed => false,
        _ => throw new InvalidOperationException($"Unrecognized ReviewSessionStatus value '{status}' encountered during merge safety copy classification.")
    };

    private static bool IsBlockingPreparationStatus(PreparationSessionStatus status) => status switch
    {
        PreparationSessionStatus.Active => true,
        PreparationSessionStatus.Completed => false,
        PreparationSessionStatus.Cancelled => false,
        _ => throw new InvalidOperationException($"Unrecognized PreparationSessionStatus value '{status}' encountered during merge safety copy classification.")
    };

    private static bool IsBlockingLearningStatus(LearningSessionStatus status) => status switch
    {
        LearningSessionStatus.Active => true,
        LearningSessionStatus.Completed => false,
        _ => throw new InvalidOperationException($"Unrecognized LearningSessionStatus value '{status}' encountered during merge safety copy classification.")
    };

    /// <summary>Ordinary user Schema-8 export: same active/incomplete-workflow filtering as
    /// <see cref="BackupSnapshotRepository.CapturePortableSnapshot"/>.</summary>
    public static Schema8BackupSnapshot CapturePortableSnapshot(SQLiteConnection connection)
    {
        var snapshot = CaptureSnapshot(connection);

        var reviewSessions = snapshot.ReviewSessions.Where(s => s.Status == ReviewSessionStatus.Completed).ToList();
        var reviewSessionIds = reviewSessions.Select(s => s.Id).ToHashSet();

        var preparationSessions = snapshot.PreparationSessions.Where(s => s.Status != PreparationSessionStatus.Active).ToList();
        var preparationSessionIds = preparationSessions.Select(s => s.Id).ToHashSet();

        var learningSessions = snapshot.LearningSessions.Where(s => s.Status == LearningSessionStatus.Completed).ToList();
        var learningSessionIds = learningSessions.Select(s => s.Id).ToHashSet();

        return snapshot with
        {
            ReviewSessions = reviewSessions,
            ReviewCandidates = snapshot.ReviewCandidates.Where(c => reviewSessionIds.Contains(c.SessionId)).ToList(),
            PreparationSessions = preparationSessions,
            PreparationCandidates = snapshot.PreparationCandidates.Where(c => preparationSessionIds.Contains(c.SessionId)).ToList(),
            LearningReviews = snapshot.LearningReviews.Where(r => learningSessionIds.Contains(r.SessionId)).ToList(),
            LearningSessions = learningSessions,
            LearningSessionCards = snapshot.LearningSessionCards.Where(q => learningSessionIds.Contains(q.SessionId)).ToList()
        };
    }

    /// <summary>Full, unfiltered Schema-8 capture (used by the internal full backup path). Bounds each
    /// table by the same <c>BackupFormatLimits</c> used for Schema 7.</summary>
    public static Schema8BackupSnapshot CaptureSnapshot(SQLiteConnection connection)
    {
        var docs = GetBoundedTable<DocumentEntity>(connection, BackupFormatLimits.MaxSourceMaterials);
        var words = GetBoundedTable<WordEntity>(connection, BackupFormatLimits.MaxVocabularyItems);
        var wordForms = GetBoundedTable<WordFormEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var sentences = GetBoundedTable<SentenceSpanEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var occurrences = GetBoundedTable<WordOccurrenceEntity>(connection, BackupFormatLimits.MaxOccurrences);
        var reviewStates = GetBoundedTable<ReviewStateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var reviewSessions = GetBoundedTable<ReviewSessionEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var reviewCandidates = GetBoundedTable<ReviewCandidateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var prepSessions = GetBoundedTable<PreparationSessionEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var prepCandidates = GetBoundedTable<PreparationCandidateEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);
        var learningSessions = GetBoundedTable<LearningSessionEntity>(connection, BackupFormatLimits.MaxOtherCountedRecords);

        var meanings = GetBoundedRawQuery<Schema8MeaningRow>(connection, "Meanings", BackupFormatLimits.MaxOtherCountedRecords);
        var contexts = GetBoundedRawQuery<Schema8ContextRow>(connection, "ContextSnapshots", BackupFormatLimits.MaxOtherCountedRecords);
        var senses = GetBoundedRawQuery<Migrations.Schema8.SenseRow>(connection, "Senses", BackupFormatLimits.MaxOtherCountedRecords);
        var answerVariants = GetBoundedRawQuery<Migrations.Schema8.AnswerVariantRow>(connection, "AnswerVariants", BackupFormatLimits.MaxOtherCountedRecords);
        var assignments = GetBoundedRawQuery<Migrations.Schema8.SenseAnswerVariantAssignmentRow>(connection, "SenseAnswerVariantAssignments", BackupFormatLimits.MaxOtherCountedRecords);
        var progress = GetBoundedRawQuery<Migrations.Schema8.AnswerVariantProgressRow>(connection, "AnswerVariantProgress", BackupFormatLimits.MaxOtherCountedRecords);
        var cards = GetBoundedRawQuery<Schema8CardRow>(connection, "LearningCards", BackupFormatLimits.MaxOtherCountedRecords);
        var reviews = GetBoundedRawQuery<Schema8ReviewRow>(connection, "LearningReviews", BackupFormatLimits.MaxOtherCountedRecords);
        var queueItems = GetBoundedRawQuery<Schema8QueueRow>(connection, "LearningSessionCards", BackupFormatLimits.MaxOtherCountedRecords);

        long otherRecords = (long)wordForms.Count + sentences.Count + meanings.Count + reviewStates.Count
            + reviewSessions.Count + reviewCandidates.Count + prepSessions.Count + prepCandidates.Count
            + contexts.Count + cards.Count + reviews.Count + learningSessions.Count + queueItems.Count
            + senses.Count + answerVariants.Count + assignments.Count + progress.Count;
        if (otherRecords > BackupFormatLimits.MaxOtherCountedRecords)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }

        return new Schema8BackupSnapshot(
            docs, words, wordForms, sentences, occurrences, meanings, reviewStates, reviewSessions,
            reviewCandidates, prepSessions, prepCandidates, contexts, senses, answerVariants, assignments,
            progress, cards, reviews, learningSessions, queueItems);
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

    private static List<T> GetBoundedRawQuery<T>(SQLiteConnection connection, string table, int limit) where T : new()
    {
        var count = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");
        if (count > limit)
        {
            throw new BackupFormatException(BackupErrorCodes.LimitExceeded);
        }
        return connection.Query<T>($"SELECT * FROM {table}");
    }
}

public sealed record Schema8PortableSnapshotCaptureResult(
    PortableSnapshotCaptureStatus Status,
    Schema8BackupSnapshot? Snapshot);
