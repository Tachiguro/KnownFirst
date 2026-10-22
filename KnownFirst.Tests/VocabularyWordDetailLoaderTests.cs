using KnownFirst.Components.Pages;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;
using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Models;
using KnownFirst.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnownFirst.Tests;

/// <summary>
/// Runtime behavior tests for the Vocabulary Word Detail page request state machine and
/// presentation helpers. These exercise the production C# types used by
/// <c>Components/Pages/VocabularyWordDetail.razor</c>; they do not render Razor markup.
/// </summary>
[TestClass]
public sealed class VocabularyWordDetailLoaderTests
{
    private static readonly DateTimeOffset BaseOffsetUtc = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NewLoader_StartsInLoadingStateWithoutDetail()
    {
        using var loader = new VocabularyWordDetailLoader(new FakeDetailService(), NullLogger.Instance);

        Assert.AreEqual(VocabularyWordDetailViewState.Loading, loader.State);
        Assert.IsTrue(loader.IsLoading);
        Assert.IsNull(loader.Detail);
    }

    [TestMethod]
    public async Task LoadAsync_FoundResultForRequestedWordId_ShowsLoadedDetail()
    {
        var service = new FakeDetailService();
        var detail = CreateDetail(wordId: 7, "alpha");
        service.Enqueue(Task.FromResult(VocabularyDetailLookupResult.Found(detail, 30)));
        using var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        var applied = await loader.LoadAsync(7);

        Assert.IsTrue(applied);
        Assert.AreEqual(VocabularyWordDetailViewState.Loaded, loader.State);
        Assert.AreSame(detail, loader.Detail);
        Assert.AreEqual(7, loader.RequestedWordId);
        Assert.IsFalse(loader.IsLoading);
        Assert.AreEqual(7, service.RequestedWordIds.Single());
        Assert.AreEqual(IVocabularyDetailService.DefaultMaxItemsPerCollection, service.RequestedLimits.Single());
    }

    [TestMethod]
    public async Task LoadAsync_NotFoundAndInvalidResults_MapToDistinctStatesWithoutDetail()
    {
        var service = new FakeDetailService();
        service.Enqueue(Task.FromResult(VocabularyDetailLookupResult.NotFound(42, 30)));
        service.Enqueue(Task.FromResult(VocabularyDetailLookupResult.InvalidWordId(0, 30)));
        using var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        await loader.LoadAsync(42);
        Assert.AreEqual(VocabularyWordDetailViewState.NotFound, loader.State);
        Assert.IsNull(loader.Detail);

        await loader.LoadAsync(0);
        Assert.AreEqual(VocabularyWordDetailViewState.InvalidWordId, loader.State);
        Assert.IsNull(loader.Detail);
    }

    [TestMethod]
    public async Task LoadAsync_ReadFailure_IsContainedAsFailedStateAndRetryRecovers()
    {
        var service = new FakeDetailService();
        service.Enqueue(Task.FromException<VocabularyDetailLookupResult>(
            new InvalidOperationException("SQLite error: no such table Senses (sensitive internals)")));
        var detail = CreateDetail(wordId: 5, "retry");
        service.Enqueue(Task.FromResult(VocabularyDetailLookupResult.Found(detail, 30)));
        using var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        var failedApplied = await loader.LoadAsync(5);

        Assert.IsTrue(failedApplied);
        Assert.AreEqual(VocabularyWordDetailViewState.Failed, loader.State);
        Assert.IsNull(loader.Detail);

        await loader.LoadAsync(5);

        Assert.AreEqual(VocabularyWordDetailViewState.Loaded, loader.State);
        Assert.AreSame(detail, loader.Detail);
        CollectionAssert.AreEqual(new[] { 5, 5 }, service.RequestedWordIds.ToArray());
    }

    [TestMethod]
    public async Task LoadAsync_FoundDetailForDifferentWordId_IsRejectedAsFailure()
    {
        var service = new FakeDetailService();
        service.Enqueue(Task.FromResult(VocabularyDetailLookupResult.Found(CreateDetail(wordId: 99, "other"), 30)));
        using var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        await loader.LoadAsync(3);

        Assert.AreEqual(VocabularyWordDetailViewState.Failed, loader.State);
        Assert.IsNull(loader.Detail);
    }

    [TestMethod]
    public async Task LoadAsync_WhileInFlight_ClearsPreviousWordDetailAndReportsLoading()
    {
        var service = new FakeDetailService();
        var first = CreateDetail(wordId: 1, "first");
        service.Enqueue(Task.FromResult(VocabularyDetailLookupResult.Found(first, 30)));
        var pending = new TaskCompletionSource<VocabularyDetailLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(pending.Task);
        using var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        await loader.LoadAsync(1);
        var secondLoad = loader.LoadAsync(2);

        Assert.AreEqual(VocabularyWordDetailViewState.Loading, loader.State);
        Assert.IsNull(loader.Detail, "The previous word's detail must not remain visible while another WordId loads.");
        Assert.AreEqual(2, loader.RequestedWordId);

        var second = CreateDetail(wordId: 2, "second");
        pending.SetResult(VocabularyDetailLookupResult.Found(second, 30));
        await secondLoad;

        Assert.AreSame(second, loader.Detail);
    }

    [TestMethod]
    public async Task LoadAsync_OlderResponseCompletingLast_IsIgnoredAndOlderTokenIsCancelled()
    {
        var service = new FakeDetailService();
        var olderPending = new TaskCompletionSource<VocabularyDetailLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerPending = new TaskCompletionSource<VocabularyDetailLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(olderPending.Task);
        service.Enqueue(newerPending.Task);
        using var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        var olderLoad = loader.LoadAsync(10);
        var newerLoad = loader.LoadAsync(20);

        Assert.IsTrue(service.ReceivedTokens[0].IsCancellationRequested, "Superseded request must be cancelled.");
        Assert.IsFalse(service.ReceivedTokens[1].IsCancellationRequested);

        var newer = CreateDetail(wordId: 20, "newer");
        newerPending.SetResult(VocabularyDetailLookupResult.Found(newer, 30));
        Assert.IsTrue(await newerLoad);

        olderPending.SetResult(VocabularyDetailLookupResult.Found(CreateDetail(wordId: 10, "older"), 30));
        Assert.IsFalse(await olderLoad, "Stale response must report that it was not applied.");

        Assert.AreEqual(VocabularyWordDetailViewState.Loaded, loader.State);
        Assert.AreSame(newer, loader.Detail);
        Assert.AreEqual(20, loader.RequestedWordId);
    }

    [TestMethod]
    public async Task LoadAsync_OlderFailureOrCancellationCompletingLast_DoesNotOverrideNewerResult()
    {
        var service = new FakeDetailService();
        var olderFailing = new TaskCompletionSource<VocabularyDetailLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var olderCancelled = new TaskCompletionSource<VocabularyDetailLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(olderFailing.Task);
        service.Enqueue(olderCancelled.Task);
        var newer = CreateDetail(wordId: 3, "newest");
        service.Enqueue(Task.FromResult(VocabularyDetailLookupResult.Found(newer, 30)));
        using var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        var failingLoad = loader.LoadAsync(1);
        var cancelledLoad = loader.LoadAsync(2);
        await loader.LoadAsync(3);

        olderFailing.SetException(new InvalidOperationException("late failure"));
        olderCancelled.SetCanceled();

        Assert.IsFalse(await failingLoad);
        Assert.IsFalse(await cancelledLoad);
        Assert.AreEqual(VocabularyWordDetailViewState.Loaded, loader.State);
        Assert.AreSame(newer, loader.Detail);
    }

    [TestMethod]
    public async Task Dispose_CancelsInFlightRequestAndIgnoresLateCompletion()
    {
        var service = new FakeDetailService();
        var pending = new TaskCompletionSource<VocabularyDetailLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(pending.Task);
        var loader = new VocabularyWordDetailLoader(service, NullLogger.Instance);

        var load = loader.LoadAsync(4);
        loader.Dispose();

        Assert.IsTrue(service.ReceivedTokens[0].IsCancellationRequested);

        pending.SetResult(VocabularyDetailLookupResult.Found(CreateDetail(wordId: 4, "late"), 30));
        Assert.IsFalse(await load);
        Assert.IsNull(loader.Detail);
    }

    [TestMethod]
    public void Presentation_TargetKindKeysDistinguishDefinitionFromTranslation()
    {
        var definitionKey = VocabularyWordDetailPresentation.GetTargetKindKey(LearningTargetKind.Definition);
        var translationKey = VocabularyWordDetailPresentation.GetTargetKindKey(LearningTargetKind.Translation);

        Assert.AreEqual("VocabularyDetail_TargetKindDefinition", definitionKey);
        Assert.AreEqual("VocabularyDetail_TargetKindTranslation", translationKey);
    }

    [TestMethod]
    public void Presentation_EveryEnumValueMapsToDistinctLocalizationKey()
    {
        AssertDistinctKeys(Enum.GetValues<Fsrs6CardState>().Select(VocabularyWordDetailPresentation.GetCardStateKey));
        AssertDistinctKeys(Enum.GetValues<ReviewRating>().Select(VocabularyWordDetailPresentation.GetRatingKey));
        AssertDistinctKeys(Enum.GetValues<AnswerVariantRequirement>().Select(VocabularyWordDetailPresentation.GetRequirementKey));
        AssertDistinctKeys(Enum.GetValues<LearningTargetKind>().Select(VocabularyWordDetailPresentation.GetTargetKindKey));
        AssertDistinctKeys(Enum.GetValues<TokenKind>().Select(VocabularyWordDetailPresentation.GetTokenKindKey));
    }

    [TestMethod]
    public void Presentation_ContextHighlightUsesPersistedUtf16SpanOnlyWhenInBounds()
    {
        var context = CreateContext("I saw the alpha here.", targetStart: 10, targetLength: 5);

        var segments = VocabularyWordDetailPresentation.TrySplitContext(context);

        Assert.IsNotNull(segments);
        Assert.AreEqual("I saw the ", segments.Value.Before);
        Assert.AreEqual("alpha", segments.Value.Target);
        Assert.AreEqual(" here.", segments.Value.After);

        Assert.IsNull(VocabularyWordDetailPresentation.TrySplitContext(CreateContext("short", targetStart: 3, targetLength: 10)));
        Assert.IsNull(VocabularyWordDetailPresentation.TrySplitContext(CreateContext("short", targetStart: -1, targetLength: 2)));
        Assert.IsNull(VocabularyWordDetailPresentation.TrySplitContext(CreateContext("short", targetStart: 1, targetLength: 0)));
        Assert.IsNull(VocabularyWordDetailPresentation.TrySplitContext(CreateContext("short", targetStart: int.MaxValue, targetLength: int.MaxValue)));
    }

    [TestMethod]
    public void Presentation_TimestampsKeepUtcInstantForMachineReadableAttribute()
    {
        var value = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));

        Assert.AreEqual("2026-03-04T03:06:07Z", VocabularyWordDetailPresentation.ToMachineTimestamp(value));
    }

    [TestMethod]
    public void Presentation_EmptyOrWhitespacePersistedTextIsTreatedAsUnavailable()
    {
        Assert.IsFalse(VocabularyWordDetailPresentation.HasText(null));
        Assert.IsFalse(VocabularyWordDetailPresentation.HasText(string.Empty));
        Assert.IsFalse(VocabularyWordDetailPresentation.HasText("   "));
        Assert.IsTrue(VocabularyWordDetailPresentation.HasText(" x "));
    }

    private static void AssertDistinctKeys(IEnumerable<string> keys)
    {
        var list = keys.ToList();
        Assert.IsNotEmpty(list);
        foreach (var key in list)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(key));
        }

        Assert.HasCount(list.Count, list.Distinct(StringComparer.Ordinal).ToList(), "Each enum value must map to its own localization key.");
    }

    private static VocabularyContextSnapshotDetail CreateContext(string text, int targetStart, int targetLength) =>
        new(
            contextSnapshotId: 1,
            meaningId: 1,
            senseId: 1,
            wordId: 1,
            sourceDocumentId: 1,
            sourceDocumentTitle: "Doc",
            text: text,
            targetStart: targetStart,
            targetLength: targetLength,
            normalizedFingerprint: "fp",
            createdAtUtc: BaseOffsetUtc);

    internal static VocabularyDetail CreateDetail(int wordId, string term) =>
        new(
            wordId,
            canonicalTerm: term,
            normalizedTerm: term,
            sourceLanguage: "en",
            tokenKind: TokenKind.Word,
            totalOccurrenceCount: 1,
            documentCount: 1,
            createdAtUtc: BaseOffsetUtc,
            updatedAtUtc: BaseOffsetUtc,
            wordLearningControl: null,
            senses: Array.Empty<VocabularySenseDetail>(),
            hasMoreSenses: false,
            maxItemsPerCollection: 30);

    private sealed class FakeDetailService : IVocabularyDetailService
    {
        private readonly Queue<Task<VocabularyDetailLookupResult>> _responses = new();

        public List<int> RequestedWordIds { get; } = [];

        public List<int> RequestedLimits { get; } = [];

        public List<CancellationToken> ReceivedTokens { get; } = [];

        public void Enqueue(Task<VocabularyDetailLookupResult> response) => _responses.Enqueue(response);

        public Task<VocabularyDetailLookupResult> GetVocabularyDetailAsync(
            int wordId,
            int maxItemsPerCollection = IVocabularyDetailService.DefaultMaxItemsPerCollection,
            CancellationToken cancellationToken = default)
        {
            RequestedWordIds.Add(wordId);
            RequestedLimits.Add(maxItemsPerCollection);
            ReceivedTokens.Add(cancellationToken);
            return _responses.Dequeue();
        }
    }
}
