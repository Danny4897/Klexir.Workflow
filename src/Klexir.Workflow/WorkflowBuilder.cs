using Klexir.Workflow.Abstractions;
using MonadicSharp;

namespace Klexir.Workflow;

/// <summary>Entry point for the fluent workflow builder: <c>Workflow.Define&lt;TRequest&gt;("Name").Step(...).Build()</c>.</summary>
public static class Workflow
{
    public static WorkflowBuilder<TRequest, TRequest> Define<TRequest>(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new WorkflowBuilder<TRequest, TRequest>(name, [], []);
    }
}

/// <summary>
/// Fluent, type-safe step accumulator. <typeparamref name="TRequest"/> stays fixed at the workflow's input type;
/// <typeparamref name="TCurrent"/> tracks the type produced by the last added step.
/// </summary>
public sealed class WorkflowBuilder<TRequest, TCurrent>
{
    private readonly string _name;
    private readonly IReadOnlyList<IErasedStep> _steps;
    private readonly IReadOnlyList<IErasedCompensation?> _compensations;

    internal WorkflowBuilder(string name, IReadOnlyList<IErasedStep> steps, IReadOnlyList<IErasedCompensation?> compensations)
    {
        _name = name;
        _steps = steps;
        _compensations = compensations;
    }

    public WorkflowBuilder<TRequest, TNext> Step<TNext>(string stepName, Func<TCurrent, Task<Result<TNext>>> execute)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepName);
        ArgumentNullException.ThrowIfNull(execute);

        return Step<TNext>(new DelegateStep<TCurrent, TNext>(stepName, execute));
    }

    /// <summary>Adds a step wrapped with retry (fixed delay) and/or a timeout — see <see cref="WorkflowStepOptions"/>.</summary>
    public WorkflowBuilder<TRequest, TNext> Step<TNext>(
        string stepName, Func<TCurrent, Task<Result<TNext>>> execute, WorkflowStepOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepName);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(options);

        return Step<TNext>(new ResilientStep<TCurrent, TNext>(new DelegateStep<TCurrent, TNext>(stepName, execute), options));
    }

    public WorkflowBuilder<TRequest, TNext> Step<TNext>(IWorkflowStep<TCurrent, TNext> step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new WorkflowBuilder<TRequest, TNext>(
            _name, [.. _steps, new ErasedStep<TCurrent, TNext>(step)], [.. _compensations, null]);
    }

    /// <summary>
    /// Registers a saga compensation for the step most recently added, run with the value that step produced.
    /// On a later step's failure, the engine runs compensations for every completed step in reverse order.
    /// </summary>
    public WorkflowBuilder<TRequest, TCurrent> Compensate(Func<TCurrent, Task<Result<Unit>>> compensate)
    {
        ArgumentNullException.ThrowIfNull(compensate);

        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("Compensate must follow a Step.");
        }

        var compensations = _compensations.ToArray();
        compensations[^1] = new Compensation<TCurrent>(compensate);
        return new WorkflowBuilder<TRequest, TCurrent>(_name, _steps, compensations);
    }

    /// <summary>Runs every branch concurrently against the current value; the current value flows unchanged into the next step. See <see cref="ParallelStep{TIn}"/> for join/failure semantics.</summary>
    public WorkflowBuilder<TRequest, TCurrent> Parallel(params (string Name, Func<TCurrent, Task<Result<Unit>>> Execute)[] branches)
    {
        ArgumentNullException.ThrowIfNull(branches);
        if (branches.Length == 0)
        {
            throw new ArgumentException("At least one branch is required.", nameof(branches));
        }

        return new WorkflowBuilder<TRequest, TCurrent>(
            _name, [.. _steps, new ParallelStep<TCurrent>(branches)], [.. _compensations, null]);
    }

    /// <summary>Runs exactly one of <paramref name="whenTrue"/>/<paramref name="whenFalse"/>, chosen by <paramref name="predicate"/>.</summary>
    public WorkflowBuilder<TRequest, TNext> Branch<TNext>(
        Func<TCurrent, bool> predicate,
        Func<TCurrent, Task<Result<TNext>>> whenTrue,
        Func<TCurrent, Task<Result<TNext>>> whenFalse)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(whenTrue);
        ArgumentNullException.ThrowIfNull(whenFalse);

        var step = new BranchStep<TCurrent, TNext>(
            predicate,
            new DelegateStep<TCurrent, TNext>("Branch:True", whenTrue),
            new DelegateStep<TCurrent, TNext>("Branch:False", whenFalse));

        return new WorkflowBuilder<TRequest, TNext>(_name, [.. _steps, step], [.. _compensations, null]);
    }

    public WorkflowDefinition<TRequest> Build() => new(_name, _steps, _compensations);
}
