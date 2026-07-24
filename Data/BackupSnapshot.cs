using KnownFirst.Data.Entities;

namespace KnownFirst.Data;

public sealed record BackupSnapshot(
    IReadOnlyList<DocumentEntity> Documents,
    IReadOnlyList<WordEntity> Words,
    IReadOnlyList<WordFormEntity> WordForms,
    IReadOnlyList<SentenceSpanEntity> SentenceSpans,
    IReadOnlyList<WordOccurrenceEntity> WordOccurrences,
    IReadOnlyList<MeaningEntity> Meanings,
    IReadOnlyList<ReviewStateEntity> ReviewStates,
    IReadOnlyList<ReviewSessionEntity> ReviewSessions,
    IReadOnlyList<ReviewCandidateEntity> ReviewCandidates,
    IReadOnlyList<PreparationSessionEntity> PreparationSessions,
    IReadOnlyList<PreparationCandidateEntity> PreparationCandidates,
    IReadOnlyList<ContextSnapshotEntity> ContextSnapshots,
    IReadOnlyList<LearningCardEntity> LearningCards,
    IReadOnlyList<LearningReviewEntity> LearningReviews,
    IReadOnlyList<LearningSessionEntity> LearningSessions,
    IReadOnlyList<LearningSessionCardEntity> LearningSessionCards);
