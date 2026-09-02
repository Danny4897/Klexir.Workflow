using FluentAssertions;
using Klexir.Workflow.Abstractions;
using Xunit;

namespace Klexir.Workflow.Tests;

public sealed class FileWorkflowStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"klexir-workflow-store-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record CartSnapshot(int ItemCount, decimal Total);

    [Fact]
    public async Task SaveAsync_then_LoadAsync_roundtrips_a_checkpoint_with_a_primitive_value()
    {
        var store = new FileWorkflowStore(_directory);
        var instanceId = WorkflowInstanceId.NewId();
        var checkpoint = new WorkflowCheckpoint("Order", 2, 42, WorkflowStatus.Running);

        await store.SaveAsync(instanceId, checkpoint);
        var loaded = await store.LoadAsync(instanceId);

        loaded.IsSuccess.Should().BeTrue();
        loaded.Value.Should().Be(checkpoint);
    }

    [Fact]
    public async Task SaveAsync_then_LoadAsync_roundtrips_a_checkpoint_with_a_record_value()
    {
        var store = new FileWorkflowStore(_directory);
        var instanceId = WorkflowInstanceId.NewId();
        var checkpoint = new WorkflowCheckpoint("Cart", 1, new CartSnapshot(3, 29.97m), WorkflowStatus.Running);

        await store.SaveAsync(instanceId, checkpoint);
        var loaded = await store.LoadAsync(instanceId);

        loaded.IsSuccess.Should().BeTrue();
        loaded.Value.CurrentValue.Should().Be(new CartSnapshot(3, 29.97m));
    }

    [Fact]
    public async Task LoadAsync_fails_for_an_unknown_instance()
    {
        var store = new FileWorkflowStore(_directory);

        var loaded = await store.LoadAsync(WorkflowInstanceId.NewId());

        loaded.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_overwrites_the_previous_checkpoint_for_the_same_instance()
    {
        var store = new FileWorkflowStore(_directory);
        var instanceId = WorkflowInstanceId.NewId();

        await store.SaveAsync(instanceId, new WorkflowCheckpoint("Order", 1, 1, WorkflowStatus.Running));
        await store.SaveAsync(instanceId, new WorkflowCheckpoint("Order", 2, 2, WorkflowStatus.Completed));

        var loaded = await store.LoadAsync(instanceId);

        loaded.Value.CompletedStepCount.Should().Be(2);
        loaded.Value.Status.Should().Be(WorkflowStatus.Completed);
    }

    [Fact]
    public async Task A_checkpoint_survives_a_fresh_store_instance_pointed_at_the_same_directory()
    {
        var instanceId = WorkflowInstanceId.NewId();
        await new FileWorkflowStore(_directory).SaveAsync(
            instanceId, new WorkflowCheckpoint("Order", 1, new CartSnapshot(1, 9.99m), WorkflowStatus.Running));

        // Simulates a process restart: nothing but the directory on disk survives.
        var reopened = new FileWorkflowStore(_directory);
        var loaded = await reopened.LoadAsync(instanceId);

        loaded.IsSuccess.Should().BeTrue();
        loaded.Value.CurrentValue.Should().Be(new CartSnapshot(1, 9.99m));
    }
}
