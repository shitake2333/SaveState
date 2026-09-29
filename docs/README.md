# SaveState documentation

SaveState turns plain classes into **strongly typed save files**: you declare the shape, a source generator writes
the plumbing, and the runtime loads old files leniently (missing sections are repaired, corrupt files are never
overwritten, structure changes are migrated).

## The mental model

```
your service (plain class, [SaveService])
    └── its state property   ([SavedState(File = "global.json", Key = "Collection")])
            └── a save file  ([SaveFile("global.json")] → a generated DTO + Collect/Restore/Repair)
                    └── bytes ([ISaveStore]: user://, a directory, memory for tests)
                            └── driven by SaveFileSession<TFile> (Load / Save / EnsureCreated)
```

Three rules explain almost every API decision:

1. **The host owns policy.** Where saves live, when they are written and what happens on first launch are yours;
   the library only offers `Load` / `Save` / `EnsureCreated` primitives (plus optional gate/scheduler helpers).
2. **Loading degrades, wiring fails.** A file that cannot be parsed or that misses a section is *data* - you get a
   `LoadResult` and keep playing. A service that was never registered is a *bug* - you get a `SaveStateException`.
3. **The generated code is inspectable.** Everything the generator emits lives in one file
   (`obj/…/SaveState.Generated.g.cs`) and reads like ordinary hand-written code.

## Where to go next

| Document | Read it when |
|---|---|
| [getting-started.md](getting-started.md) | you want a working save file in five minutes, and to understand the first-launch / save / load flow |
| [api.md](api.md) | you need the reference: every public type, member, exception and generated member |
| [migrations.md](migrations.md) | the save shape changed and old files must keep loading (rename a field, change its type) |
| [adapters.md](adapters.md) | you need a store for your engine (Godot is built in), or container wiring |
| [distribution.md](distribution.md) | you build, pack, publish or consume the packages |

## Packages

| Package | Target | What it is |
|---|---|---|
| `SaveState.Shared` | `netstandard2.0`, `net10.0` | attributes, `SaveFileDefinition`, `SaveRegistry`, `ISaveStore`, `ISaveLogger`, `StateBase`, `LoadResult` |
| `SaveState.Generator` | analyzer (`netstandard2.0`) | the source generator + `SAV001`–`SAV013` diagnostics |
| `SaveState.Runtime` | `netstandard2.0`, `net10.0` | `SaveFileSession<TFile>`, stores, `SaveJson`, `SaveGate`, migrations |
| `SaveState.Policy` (optional) | `netstandard2.0`, `net10.0` | `SaveScheduler` (coalesce/debounce) and `AsyncSaveScheduler` (write off the game thread) |
| `SaveState.Godot` | `net10.0` (Godot 4.7) | `user://` store, Godot output logger, runtime-process gate |
| `SaveState.DependencyInjection` | `netstandard2.0`, `net10.0` | `AddSaveState(...)` + `GetSaveFileSession<TFile>()` |

The repository also contains `samples/SaveState.Samples.Console` - a runnable, commented end-to-end example.
