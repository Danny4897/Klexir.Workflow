using MonadicSharp;

namespace Klexir.Workflow.Abstractions;

/// <summary>Durable storage for workflow checkpoints, enabling resume after an engine restart.</summary>
public interface IWorkflowStore
{
    Task<Result<Unit>> SaveAsync(WorkflowInstanceId instanceId, WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default);

    Task<Result<WorkflowCheckpoint>> LoadAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default);
}
