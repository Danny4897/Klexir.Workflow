using FluentAssertions;
using Klexir.Workflow.Abstractions;
using MonadicSharp;
using Xunit;

namespace Klexir.Workflow.Tests;

public sealed class WorkflowBranchingTests
{
    [Fact]
    public async Task Parallel_runs_all_branches_and_lets_the_next_step_see_the_unchanged_value()
    {
        var ran = new List<string>();
        var gate = new object();
        var definition = Workflow.Define<int>("ParallelOk")
            .Parallel(
                ("A", async (int n) => { await Task.Yield(); lock (gate) { ran.Add($"A:{n}"); } return Result<Unit>.Success(Unit.Value); }),
                ("B", async (int n) => { await Task.Yield(); lock (gate) { ran.Add($"B:{n}"); } return Result<Unit>.Success(Unit.Value); }))
            .Step("AfterParallel", (int n) => Task.FromResult(Result<int>.Success(n + 1)))
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 10);

        ran.OrderBy(x => x).Should().Equal("A:10", "B:10");
        (await engine.GetStatusAsync(started.Value)).Value.Should().Be(WorkflowStatus.Completed);
    }

    [Fact]
    public async Task ParallelStep_reports_the_first_failing_branch_in_declared_order_not_completion_order()
    {
        var step = new ParallelStep<int>(
        [
            ("First", async (int _) =>
            {
                await Task.Delay(30);
                return Result<Unit>.Failure(Error.Create("first-failed"));
            }),
            ("Second", (int _) => Task.FromResult(Result<Unit>.Failure(Error.Create("second-failed")))),
        ]);

        var result = await step.ExecuteAsync(1, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("first-failed");
    }

    [Fact]
    public async Task Branch_executes_only_the_true_branch_when_the_predicate_holds()
    {
        var ran = new List<string>();
        var definition = Workflow.Define<int>("Branch")
            .Branch<int>(
                n => n > 0,
                whenTrue: n => { ran.Add("true"); return Task.FromResult(Result<int>.Success(n)); },
                whenFalse: n => { ran.Add("false"); return Task.FromResult(Result<int>.Success(n)); })
            .Build();

        await new WorkflowEngine().StartAsync(definition, 5);

        ran.Should().Equal("true");
    }

    [Fact]
    public async Task Branch_executes_only_the_false_branch_when_the_predicate_does_not_hold()
    {
        var ran = new List<string>();
        var definition = Workflow.Define<int>("Branch")
            .Branch<int>(
                n => n > 0,
                whenTrue: n => { ran.Add("true"); return Task.FromResult(Result<int>.Success(n)); },
                whenFalse: n => { ran.Add("false"); return Task.FromResult(Result<int>.Success(n)); })
            .Build();

        await new WorkflowEngine().StartAsync(definition, -5);

        ran.Should().Equal("false");
    }

    [Fact]
    public async Task Branch_failure_in_the_chosen_branch_marks_the_instance_failed()
    {
        var definition = Workflow.Define<int>("BranchFail")
            .Branch<int>(
                _ => true,
                whenTrue: _ => Task.FromResult(Result<int>.Failure(Error.Create("boom"))),
                whenFalse: n => Task.FromResult(Result<int>.Success(n)))
            .Build();

        var engine = new WorkflowEngine();
        var started = await engine.StartAsync(definition, 1);

        (await engine.GetStatusAsync(started.Value)).Value.Should().Be(WorkflowStatus.Failed);
    }
}
