using KnownFirst.Models.Backup;

namespace KnownFirst.Tests;

/// <summary>
/// Minimal, explicit builders for <see cref="BackupPayload"/> fixtures used by the Slice 3 merge
/// preflight tests. Every builder fills in a complete, valid record with sensible defaults so each test
/// only has to specify the fields it actually cares about.
/// </summary>
internal static class MergePreflightFixtures
{
    public static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static BackupManifest Manifest() => new(
        1, "1.0.0-test", 7, BaseTime, BackupSourcePlatform.Windows,
        new BackupRecordCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        "sha256:" + new string('0', 64),
        [],
        []);

    public static BackupPayload EmptyPayload() => new(
        [], [], [], new BackupLearningData([], []), new BackupWorkflowData([], [], []),
        new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));

    public static BackupPayload Payload(
        IReadOnlyList<BackupSourceMaterial>? sourceMaterials = null,
        IReadOnlyList<BackupVocabularyItem>? vocabulary = null,
        IReadOnlyList<BackupPreparedItem>? preparedLearning = null,
        IReadOnlyList<BackupLearningCard>? cards = null,
        IReadOnlyList<BackupLearningReview>? reviews = null,
        IReadOnlyList<BackupVocabularyReviewWorkflow>? reviewWorkflows = null,
        IReadOnlyList<BackupPreparationWorkflow>? preparationWorkflows = null,
        IReadOnlyList<BackupLearningWorkflow>? learningWorkflows = null) => new(
        sourceMaterials ?? [],
        vocabulary ?? [],
        preparedLearning ?? [],
        new BackupLearningData(cards ?? [], reviews ?? []),
        new BackupWorkflowData(reviewWorkflows ?? [], preparationWorkflows ?? [], learningWorkflows ?? []),
        new BackupExtensions(new Dictionary<string, BackupExtensionPayload>(StringComparer.Ordinal)));

    public static BackupVocabularyItem Vocabulary(
        string id,
        string language = "en",
        string term = "test",
        string? identityKey = null,
        BackupKnowledgeState knowledgeState = BackupKnowledgeState.Unreviewed,
        BackupPreparationState preparationState = BackupPreparationState.Unprepared,
        IReadOnlyList<BackupEncounteredForm>? encounteredForms = null,
        IReadOnlyList<BackupLegacyReviewSummary>? legacyReviewSummaries = null) => new(
        id, language, term, identityKey ?? term.ToLowerInvariant(), BackupTokenKind.Word,
        knowledgeState, preparationState, 1, 1, BaseTime, BaseTime,
        encounteredForms ?? [],
        new BackupAutomaticLearningState(BackupLearningInteractionMode.Reading, 0, 0, 0, false),
        legacyReviewSummaries ?? []);

    public static BackupSourceMaterial SourceMaterial(
        string id,
        string contentSha256,
        string title = "Untitled",
        string textLanguage = "en",
        string explanationLanguage = "de",
        BackupLexicalLookupMode lookupMode = BackupLexicalLookupMode.Translation,
        string? targetLanguage = "de",
        string text = "text",
        IReadOnlyList<BackupSentenceRange>? sentences = null,
        IReadOnlyList<BackupOccurrence>? occurrences = null) => new(
        id, title, textLanguage, explanationLanguage, lookupMode, targetLanguage, text, contentSha256, BaseTime, 1,
        sentences ?? [], occurrences ?? []);

    public static BackupSourceReference SourceReference(string providerName = "Wiktionary") =>
        new(providerName, "English Wiktionary", "test-page", 1, "CC BY-SA");

    public static BackupPreparedItem PreparedItem(
        string id,
        string vocabularyId,
        string? definition = "a definition",
        string? translation = null,
        string sourceLanguage = "en",
        string explanationLanguage = "de",
        BackupSourceReference? source = null,
        IReadOnlyList<BackupContextSnapshot>? contexts = null) => new(
        id, vocabularyId, sourceLanguage, explanationLanguage, "term", null, null, BackupTokenKind.Word,
        null, null, translation, definition, null, null, null, [], true,
        source ?? SourceReference(), BaseTime, BaseTime, BaseTime, contexts ?? []);

    public static BackupContextSnapshot ContextSnapshot(
        string sourceMaterialId, string fingerprint, string text = "context text") =>
        new(sourceMaterialId, "Title", text, 0, text.Length, fingerprint, BaseTime);

    public static BackupLearningCard Card(
        string id,
        string vocabularyId,
        string preparedItemId,
        BackupCardDirection direction = BackupCardDirection.TermToMeaning,
        BackupCardState state = BackupCardState.Review,
        DateTime? dueAtUtc = null,
        int intervalDays = 3,
        double easeFactor = 2.5,
        int successfulReviewCount = 1,
        int lapseCount = 0,
        DateTime? lastReviewedAtUtc = null,
        BackupReviewRating? lastRating = BackupReviewRating.Good) => new(
        id, vocabularyId, preparedItemId, direction, state, dueAtUtc ?? BaseTime.AddDays(3), intervalDays, easeFactor,
        successfulReviewCount, lapseCount, lastReviewedAtUtc ?? BaseTime, lastRating, BaseTime, BaseTime);

    public static BackupLearningReview Review(
        string cardId,
        string sessionId = "ls-1",
        BackupReviewRating rating = BackupReviewRating.Good,
        bool wasTypedAnswer = true,
        bool wasCorrect = true,
        DateTime? reviewedAtUtc = null,
        DateTime? dueAtUtc = null,
        int intervalDays = 3,
        double easeFactor = 2.5) => new(
        cardId, sessionId, rating, wasTypedAnswer, wasCorrect, reviewedAtUtc ?? BaseTime, dueAtUtc ?? BaseTime.AddDays(3), intervalDays, easeFactor);

    public static BackupVocabularyReviewWorkflow ReviewWorkflow(
        string id, string sourceMaterialId, IReadOnlyList<BackupVocabularyReviewItem>? items = null) => new(
        id, sourceMaterialId, BackupReviewSessionStatus.Completed, 1, 1, 1, 0, 0, 1, BaseTime, BaseTime.AddMinutes(5), items ?? []);

    public static BackupVocabularyReviewItem ReviewItem(string id, string vocabularyId, int order = 0) => new(
        id, vocabularyId, order, BackupKnowledgeState.Known, BackupKnowledgeState.Unreviewed, 0, 0, BaseTime, 1, true, BaseTime.AddMinutes(1));

    public static BackupPreparationWorkflow PreparationWorkflow(
        string id,
        BackupPreparationSessionStatus status = BackupPreparationSessionStatus.Completed,
        IReadOnlyList<BackupPreparationItem>? items = null) => new(
        id, status, BackupPreparationMethod.Manual, 1, 1, BaseTime, BaseTime.AddMinutes(5), BaseTime.AddMinutes(5), items ?? []);

    public static BackupPreparationItem PreparationItem(string id, string vocabularyId, int order = 0) => new(
        id, vocabularyId, order, BackupPreparationCandidateStatus.Prepared, 0, null, 1, BaseTime.AddMinutes(1), null);

    public static BackupLearningWorkflow LearningWorkflow(
        string id, IReadOnlyList<BackupLearningQueueItem>? queueItems = null) => new(
        id, BackupLearningSessionStatus.Completed, 1, 1, 0, 0, 1, 0, BaseTime, BaseTime.AddMinutes(5), BaseTime.AddMinutes(5), queueItems ?? []);

    public static BackupLearningQueueItem QueueItem(string id, string cardId, int queueOrder = 0, BackupReviewRating? rating = BackupReviewRating.Good) => new(
        id, cardId, queueOrder, true, false, true, false, false, true, rating, BaseTime.AddMinutes(1));
}
