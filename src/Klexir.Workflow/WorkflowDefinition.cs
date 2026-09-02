namespace Klexir.Workflow;

/// <summary>An ordered, named chain of steps built via <see cref="Workflow.Define{TRequest}"/>, ready to run against a <see cref="WorkflowEngine"/>.</summary>
public sealed class WorkflowDefinition<TRequest>
{
    public string Name { get; }

    internal IReadOnlyList<IErasedStep> Steps { get; }

    internal WorkflowDefinition(string name, IReadOnlyList<IErasedStep> steps)
    {
        Name = name;
        Steps = steps;
    }
}
