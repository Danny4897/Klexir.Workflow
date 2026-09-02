# Klexir.Workflow

Durable workflow and saga orchestration for Klexir, built on [MonadicSharp](https://www.nuget.org/packages/MonadicSharp) `Result<T>`.

Only `Klexir.Workflow.Abstractions` is a public NuGet package (`IWorkflowStep<TIn,TOut>`, `WorkflowInstanceId`, `WorkflowStatus`).

The first increment is a sequential builder and engine: `Workflow.Define<TRequest>("Name").Step("StepName", fn).Build()` produces a type-checked, named chain of steps (each step can change the running type); `WorkflowEngine.StartAsync` runs it to completion — a step that fails (returns a failed `Result`) or throws stops the chain and marks the instance `Failed`, without propagating an exception. `GetStatusAsync` reads back `Running`/`Completed`/`Failed`.

`.Parallel(("Name", fn), ...)` runs every branch concurrently against the current value (branches are side effects — `Func<TCurrent, Task<Result<Unit>>>` — not value producers, so the current value flows unchanged into the next step); the join is deterministic by declaration order, not completion order, so with multiple failures the first-declared branch's error wins. `.Branch(predicate, whenTrue, whenFalse)` runs exactly one of two steps chosen by a predicate.

Retry/timeout/compensation, durable checkpoints and resume-after-failure are later increments — this engine currently runs synchronously in-process with no persisted state across restarts.
