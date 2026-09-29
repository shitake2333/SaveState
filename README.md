# SaveState

Project-agnostic **state + save/load** framework for .NET: an incremental source generator that turns
plain classes into strongly-typed save files, plus a runtime with **lenient, self-healing loading**.

* **No engine dependency.** Contracts are `netstandard2.0`; the file store is an interface
  (`ISaveStore`), so Godot (`user://`), a console/server-side directory or an
  in-memory dictionary for tests all work the same way.
* **No singleton assumption.** Generated code reads services from an explicit `SaveRegistry`
  (works with a DI container, hand-written `new`, or lazy singletons).
* **Declared by the host, not by the library.** A save file is a host class (`[SaveFile("global.json")]`),
  so adding a file never requires a new version of this package.
* **Missing sections are repaired, never `null`.** A section that is absent from an old file is filled
  with a default instance and reported as `LoadStatus.Repaired` — the framework never writes `null`
  into your service state (that was a real, painful bug class).
* **Corrupt files are never overwritten** by defaults: `LoadStatus.Failed` ≠ `LoadStatus.Missing`.
* **Compile-time diagnostics.** Bad file names, duplicate section keys, non-instantiable state types and
  missing setters are generator errors (`SAV001`–`SAV013`), not runtime surprises.

## Documentation

**[docs/](docs/README.md)** — start here for anything beyond the quick start:

| Document | Contents |
|---|---|
| [Getting started](docs/getting-started.md) | a working save file in five minutes; the first-launch / save / load flow; testing your host |
| [API reference](docs/api.md) | every public type and member, the generated members, the diagnostics, the file format |
| [Migrations](docs/migrations.md) | versioning rules and the declarative "old field → new field + conversion" upgrades |
| [Adapters](docs/adapters.md) | writing an `ISaveStore` for your engine, the Godot adapter, container wiring, host-side write policy |
| [Distribution](docs/distribution.md) | build/test/pack, local feeds, the same-version NuGet cache pitfall, release checklist |

Runnable example: `samples/SaveState.Samples.Console` (first launch → atomic write → reload → legacy file repair).

## Packages

| Package | Target | What it is |
|---|---|---|
| `SaveState.Shared` | `netstandard2.0`, `net10.0` | attributes, `SaveFileDefinition`, `SaveRegistry`, `ISaveStore`, `ISaveLogger`, `StateBase`, `LoadResult` |
| `SaveState.Generator` | analyzer (`netstandard2.0`) | incremental source generator |
| `SaveState.Runtime` | `netstandard2.0`, `net10.0` | `SaveFileSession<TFile>`, `FileSystemStore`, `InMemoryStore`, `SaveGate`, JSON policy, migrations |
| `SaveState.Policy` (optional) | `netstandard2.0`, `net10.0` | `SaveScheduler` (coalesce/debounce) and `AsyncSaveScheduler` (move the disk write off the game thread) |
| `SaveState.Godot` | `net10.0` (Godot 4.7) | `user://` store, Godot output logger, runtime-process gate |
| `SaveState.DependencyInjection` | `netstandard2.0`, `net10.0` | `AddSaveState(...)` + `GetSaveFileSession<TFile>()` |

## Quick start

```csharp
using System.Collections.Generic;
using SaveState;

// 1) A state object: plain serializable data + change notification.
public sealed class ProfileState : StateBase
{
    public int Coins { get; set; } = 100;
    public HashSet<int> Unlocked { get; set; } = new HashSet<int>();
}

// 2) A service: its State lives in the "Profile" section of "profile.json".
[SaveService]
public partial class PlayerProfile
{
    [SavedState(File = "profile.json", Key = "Profile")]
    public ProfileState State { get; private set; } = new ProfileState();
}

// 3) One save file (the generator fills in the implementation).
[SaveFile("profile.json")]
public sealed partial class ProfileSaveFile : SaveFileDefinition
{
}
```

```csharp
var store    = new FileSystemStore(GetSaveDirectory());          // engine-specific root
var registry = new SaveRegistry().Add(playerProfile);
var session  = new SaveFileSession<ProfileSaveFile>(store, registry, logger);

session.EnsureCreated();                  // first launch: write a complete default file
session.Save();                           // unlock / end of run: explicit write point
LoadResult result = session.Load();       // Missing | Loaded | Repaired | Failed
```

The generator adds this to your `partial class ProfileSaveFile`:

```csharp
public const string FileNameValue = "profile.json";
public override string GetFileName() => FileNameValue;
public override int Version { get; set; } = 1;
public ProfileState Profile { get; set; } = null!;

public override void CollectFrom(SaveRegistry registry) { /* service -> DTO */ }
public override void RestoreTo(SaveRegistry registry)   { /* DTO -> service, null-checked per section */ }
public override bool FillMissingDefaults(out IReadOnlyList<string> missingSections) { /* repair */ }
```

### Godot

```csharp
using SaveState.Godot;

var store   = new GodotSaveStore();                          // rooted at user:// (or "saves" subdirectory)
var logger  = GodotSaveLogger.Instance;                      // GD.PushError / PushWarning / Print
var gate    = GodotSavePolicy.CreateRuntimeGate(logger);      // no auto-save in the editor or headless tools

var session = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(profile), logger);

session.EnsureCreated();                  // first launch: a human asked for it -> not gated
gate.TrySave(session.Save);               // event-driven (unlock, end of run) -> gated
```

### Dependency injection

```csharp
services.AddSaveService<PlayerProfile>();                     // your services, registered normally

services.AddSaveState(options =>
{
    options.Store    = new GodotSaveStore();
    options.Logger   = GodotSaveLogger.Instance;
    // The generator emits AddGeneratedServices next to your services: no reflection, no forgotten service.
    options.RegisterServices = (registry, resolve) => registry.AddGeneratedServices(resolve);
});

var session = provider.GetSaveFileSession<ProfileSaveFile>();
```

### Coalescing writes

When one settlement unlocks three items, writing three times is wasteful — and writing only on exit
risks losing progress. `SaveScheduler` collapses a burst of requests into a single write:

```csharp
using var scheduler = new SaveScheduler(session.Save, logger, TimeSpan.FromMilliseconds(400));
scheduler.RequestSave();   // ×3 in the same window -> one write
scheduler.Flush();         // before quitting (Dispose also flushes)
```

### Moving the write off the game thread

Disk writes can hitch a frame, but serializing game state on a background thread races with the game
mutating it. `AsyncSaveScheduler` splits the two: the **snapshot** happens on the calling thread, the
**write** on a worker, and a burst of requests collapses into the newest state.

```csharp
using var async = session.ScheduleAsync(logger);

async.RequestSave();       // snapshot here, write on a worker; safe to call in a burst
async.Flush();             // before quitting: blocks until the pending write lands
```

`SaveFileSession` exposes the two halves directly (`SerializeSnapshot()` / `WriteSnapshot(json)`) if you
want to shape the policy yourself. The two policies compose - the gate decides *whether*, the scheduler
decides *when*:

```csharp
using var asyncSave = session.ScheduleAsync(logger);
var gate = GodotSavePolicy.CreateRuntimeGate(logger);

gate.TryRun(asyncSave.RequestSave);   // event-driven: skipped in editors/tools, written on a worker otherwise
```

## Migrating old save files

A save file's shape changes over time, and old files must keep loading. Bump `[SaveFile("profile.json", Version = 2)]`
and register one `FieldMigration` per version step: **old field → new field (+ a conversion function)**. The framework
applies them to the JSON *before* deserializing, so an old file arrives in the current shape.

```csharp
var migrations = new ISaveMigration[]
{
    // v1 -> v2: a section was renamed, a field changed type, one field was added, one was dropped.
    new FieldMigration("profile.json", fromVersion: 1)
        .Rename("OldProfile", "Profile")
        .Convert("Profile/PlayTimeSeconds", "Profile/PlayTime", JsonUpgrade.SecondsToClockText())
        .Convert("Profile/Coins", "Profile/Coins", JsonUpgrade.ToInt())        // "12" -> 12
        .SetDefault("Profile/Title", JsonValue.Create("Newcomer"))
        .Remove("Profile/LegacyFlag"),
};

var session = new SaveFileSession<ProfileSaveFile>(store, registry, logger, migrations);
```

* **Paths** are `/`-separated (`"Profile/Coins"`); dots are *not* separators, because keys such as mod package
  names contain dots.
* **The conversion function** gets the old JSON node (or `null` when the field is absent) and returns the new one;
  returning `null` drops the field. `JsonUpgrade` covers the everyday type changes
  (`ToInt` / `ToNumber` / `ToText` / `ToBool` / `ToArray` / `SecondsToClockText`) and never throws on a garbled
  value - it falls back instead.
* **Silent no-ops are reported**: if the old field name does not match anything (a typo is the usual reason), the
  migration records a warning and the session logs it, instead of quietly keeping the default value.
* Intermediate objects are created as needed, so a value can move into a new nested object.
* Migrations only touch JSON; they never reference the old C# types.


## Build, test, pack

```bash
dotnet build SaveState.sln -c Debug
dotnet test  SaveState.sln -c Debug
dotnet pack  SaveState.sln -c Release -o artifacts     # packages, generator packaged as analyzer
```

Consume locally without a feed round-trip:

```xml
<!-- nuget.config -->
<add key="savestate-local" value="./artifacts" />
```

> **Re-packing the same version?** NuGet caches packages in the global packages folder
> (`~/.nuget/packages/savestate.*`), so a consumer keeps resolving the *old* build even after
> `dotnet pack` wrote a new file with the same version. Bump `build/Version.props` (recommended), or
> clear that folder before restoring again. `scripts/verify-packages.sh` does this automatically when
> it checks that the packages work for an outside consumer.

## Roadmap

* `SaveState.Testing` (a temp-directory store, so host tests can assert on real files)
* an optional queued writer that survives shutdown by itself (today `Flush`/`Dispose` must be called)

MIT licensed.
