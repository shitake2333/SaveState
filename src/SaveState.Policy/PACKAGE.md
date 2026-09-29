# SaveState.Policy

Optional write-policy helpers for [SaveState](https://github.com/shitake2333/SaveState). The core deliberately has
no opinion about *when* to write; this package gives you the two shapes most hosts need.

```bash
dotnet add package SaveState.Policy
```

## Coalesce / debounce

```csharp
using var scheduler = new SaveScheduler(session.Save, logger, TimeSpan.FromMilliseconds(400));
scheduler.RequestSave();   // a burst inside one window becomes a single write
scheduler.Flush();         // before quitting (Dispose flushes too)
```

## Move the disk write off the game thread

Disk writes can hitch a frame, but serializing game state on a background thread races with the game mutating it,
so the two halves are split: the **snapshot** runs on the calling thread, the **write** on a worker, and a burst
collapses into the newest state.

```csharp
using var asyncSave = session.ScheduleAsync(logger);

asyncSave.RequestSave();   // snapshot here, write on a worker
asyncSave.Flush();         // block until the pending write lands
```

It composes with `SaveGate` from the runtime (the gate decides *whether*, the scheduler decides *when*):

```csharp
gate.TryRun(asyncSave.RequestSave);
```

See [Adapters](https://github.com/shitake2333/SaveState/blob/main/docs/adapters.md) for the host-side write policy
in context.

MIT licensed.
