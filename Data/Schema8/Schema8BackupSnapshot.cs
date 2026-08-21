using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;

namespace KnownFirst.Data.Schema8;

/// <summary>
/// Full-fidelity raw capture of a Schema-8 database (KF-MEANING-001 Slice 2), analogous to
/// <see cref="BackupSnapshot"/> for Schema 7. Tables unaffected by the Schema-8 migration are captured
/// via the existing <c>Data/Entities</c> classes exactly as <see cref="BackupSnapshotRepository"/> already
/// does; tables the migration changes (Meanings, ContextSnapshots, LearningCards, LearningReviews,
/// LearningSessionCards) and the four new tables use the isolated row models in this namespace /
/// <c>Data/Migrations/Schema8</c> — no existing entity class is read or written through here.
///
/// <para><c>LearningSessionStableIds</c> / <c>LearningQueueStableIds</c> are the KF-BACKUP-005A
/// Schema-10 learning-workflow identities, keyed by physical row id. They are deliberately carried
/// beside the row lists rather than on <c>LearningSessionEntity</c> / <c>Schema8QueueRow</c>: those two
/// types are also the Schema-8 and Schema-9 capture shape, and a Schema-8/9 database has no such column
/// to read. Both maps are <see langword="null"/> for a Schema-8/9 capture and populated for a Schema-10
/// capture — which is exactly the distinction the archive writer needs in order to decide whether it may
/// emit identities at all.</para>
///
/// <para><c>DerivedTermEvidenceOwningReviewCandidateIds</c> is the German Enhanced Term Recognition
/// Package 5A counterpart: the set of captured <c>ReviewCandidates</c> row ids that own at least one
/// Schema-11 <c>DerivedTermEvidenceEntries</c> row, keyed by physical row id exactly like the Schema-10
/// maps above and for the same reason — <c>DerivedTermEvidenceEntries</c> does not exist below Schema 11.
/// <see langword="null"/> below Schema 11.</para>
///
/// <para><c>DerivedTermEvidence</c> is the German Enhanced Term Recognition Package 5A-2 counterpart: the
/// actual captured <c>DerivedTermEvidenceEntries</c> rows, restricted to owning candidates already present
/// in this snapshot's captured <c>ReviewCandidates</c> — a row already excluded by a portable-export filter
/// (e.g. an Active-session candidate) is never spuriously reintroduced here. Package 5A-2 transports this
/// content through <see cref="Services.DataSafety.BackupModelMapperV2"/> instead of excluding the owning
/// candidate; the exclusion <see cref="DerivedTermEvidenceOwningReviewCandidateIds"/> once existed for is
/// superseded, but the field itself is retained because it remains a correct, harmless description of
/// which candidates own evidence. <see langword="null"/> below Schema 11.</para>
/// </summary>
public sealed record Schema8BackupSnapshot(
    IReadOnlyList<DocumentEntity> Documents,
    IReadOnlyList<WordEntity> Words,
    IReadOnlyList<WordFormEntity> WordForms,
    IReadOnlyList<SentenceSpanEntity> SentenceSpans,
    IReadOnlyList<WordOccurrenceEntity> WordOccurrences,
    IReadOnlyList<Schema8MeaningRow> Meanings,
    IReadOnlyList<ReviewStateEntity> ReviewStates,
    IReadOnlyList<ReviewSessionEntity> ReviewSessions,
    IReadOnlyList<ReviewCandidateEntity> ReviewCandidates,
    IReadOnlyList<PreparationSessionEntity> PreparationSessions,
    IReadOnlyList<PreparationCandidateEntity> PreparationCandidates,
    IReadOnlyList<Schema8ContextRow> ContextSnapshots,
    IReadOnlyList<SenseRow> Senses,
    IReadOnlyList<AnswerVariantRow> AnswerVariants,
    IReadOnlyList<SenseAnswerVariantAssignmentRow> Assignments,
    IReadOnlyList<AnswerVariantProgressRow> AnswerVariantProgress,
    IReadOnlyList<Schema8CardRow> LearningCards,
    IReadOnlyList<Schema8ReviewRow> LearningReviews,
    IReadOnlyList<LearningSessionEntity> LearningSessions,
    IReadOnlyList<Schema8QueueRow> LearningSessionCards,
    IReadOnlyDictionary<int, string>? LearningSessionStableIds = null,
    IReadOnlyDictionary<int, string>? LearningQueueStableIds = null,
    IReadOnlySet<int>? DerivedTermEvidenceOwningReviewCandidateIds = null,
    IReadOnlyList<DerivedTermEvidenceEntity>? DerivedTermEvidence = null);
