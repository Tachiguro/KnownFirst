using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Models;

namespace KnownFirst.Tests;

[TestClass]
public sealed class PreparationDailyTargetTransitionTests
{
    private static string LoadPrepareWordsMarkup()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Components", "Pages", "PrepareWords.razor"));
        if (File.Exists(projectPath))
        {
            return File.ReadAllText(projectPath);
        }

        var outputPath = Path.Combine(AppContext.BaseDirectory, "Ui", "PrepareWords.razor");
        return File.ReadAllText(outputPath);
    }

    [TestMethod]
    public void Requirement14_18_19_PrepareWords_InjectsLearningService_AndConsumesReadinessOnAccept()
    {
        var markup = LoadPrepareWordsMarkup();

        Assert.IsTrue(
            markup.Contains("@inject ILearningService LearningService", StringComparison.Ordinal),
            "PrepareWords.razor must inject ILearningService to query preparation readiness.");

        Assert.IsTrue(
            markup.Contains("GetPreparationReadinessAsync", StringComparison.Ordinal),
            "PrepareWords.razor must query GetPreparationReadinessAsync on Accept progression.");

        Assert.IsTrue(
            markup.Contains("/learn", StringComparison.Ordinal),
            "PrepareWords.razor must navigate to /learn when readiness is satisfied.");

        Assert.IsFalse(
            markup.Contains("LearningService.GetOrStartAsync", StringComparison.Ordinal),
            "PrepareWords.razor must not directly call GetOrStartAsync; the Learn page remains responsible for session startup.");

        Assert.IsFalse(
            markup.Contains("LearningDayGrant", StringComparison.Ordinal),
            "PrepareWords.razor must not reference LearningDayGrant or duplicate grant logic.");
    }

    [TestMethod]
    public async Task Requirement01_ReadinessTrue_AfterSuccessfulAccept_SelectsLearningTransition()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var readinessCalls = 0;
        var navigatedToLearn = false;
        var loadNextCalls = 0;

        Task CommitAsync()
        {
            acceptCalls++;
            return Task.CompletedTask;
        }

        Task<LearningPreparationReadiness> GetReadinessAsync()
        {
            readinessCalls++;
            return Task.FromResult(new LearningPreparationReadiness(
                ShouldTransitionToLearning: true,
                Phase: LearningDayPhase.ActiveBudgetDay,
                RemainingFreshWordDemand: 0,
                EligibleFreshWordCount: 5));
        }

        void NavigateToLearn()
        {
            navigatedToLearn = true;
        }

        Task LoadNextAsync(PreparationMethod method)
        {
            loadNextCalls++;
            return Task.CompletedTask;
        }

        var succeeded = await coordinator.CommitAcceptAndProgressAsync(
            PreparationMethod.Manual,
            CommitAsync,
            GetReadinessAsync,
            NavigateToLearn,
            LoadNextAsync);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, acceptCalls);
        Assert.AreEqual(1, readinessCalls);
        Assert.IsTrue(navigatedToLearn, "Learning transition must be selected when readiness is true.");
        Assert.AreEqual(0, loadNextCalls, "No next candidate must be loaded when transitioning to learning.");
    }

    [TestMethod]
    public async Task Requirement02_ReadinessFalse_BacklogBelowDemand_SelectsOrdinaryProgression()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var navigatedToLearn = false;
        var loadNextCalls = 0;

        Task CommitAsync()
        {
            acceptCalls++;
            return Task.CompletedTask;
        }

        Task<LearningPreparationReadiness> GetReadinessAsync() =>
            Task.FromResult(new LearningPreparationReadiness(
                ShouldTransitionToLearning: false,
                Phase: LearningDayPhase.ActiveBudgetDay,
                RemainingFreshWordDemand: 5,
                EligibleFreshWordCount: 4));

        void NavigateToLearn() => navigatedToLearn = true;

        Task LoadNextAsync(PreparationMethod method)
        {
            Assert.AreEqual(PreparationMethod.Manual, method);
            loadNextCalls++;
            return Task.CompletedTask;
        }

        var succeeded = await coordinator.CommitAcceptAndProgressAsync(
            PreparationMethod.Manual,
            CommitAsync,
            GetReadinessAsync,
            NavigateToLearn,
            LoadNextAsync);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, acceptCalls);
        Assert.IsFalse(navigatedToLearn, "Must not navigate to /learn when backlog is below remaining demand.");
        Assert.AreEqual(1, loadNextCalls, "Ordinary progression must continue when readiness is false.");
    }

    [TestMethod]
    public async Task Requirement03_ReadinessFalse_DailyCapacityExhausted_AllowsOrdinaryProgressionAndPreservesReEntry()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var navigatedToLearn = false;
        var loadNextCalls = 0;

        Task CommitAsync()
        {
            acceptCalls++;
            return Task.CompletedTask;
        }

        Task<LearningPreparationReadiness> GetReadinessAsync() =>
            Task.FromResult(new LearningPreparationReadiness(
                ShouldTransitionToLearning: false,
                Phase: LearningDayPhase.ActiveBudgetDay,
                RemainingFreshWordDemand: 0,
                EligibleFreshWordCount: 10));

        void NavigateToLearn() => navigatedToLearn = true;

        Task LoadNextAsync(PreparationMethod method)
        {
            loadNextCalls++;
            return Task.CompletedTask;
        }

        var succeeded = await coordinator.CommitAcceptAndProgressAsync(
            PreparationMethod.AutomaticOnline,
            CommitAsync,
            GetReadinessAsync,
            NavigateToLearn,
            LoadNextAsync);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, acceptCalls);
        Assert.IsFalse(navigatedToLearn, "Must not bounce to /learn when daily capacity is exhausted.");
        Assert.AreEqual(1, loadNextCalls, "Ordinary progression remains available for same-day Preparation re-entry.");
        Assert.AreEqual(PreparationMethod.AutomaticOnline, coordinator.ProgressionMethod);
    }

    [TestMethod]
    public async Task Requirement04_ReadinessIsEvaluatedOnlyAfterCommitSuccess()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var events = new List<string>();

        Task CommitAsync()
        {
            events.Add("commit");
            return Task.CompletedTask;
        }

        Task<LearningPreparationReadiness> GetReadinessAsync()
        {
            events.Add("readiness");
            return Task.FromResult(new LearningPreparationReadiness(true, LearningDayPhase.ActiveBudgetDay, 0, 5));
        }

        void NavigateToLearn() => events.Add("navigate");

        Task LoadNextAsync(PreparationMethod method)
        {
            events.Add("loadNext");
            return Task.CompletedTask;
        }

        await coordinator.CommitAcceptAndProgressAsync(
            PreparationMethod.Manual,
            CommitAsync,
            GetReadinessAsync,
            NavigateToLearn,
            LoadNextAsync);

        CollectionAssert.AreEqual(new[] { "commit", "readiness", "navigate" }, events);
    }

    [TestMethod]
    public async Task Requirement05_AcceptanceCommitFailure_ReadinessNotQueried_NavigationNotAttempted()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var readinessQueried = false;
        var navigationAttempted = false;
        var loadNextAttempted = false;

        Task CommitAsync() => throw new InvalidOperationException("synthetic database save failure");
        Task<LearningPreparationReadiness> GetReadinessAsync()
        {
            readinessQueried = true;
            return Task.FromResult(new LearningPreparationReadiness(true, LearningDayPhase.ActiveBudgetDay, 0, 5));
        }
        void NavigateToLearn() => navigationAttempted = true;
        Task LoadNextAsync(PreparationMethod method)
        {
            loadNextAttempted = true;
            return Task.CompletedTask;
        }

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            coordinator.CommitAcceptAndProgressAsync(
                PreparationMethod.Manual,
                CommitAsync,
                GetReadinessAsync,
                NavigateToLearn,
                LoadNextAsync));

        Assert.IsFalse(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.IsFalse(readinessQueried, "Readiness must never be queried if acceptance commit failed.");
        Assert.IsFalse(navigationAttempted, "Navigation must never be attempted if acceptance commit failed.");
        Assert.IsFalse(loadNextAttempted, "Candidate loading must not occur if commit failed.");
    }

    [TestMethod]
    public async Task Requirement06_07_ReadinessFailureAfterCommit_ItemSaved_ProgressionFailed_RetryDoesNotRepeatAccept()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var readinessCalls = 0;
        var failReadiness = true;
        var navigatedToLearn = false;

        Task CommitAsync()
        {
            acceptCalls++;
            return Task.CompletedTask;
        }

        Task<LearningPreparationReadiness> GetReadinessAsync()
        {
            readinessCalls++;
            if (failReadiness)
            {
                throw new InvalidOperationException("synthetic readiness evaluation failure");
            }

            return Task.FromResult(new LearningPreparationReadiness(true, LearningDayPhase.ActiveBudgetDay, 0, 5));
        }

        void NavigateToLearn() => navigatedToLearn = true;
        Task LoadNextAsync(PreparationMethod method) => Task.CompletedTask;

        var firstResult = await coordinator.CommitAcceptAndProgressAsync(
            PreparationMethod.Manual,
            CommitAsync,
            GetReadinessAsync,
            NavigateToLearn,
            LoadNextAsync);

        Assert.IsFalse(firstResult);
        Assert.IsTrue(coordinator.SavedSuccessfully, "Item must be considered saved even if readiness query fails.");
        Assert.IsTrue(coordinator.ProgressionFailed, "Progression must enter retryable failure state.");
        Assert.IsNotNull(coordinator.LastProgressionException);
        Assert.AreEqual(1, acceptCalls, "Acceptance must occur exactly once.");
        Assert.AreEqual(1, readinessCalls);
        Assert.IsFalse(navigatedToLearn);

        failReadiness = false;
        var retryResult = await coordinator.RetryProgressionAsync();

        Assert.IsTrue(retryResult);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.IsNull(coordinator.LastProgressionException);
        Assert.AreEqual(1, acceptCalls, "Acceptance must never be repeated on progression retry.");
        Assert.AreEqual(2, readinessCalls);
        Assert.IsTrue(navigatedToLearn, "Learning transition must succeed on retry.");
    }

    [TestMethod]
    public async Task Requirement08_09_NavigationFailureAfterReadinessTrue_ItemSaved_RetryAttemptsLearningTransitionWithoutReAccept()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var navigationAttempts = 0;
        var failNavigation = true;

        Task CommitAsync()
        {
            acceptCalls++;
            return Task.CompletedTask;
        }

        Task<LearningPreparationReadiness> GetReadinessAsync() =>
            Task.FromResult(new LearningPreparationReadiness(true, LearningDayPhase.ActiveBudgetDay, 0, 5));

        void NavigateToLearn()
        {
            navigationAttempts++;
            if (failNavigation)
            {
                throw new InvalidOperationException("synthetic navigation failure");
            }
        }

        Task LoadNextAsync(PreparationMethod method) => Task.CompletedTask;

        var firstResult = await coordinator.CommitAcceptAndProgressAsync(
            PreparationMethod.Manual,
            CommitAsync,
            GetReadinessAsync,
            NavigateToLearn,
            LoadNextAsync);

        Assert.IsFalse(firstResult);
        Assert.IsTrue(coordinator.SavedSuccessfully, "Item must remain saved despite navigation failure.");
        Assert.IsTrue(coordinator.ProgressionFailed);
        Assert.AreEqual(1, acceptCalls);
        Assert.AreEqual(1, navigationAttempts);

        failNavigation = false;
        var retryResult = await coordinator.RetryProgressionAsync();

        Assert.IsTrue(retryResult);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, acceptCalls, "Acceptance must remain exactly one.");
        Assert.AreEqual(2, navigationAttempts, "Retry must re-attempt navigation to /learn.");
    }

    [TestMethod]
    public async Task Requirement10_ReadinessFalse_LoadNextFailure_RetryDoesNotRepeatAccept()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var acceptCalls = 0;
        var loadNextCalls = 0;
        var failLoadNext = true;

        Task CommitAsync()
        {
            acceptCalls++;
            return Task.CompletedTask;
        }

        Task<LearningPreparationReadiness> GetReadinessAsync() =>
            Task.FromResult(new LearningPreparationReadiness(false, LearningDayPhase.ActiveBudgetDay, 5, 2));

        void NavigateToLearn() => Assert.Fail("Should not navigate when readiness is false.");

        Task LoadNextAsync(PreparationMethod method)
        {
            loadNextCalls++;
            if (failLoadNext)
            {
                throw new InvalidOperationException("synthetic next-candidate load failure");
            }

            return Task.CompletedTask;
        }

        var firstResult = await coordinator.CommitAcceptAndProgressAsync(
            PreparationMethod.Manual,
            CommitAsync,
            GetReadinessAsync,
            NavigateToLearn,
            LoadNextAsync);

        Assert.IsFalse(firstResult);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsTrue(coordinator.ProgressionFailed);
        Assert.AreEqual(1, acceptCalls);
        Assert.AreEqual(1, loadNextCalls);

        failLoadNext = false;
        var retryResult = await coordinator.RetryProgressionAsync();

        Assert.IsTrue(retryResult);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, acceptCalls, "Acceptance must not be repeated on candidate-load retry.");
        Assert.AreEqual(2, loadNextCalls);
    }

    [TestMethod]
    public async Task Requirement11_Skip_DoesNotQueryLearningReadiness()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var skipCalls = 0;
        var loadNextCalls = 0;

        Task CommitSkipAsync()
        {
            skipCalls++;
            return Task.CompletedTask;
        }

        Task LoadNextAsync(PreparationMethod method)
        {
            loadNextCalls++;
            return Task.CompletedTask;
        }

        var succeeded = await coordinator.CommitAndProgressAsync(
            PreparationMethod.Manual,
            CommitSkipAsync,
            LoadNextAsync);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, skipCalls);
        Assert.AreEqual(1, loadNextCalls);
    }

    [TestMethod]
    public async Task Requirement12_MarkKnown_DoesNotQueryLearningReadiness()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var markKnownCalls = 0;
        var loadNextCalls = 0;

        Task CommitMarkKnownAsync()
        {
            markKnownCalls++;
            return Task.CompletedTask;
        }

        Task LoadNextAsync(PreparationMethod method)
        {
            loadNextCalls++;
            return Task.CompletedTask;
        }

        var succeeded = await coordinator.CommitAndProgressAsync(
            PreparationMethod.Manual,
            CommitMarkKnownAsync,
            LoadNextAsync);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, markKnownCalls);
        Assert.AreEqual(1, loadNextCalls);
    }

    [TestMethod]
    public async Task Requirement13_Exclude_DoesNotQueryLearningReadiness()
    {
        var coordinator = new PreparationProgressionCoordinator();
        var excludeCalls = 0;
        var loadNextCalls = 0;

        Task CommitExcludeAsync()
        {
            excludeCalls++;
            return Task.CompletedTask;
        }

        Task LoadNextAsync(PreparationMethod method)
        {
            loadNextCalls++;
            return Task.CompletedTask;
        }

        var succeeded = await coordinator.CommitAndProgressAsync(
            PreparationMethod.Manual,
            CommitExcludeAsync,
            LoadNextAsync);

        Assert.IsTrue(succeeded);
        Assert.IsTrue(coordinator.SavedSuccessfully);
        Assert.IsFalse(coordinator.ProgressionFailed);
        Assert.AreEqual(1, excludeCalls);
        Assert.AreEqual(1, loadNextCalls);
    }

    [TestMethod]
    public void Requirement14_NoPageEntryOrRenderTimeReadinessQuery()
    {
        var markup = LoadPrepareWordsMarkup();

        var onInit = markup.IndexOf("OnInitializedAsync()", StringComparison.Ordinal);
        if (onInit >= 0)
        {
            var initBody = markup[onInit..markup.IndexOf('}', onInit)];
            Assert.IsFalse(initBody.Contains("GetPreparationReadinessAsync", StringComparison.Ordinal));
        }

        var loadAsync = markup.IndexOf("private async Task LoadAsync()", StringComparison.Ordinal);
        Assert.IsTrue(loadAsync >= 0);
        var loadAsyncEnd = markup.IndexOf("private async Task", loadAsync + 1, StringComparison.Ordinal);
        var loadAsyncBody = markup[loadAsync..(loadAsyncEnd > loadAsync ? loadAsyncEnd : markup.Length)];
        Assert.IsFalse(loadAsyncBody.Contains("GetPreparationReadinessAsync", StringComparison.Ordinal),
            "LoadAsync must not query Learning readiness on page load.");
    }

    [TestMethod]
    public void Requirement15_16_17_PrepareWords_LearnNavigationOccursOnlyFromAcceptPostCommit()
    {
        var markup = LoadPrepareWordsMarkup();

        Assert.IsTrue(markup.Contains("Navigation.NavigateTo(\"/learn\")", StringComparison.Ordinal));

        var skipStart = markup.IndexOf("private async Task SkipAsync()", StringComparison.Ordinal);
        var skipEnd = markup.IndexOf("private async Task", skipStart + 1, StringComparison.Ordinal);
        var skipBody = markup[skipStart..skipEnd];
        Assert.IsFalse(skipBody.Contains("/learn", StringComparison.Ordinal), "Skip must not navigate to /learn.");

        var dispStart = markup.IndexOf("private async Task ConfirmDispositionAsync()", StringComparison.Ordinal);
        var dispEnd = markup.IndexOf("private async Task", dispStart + 1, StringComparison.Ordinal);
        var dispBody = markup[dispStart..dispEnd];
        Assert.IsFalse(dispBody.Contains("/learn", StringComparison.Ordinal), "Dispositions must not navigate to /learn.");

        Assert.IsFalse(markup.Contains("PreparationService.EndAsync", StringComparison.Ordinal),
            "PrepareWords must not end/cancel preparation session when transitioning to learning.");
    }
}
