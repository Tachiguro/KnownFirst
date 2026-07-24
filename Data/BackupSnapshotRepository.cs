using KnownFirst.Data.Entities;
using KnownFirst.Services.DataSafety;
using SQLite;

namespace KnownFirst.Data;

public static class BackupSnapshotRepository
{
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
