using KnownFirst.Models;

namespace KnownFirst.Services.Study;

public interface IWorkflowStateService
{
    Task<WorkflowSnapshot> GetSnapshotAsync();
}
