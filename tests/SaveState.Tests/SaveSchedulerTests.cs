using System;
using System.Threading;
using SaveState.Policy;
using Xunit;

namespace SaveState.Tests;

public class SaveSchedulerTests
{
    [Fact]
    public void RequestSave_CollapsesManyRequestsIntoOneWrite()
    {
        int saves = 0;
        using var scheduler = new SaveScheduler(
            () => { saves++; return true; },
            debounce: TimeSpan.FromMinutes(1));   // A very long window: only Flush triggers a write

        scheduler.RequestSave();
        scheduler.RequestSave();
        scheduler.RequestSave();

        Assert.Equal(0, saves);
        Assert.Equal(3, scheduler.RequestCount);

        Assert.True(scheduler.Flush());
        Assert.Equal(1, saves);
        Assert.Equal(1, scheduler.SaveCount);

        Assert.False(scheduler.Flush(), "Flush must not write to disk when there is no pending request");
        Assert.Equal(1, saves);
    }

    [Fact]
    public void Timer_WritesAfterTheWindow()
    {
        int saves = 0;
        using var scheduler = new SaveScheduler(
            () => { saves++; return true; },
            debounce: TimeSpan.FromMilliseconds(40));

        scheduler.RequestSave();

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref saves) == 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Dispose_WritesPendingChanges()
    {
        int saves = 0;
        var scheduler = new SaveScheduler(
            () => { saves++; return true; },
            debounce: TimeSpan.FromMinutes(1));

        scheduler.RequestSave();
        scheduler.Dispose();

        Assert.Equal(1, saves);

        // After Dispose, further save requests must be ignored (no throw, no write).
        scheduler.RequestSave();
        Assert.Equal(1, saves);
    }

    [Fact]
    public void FailingSave_IsLoggedAndDoesNotThrow()
    {
        var logger = new CapturingLogger();
        using var scheduler = new SaveScheduler(
            () => throw new InvalidOperationException("save blew up (injected by the test)"),
            logger,
            TimeSpan.FromMinutes(1));

        scheduler.RequestSave();

        Assert.False(scheduler.Flush());
        Assert.True(logger.Has(SaveLogLevel.Error, "The save scheduler failed to write"));
    }
}
