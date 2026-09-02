using MonadicSharp;

namespace Klexir.Workflow.Abstractions;

/// <summary>One named, typed transformation in a workflow. Failure is communicated via <see cref="Result{T}"/>, never a thrown exception.</summary>
public interface IWorkflowStep<in TIn, TOut>
{
    string Name { get; }

    Task<Result<TOut>> ExecuteAsync(TIn input, CancellationToken cancellationToken);
}
