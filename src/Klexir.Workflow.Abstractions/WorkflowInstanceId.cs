namespace Klexir.Workflow.Abstractions;

/// <summary>Identity of one running or completed workflow instance.</summary>
public readonly record struct WorkflowInstanceId(Guid Value)
{
    public static WorkflowInstanceId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
