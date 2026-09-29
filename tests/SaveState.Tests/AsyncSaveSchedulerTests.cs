using System;
using System.Threading;
using SaveState;
using SaveState.Policy;
using Xunit;

namespace SaveState.Tests;

/// <summary>
/// Asynchronous saves: the snapshot stays on the caller's thread, the disk write moves to a worker, and a
/// burst of requests collapses into one or two writes with the newest state ending up in the file.
/// </summary>
public class AsyncSaveSchedulerTests
{
    private static (SlowStore Store, PlayerProfile Profile, SaveFileSession<ProfileSaveFile> Session) NewSession(
        SlowStore store,
        CapturingLogger? logger = null)
    {
        var profile = new PlayerProfile();
        var session = new SaveFileSession<ProfileSaveFile>(store, new SaveRegistry().Add(profile), logger);
        return (store, profile, session);
    }

    [Fact]
    public void RequestSave_ThenFlush_WritesTheCurrentState()
    {
        var store = new SlowStore { Delay = TimeSpan.Zero };
        var (_, profile, session) = NewSession(store);
        var logger = new CapturingLogger();

        using (AsyncSaveScheduler scheduler = session.ScheduleAsync(logger))
        {
            profile.State.Coins = 555;
            scheduler.RequestSave();

            Assert.True(scheduler.Flush());
            Assert.Equal(1, scheduler.SaveCount);
            Assert.Equal(1, scheduler.RequestCount);
        }

        Assert.Contains("\"Coins\": 555", store.Read("profile.json"));
    }

    [Fact]
    public void RequestSave_DoesNotWaitForTheDiskWrite_AndTheSnapshotStaysOnTheCallingThread()
    {
        int callerThread = Thread.CurrentThread.ManagedThreadId;
        int snapshotThread = 0;
        bool writeFinished = false;

        using var writeStarted = new ManualResetEventSlim(false);
        using var releaseWrite = new ManualResetEventSlim(false);

        using (var scheduler = new AsyncSaveScheduler(
                   () =>
                   {
                       snapshotThread = Thread.CurrentThread.ManagedThreadId;
                       return "{}";
                   },
                   _ =>
                   {
                       writeStarted.Set();
                       releaseWrite.Wait(TimeSpan.FromSeconds(10));
                       writeFinished = true;
                       return true;
                   }))
        {
            // RequestSave takes the snapshot here and hands the bytes to a worker: it must return while the
            // write is still in progress. If the write ran on this thread, RequestSave could not return at all
            // until the write finished - which is exactly what the assertions below catch.
            scheduler.RequestSave();

            Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(10)), "the write should have started on a worker");
            Assert.False(writeFinished, "RequestSave() waited for the disk write instead of scheduling it");

            // The snapshot, on the other hand, must run where RequestSave was called: collecting state on a
            // background thread would race with the game mutating it.
            Assert.Equal(callerThread, snapshotThread);

            releaseWrite.Set();
            Assert.True(scheduler.Flush());
            Assert.Equal(1, scheduler.SaveCount);
        }
    }

    [Fact]
    public void BurstOfRequests_CollapsesIntoFewerWrites_AndTheNewestStateWins()
    {
        var store = new SlowStore { Delay = TimeSpan.FromMilliseconds(50) };
        var (_, profile, session) = NewSession(store);

        using (AsyncSaveScheduler scheduler = session.ScheduleAsync())
        {
            for (int coins = 1; coins <= 5; coins++)
            {
                profile.State.Coins = coins * 100;
                scheduler.RequestSave();
            }

            scheduler.Flush();

            Assert.Equal(5, scheduler.RequestCount);
            Assert.True(store.Writes < 5, $"expected coalescing, but every request was written ({store.Writes})");
            Assert.True(scheduler.SkippedCount >= 1, "at least one superseded snapshot should have been dropped");
        }

        // Whatever got dropped, the file must end up holding the LAST snapshot.
        Assert.Contains("\"Coins\": 500", store.Read("profile.json"));
    }

    [Fact]
    public void Flush_WithNothingPending_ReturnsFalse()
    {
        using (AsyncSaveScheduler scheduler = new AsyncSaveScheduler(() => "{}", _ => true))
        {
            Assert.False(scheduler.Flush());
            Assert.Equal(0, scheduler.SaveCount);
        }
    }

    [Fact]
    public void Dispose_WritesThePendingSnapshot()
    {
        var store = new SlowStore { Delay = TimeSpan.Zero };
        var (_, profile, session) = NewSession(store);

        var scheduler = session.ScheduleAsync();
        profile.State.Coins = 777;
        scheduler.RequestSave();
        scheduler.Dispose();

        Assert.Equal(1, scheduler.SaveCount);
        Assert.Contains("\"Coins\": 777", store.Read("profile.json"));
    }

    [Fact]
    public void ThrowingSnapshot_IsLoggedAndSkipped_AndTheSchedulerStaysUsable()
    {
        var logger = new CapturingLogger();
        bool fail = true;
        int writes = 0;

        using (var scheduler = new AsyncSaveScheduler(
                   () => fail ? throw new InvalidOperationException("snapshot blew up (injected by the test)") : "{}",
                   _ => { writes++; return true; },
                   logger))
        {
            scheduler.RequestSave();
            scheduler.Flush();

            Assert.Equal(0, writes);
            Assert.True(logger.Has(SaveLogLevel.Error, "snapshot failed"));

            // A failing snapshot must not poison the scheduler: the next one goes through.
            fail = false;
            scheduler.RequestSave();
            scheduler.Flush();
            Assert.Equal(1, writes);
        }
    }

    [Fact]
    public void FailingWrite_IsNotCounted_AndDoesNotThrow()
    {
        var logger = new CapturingLogger();

        using (var scheduler = new AsyncSaveScheduler(() => "{}", _ => false, logger))
        {
            scheduler.RequestSave();
            scheduler.Flush();

            Assert.Equal(1, scheduler.RequestCount);
            Assert.Equal(0, scheduler.SaveCount);
        }
    }

    [Fact]
    public void RequestAfterDispose_IsIgnored()
    {
        int writes = 0;
        var scheduler = new AsyncSaveScheduler(() => "{}", _ => { writes++; return true; });
        scheduler.Dispose();

        scheduler.RequestSave();
        Assert.Equal(0, writes);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void SessionSnapshotAndWrite_CanBeUsedDirectly()
    {
        var store = new SlowStore { Delay = TimeSpan.Zero };
        var (_, profile, session) = NewSession(store);
        profile.State.Coins = 42;

        string snapshot = session.SerializeSnapshot();
        Assert.Contains("\"Coins\": 42", snapshot);
        Assert.False(store.Exists("profile.json"), "taking a snapshot must not touch the store");

        Assert.True(session.WriteSnapshot(snapshot));
        Assert.Contains("\"Coins\": 42", store.Read("profile.json"));
    }

    /// <summary>In-memory store with an artificial write delay, so coalescing is observable without timing flakiness.</summary>
    private sealed class SlowStore : ISaveStore
    {
        private readonly InMemoryStore _inner = new InMemoryStore();

        public int Writes { get; private set; }

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public bool Exists(string fileName)
        {
            return _inner.Exists(fileName);
        }

        public string Read(string fileName)
        {
            return _inner.Read(fileName);
        }

        public void Write(string fileName, string contents)
        {
            if (Delay > TimeSpan.Zero)
            {
                Thread.Sleep(Delay);
            }

            Writes++;
            _inner.Write(fileName, contents);
        }

        public bool Delete(string fileName)
        {
            return _inner.Delete(fileName);
        }

        public string Describe(string fileName)
        {
            return _inner.Describe(fileName);
        }
    }
}
