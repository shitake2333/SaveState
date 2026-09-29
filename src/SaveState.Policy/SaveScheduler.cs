using System;
using System.Threading;

namespace SaveState.Policy
{
    /// <summary>
    /// Save scheduler: it **merges** several "save requests" made within a short span into a single real write to disk.
    ///
    /// <para><b>Why it is needed</b>: real write points tend to come in bursts - three items unlocked in a row during
    /// one settlement, a score record after every turn, a slider dragged on the settings screen. Writing to disk
    /// synchronously every time is both wasteful and potentially frame-hitching; yet "write only once on exit" loses
    /// progress (a crash or a force-kill). Debouncing is the common-sense answer in between.</para>
    ///
    /// <para><b>Usage</b>: call <c>scheduler.RequestSave()</c> as many times as you like; once the window is over it
    /// writes to disk once. Call <see cref="Flush"/> (or <see cref="Dispose"/>) before shutting down so nothing is lost.</para>
    ///
    /// <para><b>Failure semantics</b>: a failed write is only logged (never thrown) - a save failure should not
    /// interrupt the match; to observe failures, inject your own <see cref="ISaveLogger"/> or call the core's
    /// <c>Save()</c> directly.</para>
    /// </summary>
    public sealed class SaveScheduler : IDisposable
    {
        private readonly Func<bool> _save;
        private readonly ISaveLogger _logger;
        private readonly object _gate = new object();

        private Timer? _timer;
        private bool _pending;
        private bool _disposed;

        /// <param name="save">The real write-to-disk action (usually <c>session.Save</c>).</param>
        /// <param name="logger">Sink for write failures.</param>
        /// <param name="debounce">The merge window; 400ms by default. Several requests inside the window produce a single write.</param>
        public SaveScheduler(Func<bool> save, ISaveLogger? logger = null, TimeSpan? debounce = null)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _logger = logger ?? NullSaveLogger.Instance;
            Debounce = debounce ?? TimeSpan.FromMilliseconds(400);
        }

        /// <summary>The merge window.</summary>
        public TimeSpan Debounce { get; }

        /// <summary>How many requests have been received in total (for diagnostics/tests).</summary>
        public int RequestCount { get; private set; }

        /// <summary>How many writes to disk actually happened in total (for diagnostics/tests).</summary>
        public int SaveCount { get; private set; }

        /// <summary>Requests a save (merged within the window; restarts the window timer).</summary>
        public void RequestSave()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                RequestCount++;
                _pending = true;

                if (_timer == null)
                {
                    _timer = new Timer(OnTimer, null, Debounce, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
                }
            }
        }

        /// <summary>Writes to disk immediately (does nothing when there is no pending request).</summary>
        /// <returns>Whether a write really happened.</returns>
        public bool Flush()
        {
            lock (_gate)
            {
                if (!_pending)
                {
                    return false;
                }

                _pending = false;
            }

            // The callback runs outside the lock: writing to disk may take a while, and it may in turn request a save (do not deadlock).
            return SaveNow();
        }

        /// <summary>Stops the timer and writes the last batch to disk (used by the shutdown path).</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }

            Flush();
        }

        private void OnTimer(object? state)
        {
            Flush();
        }

        private bool SaveNow()
        {
            SaveCount++;
            try
            {
                return _save();
            }
            catch (Exception ex)
            {
                _logger.Log(SaveLogLevel.Error, "The save scheduler failed to write.", ex);
                return false;
            }
        }
    }
}
