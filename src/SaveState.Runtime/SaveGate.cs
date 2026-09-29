using System;

namespace SaveState
{
    /// <summary>
    /// A small gate that answers one question: <em>may this process write saves right now?</em>
    ///
    /// <para><b>Why it exists.</b> Auto-saving on gameplay events (an unlock, the end of a run) is
    /// convenient, but the very same events fire in processes that must never touch the player's real
    /// save files: the editor, a headless test run, a screenshot/CI tool, a replay viewer. Hosts used to
    /// solve this with an ad-hoc static flag that each new entry point had to remember to set — and a
    /// forgotten flag either polluted a developer's save (silently) or turned saving off in production
    /// (silently). This type makes the rule explicit and testable.</para>
    ///
    /// <para><b>Usage.</b> Give it a predicate for "real run" (for Godot:
    /// <c>GodotSavePolicy.CreateRuntimeGate()</c>) and route event-driven saves through
    /// <see cref="TrySave"/>. Tests and tooling can force the answer with <see cref="AllowOverride"/>
    /// without touching the predicate.</para>
    ///
    /// <para>Write points that are <em>not</em> event driven (the settings screen pressing "Apply",
    /// the loading pipeline creating a first file) should call <c>SaveFileSession.Save()</c> directly:
    /// a human asked for them, so they should not be gated.</para>
    /// </summary>
    public sealed class SaveGate
    {
        private readonly Func<bool> _canWrite;
        private readonly ISaveLogger _logger;

        /// <param name="canWrite">Predicate: is this a process where auto-saving is allowed?</param>
        /// <param name="logger">Where skipped writes are reported (Debug level).</param>
        public SaveGate(Func<bool> canWrite, ISaveLogger? logger = null)
        {
            _canWrite = canWrite ?? throw new ArgumentNullException(nameof(canWrite));
            _logger = logger ?? NullSaveLogger.Instance;
        }

        /// <summary>
        /// Forces the gate open (<c>true</c>) or shut (<c>false</c>) regardless of the predicate;
        /// <c>null</c> (default) means "ask the predicate". Intended for tests and tooling.
        /// </summary>
        public bool? AllowOverride { get; set; }

        /// <summary>Whether a write is currently allowed.</summary>
        public bool IsOpen
        {
            get
            {
                bool? forced = AllowOverride;
                return forced ?? _canWrite();
            }
        }

        /// <summary>
        /// Runs <paramref name="save"/> only when <see cref="IsOpen"/>; otherwise logs at Debug level
        /// and returns <c>false</c> (a skipped write is normal, not an error).
        /// </summary>
        /// <returns>Whether the write ran (the result of <paramref name="save"/> when it ran).</returns>
        public bool TrySave(Func<bool> save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            if (!IsOpen)
            {
                _logger.Log(SaveLogLevel.Debug, "Auto-save skipped: this process is not a game run.");
                return false;
            }

            return save();
        }

        /// <summary>
        /// Runs <paramref name="save"/> only when <see cref="IsOpen"/>, for write points that have no result
        /// of their own - typically "schedule this write" (<c>AsyncSaveScheduler.RequestSave</c>) rather than
        /// "perform this write now".
        ///
        /// <para>Separate from <see cref="TrySave(Func{bool})"/> on purpose: an <c>Action</c> overload would make
        /// <c>() =&gt; session.Save</c> ambiguous (a method call is both a statement and an expression).</para>
        /// </summary>
        /// <returns>Whether the write point ran.</returns>
        public bool TryRun(Action save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            if (!IsOpen)
            {
                _logger.Log(SaveLogLevel.Debug, "Auto-save skipped: this process is not a game run.");
                return false;
            }

            save();
            return true;
        }
    }
}
