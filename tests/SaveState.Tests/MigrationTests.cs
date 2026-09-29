using Xunit;

namespace SaveState.Tests;

/// <summary>
/// Version migration: a file in the old shape must be upgradeable **without knowing the old C# types**
/// (a migration only changes the JSON shape).
/// </summary>
public class MigrationTests
{
    [Fact]
    public void MigratesLegacyShapeAndKeepsTheData()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var service = new LegacyProfile();

        // A v1 file: the section is called OldProfile (it is called Profile from v2 on).
        store.Write("legacy.json", "{\n  \"Version\": 1,\n  \"OldProfile\": { \"Coins\": 42 }\n}");

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(service),
            logger,
            new[] { new RenameOldProfileKeyMigration() });

        Assert.Equal(2, session.TargetVersion);

        LoadResult result = session.Load();

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Empty(result.MissingSections);
        Assert.Equal(42, service.State.Coins);
        Assert.True(logger.Has(SaveLogLevel.Info, "Migrated "));
    }

    [Fact]
    public void MissingMigration_LogsWarningAndStillLoadsWhatItCan()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var service = new LegacyProfile();

        // The version is behind (1 < 2) but no migration is registered: warn and keep reading
        // whatever shape is there.
        store.Write("legacy.json", "{\n  \"Version\": 1,\n  \"Profile\": { \"Coins\": 7 }\n}");

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(service),
            logger);

        LoadResult result = session.Load();

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(7, service.State.Coins);
        Assert.True(logger.Has(SaveLogLevel.Warn, "has no migration for v1 -> v2"));
    }

    [Fact]
    public void UpToDateFile_DoesNotRunMigrations()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();

        store.Write("legacy.json", "{\n  \"Version\": 2,\n  \"Profile\": { \"Coins\": 5 }\n}");

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(new LegacyProfile()),
            logger,
            new[] { new RenameOldProfileKeyMigration() });

        Assert.Equal(LoadStatus.Loaded, session.Load().Status);
        Assert.False(logger.Has(SaveLogLevel.Info, "Migrated "));
    }

    [Fact]
    public void FileWithoutVersionKey_IsTreatedAsV1()
    {
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var service = new LegacyProfile();

        store.Write("legacy.json", "{\n  \"OldProfile\": { \"Coins\": 11 }\n}");

        var session = new SaveFileSession<LegacySaveFile>(
            store,
            new SaveRegistry().Add(service),
            logger,
            new[] { new RenameOldProfileKeyMigration() });

        Assert.Equal(LoadStatus.Loaded, session.Load().Status);
        Assert.Equal(11, service.State.Coins);
    }
}
