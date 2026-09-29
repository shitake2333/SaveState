# Getting started

## 1. Add the packages

```bash
dotnet add package SaveState.Shared
dotnet add package SaveState.Runtime
dotnet add package SaveState.Generator     # analyzer: generates the plumbing
```

`SaveState.Generator` only has to be referenced - it is an analyzer package (`analyzers/dotnet/cs`), not a runtime
dependency. On a Godot project add `SaveState.Godot` as well, and for container wiring
`SaveState.DependencyInjection`. See [distribution.md](distribution.md) for local feeds and versioning.

## 2. Declare the state, the service and the file

A **state object** is plain serializable data (derive from `StateBase` if you want change notification for a UI).
A **service** owns state and is registered with the runtime. A **save file** is a class that says "this is one file
on disk"; the generator fills in its implementation.

```csharp
using System.Collections.Generic;
using SaveState;

public sealed class ProfileState : StateBase            // 1) the data
{
    public int Coins { get; set; } = 100;
    public HashSet<int> Unlocked { get; set; } = new HashSet<int>();
}

[SaveService]                                            // 2) the service
public partial class PlayerProfile
{
    [SavedState(File = "profile.json", Key = "Profile")] //    its state goes into "profile.json" as section "Profile"
    public ProfileState State { get; private set; } = new ProfileState();
}

[SaveFile("profile.json")]                               // 3) the file
public sealed partial class ProfileSaveFile : SaveFileDefinition
{
}
```

The generator adds this to `ProfileSaveFile`:

```csharp
public const string FileNameValue = "profile.json";
public override string GetFileName() => FileNameValue;
public override int Version { get; set; } = 1;
public ProfileState Profile { get; set; } = null!;       // one property per [SavedState] key

public override void CollectFrom(SaveRegistry registry)  { /* service -> this */ }
public override void RestoreTo(SaveRegistry registry)    { /* this -> service (null-checked per section) */ }
public override bool FillMissingDefaults(out IReadOnlyList<string> missingSections) { /* repair */ }
```

and, on the service, an `internal` accessor for the member plus an optional post-restore hook:

```csharp
internal ProfileState __SaveState_State { get => this.State; set => this.State = value; }

partial void OnAfterRestore();                            // implement it if you need to react to a load
```

Because the accessor lives inside the class, `State` can keep a `private set` - you do not have to weaken
visibility just to be saveable.

## 3. Create a session and use it

```csharp
using SaveState;

var store    = new FileSystemStore(saveDirectory);       // engine-specific root (see adapters.md)
var registry = new SaveRegistry().Add(playerProfile);    // explicit: no singleton assumption
var session  = new SaveFileSession<ProfileSaveFile>(store, registry, logger);

// First launch: make sure a complete file exists.
session.EnsureCreated();          // writes a default file when missing; repairs + writes back when a section is missing

// Write points are yours to choose:
playerProfile.State.Coins += 50;
session.Save();                   // collect -> serialize -> atomic write

// Loading:
LoadResult result = session.Load();                      // Missing | Loaded | Repaired | Failed
if (result.Status == LoadStatus.Repaired)
{
    logger.Warn("missing sections were repaired: " + string.Join(", ", result.MissingSections));
}
```

`EnsureCreated()` returns whether it wrote something, so a bootstrap step reads:

```csharp
if (session.EnsureCreated())
{
    logger.Info("created a fresh save file");
}
```

## 4. What happens on each call

| Call | What it does |
|---|---|
| `EnsureCreated()` | `Load()`, then: `Missing`/`Repaired` → `Save()`; `Loaded` → nothing; `Failed` → **leaves the corrupt file alone** |
| `Load()` | read → run migrations by version → deserialize → `FillMissingDefaults()` → `RestoreTo()` → `OnAfterRestore()` per service |
| `Save()` | `CollectFrom()` → serialize → atomic write through the store |
| `SerializeSnapshot()` / `WriteSnapshot(json)` | the two halves of `Save()`, so a scheduler can move only the disk write off the game thread |

After a `Load()`, a section that was absent from the file is a **fresh default instance**, never `null`: the
generated `RestoreTo` null-checks every section and `FillMissingDefaults` fills the gaps. That is the framework's
central promise (it used to be the source of a nasty `NullReferenceException` class of bugs).

## 5. Pick your write points

```csharp
// A human asked for it (settings screen "Apply", first launch) -> write directly:
session.Save();

// Gameplay events (unlock, end of a run) -> write often, and gate/schedule them:
using var scheduler = new SaveScheduler(session.Save, logger, TimeSpan.FromMilliseconds(400));
scheduler.RequestSave();          // bursts collapse into one write
scheduler.Flush();                // before quitting

// Never auto-write in editors, headless tools or tests:
var gate = GodotSavePolicy.CreateRuntimeGate(logger);   // or new SaveGate(() => myIsRealRunCheck, logger)
gate.TrySave(session.Save);                             // skipped + logged when the gate is closed
gate.TryRun(scheduler.RequestSave);                     // gate + scheduler together
```

## 6. Test your host against real files - without touching the player's saves

Swap the store; no need to fake the file system or to special-case paths:

```csharp
var store = new InMemoryStore();                                  // nothing hits the disk
var session = new SaveFileSession<ProfileSaveFile>(store, registry);

session.EnsureCreated();
Assert.Contains("\"Coins\": 100", store.Read("profile.json"));     // the file shape is assertable
Assert.Equal(1, store.WriteCount);                                 // "did this write?" is assertable too
```

For "does an old file still load?" tests, write the legacy JSON into the store directly and assert on the loaded
state - see [migrations.md](migrations.md).

## 7. Common mistakes the compiler catches for you

| Symptom | Diagnostic |
|---|---|
| `[SaveFile]` class is not `partial`, or does not derive from `SaveFileDefinition` | SAV001, SAV002 |
| a `[SavedState]` member whose class is not `[SaveService]` | SAV013 |
| a member with no setter (read-only field / get-only property) | SAV009 |
| a section whose type cannot be instantiated (no public parameterless ctor, abstract, interface) | SAV010 |
| two sections with the same key in one file, or a key that is not a valid C# identifier | SAV008, SAV007 |
| a section that does not say which file it belongs to (no `File` given while several files exist) | SAV006 |

The full list is in [api.md](api.md#diagnostics).
