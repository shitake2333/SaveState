# SaveState.Runtime

The load/write runtime of [SaveState](https://github.com/shitake2333/SaveState).

```bash
dotnet add package SaveState.Runtime
```

## What is in it

* **`SaveFileSession<TFile>`** - the reader/writer of one file: `Load()`, `Save()`, `EnsureCreated()`,
  `Exists()`, `Delete()`, and `SerializeSnapshot()` / `WriteSnapshot(json)` for hosts that want to move only the
  disk write off the game thread. `Load()` returns a `LoadResult` (`Missing` / `Loaded` / `Repaired` / `Failed`).
* **Stores**: `FileSystemStore` (atomic write, file-name validation) and `InMemoryStore` (tests).
* **`SaveJson`** - the JSON policy (explicit `null`s and defaults, so "missing key" can only mean an old file).
* **`SaveGate`** - "may this process write saves right now?" (keep editors, tools and tests off real save files).
* **Migrations**: `ISaveMigration`, `FieldMigration` (old field → new field + conversion) and `JsonUpgrade`.

```csharp
using SaveState;

var session = new SaveFileSession<ProfileSaveFile>(
    new FileSystemStore(saveDirectory),
    new SaveRegistry().Add(playerProfile),
    logger);

session.EnsureCreated();                    // first launch: write a complete default file
LoadResult result = session.Load();         // Missing | Loaded | Repaired | Failed
playerProfile.State.Coins += 50;
session.Save();
```

A missing section is **repaired** (a default instance is written into the file on the next save, and your state is
never set to `null`), and a file that cannot be parsed is **never overwritten** - that is the point of the four
status values.

## Documentation

* [Getting started](https://github.com/shitake2333/SaveState/blob/main/docs/getting-started.md)
* [API reference](https://github.com/shitake2333/SaveState/blob/main/docs/api.md)
* [Migrating old save files](https://github.com/shitake2333/SaveState/blob/main/docs/migrations.md)
* [Adapters](https://github.com/shitake2333/SaveState/blob/main/docs/adapters.md)

MIT licensed.
