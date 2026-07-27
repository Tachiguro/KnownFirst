namespace KnownFirst.Services.Study;

public sealed class WorkflowChangeNotifier : IWorkflowChangeNotifier
{
    public event EventHandler? Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
