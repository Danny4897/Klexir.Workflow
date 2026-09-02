# Quick example — a checkout saga

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

See the [full README](https://github.com/Danny4897/Klexir.Workflow#readme) on GitHub for the complete feature table and current gaps.
