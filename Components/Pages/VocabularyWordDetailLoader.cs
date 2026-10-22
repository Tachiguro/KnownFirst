using KnownFirst.Models;
using KnownFirst.Services;
using Microsoft.Extensions.Logging;

namespace KnownFirst.Components.Pages;

public enum VocabularyWordDetailViewState
{
    Loading = 0,
    Loaded = 1,
    NotFound = 2,
    InvalidWordId = 3,
    Failed = 4
}

/// <summary>
/// Read-only request state machine for the Vocabulary Word Detail page.
/// Each load supersedes and cancels the previous one; late results from superseded
/// requests (success, failure, or cancellation) are never applied.
/// </summary>
public sealed class VocabularyWordDetailLoader : IDisposable
{
    private readonly IVocabularyDetailService _detailService;
    private readonly ILogger _logger;
    private CancellationTokenSource? _activeRequestCts;
    private int _activeRequestId;
    private bool _disposed;

    public VocabularyWordDetailLoader(IVocabularyDetailService detailService, ILogger logger)
    {
        _detailService = detailService ?? throw new ArgumentNullException(nameof(detailService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public VocabularyWordDetailViewState State { get; private set; } = VocabularyWordDetailViewState.Loading;

    public bool IsLoading => State == VocabularyWordDetailViewState.Loading;

    public VocabularyDetail? Detail { get; private set; }

    public int RequestedWordId { get; private set; }

    /// <summary>
    /// Loads the detail projection for <paramref name="wordId"/>.
    /// Returns <see langword="true"/> when the outcome was applied, or <see langword="false"/>
    /// when the request was superseded or the loader was disposed.
    /// </summary>
    public async Task<bool> LoadAsync(int wordId)
    {
        if (_disposed)
        {
            return false;
        }

        var requestId = ++_activeRequestId;
        _activeRequestCts?.Cancel();
        _activeRequestCts?.Dispose();
        var requestCts = new CancellationTokenSource();
        _activeRequestCts = requestCts;

        RequestedWordId = wordId;
        Detail = null;
        State = VocabularyWordDetailViewState.Loading;

        VocabularyDetailLookupResult result;
        try
        {
            result = await _detailService.GetVocabularyDetailAsync(
                wordId,
                IVocabularyDetailService.DefaultMaxItemsPerCollection,
                requestCts.Token);
        }
        catch (Exception exception)
        {
            if (!IsCurrent(requestId))
            {
                return false;
            }

            _logger.LogError(exception, "Vocabulary word detail could not be loaded.");
            State = VocabularyWordDetailViewState.Failed;
            return true;
        }

        if (!IsCurrent(requestId))
        {
            return false;
        }

        if (result.IsFound)
        {
            if (result.Detail!.WordId != wordId)
            {
                _logger.LogError("Vocabulary word detail lookup returned a different word than requested.");
                State = VocabularyWordDetailViewState.Failed;
                return true;
            }

            Detail = result.Detail;
            State = VocabularyWordDetailViewState.Loaded;
        }
        else if (result.IsInvalidWordId)
        {
            State = VocabularyWordDetailViewState.InvalidWordId;
        }
        else
        {
            State = VocabularyWordDetailViewState.NotFound;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ++_activeRequestId;
        _activeRequestCts?.Cancel();
        _activeRequestCts?.Dispose();
        _activeRequestCts = null;
    }

    private bool IsCurrent(int requestId) => !_disposed && requestId == _activeRequestId;
}
