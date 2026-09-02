using System.Text.Json;
using Klexir.Workflow.Abstractions;
using MonadicSharp;

namespace Klexir.Workflow;

/// <summary>
/// Disk-backed checkpoint store: one JSON file per instance under <paramref name="directoryPath"/>, written to a
/// temp file and renamed into place so a crash mid-write can't leave a corrupt checkpoint (the "atomic post-step
/// checkpoint" the study plan asks for). <see cref="WorkflowCheckpoint.CurrentValue"/> is serialized using its own
/// runtime type, recorded alongside as an assembly-qualified name so it can be deserialized back to that type.
/// </summary>
public sealed class FileWorkflowStore(string directoryPath) : IWorkflowStore
{
    public async Task<Result<Unit>> SaveAsync(WorkflowInstanceId instanceId, WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
        await Try.ExecuteAsync(async () =>
        {
            Directory.CreateDirectory(directoryPath);

            var valueType = checkpoint.CurrentValue.GetType();
            var envelope = new CheckpointEnvelope(
                checkpoint.DefinitionName,
                checkpoint.CompletedStepCount,
                valueType.AssemblyQualifiedName!,
                JsonSerializer.Serialize(checkpoint.CurrentValue, valueType),
                checkpoint.Status);

            var path = PathFor(instanceId);
            var tempPath = $"{path}.tmp";
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(envelope), cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
            return Unit.Value;
        }).ConfigureAwait(false);

    public async Task<Result<WorkflowCheckpoint>> LoadAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(instanceId);
        if (!File.Exists(path))
        {
            return Result<WorkflowCheckpoint>.Failure(Error.NotFound("WorkflowCheckpoint", instanceId.ToString()));
        }

        return await Try.ExecuteAsync(async () =>
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<CheckpointEnvelope>(json)
                ?? throw new InvalidOperationException($"Checkpoint file for '{instanceId}' is empty or corrupt.");

            var valueType = Type.GetType(envelope.CurrentValueTypeName)
                ?? throw new InvalidOperationException($"Cannot resolve type '{envelope.CurrentValueTypeName}' to deserialize the checkpoint.");

            var value = JsonSerializer.Deserialize(envelope.CurrentValueJson, valueType)
                ?? throw new InvalidOperationException($"Checkpoint value for '{instanceId}' deserialized to null.");

            return new WorkflowCheckpoint(envelope.DefinitionName, envelope.CompletedStepCount, value, envelope.Status);
        }).ConfigureAwait(false);
    }

    private string PathFor(WorkflowInstanceId instanceId) => Path.Combine(directoryPath, $"{instanceId}.json");

    private sealed record CheckpointEnvelope(
        string DefinitionName, int CompletedStepCount, string CurrentValueTypeName, string CurrentValueJson, WorkflowStatus Status);
}
