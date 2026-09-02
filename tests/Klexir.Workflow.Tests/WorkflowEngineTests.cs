using FluentAssertions;
using Klexir.Workflow.Abstractions;
using MonadicSharp;
using Xunit;

namespace Klexir.Workflow.Tests;

public sealed class WorkflowEngineTests
{
    [Fact]
    public async Task StartAsync_runs_steps_in_order_threading_the_transformed_value_through()
    {
        var trace = new List<string>();
        var definition = Workflow.Define<int>("Trace")
            .Step("AddOne", (int n) => RecordAsync(trace, "AddOne", n, n + 1))
            .Step("Double", (int n) => RecordAsync(trace, "Double", n, n * 2))
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 5);

        started.IsSuccess.Should().BeTrue();
        trace.Should().Equal("AddOne:5", "Double:6");

        var status = await engine.GetStatusAsync(started.Value);
        status.Value.Should().Be(WorkflowStatus.Completed);
    }

    [Fact]
    public async Task StartAsync_stops_at_the_first_failing_step_and_marks_the_instance_failed()
    {
        var trace = new List<string>();
        var definition = Workflow.Define<int>("Trace")
            .Step("Fail", (int _) => Task.FromResult(Result<int>.Failure(Error.Create("boom"))))
            .Step("NeverRuns", (int n) => RecordAsync(trace, "NeverRuns", n, n))
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 1);

        started.IsSuccess.Should().BeTrue();
        trace.Should().BeEmpty();

        var status = await engine.GetStatusAsync(started.Value);
        status.Value.Should().Be(WorkflowStatus.Failed);
    }

    [Fact]
    public async Task StartAsync_treats_a_step_that_throws_as_a_failure_instead_of_propagating()
    {
        var definition = Workflow.Define<int>("Throws")
            .Step<int>("Boom", (int _) => throw new InvalidOperationException("kaboom"))
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 1);

        started.IsSuccess.Should().BeTrue();
        var status = await engine.GetStatusAsync(started.Value);
        status.Value.Should().Be(WorkflowStatus.Failed);
    }

    [Fact]
    public async Task GetStatusAsync_returns_a_failure_for_an_unknown_instance()
    {
        var engine = new WorkflowEngine();

        var status = await engine.GetStatusAsync(WorkflowInstanceId.NewId());

        status.IsFailure.Should().BeTrue();
    }

    private static Task<Result<int>> RecordAsync(List<string> trace, string step, int input, int output)
    {
        trace.Add($"{step}:{input}");
        return Task.FromResult(Result<int>.Success(output));
    }
}
