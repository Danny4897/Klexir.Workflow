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
