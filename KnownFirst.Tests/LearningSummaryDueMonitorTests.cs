using KnownFirst.Components.Pages;
using KnownFirst.Core.Learning;
using KnownFirst.Models;
using KnownFirst.Services.Study;
using KnownFirst.Tests;

namespace KnownFirst.Tests;

[TestClass]
public sealed class LearningSummaryDueMonitorTests
{
    private static readonly DateTime Epoch = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    // =========================================================================
    // RED Contract Tests: Verify Learn.razor markup and component contracts
    // =========================================================================

    [TestMethod]
    public void Learning_Summary_RequiresClockDrivenDueLifecycleAndDisposal()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // RED 1: Verify Learn.razor injects IClock, defines due availability state, and implements IAsyncDisposable
        Assert.Contains("@inject IClock Clock", markup,
            "Learn.razor must inject authoritative IClock for due monitoring.");
        Assert.Contains("@implements IAsyncDisposable", markup,
            "Learn.razor must implement IAsyncDisposable for deterministic monitor cleanup.");
        Assert.Contains("_isScheduledReviewAvailable", markup,
            "Learn.razor must track scheduled review availability state.");
        Assert.Contains("StartDueReviewAsync", markup,
            "Learn.razor must declare dedicated StartDueReviewAsync action handler.");
    }

    [TestMethod]
    public void Learning_Summary_DueAvailabilityRendersPrimaryLearnActionAndSuppressesNothingDue()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // RED 2: When scheduled review is available, Navigation_Learn primary button is rendered and Learn_NothingDue is suppressed
        Assert.Contains("@if (_isScheduledReviewAvailable)", markup,
            "Learn.razor must conditionally branch on _isScheduledReviewAvailable.");
        Assert.Contains("@onclick=\"StartDueReviewAsync\">@Localizer[\"Navigation_Learn\"]", markup,
            "Learn.razor must render explicit primary button with Navigation_Learn key invoking StartDueReviewAsync.");

        var summarySectionStart = markup.IndexOf("else if (_summary is not null)", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, summarySectionStart, "Summary section must exist.");

        var summarySection = markup[summarySectionStart..];
        var nothingDueIndex = summarySection.IndexOf("@Localizer[\"Learn_NothingDue\"]", StringComparison.Ordinal);
        var availableIndex = summarySection.IndexOf("_isScheduledReviewAvailable", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, nothingDueIndex, "Learn_NothingDue must exist in the fallback branch.");
        Assert.IsGreaterThanOrEqualTo(0, availableIndex, "_isScheduledReviewAvailable must exist before fallback.");
        Assert.IsGreaterThan(availableIndex, nothingDueIndex,
            "The Learn_NothingDue fallback branch must come after the _isScheduledReviewAvailable branch.");
    }

    [TestMethod]
    public void Learning_Summary_DueActionInvokesExistingLoadAuthorityWithoutDirectScheduling()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // RED 3: Dedicated StartDueReviewAsync must guard re-entry, cancel monitor, and call LoadAsync
        var actionMethodIndex = markup.IndexOf("StartDueReviewAsync()", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, actionMethodIndex,
            "StartDueReviewAsync method must be defined in Learn.razor.");

        var methodBody = markup[actionMethodIndex..Math.Min(markup.Length, actionMethodIndex + 500)];
        Assert.Contains("CancelDueMonitor();", methodBody,
            "StartDueReviewAsync must cancel any active due monitor.");
        Assert.Contains("await LoadAsync();", methodBody,
            "StartDueReviewAsync must route through the existing LoadAsync authority.");
        Assert.DoesNotContain("InsertSession", methodBody,
            "StartDueReviewAsync must not directly create sessions.");
        Assert.DoesNotContain("database", methodBody,
            "StartDueReviewAsync must not access database directly.");
    }

    [TestMethod]
    public void Component_DueActionContract_UsesExistingLoadAuthorityAndBlocksReentry()
    {
        var markup = UiWorkflowContractTests.LoadUi("Learn.razor");

        // Verify guarded re-entry: _isBusy || _isLoading check
        Assert.Contains("if (_isBusy || _isLoading)", markup,
            "StartDueReviewAsync must guard against re-entry while busy or loading.");
    }

    [TestMethod]
    public void LearningSummaryDueMonitor_ProductionFileMatchesTestedMonitor()
    {
        string prodSource;
        try
        {
            prodSource = UiWorkflowContractTests.LoadUi("LearningSummaryDueMonitor.cs");
        }
        catch (FileNotFoundException)
        {
            Assert.Fail("Components/Pages/LearningSummaryDueMonitor.cs helper file has not been created yet.");
            return;
        }

        Assert.Contains("public sealed class LearningSummaryDueMonitor", prodSource,
            "LearningSummaryDueMonitor.cs must declare public sealed class LearningSummaryDueMonitor.");
        Assert.Contains("IAsyncDisposable", prodSource,
            "LearningSummaryDueMonitor must implement IAsyncDisposable.");
        Assert.Contains("IDisposable", prodSource,
            "LearningSummaryDueMonitor must implement IDisposable.");
        Assert.Contains("public static bool IsDue(IClock clock, DateTime? nextDueAtUtc)", prodSource,
            "LearningSummaryDueMonitor must expose static IsDue helper.");
        Assert.AreEqual(
            "KnownFirst.Components.Pages.LearningSummaryDueMonitor",
            typeof(LearningSummaryDueMonitor).FullName,
            "Behavioral tests must execute against production KnownFirst.Components.Pages.LearningSummaryDueMonitor type rather than a test-side duplicate.");
    }

    // =========================================================================
    // Lifecycle Unit Tests for LearningSummaryDueMonitor
    // =========================================================================

    [TestMethod]
    public async Task Monitor_AlreadyDue_CompletesImmediatelyWithoutDelay()
    {
        var clock = new FakeClock(Epoch);
        var pastDueUtc = Epoch.AddMinutes(-5);

        var dueCalled = false;
        var delayCalled = false;

        Assert.IsTrue(LearningSummaryDueMonitor.IsDue(clock, pastDueUtc), "IsDue must be true when due timestamp is in the past.");

        var monitor = new LearningSummaryDueMonitor(
            clock,
            pastDueUtc,
            onDueAsync: () =>
            {
                dueCalled = true;
                return Task.CompletedTask;
            },
            delayAsync: (delay, ct) =>
            {
                delayCalled = true;
                return Task.CompletedTask;
            });

        await monitor.DisposeAsync();

        Assert.IsTrue(dueCalled, "onDueAsync must be called immediately for already-due cards.");
        Assert.IsFalse(delayCalled, "Delay must not be invoked when card is already due.");
    }

    [TestMethod]
    public async Task Monitor_FutureDue_TransitionsOnlyWhenDueInstantIsReached()
    {
        var clock = new FakeClock(Epoch);
        var futureDueUtc = Epoch.AddMinutes(10);

        Assert.IsFalse(LearningSummaryDueMonitor.IsDue(clock, futureDueUtc), "IsDue must be false when due timestamp is in the future.");

        var dueTcs = new TaskCompletionSource<bool>();
        var delayCalled = false;

        var monitor = new LearningSummaryDueMonitor(
            clock,
            futureDueUtc,
            onDueAsync: () =>
            {
                dueTcs.TrySetResult(true);
                return Task.CompletedTask;
            },
            delayAsync: async (delay, ct) =>
            {
                delayCalled = true;
                // Deterministically advance clock to the due timestamp during delay
                clock.UtcNow = futureDueUtc;
                await Task.Yield();
            });

        var completed = await Task.WhenAny(dueTcs.Task, Task.Delay(2000));
        await monitor.DisposeAsync();

        Assert.IsTrue(delayCalled, "Delay should have been called for future due card.");
        Assert.AreSame(dueTcs.Task, completed, "Monitor should transition to due once clock reaches due instant.");
        Assert.IsTrue(await dueTcs.Task, "onDueAsync must succeed.");
    }

    [TestMethod]
    public async Task Monitor_CancellationBeforeDue_EndsCleanlyWithoutCallback()
    {
        var clock = new FakeClock(Epoch);
        var futureDueUtc = Epoch.AddMinutes(10);

        var callbackCalled = false;
        var delayStartedTcs = new TaskCompletionSource<bool>();

        var monitor = new LearningSummaryDueMonitor(
            clock,
            futureDueUtc,
            onDueAsync: () =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            },
            delayAsync: async (delay, ct) =>
            {
                delayStartedTcs.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            });

        // Wait until monitor is inside delay
        await delayStartedTcs.Task;

        // Cancel / Dispose before due
        await monitor.DisposeAsync();

        // Advance clock past due
        clock.UtcNow = Epoch.AddMinutes(15);
        await Task.Delay(50);

        Assert.IsFalse(callbackCalled, "Callback must not be invoked after cancellation.");
    }

    [TestMethod]
    public async Task Monitor_Replacement_PreventsOldMonitorFromTriggeringCurrentState()
    {
        var clock = new FakeClock(Epoch);
        var firstDueUtc = Epoch.AddMinutes(5);
        var secondDueUtc = Epoch.AddMinutes(15);

        var firstCallbackCount = 0;
        var secondCallbackCount = 0;

        var firstDelayTcs = new TaskCompletionSource<bool>();
        var secondDelayTcs = new TaskCompletionSource<bool>();

        // Start first monitor
        var monitor1 = new LearningSummaryDueMonitor(
            clock,
            firstDueUtc,
            onDueAsync: () =>
            {
                Interlocked.Increment(ref firstCallbackCount);
                return Task.CompletedTask;
            },
            delayAsync: async (delay, ct) =>
            {
                firstDelayTcs.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            });

        await firstDelayTcs.Task;

        // Replace with second monitor: dispose first monitor
        await monitor1.DisposeAsync();

        var monitor2 = new LearningSummaryDueMonitor(
            clock,
            secondDueUtc,
            onDueAsync: () =>
            {
                Interlocked.Increment(ref secondCallbackCount);
                return Task.CompletedTask;
            },
            delayAsync: async (delay, ct) =>
            {
                secondDelayTcs.TrySetResult(true);
                clock.UtcNow = secondDueUtc;
                await Task.Yield();
            });

        await secondDelayTcs.Task;
        await Task.Delay(100);
        await monitor2.DisposeAsync();

        Assert.AreEqual(0, firstCallbackCount, "Old replaced monitor must never fire.");
        Assert.AreEqual(1, secondCallbackCount, "New monitor must fire when due.");
    }

    [TestMethod]
    public async Task Monitor_Disposal_PreventsCallbacksAfterDisposal()
    {
        var clock = new FakeClock(Epoch);
        var futureDueUtc = Epoch.AddMinutes(10);
        var callbackCount = 0;
        var delayStartedTcs = new TaskCompletionSource<bool>();

        var monitor = new LearningSummaryDueMonitor(
            clock,
            futureDueUtc,
            onDueAsync: () =>
            {
                Interlocked.Increment(ref callbackCount);
                return Task.CompletedTask;
            },
            delayAsync: async (delay, ct) =>
            {
                delayStartedTcs.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            });

        await delayStartedTcs.Task;
        await monitor.DisposeAsync();

        // Advance clock past due
        clock.UtcNow = Epoch.AddMinutes(20);
        await Task.Delay(50);

        Assert.AreEqual(0, callbackCount, "Disposed monitor must never fire callback.");
    }

    [TestMethod]
    public async Task Monitor_ReachingDue_DoesNotInvokeLearningService()
    {
        var clock = new FakeClock(Epoch);
        var pastDueUtc = Epoch.AddMinutes(-1);
        var onDueFired = false;

        var monitor = new LearningSummaryDueMonitor(
            clock,
            pastDueUtc,
            onDueAsync: () =>
            {
                onDueFired = true;
                return Task.CompletedTask;
            });

        await monitor.DisposeAsync();

        Assert.IsTrue(onDueFired, "onDueAsync should fire when due is reached.");
        // Monitor has no reference to ILearningService and performs no background GetOrStartAsync
    }
}
