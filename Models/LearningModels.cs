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
    long? SourceRevisionId = null);

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

public sealed record SpellingSubmissionResult(
    bool IsCorrect,
    string EnteredAnswer,
    string CorrectAnswer,
    string Difference,
    string? MatchedAlias,
    bool RatingWasPersisted);
