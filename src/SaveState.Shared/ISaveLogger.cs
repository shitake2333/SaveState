using System;

namespace SaveState
{
    /// <summary>Log levels (deliberately decoupled from <c>Microsoft.Extensions.Logging</c>: the library does not pick a logging framework for the host).</summary>
    public enum SaveLogLevel
    {
        Debug,
        Info,
        Warn,
        Error,
    }

    /// <summary>
    /// The log channel. The framework uses it only to record **non-fatal** problems (a failed write to disk, a missing
    /// section, a gap in migration), because "the save is playing up" should not interrupt the player's game.
    /// </summary>
    public interface ISaveLogger
    {
        void Log(SaveLogLevel level, string message, Exception? exception = null);
    }

    /// <summary>Log implementation that does nothing (the default).</summary>
    public sealed class NullSaveLogger : ISaveLogger
    {
        /// <summary>The singleton (stateless).</summary>
        public static readonly NullSaveLogger Instance = new NullSaveLogger();

        private NullSaveLogger()
        {
        }

        public void Log(SaveLogLevel level, string message, Exception? exception = null)
        {
        }
    }

    /// <summary>Forwards log entries to a delegate (an adapter for the console or a hand-rolled logging pipeline).</summary>
    public sealed class DelegateSaveLogger : ISaveLogger
    {
        private readonly Action<SaveLogLevel, string, Exception?> _sink;

        public DelegateSaveLogger(Action<SaveLogLevel, string, Exception?> sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public void Log(SaveLogLevel level, string message, Exception? exception = null)
        {
            _sink(level, message, exception);
        }
    }
}
