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
    string? LastErrorCode,
    LexicalLookupMode LookupMode = LexicalLookupMode.Definition,
    string? TargetLanguage = null)
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
    string? CanonicalLearningTerm = null,
    string? TopicOrDomain = null,
    string? PartOfSpeech = null,
    LexicalLookupMode? ManualInputMode = null);

internal sealed class PreparationProgressionCoordinator
{
    private Func<PreparationMethod, Task>? _lastProgressAsync;

    public bool SavedSuccessfully { get; private set; }

    public bool ProgressionFailed { get; private set; }

    public PreparationMethod ProgressionMethod { get; private set; } = PreparationMethod.Manual;

    public Exception? LastProgressionException { get; private set; }

    public async Task<bool> CommitAndProgressAsync(
        PreparationMethod method,
        Func<Task> commitAsync,
        Func<PreparationMethod, Task> progressAsync)
    {
        if (ProgressionFailed)
        {
            throw new InvalidOperationException("Retry progression before committing another preparation action.");
        }

        SavedSuccessfully = false;
        LastProgressionException = null;
        await commitAsync();
        SavedSuccessfully = true;
        ProgressionMethod = method;
        _lastProgressAsync = progressAsync;
        return await TryProgressAsync(progressAsync);
    }

    public Task<bool> CommitAcceptAndProgressAsync(
        PreparationMethod method,
        Func<Task> commitAsync,
        Func<Task<LearningPreparationReadiness>> getReadinessAsync,
        Action navigateToLearning,
        Func<PreparationMethod, Task> loadNextAsync)
    {
        return CommitAndProgressAsync(
            method,
            commitAsync,
            m => ProgressAfterAcceptAsync(m, getReadinessAsync, navigateToLearning, loadNextAsync));
    }

    public static async Task ProgressAfterAcceptAsync(
        PreparationMethod method,
        Func<Task<LearningPreparationReadiness>> getReadinessAsync,
        Action navigateToLearning,
        Func<PreparationMethod, Task> loadNextAsync)
    {
        var readiness = await getReadinessAsync();
        if (readiness.ShouldTransitionToLearning)
        {
            navigateToLearning();
            return;
        }

        await loadNextAsync(method);
    }

    public Task<bool> RetryProgressionAsync(Func<PreparationMethod, Task>? progressAsync = null)
    {
        if (!SavedSuccessfully || !ProgressionFailed)
        {
            return Task.FromResult(false);
        }

        var action = progressAsync ?? _lastProgressAsync;
        if (action is null)
        {
            return Task.FromResult(false);
        }

        return TryProgressAsync(action);
    }

    private async Task<bool> TryProgressAsync(Func<PreparationMethod, Task> progressAsync)
    {
        try
        {
            await progressAsync(ProgressionMethod);
            ProgressionFailed = false;
            LastProgressionException = null;
            return true;
        }
        catch (Exception exception)
        {
            ProgressionFailed = true;
            LastProgressionException = exception;
            return false;
        }
    }
}
