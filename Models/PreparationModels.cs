using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;

namespace KnownFirst.Models;

public sealed record PreparationContext(
    int DocumentId,
    string DocumentTitle,
    string Text,
    int TargetStart,
    int TargetLength);

public sealed record PreparationItem(
    int SessionId,
    int CandidateId,
    int WordId,
    string Term,
    TokenKind TokenKind,
    string SourceLanguage,
    string ExplanationLanguage,
    int AcceptedOccurrenceCount,
    int Position,
    int TotalItems,
    PreparationMethod Method,
    PreparationCandidateStatus Status,
    IReadOnlyList<PreparationContext> Contexts,
    LexicalResult? Result,
    int SelectedMeaningIndex,
    string? LastErrorCode)
{
    public string LearningTerm => string.IsNullOrWhiteSpace(Result?.DisplayTerm)
        ? Term
        : Result.DisplayTerm;

    public string? EncounteredSurfaceForm => string.IsNullOrWhiteSpace(Result?.EncounteredSurfaceForm)
        ? GetContextSurfaceForm()
        : Result.EncounteredSurfaceForm;

    private string? GetContextSurfaceForm()
    {
        var context = Contexts.FirstOrDefault();
        if (context is null
            || context.TargetStart < 0
            || context.TargetLength <= 0
            || context.TargetStart + context.TargetLength > context.Text.Length)
        {
            return null;
        }

        var surfaceForm = context.Text.Substring(context.TargetStart, context.TargetLength);
        return string.Equals(surfaceForm, LearningTerm, StringComparison.Ordinal)
            ? null
            : surfaceForm;
    }
}

public sealed record PreparationOverview(
    int UnpreparedCount,
    int PreparedNewItemCount,
    int DueCardCount,
    int? ActiveSessionId,
    int ActiveCompletedItems,
    int ActiveTotalItems,
    PreparationMethod? ActiveMethod,
    int LastCompletedPreparedItems);

public sealed record PreparedMeaningInput(
    string? SelectedMeaningId,
    string? AcronymExpansion,
    string? Translation,
    string Definition,
    string? DictionaryExample,
    string? AdditionalNote,
    IReadOnlyList<string> AcceptedAliases,
    string ProviderName,
    string SourceProject,
    string SourcePageTitle,
    long? SourceRevisionId,
    string Attribution,
    string? EncounteredSurfaceForm = null,
    string? GrammaticalRelationship = null,
    string? CanonicalLearningTerm = null);
