using KnownFirst.Core.Learning;
using KnownFirst.Core.Text;

namespace KnownFirst.Models;

public sealed record LearningContext(
    string DocumentTitle,
    string BeforeTarget,
    string Target,
    string AfterTarget);

public sealed record LearningCardView(
    int SessionId,
    int QueueItemId,
    int CardId,
    int WordId,
    CardDirection Direction,
    LearningInteractionMode InteractionMode,
    CardState State,
    string Term,
    TokenKind TokenKind,
    string SourceLanguage,
    string ExplanationLanguage,
    string? AcronymExpansion,
    string? Translation,
    string Definition,
    string? DictionaryExample,
    string ProviderName,
    string SourceProject,
    string SourcePageTitle,
    string Attribution,
    IReadOnlyList<string> AcceptedAliases,
    IReadOnlyList<LearningContext> Contexts,
    int AcceptedOccurrenceCount,
    bool AnswerRevealed,
    int CompletedCards,
    int TotalCards,
    string? EncounteredSurfaceForm = null,
    string? GrammaticalRelationship = null,
    long? SourceRevisionId = null,
    bool IsAgainRepeat = false);

public sealed record LearningSessionSummary(
    int SessionId,
    int CardsReviewed,
    int AgainCount,
    int HardCount,
    int GoodCount,
    int EasyCount,
    DateTime? NextDueAtUtc,
    int RemainingUnpreparedCount);

public sealed record LearningLoadResult(
    LearningCardView? Card,
    LearningSessionSummary? CompletedSummary);

/// <summary>
/// The outcome of a typed-answer check. <paramref name="MatchedAnswerVariantId"/> (KF-MEANING-001 Slice 4) is
/// the Schema-8 answer-variant identity handoff a subsequent rating consumes; it is always
/// <see langword="null"/> on the unchanged Schema-7 path. Declared last and defaulted so every existing
/// caller and test stays source-compatible.
/// </summary>
public sealed record SpellingSubmissionResult(
    bool IsCorrect,
    string EnteredAnswer,
    string CorrectAnswer,
    string Difference,
    string? MatchedAlias,
    bool RatingWasPersisted,
    int? MatchedAnswerVariantId = null);
