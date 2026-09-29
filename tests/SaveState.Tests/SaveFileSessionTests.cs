using System;
using Xunit;

namespace SaveState.Tests;

/// <summary>
/// The core contract of the save session. These test cases correspond to the very first incident
/// in this project: "a legacy file is missing one section -> the service's state is written as
/// null -> the next gameplay action throws an NRE".
/// </summary>
public class SaveFileSessionTests
{
    private static (InMemoryStore Store, PlayerProfile Profile, SaveFileSession<ProfileSaveFile> Session) NewSession(
        CapturingLogger? logger = null)
    {
        var store = new InMemoryStore();
        var profile = new PlayerProfile();
        var session = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(profile), logger);
        return (store, profile, session);
    }

    [Fact]
    public void Load_WhenFileMissing_ReportsMissing()
    {
        var session = NewSession().Session;

        LoadResult result = session.Load();

        Assert.Equal(LoadStatus.Missing, result.Status);
        Assert.False(result.NeedsWriteBack);
        Assert.Empty(result.MissingSections);
    }

    [Fact]
    public void EnsureCreated_OnFirstRun_WritesDefaults_ThenBecomesNoOp()
    {
        var (store, _, session) = NewSession();

        Assert.True(session.EnsureCreated(), "First launch: a default save file should be written");
        Assert.True(store.Exists("profile.json"));
        Assert.Equal(1, store.WriteCount);

        Assert.False(session.EnsureCreated(), "The file is already complete: it must not be written again");
        Assert.Equal(1, store.WriteCount);
        Assert.Equal(LoadStatus.Loaded, session.Load().Status);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsState()
    {
        var (store, profile, session) = NewSession();
        profile.State.Coins = 4242;
        profile.State.Unlocked.Add(7);
        profile.State.History.Add(3);

        Assert.True(session.Save());

        // Read it back through a fresh service instance (the equivalent of "restarting the game").
        var reloaded = new PlayerProfile();
        var session2 = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(reloaded));

        Assert.Equal(LoadStatus.Loaded, session2.Load().Status);
        Assert.Equal(4242, reloaded.State.Coins);
        Assert.Contains(7, reloaded.State.Unlocked);
        Assert.Contains(3, reloaded.State.History);
    }

    [Fact]
    public void Load_WhenSectionIsMissing_RepairsInsteadOfNullingTheState()
    {
        var logger = new CapturingLogger();
        var (store, profile, session) = NewSession(logger);

        // Simulate a legacy file: only a version number, no sections at all (exactly the shape
        // of that 2026-09-29 global.json).
        store.Write("profile.json", "{\n  \"Version\": 1\n}");

        LoadResult result = session.Load();

        Assert.Equal(LoadStatus.Repaired, result.Status);
        Assert.True(result.NeedsWriteBack);
        Assert.Contains("Profile", result.MissingSections);

        // ★ Core contract: the state is never null (in the old implementation this line turned into
        // null and the very next gameplay action blew up).
        ProfileState state = profile.State;
        Assert.NotNull(state);
        Assert.Equal(100, state.Coins);
        Assert.True(logger.Has(SaveLogLevel.Warn, "is missing"));

        // Write the repair back -> the next load is a complete file.
        Assert.True(session.EnsureCreated());
        LoadResult second = session.Load();
        Assert.Equal(LoadStatus.Loaded, second.Status);
        Assert.Empty(second.MissingSections);
    }

    [Fact]
    public void Load_WhenFileIsCorrupt_FailsAndIsNeverOverwrittenByDefaults()
    {
        var (store, _, session) = NewSession();
        const string broken = "{ \"Version\": 1, \"Profile\": ";
        store.Write("profile.json", broken);

        LoadResult result = session.Load();

        Assert.Equal(LoadStatus.Failed, result.Status);
        Assert.NotNull(result.Error);

        // A corrupt file is data: EnsureCreated must not overwrite it with defaults.
        Assert.False(session.EnsureCreated());
        Assert.Equal(broken, store.Read("profile.json"));
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public void Load_WhenServiceIsNotRegistered_FailsFast()
    {
        var store = new InMemoryStore();
        store.Write("profile.json", "{\n  \"Version\": 1\n}");

        var session = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry());

        SaveStateException error = Assert.Throws<SaveStateException>(() => session.Load());
        Assert.Contains("PlayerProfile", error.Message);
    }

    [Fact]
    public void Save_WhenStoreThrows_ReturnsFalseAndLogsError()
    {
        var logger = new CapturingLogger();
        var session = new SaveFileSession<ProfileSaveFile>(
            new ThrowingStore(),
            new SaveRegistry().Add(new PlayerProfile()),
            logger);

        Assert.False(session.Save());
        Assert.True(logger.Has(SaveLogLevel.Error, "Failed to write"));
    }

    [Fact]
    public void Load_WhenReadThrows_ReturnsFailedAndLogs()
    {
        var logger = new CapturingLogger();
        var session = new SaveFileSession<ProfileSaveFile>(
            new ThrowingStore(),
            new SaveRegistry().Add(new PlayerProfile()),
            logger);

        LoadResult result = session.Load();

        Assert.Equal(LoadStatus.Failed, result.Status);
        Assert.True(logger.Has(SaveLogLevel.Error, "Read failed"));
    }

    [Fact]
    public void Delete_RemovesTheFile()
    {
        var (store, _, session) = NewSession();
        Assert.True(session.EnsureCreated());

        Assert.True(session.Delete());
        Assert.False(store.Exists("profile.json"));
        Assert.False(session.Delete());
    }

    [Fact]
    public void Save_WritesDefaultAndNullishValuesExplicitly()
    {
        var (store, profile, session) = NewSession();
        profile.State.Coins = 0;

        Assert.True(session.Save());
        string json = store.Read("profile.json");

        // Deliberately "do not ignore defaults": a missing key and an empty value must stay
        // distinguishable (see the notes on SaveJson).
        Assert.Contains("\"Coins\": 0", json);
        Assert.Contains("\"Unlocked\"", json);
        Assert.Contains("\"Version\": 1", json);
    }

    [Fact]
    public void TargetVersion_ComesFromTheSaveFileAttribute()
    {
        Assert.Equal(1, new SaveFileSession<ProfileSaveFile>(new InMemoryStore(), new SaveRegistry()).TargetVersion);
        Assert.Equal(2, new SaveFileSession<LegacySaveFile>(new InMemoryStore(), new SaveRegistry()).TargetVersion);
    }

    [Fact]
    public void RestoreCallsTheGeneratedOnAfterRestoreHook()
    {
        var (store, profile, session) = NewSession();
        Assert.Equal(0, profile.RestoreCount);

        // A missing file is not a restore.
        session.Load();
        Assert.Equal(0, profile.RestoreCount);

        profile.State.Coins = 7;
        Assert.True(session.Save());

        var reloaded = new PlayerProfile();
        var loadSession = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(reloaded));

        Assert.Equal(LoadStatus.Loaded, loadSession.Load().Status);
        Assert.Equal(1, reloaded.RestoreCount);

        // A repaired (missing-section) load also counts: the state was replaced by a default instance.
        store.Write("profile.json", "{\n  \"Version\": 1\n}");
        var afterRepair = new PlayerProfile();
        Assert.Equal(
            LoadStatus.Repaired,
            new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(afterRepair)).Load().Status);
        Assert.Equal(1, afterRepair.RestoreCount);
    }

    [Fact]
    public void ExplicitFileName_ServesSeveralFilesWithOneShape()
    {
        var store = new InMemoryStore();
        var first = new PlayerProfile();
        var second = new PlayerProfile();

        // Save slots: one DTO shape, one version, several files.
        var slot0 = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(first), null, null, null, "save_0.json");
        var slot1 = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(second), null, null, null, "save_1.json");

        Assert.Equal("save_0.json", slot0.FileName);
        Assert.Equal("save_1.json", slot1.FileName);

        first.State.Coins = 11;
        second.State.Coins = 22;
        Assert.True(slot0.Save());
        Assert.True(slot1.Save());

        Assert.Contains("\"Coins\": 11", store.Read("save_0.json"));
        Assert.Contains("\"Coins\": 22", store.Read("save_1.json"));
    }

    [Fact]
    public void FileName_ComesFromTheSaveFileAttribute()
    {
        Assert.Equal("profile.json", new SaveFileSession<ProfileSaveFile>(new InMemoryStore(), new SaveRegistry()).FileName);
    }
}
