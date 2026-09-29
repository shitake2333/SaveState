# SaveState.Shared

Contracts of [SaveState](https://github.com/shitake2333/SaveState) - the package that has no dependencies at all,
so any project (and any engine adapter) can reference it.

```bash
dotnet add package SaveState.Shared
```

## What is in it

* **Attributes**: `[SaveFile("global.json", Version = 2)]`, `[SaveService]`, `[SavedState(Key = "…", File = "…")]`.
* **Base types**: `SaveFileDefinition` (the base of every generated save-file DTO), `SaveRegistry`, `StateBase`.
* **Engine boundary**: `ISaveStore` (where the bytes go), `ISaveLogger` (where diagnostics go).
* **Results**: `LoadStatus` / `LoadResult` - `Missing`, `Loaded`, `Repaired`, `Failed`.
* **`SaveStateException`** - thrown for wiring errors (a service that was never registered, an invalid file name).

Usually referenced together with [`SaveState.Generator`](https://www.nuget.org/packages/SaveState.Generator)
(generates the plumbing) and [`SaveState.Runtime`](https://www.nuget.org/packages/SaveState.Runtime)
(loads and writes the files).

## Documentation

* [Getting started](https://github.com/shitake2333/SaveState/blob/main/docs/getting-started.md)
* [API reference](https://github.com/shitake2333/SaveState/blob/main/docs/api.md)

MIT licensed.
