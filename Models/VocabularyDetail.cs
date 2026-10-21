using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Text;
using KnownFirst.Data.Entities;
using KnownFirst.Data.Migrations.Schema8;

namespace KnownFirst.Models;

/// <summary>
/// Distinguishes valid found detail projections from invalid or nonexistent word identifiers.
/// Actual database failures propagate as exceptions rather than returning a non-exception status.
/// </summary>
public enum VocabularyDetailLookupStatus
{
    Found = 0,
    NotFound = 1,
    InvalidWordId = 2
}

/// <summary>
/// Result envelope for a single-word Vocabulary Detail lookup.
/// </summary>
public sealed record VocabularyDetailLookupResult
{
    public VocabularyDetailLookupResult(
        VocabularyDetailLookupStatus status,
        int requestedWordId,
        int maxItemsPerCollection,
        VocabularyDetail? detail)
    {
        Status = status;
        RequestedWordId = requestedWordId;
        MaxItemsPerCollection = maxItemsPerCollection;
        Detail = detail;
    }

    public VocabularyDetailLookupStatus Status { get; }

    public int RequestedWordId { get; }

    public int MaxItemsPerCollection { get; }

    public VocabularyDetail? Detail { get; }

    public bool IsFound => Status == VocabularyDetailLookupStatus.Found && Detail is not null;

    public bool IsNotFound => Status == VocabularyDetailLookupStatus.NotFound;

    public bool IsInvalidWordId => Status == VocabularyDetailLookupStatus.InvalidWordId;

    public static VocabularyDetailLookupResult Found(VocabularyDetail detail, int maxItemsPerCollection)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new VocabularyDetailLookupResult(
            VocabularyDetailLookupStatus.Found,
            detail.WordId,
            maxItemsPerCollection,
            detail);
    }

    public static VocabularyDetailLookupResult NotFound(int requestedWordId, int maxItemsPerCollection) =>
        new(
            VocabularyDetailLookupStatus.NotFound,
            requestedWordId,
            maxItemsPerCollection,
            detail: null);

    public static VocabularyDetailLookupResult InvalidWordId(int requestedWordId, int maxItemsPerCollection) =>
        new(
            VocabularyDetailLookupStatus.InvalidWordId,
            requestedWordId,
            maxItemsPerCollection,
            detail: null);
}

/// <summary>
/// Read-only Schema-14 projection of a persisted Word and its bounded Sense, Meaning,
/// ContextSnapshot, LearningTarget, AnswerVariant, FSRS state, review history, and explicit control children.
/// Preserves the strict authority chain: Word -> Sense -> LearningTarget -> Target FSRS state.
/// </summary>
public sealed record VocabularyDetail
{
    public VocabularyDetail(
        int wordId,
        string canonicalTerm,
        string normalizedTerm,
        string sourceLanguage,
        TokenKind tokenKind,
        int totalOccurrenceCount,
        int documentCount,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        VocabularyWordLearningControlDetail? wordLearningControl,
        IReadOnlyList<VocabularySenseDetail> senses,
        bool hasMoreSenses,
        int maxItemsPerCollection)
    {
        ArgumentNullException.ThrowIfNull(canonicalTerm);
        ArgumentNullException.ThrowIfNull(normalizedTerm);
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        ArgumentNullException.ThrowIfNull(senses);

        WordId = wordId;
        CanonicalTerm = canonicalTerm;
        NormalizedTerm = normalizedTerm;
        SourceLanguage = sourceLanguage;
        TokenKind = tokenKind;
        TotalOccurrenceCount = totalOccurrenceCount;
        DocumentCount = documentCount;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        WordLearningControl = wordLearningControl;
        Senses = senses;
        HasMoreSenses = hasMoreSenses;
        MaxItemsPerCollection = maxItemsPerCollection;
        HasAnyTruncatedCollection = hasMoreSenses || senses.Any(static s => s.HasAnyTruncatedCollection);
    }

    public int WordId { get; }

    public int Id => WordId;

    public string CanonicalTerm { get; }

    public string NormalizedTerm { get; }

    public string SourceLanguage { get; }

    public string Language => SourceLanguage;

    public TokenKind TokenKind { get; }

    public int TotalOccurrenceCount { get; }

    public int DocumentCount { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>
    /// Explicit word-level learning control (<c>WordLearningControls</c>), if present.
    /// Separate from FSRS scheduling state.
    /// </summary>
    public VocabularyWordLearningControlDetail? WordLearningControl { get; }

    public bool IsMarkedAlreadyKnown => WordLearningControl is not null;

    public IReadOnlyList<VocabularySenseDetail> Senses { get; }

    public bool HasMoreSenses { get; }

    public int MaxItemsPerCollection { get; }

    public bool HasAnyTruncatedCollection { get; }

    public bool IsTruncated => HasAnyTruncatedCollection;
}

/// <summary>
/// Explicit word-level learning control row from <c>WordLearningControls</c>.
/// </summary>
public sealed record VocabularyWordLearningControlDetail(
    int WordId,
    DateTimeOffset DecidedAtUtc);

/// <summary>
/// Explicit sense-level learning control row from <c>SenseLearningControls</c>.
/// </summary>
public sealed record VocabularySenseLearningControlDetail(
    int SenseId,
    DateTimeOffset DecidedAtUtc);

/// <summary>
/// Read-only projection of a persisted Sense belonging to a Word, along with its bounded
/// Meanings, ContextSnapshots, LearningTargets, and explicit sense-level learning control.
/// </summary>
public sealed record VocabularySenseDetail
{
    public VocabularySenseDetail(
        int senseId,
        int wordId,
        string stableId,
        string sourceLanguage,
        string explanationLanguage,
        string providerSenseId,
        string topicOrDomain,
        string partOfSpeech,
        string grammaticalRelationship,
        string acronymExpansion,
        int? defaultMeaningId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        VocabularySenseLearningControlDetail? senseLearningControl,
        IReadOnlyList<VocabularyMeaningDetail> meanings,
        bool hasMoreMeanings,
        IReadOnlyList<VocabularyContextSnapshotDetail> contextSnapshots,
        bool hasMoreContextSnapshots,
        IReadOnlyList<VocabularyLearningTargetDetail> learningTargets,
        bool hasMoreLearningTargets)
    {
        ArgumentNullException.ThrowIfNull(stableId);
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        ArgumentNullException.ThrowIfNull(explanationLanguage);
        ArgumentNullException.ThrowIfNull(providerSenseId);
        ArgumentNullException.ThrowIfNull(topicOrDomain);
        ArgumentNullException.ThrowIfNull(partOfSpeech);
        ArgumentNullException.ThrowIfNull(grammaticalRelationship);
        ArgumentNullException.ThrowIfNull(acronymExpansion);
        ArgumentNullException.ThrowIfNull(meanings);
        ArgumentNullException.ThrowIfNull(contextSnapshots);
        ArgumentNullException.ThrowIfNull(learningTargets);

        SenseId = senseId;
        WordId = wordId;
        StableId = stableId;
        SourceLanguage = sourceLanguage;
        ExplanationLanguage = explanationLanguage;
        ProviderSenseId = providerSenseId;
        TopicOrDomain = topicOrDomain;
        PartOfSpeech = partOfSpeech;
        GrammaticalRelationship = grammaticalRelationship;
        AcronymExpansion = acronymExpansion;
        DefaultMeaningId = defaultMeaningId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        SenseLearningControl = senseLearningControl;
        Meanings = meanings;
        HasMoreMeanings = hasMoreMeanings;
        ContextSnapshots = contextSnapshots;
        HasMoreContextSnapshots = hasMoreContextSnapshots;
        LearningTargets = learningTargets;
        HasMoreLearningTargets = hasMoreLearningTargets;
        HasAnyTruncatedCollection =
            hasMoreMeanings
            || hasMoreContextSnapshots
            || hasMoreLearningTargets
            || learningTargets.Any(static t => t.HasAnyTruncatedCollection);
    }

    public int SenseId { get; }

    public int Id => SenseId;

    public int WordId { get; }

    public string StableId { get; }

    public string SourceLanguage { get; }

    public string ExplanationLanguage { get; }

    public string ProviderSenseId { get; }

    public string TopicOrDomain { get; }

    public string PartOfSpeech { get; }

    public string GrammaticalRelationship { get; }

    public string AcronymExpansion { get; }

    public int? DefaultMeaningId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>
    /// Explicit sense-level learning control (<c>SenseLearningControls</c>), if present.
    /// Separate from FSRS scheduling state.
    /// </summary>
    public VocabularySenseLearningControlDetail? SenseLearningControl { get; }

    public bool IsStopped => SenseLearningControl is not null;

    public IReadOnlyList<VocabularyMeaningDetail> Meanings { get; }

    public bool HasMoreMeanings { get; }

    public IReadOnlyList<VocabularyContextSnapshotDetail> ContextSnapshots { get; }

    public bool HasMoreContextSnapshots { get; }

    public IReadOnlyList<VocabularyLearningTargetDetail> LearningTargets { get; }

    public bool HasMoreLearningTargets { get; }

    public bool HasAnyTruncatedCollection { get; }

    public bool IsTruncated => HasAnyTruncatedCollection;
}

/// <summary>
/// Read-only projection of a persisted Meaning belonging to a Sense and Word.
/// Preserves Definition and Translation distinctly without conflation.
/// </summary>
public sealed record VocabularyMeaningDetail
{
    public VocabularyMeaningDetail(
        int meaningId,
        int senseId,
        int wordId,
        string? stableId,
        string sourceLanguage,
        string explanationLanguage,
        string displayTerm,
        string encounteredSurfaceForm,
        string grammaticalRelationship,
        TokenKind tokenKind,
        string selectedMeaningId,
        string acronymExpansion,
        string definition,
        string translation,
        string dictionaryExample,
        string additionalNote,
        IReadOnlyList<string> acceptedAliases,
        string source,
        string sourceProject,
        string sourcePageTitle,
        long? sourceRevisionId,
        string attribution,
        bool confirmedByUser,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset preparedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        ArgumentNullException.ThrowIfNull(explanationLanguage);
        ArgumentNullException.ThrowIfNull(displayTerm);
        ArgumentNullException.ThrowIfNull(encounteredSurfaceForm);
        ArgumentNullException.ThrowIfNull(grammaticalRelationship);
        ArgumentNullException.ThrowIfNull(selectedMeaningId);
        ArgumentNullException.ThrowIfNull(acronymExpansion);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(dictionaryExample);
        ArgumentNullException.ThrowIfNull(additionalNote);
        ArgumentNullException.ThrowIfNull(acceptedAliases);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceProject);
        ArgumentNullException.ThrowIfNull(sourcePageTitle);
        ArgumentNullException.ThrowIfNull(attribution);

        MeaningId = meaningId;
        SenseId = senseId;
        WordId = wordId;
        StableId = stableId;
        SourceLanguage = sourceLanguage;
        ExplanationLanguage = explanationLanguage;
        DisplayTerm = displayTerm;
        EncounteredSurfaceForm = encounteredSurfaceForm;
        GrammaticalRelationship = grammaticalRelationship;
        TokenKind = tokenKind;
        SelectedMeaningId = selectedMeaningId;
        AcronymExpansion = acronymExpansion;
        Definition = definition;
        Translation = translation;
        DictionaryExample = dictionaryExample;
        AdditionalNote = additionalNote;
        AcceptedAliases = acceptedAliases;
        Source = source;
        SourceProject = sourceProject;
        SourcePageTitle = sourcePageTitle;
        SourceRevisionId = sourceRevisionId;
        Attribution = attribution;
        ConfirmedByUser = confirmedByUser;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        PreparedAtUtc = preparedAtUtc;
    }

    public int MeaningId { get; }

    public int Id => MeaningId;

    public int SenseId { get; }

    public int WordId { get; }

    public string? StableId { get; }

    public string SourceLanguage { get; }

    public string ExplanationLanguage { get; }

    public string DisplayTerm { get; }

    public string EncounteredSurfaceForm { get; }

    public string GrammaticalRelationship { get; }

    public TokenKind TokenKind { get; }

    public string SelectedMeaningId { get; }

    public string AcronymExpansion { get; }

    public string Definition { get; }

    public string Translation { get; }

    public string DictionaryExample { get; }

    public string AdditionalNote { get; }

    public IReadOnlyList<string> AcceptedAliases { get; }

    public string Source { get; }

    public string SourceProject { get; }

    public string SourcePageTitle { get; }

    public long? SourceRevisionId { get; }

    public string Attribution { get; }

    public bool ConfirmedByUser { get; }

    public bool HasProviderAttribution =>
        !string.IsNullOrWhiteSpace(Source)
        || !string.IsNullOrWhiteSpace(SourceProject)
        || !string.IsNullOrWhiteSpace(Attribution);

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public DateTimeOffset PreparedAtUtc { get; }
}

/// <summary>
/// Read-only projection of a persisted ContextSnapshot associated with a Sense, Meaning, and Word.
/// </summary>
public sealed record VocabularyContextSnapshotDetail
{
    public VocabularyContextSnapshotDetail(
        int contextSnapshotId,
        int meaningId,
        int senseId,
        int wordId,
        int sourceDocumentId,
        string sourceDocumentTitle,
        string text,
        int targetStart,
        int targetLength,
        string normalizedFingerprint,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sourceDocumentTitle);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(normalizedFingerprint);

        ContextSnapshotId = contextSnapshotId;
        MeaningId = meaningId;
        SenseId = senseId;
        WordId = wordId;
        SourceDocumentId = sourceDocumentId;
        SourceDocumentTitle = sourceDocumentTitle;
        Text = text;
        TargetStart = targetStart;
        TargetLength = targetLength;
        NormalizedFingerprint = normalizedFingerprint;
        CreatedAtUtc = createdAtUtc;
    }

    public int ContextSnapshotId { get; }

    public int Id => ContextSnapshotId;

    public int MeaningId { get; }

    public int SenseId { get; }

    public int WordId { get; }

    public int SourceDocumentId { get; }

    public string SourceDocumentTitle { get; }

    public string Text { get; }

    public int TargetStart { get; }

    public int TargetLength { get; }

    public string NormalizedFingerprint { get; }

    public DateTimeOffset CreatedAtUtc { get; }
}

/// <summary>
/// Read-only projection of a persisted LearningTarget belonging to a Sense, including its
/// Target-owned FSRS state, bounded AnswerVariants, bounded factual review history, and active-learning eligibility.
/// </summary>
public sealed record VocabularyLearningTargetDetail
{
    public VocabularyLearningTargetDetail(
        int targetId,
        int senseId,
        string stableId,
        LearningTargetKind targetKind,
        string sourceLanguage,
        string targetLanguage,
        bool typingOptOut,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        VocabularyTargetFsrsStateDetail? fsrsState,
        IReadOnlyList<VocabularyAnswerVariantDetail> answerVariants,
        bool hasMoreAnswerVariants,
        IReadOnlyList<VocabularyTargetReviewHistoryEntryDetail> reviewHistory,
        bool hasMoreReviewHistory,
        bool isEligibleForActiveLearning)
    {
        ArgumentNullException.ThrowIfNull(stableId);
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        ArgumentNullException.ThrowIfNull(targetLanguage);
        ArgumentNullException.ThrowIfNull(answerVariants);
        ArgumentNullException.ThrowIfNull(reviewHistory);

        TargetId = targetId;
        SenseId = senseId;
        StableId = stableId;
        TargetKind = targetKind;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        TypingOptOut = typingOptOut;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        FsrsState = fsrsState;
        AnswerVariants = answerVariants;
        HasMoreAnswerVariants = hasMoreAnswerVariants;
        ReviewHistory = reviewHistory;
        HasMoreReviewHistory = hasMoreReviewHistory;
        IsEligibleForActiveLearning = isEligibleForActiveLearning;
    }

    public int TargetId { get; }

    public int Id => TargetId;

    public int SenseId { get; }

    public string StableId { get; }

    public LearningTargetKind TargetKind { get; }

    public string SourceLanguage { get; }

    public string TargetLanguage { get; }

    public bool TypingOptOut { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>
    /// Persisted <c>TargetFsrsStates</c> row for this target, or <see langword="null"/> if no state row exists.
    /// Never fabricated from word status, legacy cards, or another target.
    /// </summary>
    public VocabularyTargetFsrsStateDetail? FsrsState { get; }

    public bool HasFsrsState => FsrsState is not null;

    public IReadOnlyList<VocabularyAnswerVariantDetail> AnswerVariants { get; }

    public bool HasMoreAnswerVariants { get; }

    public IReadOnlyList<VocabularyTargetReviewHistoryEntryDetail> ReviewHistory { get; }

    public bool HasMoreReviewHistory { get; }

    /// <summary>
    /// Indicates whether the target is eligible for active learning after applying explicit
    /// <c>WordLearningControls</c> and <c>SenseLearningControls</c> suppression rules.
    /// </summary>
    public bool IsEligibleForActiveLearning { get; }

    public bool HasAnyTruncatedCollection => HasMoreAnswerVariants || HasMoreReviewHistory;

    public bool IsTruncated => HasAnyTruncatedCollection;
}

/// <summary>
/// Read-only projection of a persisted TargetAnswerVariant belonging to a LearningTarget.
/// </summary>
public sealed record VocabularyAnswerVariantDetail
{
    public VocabularyAnswerVariantDetail(
        int variantId,
        int targetId,
        string stableId,
        string answerLanguage,
        string displayText,
        string normalizedText,
        AnswerVariantRequirement requirement,
        bool isPreferred,
        DateTimeOffset? requiredSinceUtc,
        int? sourceMeaningId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(stableId);
        ArgumentNullException.ThrowIfNull(answerLanguage);
        ArgumentNullException.ThrowIfNull(displayText);
        ArgumentNullException.ThrowIfNull(normalizedText);

        VariantId = variantId;
        TargetId = targetId;
        StableId = stableId;
        AnswerLanguage = answerLanguage;
        DisplayText = displayText;
        NormalizedText = normalizedText;
        Requirement = requirement;
        IsPreferred = isPreferred;
        RequiredSinceUtc = requiredSinceUtc;
        SourceMeaningId = sourceMeaningId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public int VariantId { get; }

    public int Id => VariantId;

    public int TargetId { get; }

    public string StableId { get; }

    public string AnswerLanguage { get; }

    public string DisplayText { get; }

    public string NormalizedText { get; }

    public AnswerVariantRequirement Requirement { get; }

    public bool IsRequired => Requirement == AnswerVariantRequirement.Required;

    public bool IsPreferred { get; }

    public DateTimeOffset? RequiredSinceUtc { get; }

    public int? SourceMeaningId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }
}

/// <summary>
/// Read-only projection of a persisted <c>TargetFsrsStates</c> row for a LearningTarget.
/// </summary>
public sealed record VocabularyTargetFsrsStateDetail(
    int TargetId,
    Fsrs6CardState State,
    double? Stability,
    double? Difficulty,
    DateTimeOffset? LastReviewedAtUtc,
    int? StepIndex,
    DateTimeOffset? DueAtUtc);

/// <summary>
/// Read-only projection of a persisted <c>TargetFsrsReviewHistoryEntries</c> row for a LearningTarget.
/// </summary>
public sealed record VocabularyTargetReviewHistoryEntryDetail
{
    public VocabularyTargetReviewHistoryEntryDetail(
        int entryId,
        int targetId,
        string stableId,
        int sequenceNumber,
        ReviewRating rating,
        DateTimeOffset reviewedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(stableId);

        EntryId = entryId;
        TargetId = targetId;
        StableId = stableId;
        SequenceNumber = sequenceNumber;
        Rating = rating;
        ReviewedAtUtc = reviewedAtUtc;
    }

    public int EntryId { get; }

    public int Id => EntryId;

    public int TargetId { get; }

    public string StableId { get; }

    public int SequenceNumber { get; }

    public ReviewRating Rating { get; }

    public DateTimeOffset ReviewedAtUtc { get; }
}
