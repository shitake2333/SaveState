using System;
using System.Collections.Generic;
using System.IO;
using SaveState;
using SaveState.Policy;

namespace SaveState.Samples.Console;

// ── 1) A state object: plain serializable data + change notification ─────────
public sealed class ProfileState : StateBase
{
    public int Coins { get; set; } = 100;

    public HashSet<int> Unlocked { get; set; } = new HashSet<int>();
}

// ── 2) A service: its State goes into the "Profile" section of profile.json ───
[SaveService]
public partial class PlayerProfile
{
    [SavedState(File = "profile.json", Key = "Profile")]
    public ProfileState State { get; private set; } = new ProfileState();
}

// ── 3) A save file (the implementation is supplied by the generator) ──────────
[SaveFile("profile.json")]
public sealed partial class ProfileSaveFile : SaveFileDefinition
{
}

public static class Program
{
    public static int Main(string[] args)
    {
        string root = args.Length > 0
            ? args[0]
            : Path.Combine(Path.GetTempPath(), "SaveState.Sample");

        // The demo is repeatable: every run starts from "first launch" (no save file).
        Directory.CreateDirectory(root);
        string filePath = Path.Combine(root, "profile.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        var store = new FileSystemStore(root);
        var logger = new DelegateSaveLogger((level, message, exception) =>
            System.Console.WriteLine("    [" + level + "] " + message));

        System.Console.WriteLine("Save directory: " + root);

        // ── First launch: create a file when there is none ─────────────────
        var profile = new PlayerProfile();
        var registry = new SaveRegistry().Add(profile);
        var session = new SaveFileSession<ProfileSaveFile>(store, registry, logger);

        System.Console.WriteLine();
        System.Console.WriteLine("1) First launch: EnsureCreated -> " + session.EnsureCreated());
        PrintFile(store, "profile.json");

        // ── Gameplay changes + the policy package (merge/debounce) writing to disk ──
        profile.State.Coins += 250;
        profile.State.Unlocked.Add(7);
        profile.State.Unlocked.Add(9);

        using (var scheduler = new SaveScheduler(session.Save, logger, TimeSpan.FromMilliseconds(50)))
        {
            // Three items unlocked in a row inside one settlement: three requests -> one write.
            scheduler.RequestSave();
            scheduler.RequestSave();
            scheduler.RequestSave();
            scheduler.Flush();
            System.Console.WriteLine();
            System.Console.WriteLine("2) Three save requests -> actual writes: " + scheduler.SaveCount);
        }

        PrintFile(store, "profile.json");

        // ── Reload (the equivalent of restarting the game) ─────────────────
        var reloaded = new PlayerProfile();
        var loadSession = new SaveFileSession<ProfileSaveFile>(
            store,
            new SaveRegistry().Add(reloaded),
            logger);

        System.Console.WriteLine();
        System.Console.WriteLine("3) Reloading: " + loadSession.Load());
        System.Console.WriteLine("   coins=" + reloaded.State.Coins + ", unlocked=" + reloaded.State.Unlocked.Count);

        // ── The legacy file is missing a section: it is filled with defaults and the
        //    state does **not** become null ─────────────────────────────────────────
        store.Write("profile.json", "{\n  \"Version\": 1\n}");
        var afterLegacy = new PlayerProfile();
        var legacySession = new SaveFileSession<ProfileSaveFile>(
            store,
            new SaveRegistry().Add(afterLegacy),
            logger);

        System.Console.WriteLine();
        System.Console.WriteLine("4) Loading a legacy file with only a version (Profile section missing)");
        LoadResult result = legacySession.Load();
        System.Console.WriteLine("   result: " + result);
        System.Console.WriteLine("   is State null: " + (afterLegacy.State == null));
        System.Console.WriteLine("   after write-back repair: " + legacySession.EnsureCreated() + " -> reload: " + legacySession.Load());

        System.Console.WriteLine();
        System.Console.WriteLine("Done.");
        return 0;
    }

    private static void PrintFile(ISaveStore store, string fileName)
    {
        System.Console.WriteLine("   " + store.Describe(fileName) + ":");
        foreach (string line in store.Read(fileName).Split('\n'))
        {
            System.Console.WriteLine("     " + line.TrimEnd('\r'));
        }
    }
}
