using Klexir.Workflow.Abstractions;
using MonadicSharp;

namespace Klexir.Workflow;

/// <summary>Type-erased adapter so a heterogeneous chain of typed steps can be stored in one list.</summary>
internal interface IErasedStep
{
    string Name { get; }

    Task<Result<object>> ExecuteAsync(object input, CancellationToken cancellationToken);
}

internal sealed class ErasedStep<TIn, TOut>(IWorkflowStep<TIn, TOut> step) : IErasedStep
{
    public string Name => step.Name;

    public async Task<Result<object>> ExecuteAsync(object input, CancellationToken cancellationToken)
    {
        var result = await step.ExecuteAsync((TIn)input, cancellationToken).ConfigureAwait(false);
        return result.Map(value => (object)value!);
    }
}

internal sealed class DelegateStep<TIn, TOut>(string name, Func<TIn, Task<Result<TOut>>> execute) : IWorkflowStep<TIn, TOut>
{
    public string Name { get; } = name;

    public Task<Result<TOut>> ExecuteAsync(TIn input, CancellationToken cancellationToken) => execute(input);
}

/// <summary>
/// Wraps a step with retry (fixed delay) and/or a timeout. Timeout bounds the caller's wait via
/// <c>Task.WaitAsync</c> rather than requiring the step itself to observe cancellation, so it also times out a
/// step whose delegate never checks the token.
/// </summary>
internal sealed class ResilientStep<TIn, TOut>(IWorkflowStep<TIn, TOut> inner, WorkflowStepOptions options) : IWorkflowStep<TIn, TOut>
{
    public string Name => inner.Name;

    public async Task<Result<TOut>> ExecuteAsync(TIn input, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            var result = await InvokeWithTimeoutAsync(input, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess || attempt >= options.MaxAttempts)
            {
                return result;
            }

            if (options.RetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<Result<TOut>> InvokeWithTimeoutAsync(TIn input, CancellationToken cancellationToken)
    {
        if (options.Timeout is not { } timeout)
        {
            return await inner.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await inner.ExecuteAsync(input, cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Result<TOut>.Failure(Error.Create($"Step '{inner.Name}' timed out after {timeout}."));
        }
    }
}

/// <summary>Type-erased saga compensation, paired by index with the step that produced the value it rolls back.</summary>
internal interface IErasedCompensation
{
    Task CompensateAsync(object producedValue, CancellationToken cancellationToken);
}

internal sealed class Compensation<TValue>(Func<TValue, Task<Result<Unit>>> compensate) : IErasedCompensation
{
    public Task CompensateAsync(object producedValue, CancellationToken cancellationToken) =>
        compensate((TValue)producedValue);
}

/// <summary>
/// Runs every branch concurrently against the same input. Join is deterministic by declaration order, not
/// completion order: on multiple failures, the first branch (by declaration) wins. On success the input passes
/// through unchanged — branches are side effects, not value producers.
/// </summary>
internal sealed class ParallelStep<TIn>(IReadOnlyList<(string Name, Func<TIn, Task<Result<Unit>>> Execute)> branches) : IErasedStep
{
    public string Name => "Parallel";

    public async Task<Result<object>> ExecuteAsync(object input, CancellationToken cancellationToken)
    {
        var typedInput = (TIn)input;
        var tasks = branches.Select(branch => branch.Execute(typedInput)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var task in tasks)
        {
            var result = await task.ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result<object>.Failure(result.Error);
            }
        }

        return Result<object>.Success(input);
    }
}

/// <summary>Evaluates a predicate against the input and runs exactly one of two steps — never both.</summary>
internal sealed class BranchStep<TIn, TOut>(
    Func<TIn, bool> predicate,
    IWorkflowStep<TIn, TOut> whenTrue,
    IWorkflowStep<TIn, TOut> whenFalse) : IErasedStep
{
    public string Name => "Branch";

    public async Task<Result<object>> ExecuteAsync(object input, CancellationToken cancellationToken)
    {
        var typedInput = (TIn)input;
        var chosen = predicate(typedInput) ? whenTrue : whenFalse;
        var result = await chosen.ExecuteAsync(typedInput, cancellationToken).ConfigureAwait(false);
        return result.Map(value => (object)value!);
    }
}
