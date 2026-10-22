using System.Xml.Linq;
using KnownFirst.Components.Pages;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Text;
using KnownFirst.Data.Migrations.Schema8;
using KnownFirst.Data.Targets;
using KnownFirst.Models;
using KnownFirst.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnownFirst.Tests;

[TestClass]
public sealed class VocabularyWordDetailEquivalentAnswerTests
{
    private static readonly DateTimeOffset BaseOffsetUtc = new(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void TargetEligibility_PersistedUnambiguousTarget_IsEligible()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var eligibility = coordinator.GetEligibility(detail, sense, target);

        Assert.AreEqual(TargetEquivalentAnswerEligibility.Eligible, eligibility);
    }

    [TestMethod]
    public void TargetEligibility_TruncatedAnswerVariants_ReportsTruncatedVariants()
    {
        var (detail, sense, target) = CreateHierarchy(
            wordId: 10,
            senseId: 20,
            targetId: 30,
            targetLang: "de",
            hasMoreVariants: true);
        var service = new FakeCorrectionService();
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var eligibility = coordinator.GetEligibility(detail, sense, target);

        Assert.AreEqual(TargetEquivalentAnswerEligibility.TruncatedVariants, eligibility);
    }

    [TestMethod]
    public void TargetEligibility_MismatchedSenseOrWord_ReportsIncompleteOrAmbiguous()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var mismatchedSense = new VocabularySenseDetail(
            senseId: 99,
            wordId: 999, // Mismatched word
            stableId: "s-mismatch",
            sourceLanguage: "en",
            explanationLanguage: "de",
            providerSenseId: string.Empty,
            topicOrDomain: string.Empty,
            partOfSpeech: string.Empty,
            grammaticalRelationship: string.Empty,
            acronymExpansion: string.Empty,
            defaultMeaningId: null,
            createdAtUtc: BaseOffsetUtc,
            updatedAtUtc: BaseOffsetUtc,
            senseLearningControl: null,
            meanings: [],
            hasMoreMeanings: false,
            contextSnapshots: [],
            hasMoreContextSnapshots: false,
            learningTargets: [target],
            hasMoreLearningTargets: false);

        var service = new FakeCorrectionService();
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        Assert.AreEqual(TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous, coordinator.GetEligibility(detail, mismatchedSense, target));
        Assert.AreEqual(TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous, coordinator.GetEligibility(null, sense, target));
        Assert.AreEqual(TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous, coordinator.GetEligibility(detail, null, target));
        Assert.AreEqual(TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous, coordinator.GetEligibility(detail, sense, null));
    }

    [TestMethod]
    public void TargetEligibility_MissingTargetLanguage_ReportsIncompleteOrAmbiguous()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "");
        var service = new FakeCorrectionService();
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var eligibility = coordinator.GetEligibility(detail, sense, target);

        Assert.AreEqual(TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous, eligibility);
    }

    [TestMethod]
    public async Task SubmitAsync_WithoutAffirmativeConfirmation_IsBlockedAndDoesNotCallService()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "Valid answer";
        state.IsSameMeaningConfirmed = false;

        Assert.IsFalse(state.CanSubmit);

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsFalse(submitted);
        Assert.AreEqual(0, service.ReceivedRequests.Count);
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerConfirmationRequired", state.FeedbackMessageKey);
        Assert.IsFalse(state.IsSuccessFeedback);
    }

    [TestMethod]
    public async Task SubmitAsync_WithEmptyOrWhitespaceAnswerText_IsBlockedAndDoesNotCallService()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "   ";
        state.IsSameMeaningConfirmed = true;

        Assert.IsFalse(state.CanSubmit);

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsFalse(submitted);
        Assert.AreEqual(0, service.ReceivedRequests.Count);
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerEmpty", state.FeedbackMessageKey);
    }

    [TestMethod]
    public async Task SubmitAsync_WithAnswerTextExceeding200Chars_IsBlockedAndDoesNotCallService()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = new string('x', 201);
        state.IsSameMeaningConfirmed = true;

        Assert.IsFalse(state.CanSubmit);

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsFalse(submitted);
        Assert.AreEqual(0, service.ReceivedRequests.Count);
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerTooLong", state.FeedbackMessageKey);
    }

    [TestMethod]
    public async Task SubmitAsync_BindsCorrectWordSenseTargetAndLanguageRequest_CallsService()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        var reloadedWordId = -1;
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(
            service,
            id => { reloadedWordId = id; return Task.FromResult(true); },
            NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "  Synonym Wort  ";
        state.IsSameMeaningConfirmed = true;

        var dummyVariant = CreatePersistedVariant(30, "de", "Synonym Wort");
        service.Enqueue(Task.FromResult(AddAcceptedAnswerVariantResult.Success(dummyVariant)));

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsTrue(submitted);
        Assert.AreEqual(1, service.ReceivedRequests.Count);
        var req = service.ReceivedRequests.Single();
        Assert.AreEqual(10, req.WordId);
        Assert.AreEqual(20, req.SenseId);
        Assert.AreEqual(30, req.TargetId);
        Assert.AreEqual("Synonym Wort", req.AnswerText);
        Assert.AreEqual("de", req.ExpectedLanguage);
        Assert.AreEqual(10, reloadedWordId);
    }

    [TestMethod]
    public async Task SubmitAsync_ConcurrentOrDoubleSubmit_IsPreventedAndCallsServiceOnce()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        var pendingTcs = new TaskCompletionSource<AddAcceptedAnswerVariantResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(pendingTcs.Task);
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "Erstversuch";
        state.IsSameMeaningConfirmed = true;

        var firstSubmit = coordinator.SubmitAsync(detail, sense, target);
        Assert.IsTrue(state.IsSubmitting);
        Assert.IsTrue(coordinator.IsSubmitting);

        // Concurrent attempt while in flight
        var secondSubmit = coordinator.SubmitAsync(detail, sense, target);
        Assert.IsFalse(await secondSubmit);
        Assert.AreEqual(1, service.ReceivedRequests.Count);

        var dummyVariant = CreatePersistedVariant(30, "de", "Erstversuch");
        pendingTcs.SetResult(AddAcceptedAnswerVariantResult.Success(dummyVariant));
        Assert.IsTrue(await firstSubmit);
        Assert.IsFalse(state.IsSubmitting);
        Assert.IsFalse(coordinator.IsSubmitting);
    }

    [TestMethod]
    public async Task SubmitAsync_DuplicateAnswerOutcome_SetsDuplicateFeedbackAndRetainsInput()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        service.Enqueue(Task.FromResult(AddAcceptedAnswerVariantResult.DuplicateAnswer("doppelt")));
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "Doppelt";
        state.IsSameMeaningConfirmed = true;

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsFalse(submitted);
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerDuplicate", state.FeedbackMessageKey);
        Assert.IsFalse(state.IsSuccessFeedback);
        Assert.AreEqual("Doppelt", state.AnswerText);
        Assert.IsTrue(state.IsSameMeaningConfirmed);
    }

    [TestMethod]
    public async Task SubmitAsync_ActiveWorkflowBlockedOutcome_SetsWorkflowBlockedFeedbackAndRetainsInput()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        service.Enqueue(Task.FromResult(AddAcceptedAnswerVariantResult.BlockedByActiveWorkflow("workflow active")));
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "Versuch";
        state.IsSameMeaningConfirmed = true;

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsFalse(submitted);
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerWorkflowBlocked", state.FeedbackMessageKey);
        Assert.IsFalse(state.IsSuccessFeedback);
        Assert.AreEqual("Versuch", state.AnswerText);
        Assert.IsTrue(state.IsSameMeaningConfirmed);
    }

    [TestMethod]
    public async Task SubmitAsync_SuccessOutcome_ClearsFormSetsSuccessFeedbackAndReloadsDetail()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        var reloadedWordId = -1;
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(
            service,
            id => { reloadedWordId = id; return Task.FromResult(true); },
            NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "Neues Wort";
        state.IsSameMeaningConfirmed = true;

        var dummyVariant = CreatePersistedVariant(30, "de", "Neues Wort");
        service.Enqueue(Task.FromResult(AddAcceptedAnswerVariantResult.Success(dummyVariant)));

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsTrue(submitted);
        Assert.AreEqual(string.Empty, state.AnswerText);
        Assert.IsFalse(state.IsSameMeaningConfirmed);
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerSuccess", state.FeedbackMessageKey);
        Assert.IsTrue(state.IsSuccessFeedback);
        Assert.AreEqual(10, reloadedWordId);
    }

    [TestMethod]
    public async Task SubmitAsync_ServiceException_IsContainedWithoutLeakingInternals()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        service.Enqueue(Task.FromException<AddAcceptedAnswerVariantResult>(
            new InvalidOperationException("SQLite table lock failure (sensitive internals)")));
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);

        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "Test";
        state.IsSameMeaningConfirmed = true;

        var submitted = await coordinator.SubmitAsync(detail, sense, target);

        Assert.IsFalse(submitted);
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerFailure", state.FeedbackMessageKey);
        Assert.IsFalse(state.IsSuccessFeedback);
        Assert.IsFalse(state.IsSubmitting);
    }

    [TestMethod]
    public async Task ResetForWord_CancelsInFlightAndDiscardsLateCompletionWithoutReloadingNewWord()
    {
        var (detailWord1, senseWord1, targetWord1) = CreateHierarchy(wordId: 1, senseId: 10, targetId: 100, targetLang: "de");
        var service = new FakeCorrectionService();
        var pendingTcs = new TaskCompletionSource<AddAcceptedAnswerVariantResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(pendingTcs.Task);

        var reloadCount = 0;
        using var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(
            service,
            _ => { Interlocked.Increment(ref reloadCount); return Task.FromResult(true); },
            NullLogger.Instance);

        var stateWord1 = coordinator.GetOrCreateState(detailWord1, senseWord1, targetWord1);
        stateWord1.AnswerText = "Word1Answer";
        stateWord1.IsSameMeaningConfirmed = true;

        var submitTask = coordinator.SubmitAsync(detailWord1, senseWord1, targetWord1);

        // User navigates to Word 2 while submit for Word 1 is in flight
        coordinator.ResetForWord(2);

        var dummyVariant = CreatePersistedVariant(100, "de", "Word1Answer");
        pendingTcs.SetResult(AddAcceptedAnswerVariantResult.Success(dummyVariant));

        var outcome = await submitTask;
        Assert.IsFalse(outcome, "Late result from superseded word must report false and not apply.");
        Assert.AreEqual(0, reloadCount, "Reload must not be triggered for superseded word result.");
    }

    [TestMethod]
    public async Task DisposedCoordinator_BlocksSubmissionsAndDiscardsLateResults()
    {
        var (detail, sense, target) = CreateHierarchy(wordId: 10, senseId: 20, targetId: 30, targetLang: "de");
        var service = new FakeCorrectionService();
        var pendingTcs = new TaskCompletionSource<AddAcceptedAnswerVariantResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Enqueue(pendingTcs.Task);

        var coordinator = new VocabularyWordDetailEquivalentAnswerCoordinator(service, _ => Task.FromResult(true), NullLogger.Instance);
        var state = coordinator.GetOrCreateState(detail, sense, target);
        state.AnswerText = "Antwort";
        state.IsSameMeaningConfirmed = true;

        var submitTask = coordinator.SubmitAsync(detail, sense, target);
        coordinator.Dispose();

        pendingTcs.SetResult(AddAcceptedAnswerVariantResult.Success(CreatePersistedVariant(30, "de", "Antwort")));
        Assert.IsFalse(await submitTask);

        // Subsequent submission after dispose must be blocked immediately
        Assert.IsFalse(await coordinator.SubmitAsync(detail, sense, target));
    }

    [TestMethod]
    public void PresentationHelpers_MapAllStatusesAndEligibilitiesCorrectly()
    {
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerSuccess", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.Success));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerDuplicate", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.DuplicateAnswer));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerWorkflowBlocked", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.BlockedByActiveWorkflow));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerEmpty", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.EmptyAnswerText));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerTooLong", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.AnswerTextTooLong));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerLanguageMismatch", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.InvalidAnswerLanguage));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerFailure", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.OwnershipMismatch));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerFailure", VocabularyWordDetailPresentation.GetEquivalentAnswerStatusKey(AddAcceptedAnswerVariantStatus.TargetNotFound));

        Assert.AreEqual("VocabularyDetail_EquivalentAnswerTruncatedNotice", VocabularyWordDetailPresentation.GetEligibilityNoticeKey(TargetEquivalentAnswerEligibility.TruncatedVariants));
        Assert.AreEqual("VocabularyDetail_EquivalentAnswerUnavailableNotice", VocabularyWordDetailPresentation.GetEligibilityNoticeKey(TargetEquivalentAnswerEligibility.IncompleteOrAmbiguous));
        Assert.AreEqual(string.Empty, VocabularyWordDetailPresentation.GetEligibilityNoticeKey(TargetEquivalentAnswerEligibility.Eligible));
    }

    [TestMethod]
    public void Localization_AllEquivalentAnswerKeys_ArePresentAndNonEmptyInAllThreeCultures()
    {
        var root = FindRepositoryRoot();
        var enResx = ReadResxKeys(Path.Combine(root, "Resources", "Localization", "SharedResource.resx"));
        var deResx = ReadResxKeys(Path.Combine(root, "Resources", "Localization", "SharedResource.de.resx"));
        var ruResx = ReadResxKeys(Path.Combine(root, "Resources", "Localization", "SharedResource.ru.resx"));

        string[] requiredKeys =
        [
            "VocabularyDetail_ReadOnlyNotice",
            "VocabularyDetail_AddEquivalentAnswerTitle",
            "VocabularyDetail_EquivalentAnswerClarification",
            "VocabularyDetail_EquivalentAnswerLanguage",
            "VocabularyDetail_EquivalentAnswerInputLabel",
            "VocabularyDetail_EquivalentAnswerInputPlaceholder",
            "VocabularyDetail_EquivalentAnswerSameMeaningConfirmation",
            "VocabularyDetail_EquivalentAnswerSaveButton",
            "VocabularyDetail_EquivalentAnswerSaving",
            "VocabularyDetail_EquivalentAnswerSuccess",
            "VocabularyDetail_EquivalentAnswerDuplicate",
            "VocabularyDetail_EquivalentAnswerWorkflowBlocked",
            "VocabularyDetail_EquivalentAnswerEmpty",
            "VocabularyDetail_EquivalentAnswerTooLong",
            "VocabularyDetail_EquivalentAnswerConfirmationRequired",
            "VocabularyDetail_EquivalentAnswerLanguageMismatch",
            "VocabularyDetail_EquivalentAnswerFailure",
            "VocabularyDetail_EquivalentAnswerTruncatedNotice",
            "VocabularyDetail_EquivalentAnswerUnavailableNotice"
        ];

        foreach (var key in requiredKeys)
        {
            Assert.IsTrue(enResx.ContainsKey(key), $"SharedResource.resx must contain {key}.");
            Assert.IsTrue(deResx.ContainsKey(key), $"SharedResource.de.resx must contain {key}.");
            Assert.IsTrue(ruResx.ContainsKey(key), $"SharedResource.ru.resx must contain {key}.");

            Assert.IsFalse(string.IsNullOrWhiteSpace(enResx[key]), $"SharedResource.resx value for {key} must not be empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(deResx[key]), $"SharedResource.de.resx value for {key} must not be empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ruResx[key]), $"SharedResource.ru.resx value for {key} must not be empty.");
        }

        // Truthful read-only notice verification
        Assert.IsFalse(enResx["VocabularyDetail_ReadOnlyNotice"].Contains("nothing can be changed", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(deResx["VocabularyDetail_ReadOnlyNotice"].Contains("nichts geändert werden", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(ruResx["VocabularyDetail_ReadOnlyNotice"].Contains("ничего нельзя изменить", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RazorMarkup_ContainsRequiredAccessibilityAttributesAndMaxlength()
    {
        var root = FindRepositoryRoot();
        var razorContent = File.ReadAllText(Path.Combine(root, "Components", "Pages", "VocabularyWordDetail.razor"));

        StringAssert.Contains(razorContent, "maxlength=\"200\"");
        StringAssert.Contains(razorContent, "type=\"checkbox\"");
        StringAssert.Contains(razorContent, "aria-describedby");
        StringAssert.Contains(razorContent, "role=\"alert\"");
        StringAssert.Contains(razorContent, "role=\"status\"");
        StringAssert.Contains(razorContent, "IVocabularyCorrectionService");
    }

    private static (VocabularyDetail Detail, VocabularySenseDetail Sense, VocabularyLearningTargetDetail Target) CreateHierarchy(
        int wordId,
        int senseId,
        int targetId,
        string targetLang,
        bool hasMoreVariants = false)
    {
        var target = new VocabularyLearningTargetDetail(
            targetId: targetId,
            senseId: senseId,
            stableId: $"t-{targetId}",
            targetKind: LearningTargetKind.Definition,
            sourceLanguage: "en",
            targetLanguage: targetLang,
            typingOptOut: false,
            createdAtUtc: BaseOffsetUtc,
            updatedAtUtc: BaseOffsetUtc,
            fsrsState: null,
            answerVariants: [],
            hasMoreAnswerVariants: hasMoreVariants,
            reviewHistory: [],
            hasMoreReviewHistory: false,
            isUnblockedByLearningControls: true);

        var sense = new VocabularySenseDetail(
            senseId: senseId,
            wordId: wordId,
            stableId: $"s-{senseId}",
            sourceLanguage: "en",
            explanationLanguage: "de",
            providerSenseId: string.Empty,
            topicOrDomain: string.Empty,
            partOfSpeech: string.Empty,
            grammaticalRelationship: string.Empty,
            acronymExpansion: string.Empty,
            defaultMeaningId: null,
            createdAtUtc: BaseOffsetUtc,
            updatedAtUtc: BaseOffsetUtc,
            senseLearningControl: null,
            meanings: [],
            hasMoreMeanings: false,
            contextSnapshots: [],
            hasMoreContextSnapshots: false,
            learningTargets: [target],
            hasMoreLearningTargets: false);

        var detail = new VocabularyDetail(
            wordId: wordId,
            canonicalTerm: "test",
            normalizedTerm: "test",
            sourceLanguage: "en",
            tokenKind: TokenKind.Word,
            totalOccurrenceCount: 1,
            documentCount: 1,
            createdAtUtc: BaseOffsetUtc,
            updatedAtUtc: BaseOffsetUtc,
            wordLearningControl: null,
            senses: [sense],
            hasMoreSenses: false,
            maxItemsPerCollection: 30);

        return (detail, sense, target);
    }

    private static PersistedTargetAnswerVariant CreatePersistedVariant(int targetId, string lang, string text) =>
        new(
            Id: 101,
            StableId: "v-101",
            TargetId: targetId,
            AnswerLanguage: lang,
            DisplayText: text,
            NormalizedText: text.ToLowerInvariant(),
            Requirement: AnswerVariantRequirement.AcceptedOnly,
            IsPreferred: false,
            RequiredSinceUtc: null,
            SourceMeaningId: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

    private static Dictionary<string, string> ReadResxKeys(string path)
    {
        var doc = XDocument.Load(path);
        return doc.Root!
            .Elements("data")
            .Where(e => e.Attribute("name") is not null)
            .ToDictionary(
                e => (string)e.Attribute("name")!,
                e => (string?)e.Element("value") ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "KnownFirst.slnx")) ||
                File.Exists(Path.Combine(directory, "KnownFirst.csproj")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeCorrectionService : IVocabularyCorrectionService
    {
        private readonly Queue<Task<AddAcceptedAnswerVariantResult>> _responses = new();

        public List<AddAcceptedAnswerVariantRequest> ReceivedRequests { get; } = [];

        public void Enqueue(Task<AddAcceptedAnswerVariantResult> response) => _responses.Enqueue(response);

        public Task<AddAcceptedAnswerVariantResult> AddAcceptedAnswerVariantAsync(
            AddAcceptedAnswerVariantRequest request,
            CancellationToken cancellationToken = default)
        {
            ReceivedRequests.Add(request);
            if (_responses.Count > 0)
            {
                return _responses.Dequeue();
            }

            return Task.FromResult(AddAcceptedAnswerVariantResult.Success(CreatePersistedVariant(request.TargetId, request.ExpectedLanguage ?? "en", request.AnswerText)));
        }
    }
}
