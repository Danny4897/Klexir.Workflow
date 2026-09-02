using System.Collections.Concurrent;
using Klexir.Workflow.Abstractions;
using MonadicSharp;

namespace Klexir.Workflow;

/// <summary>
/// Runs a <see cref="WorkflowDefinition{TRequest}"/>, tracking instance status. With a <see cref="IWorkflowStore"/>
/// configured, every completed step is checkpointed atomically, and <see cref="ResumeAsync{TRequest}"/> continues
/// a run from its last checkpoint — e.g. after this engine object is recreated.
/// </summary>
public sealed class WorkflowEngine(IWorkflowStore? store = null)
{
    private readonly ConcurrentDictionary<WorkflowInstanceId, WorkflowStatus> _instances = new();

    public async Task<Result<WorkflowInstanceId>> StartAsync<TRequest>(
        WorkflowDefinition<TRequest> definition, TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);

        var instanceId = WorkflowInstanceId.NewId();
        _instances[instanceId] = WorkflowStatus.Running;

        await RunFromAsync(definition, instanceId, 0, request, cancellationToken).ConfigureAwait(false);
        return Result<WorkflowInstanceId>.Success(instanceId);
    }

    /// <summary>
    /// Continues a checkpointed instance from where it left off. Only steps completed <em>during this resumed
    /// run</em> are tracked for compensation — steps that finished before the checkpoint are not re-compensated
    /// if a later step now fails.
    /// </summary>
    public async Task<Result<Unit>> ResumeAsync<TRequest>(
        WorkflowDefinition<TRequest> definition, WorkflowInstanceId instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (store is null)
        {
            return Result<Unit>.Failure(Error.Create("No workflow store configured; nothing to resume from."));
        }

        var loaded = await store.LoadAsync(instanceId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return Result<Unit>.Failure(loaded.Error);
        }

        var checkpoint = loaded.Value;
        if (checkpoint.DefinitionName != definition.Name)
        {
            return Result<Unit>.Failure(Error.Create(
                $"Checkpoint belongs to workflow '{checkpoint.DefinitionName}', not '{definition.Name}'."));
        }

        if (checkpoint.Status != WorkflowStatus.Running)
        {
            return Result<Unit>.Failure(Error.Create($"Instance is {checkpoint.Status}, not resumable."));
        }

        _instances[instanceId] = WorkflowStatus.Running;
        await RunFromAsync(definition, instanceId, checkpoint.CompletedStepCount, checkpoint.CurrentValue, cancellationToken)
            .ConfigureAwait(false);
        return Result<Unit>.Success(Unit.Value);
    }

    public Task<Result<WorkflowStatus>> GetStatusAsync(WorkflowInstanceId instanceId) =>
        Task.FromResult(_instances.TryGetValue(instanceId, out var status)
            ? Result<WorkflowStatus>.Success(status)
            : Result<WorkflowStatus>.Failure(Error.NotFound("WorkflowInstance", instanceId.ToString())));

    private async Task RunFromAsync<TRequest>(
        WorkflowDefinition<TRequest> definition, WorkflowInstanceId instanceId, int startStepIndex, object current, CancellationToken cancellationToken)
    {
        var completed = new List<(int StepIndex, object Value)>();

        for (var i = startStepIndex; i < definition.Steps.Count; i++)
        {
            var outcome = await Try.ExecuteAsync(() => definition.Steps[i].ExecuteAsync(current, cancellationToken)).ConfigureAwait(false);
            var stepResult = outcome.IsSuccess ? outcome.Value : Result<object>.Failure(outcome.Error);

            if (stepResult.IsFailure)
            {
                await CompensateAsync(definition, completed, cancellationToken).ConfigureAwait(false);
                _instances[instanceId] = WorkflowStatus.Failed;
                await CheckpointAsync(definition, instanceId, i, current, WorkflowStatus.Failed, cancellationToken).ConfigureAwait(false);
                return;
            }

            current = stepResult.Value;
            completed.Add((i, current));
            await CheckpointAsync(definition, instanceId, i + 1, current, WorkflowStatus.Running, cancellationToken).ConfigureAwait(false);
        }

        _instances[instanceId] = WorkflowStatus.Completed;
        await CheckpointAsync(definition, instanceId, definition.Steps.Count, current, WorkflowStatus.Completed, cancellationToken).ConfigureAwait(false);
    }

    private Task CheckpointAsync<TRequest>(
        WorkflowDefinition<TRequest> definition, WorkflowInstanceId instanceId, int completedStepCount, object value, WorkflowStatus status, CancellationToken cancellationToken) =>
        store?.SaveAsync(instanceId, new WorkflowCheckpoint(definition.Name, completedStepCount, value, status), cancellationToken)
            ?? Task.CompletedTask;

    /// <summary>Runs compensations for every step completed in this run, in reverse order. Best-effort: a compensation's own failure is swallowed — the primary failure already dominates the outcome.</summary>
    private static async Task CompensateAsync<TRequest>(
        WorkflowDefinition<TRequest> definition, IReadOnlyList<(int StepIndex, object Value)> completed, CancellationToken cancellationToken)
    {
        for (var i = completed.Count - 1; i >= 0; i--)
        {
            var (stepIndex, value) = completed[i];
            var compensation = definition.Compensations[stepIndex];
            if (compensation is null)
            {
                continue;
            }

            try
            {
                await compensation.CompensateAsync(value, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort rollback — a compensation failure doesn't change an already-failed outcome.
            }
        }
    }
}
