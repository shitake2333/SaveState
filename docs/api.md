# API reference

Everything public, per package. Signatures are C#; `?` marks nullable.

---

## SaveState.Shared

Attributes, contracts and the two data types the runtime hands back. No dependencies (not even `System.Text.Json`),
so an adapter for any engine can reference only this package.

### Attributes

#### `[SaveFile]` — `SaveFileAttribute`

Declares one save file. Place it on a **partial class** that derives from `SaveFileDefinition`.

```csharp
[SaveFile("global.json", Version = 2)]
public sealed partial class GlobalSaveFile : SaveFileDefinition { }
```

| Member | Type | Meaning |
|---|---|---|
| `SaveFileAttribute(string fileName)` | ctor | File name relative to the store root. Must be a plain name (no `/`, `\`, `..`); otherwise SAV004. |
| `FileName` | `string` | The name as given. |
| `Version` | `int` (init, default `1`) | Current schema version written into the file and used as the migration target. |

#### `[SaveService]` — `SaveServiceAttribute`

Marks a class whose `[SavedState]` members take part in saving. The class must be `partial` (SAV012) and top-level
(SAV005). The service instance is created and registered by the host - the library never assumes a singleton.

#### `[SavedState]` — `SavedStateAttribute`

Marks a property or field that becomes one **section** of a save file.

| Member | Type | Meaning |
|---|---|---|
| `Key` | `string?` (init) | Section key (and the generated DTO property name). Default: the member name. Must be a valid identifier (SAV007). |
| `File` | `string?` (init) | The `[SaveFile]` name this section belongs to. May be omitted when the assembly has exactly one save file; otherwise it is required (SAV006). |

The member may be private and may have a `private set` - the generator writes the accessor inside the class.

### `SaveFileDefinition` (abstract)

The base class of every declared save file. You declare the class; the generator implements these members:

| Member | Meaning |
|---|---|
| `abstract string GetFileName()` | The file name, from the attribute. A *method*, not a property, so it is never serialized into the file. |
| `abstract int Version { get; set; }` | Current version (serialized). |
| `abstract void CollectFrom(SaveRegistry registry)` | Service state → this DTO. Called before writing. |
| `abstract void RestoreTo(SaveRegistry registry)` | This DTO → service state, **null-checking every section**, then calling each service's `OnAfterRestore`. Called after reading. |
| `abstract bool FillMissingDefaults(out IReadOnlyList<string> missingSections)` | Fills absent sections with default instances; returns whether anything was repaired. |

### `SaveRegistry`

| Member | Meaning |
|---|---|
| `SaveRegistry Add<T>(T service) where T : class` | Registers a service instance (keyed by `typeof(T)`). Throws `SaveStateException` on a duplicate. Returns `this`, so calls chain. |
| `T Get<T>() where T : class` | Returns the instance; throws `SaveStateException` naming the type when it was never registered. |
| `bool TryGet<T>(out T? service)` | Non-throwing lookup. |
| `int Count` | Number of registered services. |

The generated code resolves services through `Get<T>()`, so a forgotten registration fails immediately (with the
type name) instead of silently writing an empty file.

### `ISaveStore`

The only place the framework touches the outside world. Implement it for your engine (see
[adapters.md](adapters.md)); `SaveState.Runtime` ships a file-system and an in-memory implementation.

| Member | Contract |
|---|---|
| `bool Exists(string fileName)` | Does the file exist? |
| `string Read(string fileName)` | Full UTF-8 text. |
| `void Write(string fileName, string contents)` | Must be **atomic** (complete write or the old file untouched). |
| `bool Delete(string fileName)` | Returns whether something was deleted. |
| `string Describe(string fileName)` | A human-readable location for logs (usually the absolute path). |

### `ISaveLogger`

| Member | Meaning |
|---|---|
| `void Log(SaveLogLevel level, string message, Exception? exception = null)` | The framework's only output. Used for non-fatal problems (failed write, repaired section, migration gap) and skipped writes. |

`SaveLogLevel`: `Debug`, `Info`, `Warn`, `Error`.
Implementations: `NullSaveLogger.Instance` (default), `DelegateSaveLogger(Action<SaveLogLevel, string, Exception?>)`.

### `LoadStatus` / `LoadResult`

`LoadStatus`: `Missing`, `Loaded`, `Repaired`, `Failed` - four outcomes, because they need four different reactions.

`LoadResult` (readonly struct):

| Member | Meaning |
|---|---|
| `LoadStatus Status` | The outcome. |
| `IReadOnlyList<string> MissingSections` | Sections that were absent and got default instances (`Repaired`). |
| `Exception? Error` | The cause when `Failed`. |
| `bool NeedsWriteBack` | `Status == Repaired` - the caller may want to `EnsureCreated()`. |
| `LoadResult.Missing()` / `Loaded()` / `Repaired(...)` / `Failed(...)` | Factories (also used by custom `ISaveStore`-level tests). |

### `StateBase` (abstract)

Optional base for state objects: `INotifyPropertyChanged` plumbing.

| Member | Meaning |
|---|---|
| `event PropertyChangedEventHandler? PropertyChanged` | Standard change notification. |
| `protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)` | Assigns and notifies only when the value really changed; returns whether it changed. |
| `public void OnPropertyChanged([CallerMemberName] string? propertyName = null)` | Notify manually - needed when a collection field is mutated in place. |

### `SaveStateException`

Thrown for **wiring and contract errors** (service not registered, invalid file name, a migration that was misconfigured,
`SaveFileSession` without a file name). Data problems never use it - they come back as `LoadStatus.Failed`.

---

## SaveState.Runtime

### `SaveFileSession<TFile>` — `where TFile : SaveFileDefinition, new()`

The reader/writer of one save file.

```csharp
public SaveFileSession(
    ISaveStore store,
    SaveRegistry registry,
    ISaveLogger? logger = null,
    IEnumerable<ISaveMigration>? migrations = null,
    JsonSerializerOptions? options = null,
    string? fileName = null)
```

`fileName` overrides the `[SaveFile]` name. That is how several files share one shape: save slots
(`save_0.json`, `save_1.json`, …) use one DTO and one version, and the host passes the name per slot.
When neither is available the constructor throws `SaveStateException`.

| Member | Behaviour |
|---|---|
| `string FileName` | Resolved file name. |
| `int TargetVersion` | The DTO's current version (migration target). |
| `bool Exists()` / `bool Delete()` | File presence / removal. |
| `LoadResult Load()` | Read → migrate (by version) → deserialize → repair → restore → `OnAfterRestore`. Data/IO problems → `Failed` (logged); a missing service → `SaveStateException`. |
| `bool EnsureCreated()` | `Missing`/`Repaired` → write and return `true`; `Loaded` → `false`; `Failed` → `false` **without touching the file**. |
| `bool Save()` | `WriteSnapshot(SerializeSnapshot())`. Returns `false` (and logs) on failure. |
| `string SerializeSnapshot()` | Snapshot half: collect + serialize. **Must run on the thread that owns the state.** |
| `bool WriteSnapshot(string json)` | Write half: store write only; safe to run on a worker. |

### `SaveJson`

| Member | Meaning |
|---|---|
| `static JsonSerializerOptions DefaultOptions` | Shared options: case-insensitive, indented, comments skipped, trailing commas allowed, numbers readable from strings, and **nothing ignored** - a written file always contains every field, so "missing key" can only mean "old/hand-edited file". |
| `static JsonSerializerOptions CreateDefaultOptions()` | A fresh copy to tweak before passing to a session. |

### `FileSystemStore`

`FileSystemStore(string rootDirectory)`; throws `ArgumentException` for an empty root.

* `string Root` - the directory.
* `Write` is atomic: write `.tmp`, then replace (`File.Replace`, with a delete+move fallback), and create the
  directory when needed.
* File names are validated: no `/`, `\`, `..` or rooted paths (throws `SaveStateException`) - a save can never
  escape the root.
* `Read` expects the file to exist; use `Exists` first.

### `InMemoryStore`

Zero I/O store for tests and embedded use.

| Member | Meaning |
|---|---|
| `int WriteCount` | How many writes happened (assert "did this write?"). |
| `IReadOnlyDictionary<string, string> Files` | Current contents. |
| `Read` on a missing file | Throws `SaveStateException`. |

### `SaveGate`

Answers "may this process write saves right now?" and is how hosts keep editors, headless tools and test runs away
from real save files.

```csharp
public SaveGate(Func<bool> canWrite, ISaveLogger? logger = null)
```

| Member | Meaning |
|---|---|
| `bool? AllowOverride` | Forces open/closed; `null` (default) asks the predicate. Test seam. |
| `bool IsOpen` | `AllowOverride ?? canWrite()`. |
| `bool TrySave(Func<bool> save)` | Runs `save` when open and returns its result; otherwise logs at `Debug` and returns `false`. |
| `bool TryRun(Action save)` | Same for write points without a result (e.g. `AsyncSaveScheduler.RequestSave`). Separate name because an `Action` overload would make `() => session.Save` ambiguous. |

### Migrations

| Type | Members |
|---|---|
| `ISaveMigration` | `string FileName`, `int FromVersion` (upgrades *from* it, producing `FromVersion + 1`), `void Apply(JsonObject root)`. |
| `ISaveMigrationDiagnostics` (optional) | `IReadOnlyList<string> Warnings` from the last `Apply`; `SaveFileSession` logs them. |
| `FieldMigration` | See below. |

#### `FieldMigration` — declare old field → new field (+ conversion)

```csharp
public FieldMigration(string fileName, int fromVersion)
```

| Member | Meaning |
|---|---|
| `FieldMigration Rename(string sourcePath, string targetPath)` | Moves the value unchanged (a rename, or a move elsewhere). |
| `FieldMigration Convert(string sourcePath, string targetPath, Func<JsonNode?, JsonNode?> convert)` | Moves the value through `convert` - where type/shape changes happen. `convert` receives `null` when the source is absent and may derive a value; returning `null` drops the field. |
| `FieldMigration Remove(string path)` | Drops a field the new shape no longer has. |
| `FieldMigration SetDefault(string path, JsonNode? value)` | Adds `value` only when the field is absent (existing values are never overwritten). |
| `bool WarnWhenSourceIsMissing` | Default `true`: report a source path that matched nothing. |
| `IReadOnlyList<string> Warnings` | Paths that matched nothing during the last `Apply`. |
| `void Apply(JsonObject root)` | Applies the operations in the order they were added. |

Paths are `/`-separated (`"Profile/Coins"`); dots are not separators because keys such as mod package names contain
them. Intermediate objects of a new target are created as needed.

#### `JsonUpgrade` — ready-made conversions

| Method | Converts to |
|---|---|
| `Keep()` | the value unchanged |
| `ToInt(int fallback = 0)` | JSON number, rounded; tolerant of strings |
| `ToNumber(double fallback = 0)` | JSON number |
| `ToBool(bool fallback = false)` | `true`/`false`/`1`/`0`/`yes`/`no` (case-insensitive) |
| `ToText(string fallback = "")` | JSON string |
| `ToText(Func<string?, string?> project)` | JSON string built from the old value as text |
| `ToArray()` | single-element array from a scalar, arrays kept as-is |
| `SecondsToClockText()` | `mm:ss` text from a number of seconds |

None of them throw: an unreadable value becomes the fallback.

---

## SaveState.Policy (optional)

### `SaveScheduler` — coalesce/debounce

```csharp
public SaveScheduler(Func<bool> save, ISaveLogger? logger = null, TimeSpan? debounce = null)  // default 400 ms
```

| Member | Meaning |
|---|---|
| `TimeSpan Debounce` | The merge window. |
| `int RequestCount` / `int SaveCount` | Requests received / writes performed. |
| `void RequestSave()` | Records a request; a burst inside one window becomes one write. |
| `bool Flush()` | Writes now if something is pending; returns whether it wrote. |
| `void Dispose()` | Stops the timer and flushes. |

Failures are logged, never thrown.

### `AsyncSaveScheduler` — write off the game thread

```csharp
public AsyncSaveScheduler(Func<string> snapshot, Func<string, bool> write, ISaveLogger? logger = null)
```

`RequestSave()` calls `snapshot()` **on the calling thread** (collecting state on a background thread would race
with the game mutating it) and hands the text to a single worker: intermediate snapshots are dropped, the newest one
is written, so the file ends up holding the latest state.

| Member | Meaning |
|---|---|
| `int RequestCount` / `int SaveCount` / `int SkippedCount` | Requests / writes / snapshots superseded before being written. |
| `void RequestSave()` | Snapshot here, write on a worker. |
| `bool Flush()` | Blocks until everything pending is written; returns whether there was work. |
| `void Dispose()` | Requests a caller-side `Flush()` (call it from the thread that owns the state), then stops. |

Convenience: `session.ScheduleAsync(logger)` (`SaveSessionExtensions`) wires a session's
`SerializeSnapshot`/`WriteSnapshot` into a scheduler.

---

## SaveState.Godot

| Type | Members |
|---|---|
| `GodotSaveStore` | `GodotSaveStore(string subdirectory = "")` (e.g. `"saves"` → `user://saves/`, rejects `..`/`:`), `string Root`. Atomic writes and file-name validation come from `FileSystemStore`. |
| `GodotSaveLogger` | `static GodotSaveLogger Instance`, `Log(...)` → `GD.PushError` / `PushWarning` / `Print` with a `[SaveState]` prefix. |
| `GodotRuntime` | `static bool IsEditor` (`Engine.IsEditorHint()` or the `editor` feature tag), `static bool IsHeadless` (`--headless`), `static bool IsRuntimeProcess` (neither). |
| `GodotSavePolicy` | `static SaveGate CreateRuntimeGate(ISaveLogger? logger = null)` - gate that only allows writes in a real game run. |

The store root is `ProjectSettings.GlobalizePath("user://…")`, so saves land where Godot expects them on every platform.

---

## SaveState.DependencyInjection

```csharp
services.AddSaveService<PlayerProfile>();          // your services, registered normally

services.AddSaveState(options =>
{
    options.Store    = new FileSystemStore(dir);   // required
    options.Logger   = new DelegateSaveLogger(...); // optional
    options.Migrations.Add(new FieldMigration("profile.json", 1));   // optional
    options.JsonOptions = null;                    // optional: SaveJson.DefaultOptions otherwise
    options.RegisterServices = (registry, resolve) => registry.AddGeneratedServices(resolve);  // recommended
});

var session = provider.GetSaveFileSession<ProfileSaveFile>();
```

| Member | Meaning |
|---|---|
| `SaveStateOptions.Store` | `ISaveStore`; `AddSaveState` throws `SaveStateException` when it is missing. |
| `SaveStateOptions.Logger` | `ISaveLogger` (defaults to `NullSaveLogger`). |
| `SaveStateOptions.Migrations` | Migrations handed to every session. |
| `SaveStateOptions.JsonOptions` | Optional JSON override. |
| `SaveStateOptions.RegisterServices` | `(SaveRegistry, Func<Type, object>)` - fills the registry from the container. Pass the generated `registry.AddGeneratedServices(resolve)` and no service can be forgotten; manual `registry.Add(...)` works too. |
| `AddSaveService<TService>()` | Registers a service as a singleton (sugar for intent). |
| `GetSaveFileSession<TFile>()` | Builds a session from the container. |

---

## Generated members

| Where | Member | Notes |
|---|---|---|
| file class | `const string FileNameValue` | The `[SaveFile]` name. |
| file class | `override string GetFileName()` | Returns `FileNameValue`. |
| file class | `override int Version { get; set; } = <SaveFile.Version>` | Serialized migration anchor. |
| file class | one property per section key | Populated by `CollectFrom`, read by `RestoreTo`. |
| file class | `override void CollectFrom(SaveRegistry)`, `override void RestoreTo(SaveRegistry)`, `override bool FillMissingDefaults(out IReadOnlyList<string>)` | The plumbing described above. |
| service class | `internal <T> __SaveState_<Member> { get; set; }` | Accessor, so the member can stay private. |
| service class | `partial void OnAfterRestore()` + `internal void __InvokeAfterRestore()` | Optional post-restore hook, called once per service per file after its sections are in place (also when a section was repaired). |
| assembly | `internal static class SaveServiceRegistration` (namespace `SaveState`) | `AddGeneratedServices(this SaveRegistry, Func<Type, object>)` - registers every `[SaveService]` of the assembly through your resolver (used by the DI package). Emitted only when the assembly has at least one service. |

Generated code lives in one file: `obj/<config>/generated/SaveState.Generator/SaveState.Generator.SaveStateGenerator/SaveState.Generated.g.cs`.

---

## Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| SAV001 | Error | `[SaveFile]` class is not `partial` |
| SAV002 | Error | `[SaveFile]` class does not derive from `SaveFileDefinition` |
| SAV003 | Error | `[SaveFile]` class needs a public parameterless constructor |
| SAV004 | Error | save file name is not a plain file name |
| SAV005 | Error | save file or service is a nested/generic type |
| SAV006 | Error | a section's file cannot be determined |
| SAV007 | Error | section key is not a valid C# identifier |
| SAV008 | Error | section key appears twice in one file |
| SAV009 | Error | `[SavedState]` member is not writable (read-only field / no setter) |
| SAV010 | Error | section type cannot be instantiated (default instance needed for a repair) |
| SAV011 | Warning | save file has no sections |
| SAV012 | Error | `[SaveService]` class is not `partial` |
| SAV013 | Error | `[SavedState]` member whose class is not marked `[SaveService]` |

Release tracking lives next to the generator (`AnalyzerReleases.Shipped.md` / `.Unshipped.md`) and is validated at
build time, so the table above cannot drift away from the descriptors.

---

## The file format

```json
{
  "Version": 1,
  "Profile": {
    "Coins": 100,
    "Unlocked": [7, 9]
  }
}
```

* `Version` is always written; a file without it is treated as version 1.
* Section keys are the `[SavedState]` keys - they are part of the contract, so renaming one is a *shape change*
  (bump `Version`, add a migration; see [migrations.md](migrations.md)).
* Every field is written, including defaults and `null`s, so the file describes the full shape.
* Unknown keys are ignored on read (a newer file read by an older build loses the extra data on the next write).
