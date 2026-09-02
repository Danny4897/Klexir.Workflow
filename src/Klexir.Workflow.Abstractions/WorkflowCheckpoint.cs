namespace Klexir.Workflow.Abstractions;

/// <summary>
/// Durable snapshot of one workflow instance, saved atomically after every step. <paramref name="CompletedStepCount"/>
/// is how many steps have finished (0-based next-step index); <paramref name="CurrentValue"/> is what the most
/// recently completed step produced (or the original request, for a checkpoint before any step has run).
/// </summary>
public sealed record WorkflowCheckpoint(string DefinitionName, int CompletedStepCount, object CurrentValue, WorkflowStatus Status);
