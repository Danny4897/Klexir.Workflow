namespace Klexir.Workflow.Abstractions;

/// <summary>Per-step resilience: retry with fixed delay, and/or a timeout. Defaults (MaxAttempts=1, no timeout) run a step exactly once, unbounded.</summary>
public sealed record WorkflowStepOptions
{
    public int MaxAttempts { get; init; } = 1;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.Zero;

    public TimeSpan? Timeout { get; init; }
}
