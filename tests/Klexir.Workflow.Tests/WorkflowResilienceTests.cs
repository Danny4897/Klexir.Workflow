using FluentAssertions;
using Klexir.Workflow.Abstractions;
using MonadicSharp;
using Xunit;

namespace Klexir.Workflow.Tests;

public sealed class WorkflowResilienceTests
{
    [Fact]
    public async Task Step_with_retry_succeeds_after_transient_failures_within_max_attempts()
    {
        var attempts = 0;
        var definition = Workflow.Define<int>("Flaky")
            .Step("Flaky", (int n) =>
            {
                attempts++;
                return Task.FromResult(attempts < 3
                    ? Result<int>.Failure(Error.Create("not yet"))
                    : Result<int>.Success(n));
            }, new WorkflowStepOptions { MaxAttempts = 3 })
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 1);

        attempts.Should().Be(3);
        (await engine.GetStatusAsync(started.Value)).Value.Should().Be(WorkflowStatus.Completed);
    }

    [Fact]
    public async Task Step_with_retry_exhausted_marks_the_instance_failed()
    {
        var attempts = 0;
        var definition = Workflow.Define<int>("AlwaysFails")
            .Step("AlwaysFails", (int _) =>
            {
                attempts++;
                return Task.FromResult(Result<int>.Failure(Error.Create("boom")));
            }, new WorkflowStepOptions { MaxAttempts = 2 })
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 1);

        attempts.Should().Be(2);
        (await engine.GetStatusAsync(started.Value)).Value.Should().Be(WorkflowStatus.Failed);
    }

    [Fact]
    public async Task Step_with_timeout_fails_when_the_step_does_not_complete_in_time()
    {
        var definition = Workflow.Define<int>("SlowStep")
            .Step("SlowStep", async (int n) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return Result<int>.Success(n);
            }, new WorkflowStepOptions { Timeout = TimeSpan.FromMilliseconds(50) })
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 1);

        (await engine.GetStatusAsync(started.Value)).Value.Should().Be(WorkflowStatus.Failed);
    }

    [Fact]
    public async Task Compensate_runs_in_reverse_order_for_completed_steps_when_a_later_step_fails()
    {
        var compensated = new List<string>();
        var definition = Workflow.Define<int>("Saga")
            .Step("ReserveStock", (int n) => Task.FromResult(Result<int>.Success(n + 1)))
            .Compensate(n => { compensated.Add($"ReleaseStock:{n}"); return Task.FromResult(Result<Unit>.Success(Unit.Value)); })
            .Step("ChargePayment", (int n) => Task.FromResult(Result<int>.Success(n + 1)))
            .Compensate(n => { compensated.Add($"RefundPayment:{n}"); return Task.FromResult(Result<Unit>.Success(Unit.Value)); })
            .Step("ShipOrder", (int _) => Task.FromResult(Result<int>.Failure(Error.Create("shipping failed"))))
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 0);

        compensated.Should().Equal("RefundPayment:2", "ReleaseStock:1");
        (await engine.GetStatusAsync(started.Value)).Value.Should().Be(WorkflowStatus.Failed);
    }

    [Fact]
    public async Task Compensate_does_not_run_for_a_step_that_never_completed()
    {
        var compensated = new List<string>();
        var definition = Workflow.Define<int>("Saga")
            .Step("ReserveStock", (int _) => Task.FromResult(Result<int>.Failure(Error.Create("out of stock"))))
            .Compensate(n => { compensated.Add($"ReleaseStock:{n}"); return Task.FromResult(Result<Unit>.Success(Unit.Value)); })
            .Build();

        await new WorkflowEngine().StartAsync(definition, 0);

        compensated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_successful_workflow_never_runs_compensations()
    {
        var compensated = new List<string>();
        var definition = Workflow.Define<int>("Saga")
            .Step("ReserveStock", (int n) => Task.FromResult(Result<int>.Success(n + 1)))
            .Compensate(n => { compensated.Add($"ReleaseStock:{n}"); return Task.FromResult(Result<Unit>.Success(Unit.Value)); })
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 0);

        compensated.Should().BeEmpty();
        (await engine.GetStatusAsync(started.Value)).Value.Should().Be(WorkflowStatus.Completed);
    }
}
