namespace KnownFirst.Components.Pages;

using KnownFirst.Models;
using KnownFirst.Services;
using Microsoft.Extensions.Logging;

public enum TargetEquivalentAnswerEligibility
{
    Eligible = 0,
    TruncatedVariants = 1,
    IncompleteOrAmbiguous = 2
}

public sealed class TargetEquivalentAnswerFormState
{
    public TargetEquivalentAnswerFormState(int targetId, int wordId, int senseId, string targetLanguage)
    {
        TargetId = targetId;
        WordId = wordId;
        SenseId = senseId;
        TargetLanguage = targetLanguage;
    }

    public int TargetId { get; }
    public int WordId { get; }
    public int SenseId { get; }
    public string TargetLanguage { get; }
    public string AnswerText { get; set; } = string.Empty;
    public bool IsSameMeaningConfirmed { get; set; }
    public bool IsSubmitting { get; set; }
    public string? FeedbackMessageKey { get; set; }
    public bool IsSuccessFeedback { get; set; }

    public bool CanSubmit =>
        !IsSubmitting &&
        IsSameMeaningConfirmed &&
        !string.IsNullOrWhiteSpace(AnswerText) &&
        AnswerText.Trim().Length <= IVocabularyCorrectionService.MaxAnswerTextLength;
}

public sealed class VocabularyWordDetailEquivalentAnswerCoordinator : IDisposable
{
    private readonly IVocabularyCorrectionService _correctionService;
    private readonly Func<int, Task<bool>> _reloadWordDetailAsync;
    private readonly ILogger _logger;
    private readonly Dictionary<int, TargetEquivalentAnswerFormState> _states = new();
    private CancellationTokenSource? _activeCts;
    private bool _disposed;

    public VocabularyWordDetailEquivalentAnswerCoordinator(
        IVocabularyCorrectionService correctionService,
        Func<int, Task<bool>> reloadWordDetailAsync,
        ILogger logger)
    {
        _correctionService = correctionService ?? throw new ArgumentNullException(nameof(correctionService));
        _reloadWordDetailAsync = reloadWordDetailAsync ?? throw new ArgumentNullException(nameof(reloadWordDetailAsync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int ActiveWordId { get; private set; }

    public bool IsDisposed => _disposed;

    public bool IsSubmitting => _states.Values.Any(s => s.IsSubmitting);

    public TargetEquivalentAnswerEligibility GetEligibility(
        VocabularyDetail? detail,
        VocabularySenseDetail? sense,
        VocabularyLearningTargetDetail? target)
    {
        if (detail is null || sense is null || target is null)
        {
            return TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous;
        }

        if (detail.WordId <= 0 || sense.SenseId <= 0 || target.TargetId <= 0)
        {
            return TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous;
        }

        if (sense.WordId != detail.WordId || target.SenseId != sense.SenseId)
        {
            return TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous;
        }

        if (string.IsNullOrWhiteSpace(target.TargetLanguage))
        {
            return TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous;
        }

        if (target.HasMoreAnswerVariants)
        {
            return TargetEquivalentAnswerEligibility.TruncatedVariants;
        }

        return TargetEquivalentAnswerEligibility.Eligible;
    }

    public TargetEquivalentAnswerFormState GetOrCreateState(
        VocabularyDetail detail,
        VocabularySenseDetail sense,
        VocabularyLearningTargetDetail target)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(sense);
        ArgumentNullException.ThrowIfNull(target);

        if (ActiveWordId == 0)
        {
            ActiveWordId = detail.WordId;
        }
        else if (detail.WordId != ActiveWordId)
        {
            ResetForWord(detail.WordId);
        }

        if (_states.TryGetValue(target.TargetId, out var existing))
        {
            return existing;
        }

        var created = new TargetEquivalentAnswerFormState(
            target.TargetId,
            detail.WordId,
            sense.SenseId,
            target.TargetLanguage);
        _states[target.TargetId] = created;
        return created;
    }

    public TargetEquivalentAnswerFormState? GetState(int targetId) =>
        _states.GetValueOrDefault(targetId);

    public void ResetForWord(int wordId)
    {
        ActiveWordId = wordId;
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;
        _states.Clear();
    }

    public async Task<bool> SubmitAsync(
        VocabularyDetail detail,
        VocabularySenseDetail sense,
        VocabularyLearningTargetDetail target)
    {
        if (_disposed)
        {
            return false;
        }

        if (detail is null || sense is null || target is null)
        {
            return false;
        }

        if (ActiveWordId != 0 && detail.WordId != ActiveWordId)
        {
            return false;
        }

        var state = GetOrCreateState(detail, sense, target);

        if (state.IsSubmitting || IsSubmitting)
        {
            return false;
        }

        var eligibility = GetEligibility(detail, sense, target);
        if (eligibility != TargetEquivalentAnswerEligibility.Eligible)
        {
            state.FeedbackMessageKey = VocabularyWordDetailPresentation.GetEligibilityNoticeKey(eligibility);
            state.IsSuccessFeedback = false;
            return false;
        }

        if (string.IsNullOrWhiteSpace(state.AnswerText))
        {
            state.FeedbackMessageKey = "VocabularyDetail_EquivalentAnswerEmpty";
            state.IsSuccessFeedback = false;
            return false;
        }

        var trimmed = state.AnswerText.Trim();
        if (trimmed.Length > IVocabularyCorrectionService.MaxAnswerTextLength)
        {
            state.FeedbackMessageKey = "VocabularyDetail_EquivalentAnswerTooLong";
            state.IsSuccessFeedback = false;
            return false;
        }

        if (!state.IsSameMeaningConfirmed)
        {
            state.FeedbackMessageKey = "VocabularyDetail_EquivalentAnswerConfirmationRequired";
            state.IsSuccessFeedback = false;
            return false;
        }

        state.IsSubmitting = true;
        state.FeedbackMessageKey = null;

        _activeCts?.Cancel();
        _activeCts?.Dispose();
        var cts = new CancellationTokenSource();
        _activeCts = cts;
        var requestWordId = detail.WordId;

        try
        {
            var request = new AddAcceptedAnswerVariantRequest(
                wordId: detail.WordId,
                senseId: sense.SenseId,
                targetId: target.TargetId,
                answerText: trimmed,
                expectedLanguage: target.TargetLanguage);

            var result = await _correctionService.AddAcceptedAnswerVariantAsync(request, cts.Token).ConfigureAwait(false);

            if (_disposed || ActiveWordId != requestWordId)
            {
                return false;
            }

            if (result.IsSuccess)
            {
                state.AnswerText = string.Empty;
                state.IsSameMeaningConfirmed = false;
                state.FeedbackMessageKey = "VocabularyDetail_EquivalentAnswerSuccess";
                state.IsSuccessFeedback = true;
                await _reloadWordDetailAsync(requestWordId).ConfigureAwait(false);
                return true;
            }

            state.FeedbackMessageKey = VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(result.Status);
            state.IsSuccessFeedback = false;
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected error adding equivalent answer to target {TargetId}.", target.TargetId);
            state.FeedbackMessageKey = "VocabularyDetail_EquivalentAnswerFailure";
            state.IsSuccessFeedback = false;
            return false;
        }
        finally
        {
            state.IsSubmitting = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;
        _states.Clear();
    }
}
