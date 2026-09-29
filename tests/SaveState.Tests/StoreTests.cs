using System;
using System.IO;
using Xunit;

namespace SaveState.Tests;

public class StoreTests
{
    private static string NewTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SaveState.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void FileSystemStore_RoundTrips()
    {
        string dir = NewTempDirectory();
        try
        {
            var store = new FileSystemStore(dir);

            Assert.False(store.Exists("a.json"));
            store.Write("a.json", "{\"x\":1}");
            Assert.True(store.Exists("a.json"));
            Assert.Equal("{\"x\":1}", store.Read("a.json"));
            Assert.True(store.Delete("a.json"));
            Assert.False(store.Delete("a.json"));
            Assert.False(store.Exists("a.json"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileSystemStore_LeavesNoTempFileBehind_AndOverwritesAtomically()
    {
        string dir = NewTempDirectory();
        try
        {
            var store = new FileSystemStore(dir);
            store.Write("a.json", "first");
            store.Write("a.json", "second");

            Assert.Equal("second", store.Read("a.json"));
            Assert.False(File.Exists(Path.Combine(dir, "a.json.tmp")), "The temp file of the atomic write must already have been replaced");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileSystemStore_RejectsPathsInFileName()
    {
        var store = new FileSystemStore(NewTempDirectory());

        Assert.Throws<SaveStateException>(() => store.Write("sub/a.json", "x"));
        Assert.Throws<SaveStateException>(() => store.Write("..\\a.json", "x"));
        Assert.Throws<SaveStateException>(() => store.Write(string.Empty, "x"));
    }

    [Fact]
    public void FileSystemStore_RequiresARootDirectory()
    {
        Assert.Throws<ArgumentException>(() => new FileSystemStore("  "));
    }

    [Fact]
    public void InMemoryStore_CountsWrites()
    {
        var store = new InMemoryStore();

        Assert.False(store.Exists("a.json"));
        store.Write("a.json", "1");
        store.Write("a.json", "2");

        Assert.Equal(2, store.WriteCount);
        Assert.Equal("2", store.Read("a.json"));
        Assert.True(store.Delete("a.json"));
        Assert.Empty(store.Files);
        Assert.Equal("memory://a.json", store.Describe("a.json"));
    }

    [Fact]
    public void InMemoryStore_ReadOfMissingFileThrows()
    {
        var store = new InMemoryStore();
        Assert.Throws<SaveStateException>(() => store.Read("nope.json"));
    }
}
