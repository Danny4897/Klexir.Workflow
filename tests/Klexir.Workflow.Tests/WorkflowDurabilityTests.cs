using FluentAssertions;
using Klexir.Workflow.Abstractions;
using MonadicSharp;
using Xunit;

namespace Klexir.Workflow.Tests;

public sealed class WorkflowDurabilityTests
{
    [Fact]
    public async Task StartAsync_with_a_store_saves_a_checkpoint_after_each_successful_step()
    {
        var store = new InMemoryWorkflowStore();
        var definition = Workflow.Define<int>("Checkpointed")
            .Step("Inc", (int n) => Task.FromResult(Result<int>.Success(n + 1)))
            .Build();

        var engine = new WorkflowEngine(store);
        var started = await engine.StartAsync(definition, 1);

        var checkpoint = await store.LoadAsync(started.Value);
        checkpoint.IsSuccess.Should().BeTrue();
        checkpoint.Value.Status.Should().Be(WorkflowStatus.Completed);
        checkpoint.Value.CompletedStepCount.Should().Be(1);
        checkpoint.Value.CurrentValue.Should().Be(2);
    }

    [Fact]
    public async Task ResumeAsync_continues_execution_from_a_manually_seeded_checkpoint()
    {
        var store = new InMemoryWorkflowStore();
        var ran = new List<string>();
        var definition = Workflow.Define<int>("Resumable")
            .Step("StepA", (int n) => { ran.Add("A"); return Task.FromResult(Result<int>.Success(n + 1)); })
            .Step("StepB", (int n) => { ran.Add("B"); return Task.FromResult(Result<int>.Success(n + 1)); })
            .Step("StepC", (int n) => { ran.Add("C"); return Task.FromResult(Result<int>.Success(n + 1)); })
            .Build();

        var instanceId = WorkflowInstanceId.NewId();
        // Simulate a crash after StepA completed (value became 6) but before StepB ran.
        await store.SaveAsync(instanceId, new WorkflowCheckpoint("Resumable", 1, 6, WorkflowStatus.Running));

        var engine = new WorkflowEngine(store);
        var resumed = await engine.ResumeAsync(definition, instanceId);

        resumed.IsSuccess.Should().BeTrue();
        ran.Should().Equal("B", "C");
        (await engine.GetStatusAsync(instanceId)).Value.Should().Be(WorkflowStatus.Completed);
    }

    [Fact]
    public async Task ResumeAsync_fails_when_no_store_is_configured()
    {
        var definition = Workflow.Define<int>("NoStore").Step("Inc", (int n) => Task.FromResult(Result<int>.Success(n + 1))).Build();
        var engine = new WorkflowEngine();

        var resumed = await engine.ResumeAsync(definition, WorkflowInstanceId.NewId());

        resumed.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ResumeAsync_fails_when_the_checkpoint_belongs_to_a_different_definition()
    {
        var store = new InMemoryWorkflowStore();
        var definition = Workflow.Define<int>("Actual").Step("Inc", (int n) => Task.FromResult(Result<int>.Success(n + 1))).Build();
        var instanceId = WorkflowInstanceId.NewId();
        await store.SaveAsync(instanceId, new WorkflowCheckpoint("SomeOtherWorkflow", 0, 1, WorkflowStatus.Running));

        var resumed = await new WorkflowEngine(store).ResumeAsync(definition, instanceId);

        resumed.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ResumeAsync_fails_when_the_instance_is_already_completed()
    {
        var store = new InMemoryWorkflowStore();
        var definition = Workflow.Define<int>("Done").Step("Inc", (int n) => Task.FromResult(Result<int>.Success(n + 1))).Build();
        var instanceId = WorkflowInstanceId.NewId();
        await store.SaveAsync(instanceId, new WorkflowCheckpoint("Done", 1, 2, WorkflowStatus.Completed));

        var resumed = await new WorkflowEngine(store).ResumeAsync(definition, instanceId);

        resumed.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Compensation_after_a_resume_only_covers_steps_completed_since_the_resume_point()
    {
        var store = new InMemoryWorkflowStore();
        var compensated = new List<string>();
        var definition = Workflow.Define<int>("PartialCompensation")
            .Step("StepA", (int n) => Task.FromResult(Result<int>.Success(n + 1)))
            .Compensate(n => { compensated.Add($"UndoA:{n}"); return Task.FromResult(Result<Unit>.Success(Unit.Value)); })
            .Step("StepB", (int n) => Task.FromResult(Result<int>.Success(n + 1)))
            .Compensate(n => { compensated.Add($"UndoB:{n}"); return Task.FromResult(Result<Unit>.Success(Unit.Value)); })
            .Step("StepC", (int _) => Task.FromResult(Result<int>.Failure(Error.Create("boom"))))
            .Build();

        var instanceId = WorkflowInstanceId.NewId();
        // StepA already completed (and is therefore not part of this run's completed-steps tracking).
        await store.SaveAsync(instanceId, new WorkflowCheckpoint("PartialCompensation", 1, 6, WorkflowStatus.Running));

        await new WorkflowEngine(store).ResumeAsync(definition, instanceId);

        compensated.Should().Equal("UndoB:7");
    }
}
