using System.Collections.Concurrent;
using Klexir.Workflow.Abstractions;
using MonadicSharp;

namespace Klexir.Workflow;

/// <summary>
/// Runs a <see cref="WorkflowDefinition{TRequest}"/> to completion synchronously, tracking instance status.
/// Durable checkpoints and resume-after-failure are a later increment; this engine holds no state across restarts.
/// </summary>
public sealed class WorkflowEngine
{
    private readonly ConcurrentDictionary<WorkflowInstanceId, WorkflowStatus> _instances = new();

    public async Task<Result<WorkflowInstanceId>> StartAsync<TRequest>(
        WorkflowDefinition<TRequest> definition, TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);

        var instanceId = WorkflowInstanceId.NewId();
        _instances[instanceId] = WorkflowStatus.Running;

        object current = request;

        foreach (var step in definition.Steps)
        {
            var outcome = await Try.ExecuteAsync(() => step.ExecuteAsync(current, cancellationToken)).ConfigureAwait(false);
            var stepResult = outcome.IsSuccess ? outcome.Value : Result<object>.Failure(outcome.Error);

            if (stepResult.IsFailure)
            {
                _instances[instanceId] = WorkflowStatus.Failed;
                return Result<WorkflowInstanceId>.Success(instanceId);
            }

            current = stepResult.Value;
        }

        _instances[instanceId] = WorkflowStatus.Completed;
        return Result<WorkflowInstanceId>.Success(instanceId);
    }

    public Task<Result<WorkflowStatus>> GetStatusAsync(WorkflowInstanceId instanceId) =>
        Task.FromResult(_instances.TryGetValue(instanceId, out var status)
            ? Result<WorkflowStatus>.Success(status)
            : Result<WorkflowStatus>.Failure(Error.NotFound("WorkflowInstance", instanceId.ToString())));
}
