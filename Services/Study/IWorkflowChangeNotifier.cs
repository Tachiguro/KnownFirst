namespace KnownFirst.Services.Study;

public interface IWorkflowChangeNotifier
{
    event EventHandler? Changed;

    void NotifyChanged();
}
