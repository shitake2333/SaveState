# Adapting SaveState to your engine or container

## What an adapter has to provide

Only three things are engine-specific; everything else is shared:

| Need | Contract | Built in |
|---|---|---|
| where the bytes go | `ISaveStore` | `FileSystemStore`, `InMemoryStore`, `GodotSaveStore` |
| where diagnostics go | `ISaveLogger` | `NullSaveLogger`, `DelegateSaveLogger`, `GodotSaveLogger` |
| "is this a real run?" (for auto-saves) | a `Func<bool>` for `SaveGate` | `GodotRuntime.IsRuntimeProcess` via `GodotSavePolicy.CreateRuntimeGate()` |

## Writing a store

A store for a platform the framework does not ship an adapter for is usually a thin wrapper: keep the atomic write
and the file-name validation from `FileSystemStore` and only decide the root directory.

```csharp
using SaveState;

/// <summary>A store rooted wherever the platform puts writable user data.</summary>
public sealed class PlatformSaveStore : ISaveStore
{
    private readonly FileSystemStore _inner;

    public PlatformSaveStore(string writableUserDirectory)
        => _inner = new FileSystemStore(writableUserDirectory);

    // Reuse the framework's implementation: atomic write + file-name validation, no extra code.
    public bool Exists(string fileName) => _inner.Exists(fileName);
    public string Read(string fileName) => _inner.Read(fileName);
    public void Write(string fileName, string contents) => _inner.Write(fileName, contents);
    public bool Delete(string fileName) => _inner.Delete(fileName);
    public string Describe(string fileName) => _inner.Describe(fileName);
}
```

Only the root differs per platform (`user://` on Godot, a `Documents`/`Application Support` directory elsewhere, a
data directory on a server). For a platform without a filesystem at all (consoles with their own storage API, a
service with blob storage), implement `ISaveStore` directly - `Write` has to be atomic, that is the only hard rule.

Requirements of an implementation:

* **`Write` must be atomic** - a half-written save file is worse than a missing one. `FileSystemStore` writes a
  `.tmp` file and replaces the target.
* **`Read` may throw** for IO problems; `SaveFileSession` turns that into `LoadStatus.Failed` and never overwrites
  the file afterwards.
* **`Describe` should be useful in a log line** (usually an absolute path).
* The store is responsible for *bytes only* - file names are validated by the framework
  (`no /, \, .. or rooted paths`).

Embedded platforms: keep the store synchronous and small; the optional `SaveState.Policy` package is the place for
scheduling decisions, not the store.

## Godot

```csharp
using SaveState;
using SaveState.Godot;

var store  = new GodotSaveStore();          // user://  (or new GodotSaveStore("saves"))
var logger = GodotSaveLogger.Instance;      // GD.PushError / PushWarning / Print
var gate   = GodotSavePolicy.CreateRuntimeGate(logger);

var session = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(profile), logger);

session.EnsureCreated();        // a human asked for it: not gated
gate.TrySave(session.Save);     // gameplay event: skipped in the editor and in headless tool/test runs
```

* `GodotRuntime.IsRuntimeProcess` is false in the editor, in `--script` tool runs and under `--headless` - exactly
  the processes that must not touch a developer's or a CI machine's save files.
* `GodotSaveLogger` reports errors/warnings through Godot's own push functions, so they show up as engine
  errors/warnings and carry the real file path.
* In headless CI runs nothing is auto-saved; a test that *wants* to verify a write should use an `InMemoryStore`
  (or a temporary directory) plus `gate.AllowOverride = true`, never the real `user://`.

## Container wiring (Microsoft.Extensions.DependencyInjection)

```csharp
services.AddSaveService<PlayerProfile>();     // your services, registered normally

services.AddSaveState(options =>
{
    options.Store  = new GodotSaveStore();
    options.Logger = GodotSaveLogger.Instance;
    options.RegisterServices = (registry, resolve) => registry.AddGeneratedServices(resolve);
});

var session = provider.GetSaveFileSession<ProfileSaveFile>();
```

`AddGeneratedServices` is emitted next to your services: it resolves each `[SaveService]` type through the container
and registers it, so the registry cannot drift away from the container (no reflection, no assembly scanning).

Only `options.Store` is mandatory; a missing store throws `SaveStateException` at startup rather than writing
somewhere unexpected later.

## Host-side write policy

The library deliberately has no opinion about *when* to save. A typical host ends up with:

```csharp
public static class Saves
{
    private static readonly SaveRegistry Registry = new SaveRegistry().Add(player).Add(settings);
    private static readonly ISaveStore Store = new GodotSaveStore();
    private static readonly ISaveLogger Logger = GodotSaveLogger.Instance;

    public static readonly SaveGate Gate = new SaveGate(() => Game.IsRunning, Logger);

    private static readonly SaveFileSession<GlobalSaveFile> Global =
        new SaveFileSession<GlobalSaveFile>(Store, Registry, Logger);

    public static bool SaveGlobal() => Global.Save();
    public static LoadResult LoadGlobal() => Global.Load();
    public static bool EnsureGlobal() => Global.EnsureCreated();
}
```

* `Gate` keeps editors/tools/test runs out of real save files.
* First launch and human actions call `EnsureCreated()` / `Save()` directly (a human asked for them).
* Gameplay events go through the gate, optionally wrapped in a `SaveScheduler` / `AsyncSaveScheduler`.
