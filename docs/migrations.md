# Migrating old save files

When a save shape changes, old files must keep loading. SaveState does that in two steps:

1. **Version the file**: `[SaveFile("profile.json", Version = 2)]` — the current shape's version.
2. **Describe the change**: register one migration per version step. A migration only rewrites the JSON *shape*
   (before deserialization), so it never references the old C# types - they do not have to stay in your codebase.

```csharp
var migrations = new ISaveMigration[]
{
    new FieldMigration("profile.json", fromVersion: 1)
        .Rename("OldProfile", "Profile")                                            // a section was renamed
        .Convert("Profile/PlayTimeSeconds", "Profile/PlayTime", JsonUpgrade.SecondsToClockText())  // it changed type
        .SetDefault("Profile/Title", JsonValue.Create("Newcomer"))                  // a field was added
        .Remove("Profile/LegacyFlag"),                                              // a field was dropped

    new FieldMigration("profile.json", fromVersion: 2)
        .Convert("Profile/Coins", "Profile/Coins", JsonUpgrade.ToInt()),            // "12" -> 12
};

var session = new SaveFileSession<ProfileSaveFile>(store, registry, logger, migrations);
```

`Load()` reads the file's `Version` and runs **every** migration from there up to `TargetVersion`, in version order
(registration order does not matter). The `Version` field is bumped as it goes, so the rest of the pipeline sees the
current shape.

## The operations

| Operation | Use it for |
|---|---|
| `Rename(oldPath, newPath)` | a field (or a whole section) was renamed or moved; the value is kept as-is |
| `Convert(oldPath, newPath, convert)` | the field changed type or shape - `convert` gets the old node (or `null` when absent) and returns the new one; `null` drops the field |
| `SetDefault(path, value)` | a field that only exists from this version on; an existing value is never overwritten |
| `Remove(path)` | a field the new shape no longer has |

Operations run in the order you added them, so you can rename a section and then fix a field inside it.

### Paths

`/`-separated object paths: `"Profile/Coins"`, `"Profile/Wallet/Coins"`. A leading slash is allowed. Arrays are not
traversed - migrate the field that holds the array.

**Dots are not separators** on purpose: keys such as mod package names (`com.example.mod`) contain them.

Intermediate objects of a new target are created as needed, so a value can move into a new nested object:

```csharp
.Rename("Profile/Coins", "Profile/Wallet/Coins")
```

### Conversions

`convert` is `Func<JsonNode?, JsonNode?>`: full control, no type parameters to fight. `JsonUpgrade` has the everyday
cases, and none of them throw - an unreadable value becomes the fallback, because a migration runs on a player's real
file and "one field is garbled" must not become "the save cannot load".

| Helper | Result |
|---|---|
| `JsonUpgrade.Keep()` | unchanged |
| `JsonUpgrade.ToInt(int fallback = 0)` | number, rounded; strings like `"12"` are parsed |
| `JsonUpgrade.ToNumber(double fallback = 0)` | number |
| `JsonUpgrade.ToBool(bool fallback = false)` | `true`/`false`/`1`/`0`/`yes`/`no` |
| `JsonUpgrade.ToText(string fallback = "")` | string (numbers/booleans as text) |
| `JsonUpgrade.ToText(project)` | string built from the old value as text (`Func<string?, string?>`) |
| `JsonUpgrade.ToArray()` | a scalar becomes a one-element array; an array stays as it is |
| `JsonUpgrade.SecondsToClockText()` | `125` → `"2:05"` |

Anything else is a lambda:

```csharp
.Convert("Profile/TotalMinutes", "Profile/TotalSeconds", node => JsonValue.Create((int)(node?.GetValue<double>() ?? 0) * 60))
```

## Reports instead of silence

The most common migration failure is a **silent no-op**: the old field name does not match anything, so the file
loads with the default value and the player quietly loses progress. `FieldMigration` records that:

```csharp
var migration = new FieldMigration("profile.json", fromVersion: 1)
    .Rename("Profile/CoinsTypo", "Profile/Coins");   // oops: the real name was "Coins"

// migration.Warnings -> ["field \"Profile/CoinsTypo\" was not found"]
```

and `SaveFileSession` logs the warnings through your `ISaveLogger`:

```
Warn  profile.json migration v1 -> v2: field "Profile/CoinsTypo" was not found.
```

Set `WarnWhenSourceIsMissing = false` for an operation that is legitimately allowed to find nothing.

## Writing your own migration

`FieldMigration` is the declarative option. For a structural rewrite (splitting one section into two, recomputing
derived data), implement `ISaveMigration` directly - it is the same interface:

```csharp
public sealed class SplitWalletMigration : ISaveMigration, ISaveMigrationDiagnostics
{
    private readonly List<string> _warnings = new List<string>();

    public string FileName => "profile.json";
    public int FromVersion => 3;
    public IReadOnlyList<string> Warnings => _warnings;

    public void Apply(JsonObject root)
    {
        _warnings.Clear();
        if (root["Profile"] is not JsonObject profile || profile["Coins"] is not JsonNode coins)
        {
            _warnings.Add("Profile/Coins was not found");
            return;
        }

        profile.Remove("Coins");
        profile["Wallet"] = new JsonObject { ["Coins"] = coins.DeepClone() };
    }
}
```

## Rules of thumb

* **Bump `Version` in the same commit** as the shape change, and add the migration for the old version.
* **Never reuse a key for different data** - add a new key and migrate, otherwise old values are silently reinterpreted.
* **Migrations are additive**: keep them forever (a player may be several versions behind and every step must run).
* Test them the way the runtime does: put the old JSON into an `InMemoryStore` and assert on the loaded state.

```csharp
[Fact]
public void LegacyFile_LoadsIntoTheCurrentShape()
{
    var store = new InMemoryStore();
    var service = new PlayerProfile();

    store.Write("profile.json", "{\n  \"Version\": 1,\n  \"Profile\": { \"OldCoins\": 42 }\n}");

    var session = new SaveFileSession<ProfileSaveFile>(
        store,
        new SaveRegistry().Add(service),
        migrations: new ISaveMigration[]
        {
            new FieldMigration("profile.json", fromVersion: 1)
                .Rename("Profile/OldCoins", "Profile/Coins"),
        });

    Assert.Equal(LoadStatus.Loaded, session.Load().Status);
    Assert.Equal(42, service.State.Coins);
}
```

## When the version gap has no migration

A file whose version is behind but has no migration for a step is loaded **as it is** (a warning is logged):
it is better to let the player continue with whatever the current shape can read than to refuse the file.
If that is too permissive for your situation, check `LoadResult.MissingSections` / your own logger in the host.
