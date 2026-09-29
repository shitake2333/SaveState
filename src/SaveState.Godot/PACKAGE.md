# SaveState.Godot

Godot 4 adapter for [SaveState](https://github.com/shitake2333/SaveState): the three things every Godot host would
otherwise write itself.

```bash
dotnet add package SaveState.Godot
```

Built against **Godot 4.7** (`net10.0`); add it next to `SaveState.Shared` + `SaveState.Runtime` +
`SaveState.Generator` in a Godot project.

## What is in it

* **`GodotSaveStore`** - an `ISaveStore` rooted at `user://` (or a subdirectory such as `"saves"`), with the
  framework's atomic write and file-name validation.
* **`GodotSaveLogger`** - reports through `GD.PushError` / `PushWarning` / `Print` with the real file path, so a
  failed save shows up where a Godot developer looks.
* **`GodotRuntime`** - `IsEditor`, `IsHeadless`, `IsRuntimeProcess`.
* **`GodotSavePolicy.CreateRuntimeGate()`** - a `SaveGate` that only allows writes in a real game run, so the
  editor, `--script` tool runs and headless CI never touch a player's save files.

```csharp
using SaveState;
using SaveState.Godot;

var store   = new GodotSaveStore();                         // user://
var logger  = GodotSaveLogger.Instance;
var gate    = GodotSavePolicy.CreateRuntimeGate(logger);

var session = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(profile), logger);

session.EnsureCreated();      // a human asked for it: not gated
gate.TrySave(session.Save);   // gameplay event: skipped outside a real run
```

See [Adapters](https://github.com/shitake2333/SaveState/blob/main/docs/adapters.md).

MIT licensed.
