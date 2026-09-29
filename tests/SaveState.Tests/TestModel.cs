using System.Collections.Generic;
using System.Text.Json.Nodes;
using SaveState;

namespace SaveState.Tests;

/// <summary>An ordinary state object (serializable data + change notification).</summary>
public sealed class ProfileState : StateBase
{
    private int _coins = 100;

    public int Coins
    {
        get { return _coins; }
        set { SetProperty(ref _coins, value); }
    }

    public HashSet<int> Unlocked { get; set; } = new HashSet<int>();

    public List<int> History { get; set; } = new List<int>();
}

/// <summary>A save service: its <c>State</c> goes into the "Profile" section of "profile.json".</summary>
[SaveService]
public partial class PlayerProfile
{
    [SavedState(File = "profile.json", Key = "Profile")]
    public ProfileState State { get; private set; } = new ProfileState();

    /// <summary>How many times the generated post-restore hook ran (see OnAfterRestore below).</summary>
    public int RestoreCount { get; private set; }

    /// <summary>Implementing the optional hook proves the hook is generated and invoked by RestoreTo.</summary>
    partial void OnAfterRestore() => RestoreCount++;
}

/// <summary>A save file (the generator supplies the implementation).</summary>
[SaveFile("profile.json")]
public sealed partial class ProfileSaveFile : SaveFileDefinition
{
}

/// <summary>A second save file: version 2, used to exercise migration.</summary>
[SaveFile("legacy.json", Version = 2)]
public sealed partial class LegacySaveFile : SaveFileDefinition
{
}

/// <summary>A second service: its state goes into "legacy.json".</summary>
[SaveService]
public partial class LegacyProfile
{
    [SavedState(File = "legacy.json", Key = "Profile")]
    public ProfileState State { get; private set; } = new ProfileState();
}

/// <summary>v1 -> v2: the legacy file kept the section under "OldProfile"; from v2 on it is renamed to "Profile".</summary>
public sealed class RenameOldProfileKeyMigration : ISaveMigration
{
    public string FileName
    {
        get { return "legacy.json"; }
    }

    public int FromVersion
    {
        get { return 1; }
    }

    public void Apply(JsonObject root)
    {
        JsonNode? old = root["OldProfile"];
        if (old == null)
        {
            return;
        }

        root.Remove("OldProfile");
        root["Profile"] = old;
    }
}
