using System.Collections.Concurrent;
using Klexir.Workflow.Abstractions;
using MonadicSharp;

namespace Klexir.Workflow;

/// <summary>
/// Process-local checkpoint store. Demonstrates the durability mechanism (survives an engine object being
/// recreated within the same process); a real durable store would persist <see cref="WorkflowCheckpoint"/>
/// to disk or a database, which needs serializing <see cref="WorkflowCheckpoint.CurrentValue"/> — not solved here.
/// </summary>
public sealed class InMemoryWorkflowStore : IWorkflowStore
{
    private readonly ConcurrentDictionary<WorkflowInstanceId, WorkflowCheckpoint> _checkpoints = new();

    public Task<Result<Unit>> SaveAsync(WorkflowInstanceId instanceId, WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        _checkpoints[instanceId] = checkpoint;
        return Task.FromResult(Result<Unit>.Success(Unit.Value));
    }

    public Task<Result<WorkflowCheckpoint>> LoadAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_checkpoints.TryGetValue(instanceId, out var checkpoint)
            ? Result<WorkflowCheckpoint>.Success(checkpoint)
            : Result<WorkflowCheckpoint>.Failure(Error.NotFound("WorkflowCheckpoint", instanceId.ToString())));
}
