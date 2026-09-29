using System.Collections.Generic;
using System.Text.Json.Nodes;
using SaveState;
using Xunit;

namespace SaveState.Tests;

/// <summary>
/// Declarative field migration: hand the storage layer "this old field, that new field, this conversion" and old
/// save files come out in the current shape. These cases cover renames, type changes, added/dropped fields, the
/// silent-no-op trap, and the whole thing through <see cref="SaveFileSession"/>.
/// </summary>
public class FieldMigrationTests
{
    private static JsonObject Parse(string json)
    {
        return (JsonObject)JsonNode.Parse(json)!;
    }

    private static string Text(JsonNode? node)
    {
        return node == null ? "<null>" : node.ToJsonString();
    }

    [Fact]
    public void Rename_MovesAValue_IncludingNestedPaths()
    {
        var root = Parse("{\"Version\":1,\"OldProfile\":{\"Coins\":42,\"Name\":\"Ada\"}}");

        new FieldMigration("profile.json", 1)
            .Rename("OldProfile", "Profile")
            .Apply(root);

        Assert.Null(root["OldProfile"]);
        Assert.Equal(42, (int)root["Profile"]!["Coins"]!);
        Assert.Equal("Ada", (string)root["Profile"]!["Name"]!);
    }

    [Fact]
    public void Rename_OfANestedField_LeavesItsSiblingsAlone()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{\"OldCoins\":7,\"Name\":\"Ada\"}}");

        new FieldMigration("profile.json", 1)
            .Rename("Profile/OldCoins", "Profile/Coins")
            .Apply(root);

        Assert.Equal(7, (int)root["Profile"]!["Coins"]!);
        Assert.Equal("Ada", (string)root["Profile"]!["Name"]!);
        Assert.Null(root["Profile"]!["OldCoins"]);
    }

    [Fact]
    public void Rename_WhenTheOldFieldIsMissing_IsReportedAndChangesNothing()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{\"Coins\":5}}");
        var migration = new FieldMigration("profile.json", 1)
            .Rename("Profile/CoinsTypo", "Profile/Coins");

        migration.Apply(root);

        Assert.Equal(5, (int)root["Profile"]!["Coins"]!);
        string warning = Assert.Single(migration.Warnings);
        Assert.Contains("Profile/CoinsTypo", warning);
    }

    [Fact]
    public void Rename_CanBeToldToStayQuiet()
    {
        var root = Parse("{\"Version\":1}");
        var migration = new FieldMigration("profile.json", 1) { WarnWhenSourceIsMissing = false }
            .Rename("Profile/Old", "Profile/New");

        migration.Apply(root);

        Assert.Empty(migration.Warnings);
    }

    [Fact]
    public void Convert_ChangesTheType_WithTheHostsFunction()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{\"PlayTimeSeconds\":125}}");

        new FieldMigration("profile.json", 1)
            .Convert("Profile/PlayTimeSeconds", "Profile/PlayTime", JsonUpgrade.SecondsToClockText())
            .Apply(root);

        Assert.Null(root["Profile"]!["PlayTimeSeconds"]);
        Assert.Equal("2:05", (string)root["Profile"]!["PlayTime"]!);
    }

    [Fact]
    public void Convert_CanDeriveAValueFromNothing()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{}}");

        // The old field never existed; the conversion still runs with null and may invent a value.
        var migration = new FieldMigration("profile.json", 1)
            .Convert(
                "Profile/Missing",
                "Profile/Title",
                node => JsonValue.Create(node == null ? "Newcomer" : (string?)node));

        migration.Apply(root);

        Assert.Equal("Newcomer", (string)root["Profile"]!["Title"]!);
        Assert.Single(migration.Warnings);
    }

    [Fact]
    public void Convert_ReturningNull_DropsTheOldField()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{\"Secret\":\"x\"}}");

        new FieldMigration("profile.json", 1)
            .Convert("Profile/Secret", "Profile/Secret2", _ => null)
            .Apply(root);

        Assert.Null(root["Profile"]!["Secret"]);
        Assert.Null(root["Profile"]!["Secret2"]);
    }

    [Fact]
    public void Convert_CanMoveAValueIntoANewNestedObject()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{\"Coins\":9}}");

        new FieldMigration("profile.json", 1)
            .Rename("Profile/Coins", "Profile/Wallet/Coins")
            .Apply(root);

        Assert.Equal(9, (int)root["Profile"]!["Wallet"]!["Coins"]!);
        Assert.Null(root["Profile"]!["Coins"]);
    }

    [Fact]
    public void Remove_DropsAFieldThatTheNewShapeDoesNotHave()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{\"Coins\":1,\"Legacy\":true}}");

        new FieldMigration("profile.json", 1)
            .Remove("Profile/Legacy")
            .Apply(root);

        Assert.Null(root["Profile"]!["Legacy"]);
        Assert.Equal(1, (int)root["Profile"]!["Coins"]!);
    }

    [Fact]
    public void SetDefault_OnlyFillsFieldsThatAreAbsent()
    {
        var root = Parse("{\"Version\":1,\"Profile\":{\"Title\":\"Veteran\"}}");

        new FieldMigration("profile.json", 1)
            .SetDefault("Profile/Title", JsonValue.Create("Newcomer"))
            .SetDefault("Profile/Level", JsonValue.Create(1))
            .Apply(root);

        Assert.Equal("Veteran", (string)root["Profile"]!["Title"]!);   // an existing value is never overwritten
        Assert.Equal(1, (int)root["Profile"]!["Level"]!);
    }

    [Fact]
    public void Operations_RunInTheOrderTheyWereAdded()
    {
        var root = Parse("{\"Version\":1,\"Old\":{\"Coins\":\"12\"}}");

        new FieldMigration("profile.json", 1)
            .Rename("Old", "Profile")                                  // rename the section first…
            .Convert("Profile/Coins", "Profile/Coins", JsonUpgrade.ToInt())  // …then fix the type inside it
            .Apply(root);

        Assert.Equal(12, (int)root["Profile"]!["Coins"]!);
    }

    [Theory]
    [InlineData("{\"Value\":12}", 12)]
    [InlineData("{\"Value\":\"12\"}", 12)]          // text that holds a number
    [InlineData("{\"Value\":\"oops\"}", 3)]         // unreadable → fallback
    [InlineData("{\"Other\":1}", 3)]                // missing → fallback
    public void JsonUpgrade_ToInt_IsTolerant(string json, int expected)
    {
        var root = Parse(json);

        new FieldMigration("profile.json", 1)
            .Convert("Value", "Value", JsonUpgrade.ToInt(fallback: 3))
            .Apply(root);

        Assert.Equal(expected, (int)root["Value"]!);
    }

    [Fact]
    public void JsonUpgrade_ToBool_AcceptsTheUsualSpellings()
    {
        foreach ((string json, bool expected) in new (string, bool)[]
                 {
                     ("{\"Value\":true}", true),
                     ("{\"Value\":\"true\"}", true),
                     ("{\"Value\":1}", true),
                     ("{\"Value\":\"yes\"}", true),
                     ("{\"Value\":\"no\"}", false),
                     ("{\"Value\":\"garbage\"}", false),
                 })
        {
            var root = Parse(json);
            new FieldMigration("profile.json", 1)
                .Convert("Value", "Value", JsonUpgrade.ToBool())
                .Apply(root);

            Assert.Equal(expected, (bool)root["Value"]!);
        }
    }

    [Fact]
    public void JsonUpgrade_ToArray_WrapsAScalarAndKeepsAnArray()
    {
        var scalar = Parse("{\"Tags\":\"solo\"}");
        new FieldMigration("profile.json", 1).Convert("Tags", "Tags", JsonUpgrade.ToArray()).Apply(scalar);
        Assert.Equal(1, ((JsonArray)scalar["Tags"]!).Count);
        Assert.Equal("solo", (string)scalar["Tags"]![0]!);

        var array = Parse("{\"Tags\":[\"a\",\"b\"]}");
        new FieldMigration("profile.json", 1).Convert("Tags", "Tags", JsonUpgrade.ToArray()).Apply(array);
        Assert.Equal(2, ((JsonArray)array["Tags"]!).Count);
    }

    [Fact]
    public void JsonUpgrade_ToText_UsesTheProjection()
    {
        var root = Parse("{\"Value\":7}");

        new FieldMigration("profile.json", 1)
            .Convert("Value", "Value", JsonUpgrade.ToText(text => "n=" + text))
            .Apply(root);

        Assert.Equal("n=7", (string)root["Value"]!);
    }

    [Fact]
    public void Migration_RejectsBadConfiguration()
    {
        Assert.Throws<SaveStateException>(() => new FieldMigration("", 1));
        Assert.Throws<SaveStateException>(() => new FieldMigration("profile.json", 0));
        Assert.Throws<SaveStateException>(() => new FieldMigration("profile.json", 1).Rename("", "Target"));
        Assert.Throws<SaveStateException>(() => new FieldMigration("profile.json", 1).Rename("A", " "));
        Assert.Throws<SaveStateException>(() => new FieldMigration("profile.json", 1).Rename("A//B", "C"));
        Assert.Throws<System.ArgumentNullException>(() => new FieldMigration("profile.json", 1).Convert("A", "B", null!));
    }

    // ── Through the session: the actual "old file still loads" promise ─────────────────────────

    [Fact]
    public void LegacyFile_WithARenamedField_LoadsIntoTheCurrentShape()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var service = new LegacyProfile();

        // legacy.json is at version 2; this file is a v1 file that called the field "OldCoins".
        store.Write("legacy.json", "{\n  \"Version\": 1,\n  \"Profile\": { \"OldCoins\": 42, \"Unlocked\": [7] }\n}");

        var migrations = new ISaveMigration[]
        {
            new FieldMigration("legacy.json", fromVersion: 1)
                .Rename("Profile/OldCoins", "Profile/Coins"),
        };

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(service),
            logger,
            migrations);

        LoadResult result = session.Load();

        Assert.True(result.Status == LoadStatus.Loaded, string.Join(" | ", logger.Entries));
        Assert.Equal(42, service.State.Coins);
        Assert.Contains(7, service.State.Unlocked);
    }

    [Fact]
    public void LegacyFile_WithAChangedType_LoadsIntoTheCurrentShape()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var service = new LegacyProfile();

        // v1 stored the coins as text with separators; v2 stores a number.
        store.Write("legacy.json", "{\n  \"Version\": 1,\n  \"Profile\": { \"Coins\": \"1_000\" }\n}");

        var migrations = new ISaveMigration[]
        {
            new FieldMigration("legacy.json", fromVersion: 1)
                .Convert("Profile/Coins", "Profile/Coins", JsonUpgrade.ToText(node => node?.Replace("_", string.Empty)))
                .Convert("Profile/Coins", "Profile/Coins", JsonUpgrade.ToInt()),
        };

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(service),
            logger,
            migrations);

        LoadResult loaded = session.Load();
        Assert.True(loaded.Status == LoadStatus.Loaded, string.Join(" | ", logger.Entries));
        Assert.Equal(1000, service.State.Coins);
    }

    [Fact]
    public void MissingSourceField_IsLoggedByTheSession()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var service = new LegacyProfile();

        store.Write("legacy.json", "{\n  \"Version\": 1,\n  \"Profile\": { \"Coins\": 5 }\n}");

        var migrations = new ISaveMigration[]
        {
            // A typo in the old field name: without the warning this would silently keep the default value.
            new FieldMigration("legacy.json", fromVersion: 1)
                .Rename("Profile/CoinsTypo", "Profile/Coins"),
        };

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(service),
            logger,
            migrations);

        session.Load();

        Assert.True(logger.Has(SaveLogLevel.Warn, "Profile/CoinsTypo"));
        Assert.Equal(5, service.State.Coins);
    }

    [Fact]
    public void SeveralMigrations_ApplyInVersionOrder()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var service = new LegacyProfile();

        // v1 -> v2 renames the section, v2 -> v3 changes the type of a field inside it.
        store.Write("legacy.json", "{\n  \"Version\": 1,\n  \"OldProfile\": { \"Coins\": \"7\" }\n}");

        var migrations = new ISaveMigration[]
        {
            // Registered out of order on purpose: the session sorts by version.
            new FieldMigration("legacy.json", fromVersion: 2)
                .Convert("Profile/Coins", "Profile/Coins", JsonUpgrade.ToInt()),
            new FieldMigration("legacy.json", fromVersion: 1)
                .Rename("OldProfile", "Profile"),
        };

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(service),
            logger,
            migrations);

        LoadResult loaded = session.Load();
        Assert.True(loaded.Status == LoadStatus.Loaded, string.Join(" | ", logger.Entries));
        Assert.Equal(7, service.State.Coins);
    }
}
