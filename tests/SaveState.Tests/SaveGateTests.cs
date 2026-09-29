using System;
using Xunit;

namespace SaveState.Tests;

/// <summary>
/// The auto-save gate: event-driven saves must be skipped in processes that are not real game runs
/// (editor, headless tools, CI) and the rule must be easy to verify from a test.
/// </summary>
public class SaveGateTests
{
    [Fact]
    public void TrySave_RunsTheWrite_WhenTheGateIsOpen()
    {
        int writes = 0;
        var gate = new SaveGate(() => true);

        Assert.True(gate.IsOpen);
        Assert.True(gate.TrySave(() => { writes++; return true; }));
        Assert.Equal(1, writes);
    }

    [Fact]
    public void TrySave_SkipsTheWrite_WhenTheGateIsClosed_AndLogsAtDebugLevel()
    {
        int writes = 0;
        var logger = new CapturingLogger();
        var gate = new SaveGate(() => false, logger);

        Assert.False(gate.IsOpen);
        Assert.False(gate.TrySave(() => { writes++; return true; }));
        Assert.Equal(0, writes);
        Assert.True(logger.Has(SaveLogLevel.Debug, "Auto-save skipped"));
    }

    [Fact]
    public void AllowOverride_WinsOverThePredicate()
    {
        var gate = new SaveGate(() => false);

        gate.AllowOverride = true;
        Assert.True(gate.IsOpen);

        gate.AllowOverride = false;
        Assert.False(gate.IsOpen);

        // null = ask the predicate again (this is what tests reset to).
        gate.AllowOverride = null;
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void TrySave_PropagatesTheSaveResult()
    {
        var gate = new SaveGate(() => true);

        Assert.False(gate.TrySave(() => false));
    }

    [Fact]
    public void TryRun_IsForWritePointsWithoutAResult()
    {
        int scheduled = 0;
        var open = new SaveGate(() => true);
        var closed = new SaveGate(() => false);

        Assert.True(open.TryRun(() => scheduled++));
        Assert.False(closed.TryRun(() => scheduled++));
        Assert.Equal(1, scheduled);
    }

    [Fact]
    public void SaveGate_RequiresAPredicate()
    {
        Assert.Throws<ArgumentNullException>(() => new SaveGate(null!));
    }
}
