# Klexir.Workflow

[![CI](https://github.com/Danny4897/Klexir.Workflow/actions/workflows/ci.yml/badge.svg)](https://github.com/Danny4897/Klexir.Workflow/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/docs-vitepress-7c3aed.svg)](https://danny4897.github.io/Klexir.Workflow/)

Durable workflow and saga orchestration for Klexir, built on [MonadicSharp](https://www.nuget.org/packages/MonadicSharp/) `Result<T>`. A fluent, type-checked step builder plus an engine that runs it, checkpoints it, and can resume it after a restart.

> **Status: public research repo, not yet published to NuGet.** Reference the project directly until/unless it's published.

---

## Quick example — a checkout saga

```csharp
var checkout = Workflow.Define<Cart>("Checkout")
    .Step("ReserveStock", cart => _inventory.ReserveAsync(cart))
    .Compensate(reservation => _inventory.ReleaseAsync(reservation))
    .Step("ChargePayment", reservation => _payments.ChargeAsync(reservation), new WorkflowStepOptions
    {
        MaxAttempts = 3,
        RetryDelay = TimeSpan.FromSeconds(1),
        Timeout = TimeSpan.FromSeconds(10),
    })
    .Compensate(charge => _payments.RefundAsync(charge))
    .Step("ShipOrder", charge => _shipping.ShipAsync(charge))
    .Build();

var store = new FileWorkflowStore("./workflow-checkpoints"); // one JSON file per instance, atomic writes
var engine = new WorkflowEngine(store);

var instance = await engine.StartAsync(checkout, cart);
var status = await engine.GetStatusAsync(instance.Value); // Running / Completed / Failed
```

If `ShipOrder` fails, the engine runs `RefundAsync` then `ReleaseAsync` — in that reverse order — before marking the instance `Failed`. Nothing here throws; check `status` and the `Result<T>` each step returned.

If the process crashes mid-saga, a fresh `WorkflowEngine(store)` can pick the same instance back up:

```csharp
await engine.ResumeAsync(checkout, instance.Value);
```

---

## What's in the box

| Capability | API | Notes |
|---|---|---|
| Sequential steps | `Workflow.Define<T>(name).Step(name, fn).Build()` | Each step can change the running type; type-checked at compile time |
| Parallel branches | `.Parallel(("Name", fn), ...)` | Side-effecting branches run concurrently; join is deterministic by declaration order, not completion order |
| Conditional branching | `.Branch(predicate, whenTrue, whenFalse)` | Runs exactly one of two steps |
| Retry & timeout | `.Step(name, fn, new WorkflowStepOptions { ... })` | Timeout bounds the *caller's* wait (`Task.WaitAsync`), so it works even if the step ignores `CancellationToken` |
| Saga compensation | `.Compensate(fn)` | Attaches to the step just added; runs in reverse order on a later failure |
| Durable checkpoints | `WorkflowEngine(IWorkflowStore?)`, `ResumeAsync` | Checkpoints after every step; resume continues from the last one |
| Persistence | `FileWorkflowStore`, `InMemoryWorkflowStore` | File store: one JSON file per instance, temp-file-then-rename so a crash mid-write can't corrupt it — genuinely survives a process restart, not just an engine object being recreated |

## Not there yet

- Compensating steps that completed *before* the last checkpoint, if a step after a resume fails (only steps completed during the resumed run are compensated — see the XML docs on `ResumeAsync`)
- Scheduling workflows to start in the future, and EventFlow-driven transitions
- `FileWorkflowStore` resolves `CurrentValue`'s type via `Type.GetType(AssemblyQualifiedName)` — fine within one process/deployment, but a checkpoint written by one version of your types and read by an incompatible one will fail to deserialize

## Requirements

.NET 8 SDK + [MonadicSharp](https://www.nuget.org/packages/MonadicSharp/) `Result<T>`.
