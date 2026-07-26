using KnownFirst.Services.Study;

namespace KnownFirst.Tests;

[TestClass]
public sealed class WorkflowChangeNotifierTests
{
    [TestMethod]
    public void NotifyChanged_WithNoSubscribers_DoesNotThrow()
    {
        IWorkflowChangeNotifier notifier = new WorkflowChangeNotifier();

        notifier.NotifyChanged();
    }

    [TestMethod]
    public void NotifyChanged_InvokesEachSubscribedHandlerExactlyOnce()
    {
        IWorkflowChangeNotifier notifier = new WorkflowChangeNotifier();
        var callCount = 0;
        void Handler(object? sender, EventArgs eventArgs) => callCount++;
        notifier.Changed += Handler;

        notifier.NotifyChanged();

        Assert.AreEqual(1, callCount);
        notifier.Changed -= Handler;
    }

    [TestMethod]
    public void NotifyChanged_AfterUnsubscribing_DoesNotInvokeTheHandler()
    {
        IWorkflowChangeNotifier notifier = new WorkflowChangeNotifier();
        var callCount = 0;
        void Handler(object? sender, EventArgs eventArgs) => callCount++;
        notifier.Changed += Handler;
        notifier.Changed -= Handler;

        notifier.NotifyChanged();

        Assert.AreEqual(0, callCount);
    }

    [TestMethod]
    public void NotifyChanged_WithMultipleSubscribers_InvokesEveryOne()
    {
        IWorkflowChangeNotifier notifier = new WorkflowChangeNotifier();
        var firstCallCount = 0;
        var secondCallCount = 0;
        void First(object? sender, EventArgs eventArgs) => firstCallCount++;
        void Second(object? sender, EventArgs eventArgs) => secondCallCount++;
        notifier.Changed += First;
        notifier.Changed += Second;

        notifier.NotifyChanged();

        Assert.AreEqual(1, firstCallCount);
        Assert.AreEqual(1, secondCallCount);
        notifier.Changed -= First;
        notifier.Changed -= Second;
    }
}
