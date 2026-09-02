namespace Klexir.Workflow;

/// <summary>An ordered, named chain of steps built via <see cref="Workflow.Define{TRequest}"/>, ready to run against a <see cref="WorkflowEngine"/>.</summary>
public sealed class WorkflowDefinition<TRequest>
{
    public string Name { get; }

    internal IReadOnlyList<IErasedStep> Steps { get; }

    /// <summary>Index-aligned with <see cref="Steps"/>; null where that step has no registered compensation.</summary>
    internal IReadOnlyList<IErasedCompensation?> Compensations { get; }

    internal WorkflowDefinition(string name, IReadOnlyList<IErasedStep> steps, IReadOnlyList<IErasedCompensation?> compensations)
    {
        Name = name;
        Steps = steps;
        Compensations = compensations;
    }
}
