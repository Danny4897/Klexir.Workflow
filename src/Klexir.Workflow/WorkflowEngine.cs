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
        var completedValues = new List<object>();

        for (var i = 0; i < definition.Steps.Count; i++)
        {
            var outcome = await Try.ExecuteAsync(() => definition.Steps[i].ExecuteAsync(current, cancellationToken)).ConfigureAwait(false);
            var stepResult = outcome.IsSuccess ? outcome.Value : Result<object>.Failure(outcome.Error);

            if (stepResult.IsFailure)
            {
                await CompensateAsync(definition, completedValues, cancellationToken).ConfigureAwait(false);
                _instances[instanceId] = WorkflowStatus.Failed;
                return Result<WorkflowInstanceId>.Success(instanceId);
            }

            current = stepResult.Value;
            completedValues.Add(current);
        }

        _instances[instanceId] = WorkflowStatus.Completed;
        return Result<WorkflowInstanceId>.Success(instanceId);
    }

    public Task<Result<WorkflowStatus>> GetStatusAsync(WorkflowInstanceId instanceId) =>
        Task.FromResult(_instances.TryGetValue(instanceId, out var status)
            ? Result<WorkflowStatus>.Success(status)
            : Result<WorkflowStatus>.Failure(Error.NotFound("WorkflowInstance", instanceId.ToString())));

    /// <summary>Runs compensations for every completed step in reverse order. Best-effort: a compensation's own failure is swallowed — the primary failure already dominates the outcome.</summary>
    private static async Task CompensateAsync<TRequest>(
        WorkflowDefinition<TRequest> definition, IReadOnlyList<object> completedValues, CancellationToken cancellationToken)
    {
        for (var i = completedValues.Count - 1; i >= 0; i--)
        {
            var compensation = definition.Compensations[i];
            if (compensation is null)
            {
                continue;
            }

            try
            {
                await compensation.CompensateAsync(completedValues[i], cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort rollback — a compensation failure doesn't change an already-failed outcome.
            }
        }
    }
}
