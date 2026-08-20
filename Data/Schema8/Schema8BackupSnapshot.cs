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
/// It carries no evidence content itself (that table is never captured or exported here at all); it exists
/// solely so <see cref="Services.DataSafety.BackupModelMapperV2"/> can exclude a Completed session's
/// derived-evidence-only retained candidate from the exported <c>Items</c> list without disturbing any
/// other candidate a Completed session may legitimately carry (e.g. one written back by restore/merge).
/// <see langword="null"/> below Schema 11.</para>
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
    IReadOnlySet<int>? DerivedTermEvidenceOwningReviewCandidateIds = null);
