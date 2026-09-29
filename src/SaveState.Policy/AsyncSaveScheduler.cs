using System;
using System.Threading;
using System.Threading.Tasks;

namespace SaveState.Policy
{
    /// <summary>
    /// Moves the <em>disk write</em> off the caller's thread while keeping the <em>snapshot</em> on it.
    ///
    /// <para><b>The split is the whole point.</b> Serializing game state on a background thread is a data
    /// race waiting to happen (the game keeps mutating lists/sets while the serializer walks them), so
    /// <see cref="RequestSave"/> takes the snapshot synchronously on the calling thread and only hands the
    /// finished JSON text to a worker. Collection + serialization are microseconds; the file write (and the
    /// filesystem flush behind it) is the part that can hitch a frame.</para>
    ///
    /// <para><b>Coalescing.</b> If requests arrive while a write is in flight, the intermediate snapshots
    /// are dropped and only the <em>newest</em> one is written (a save file is a full snapshot, so the older
    /// ones are pointless). That also guarantees the file ends up holding the latest state:
    /// there is exactly one writer at a time, in order.</para>
    ///
    /// <para><b>Failure semantics.</b> A snapshot that throws is logged and skipped (the game keeps running).
    /// A write that fails is logged by the session/store and does not interrupt the scheduler.</para>
    ///
    /// <para><b>Shutdown.</b> Call <see cref="Flush"/> (or <see cref="Dispose"/>) before quitting, otherwise
    /// a pending snapshot is lost.</para>
    /// </summary>
    public sealed class AsyncSaveScheduler : IDisposable
    {
        private readonly Func<string> _snapshot;
        private readonly Func<string, bool> _write;
        private readonly ISaveLogger _logger;
        private readonly object _gate = new object();

        /// <summary>The newest snapshot waiting to be written; <c>null</c> means "nothing to do".</summary>
        private string? _pending;

        /// <summary>True while a worker owns <see cref="_pending"/> / is inside the write.</summary>
        private bool _writing;

        private Task _worker = Task.CompletedTask;

        private int _requestCount;
        private int _saveCount;
        private int _skippedCount;
        private bool _disposed;

        /// <param name="snapshot">Takes a snapshot of the state (runs on the calling thread).</param>
        /// <param name="write">Writes a snapshot (runs on the worker thread; returns success).</param>
        /// <param name="logger">Where skipped/failed work is reported.</param>
        public AsyncSaveScheduler(Func<string> snapshot, Func<string, bool> write, ISaveLogger? logger = null)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _logger = logger ?? NullSaveLogger.Instance;
        }

        /// <summary>How many save requests were made.</summary>
        public int RequestCount
        {
            get { return Volatile.Read(ref _requestCount); }
        }

        /// <summary>How many snapshots actually reached the store.</summary>
        public int SaveCount
        {
            get { return Volatile.Read(ref _saveCount); }
        }

        /// <summary>How many snapshots were replaced before they could be written (the coalescing effect).</summary>
        public int SkippedCount
        {
            get { return Volatile.Read(ref _skippedCount); }
        }

        /// <summary>
        /// Takes a snapshot on the calling thread and schedules it to be written.
        /// Safe to call repeatedly from the same (main) thread; a burst collapses into one or two writes.
        /// </summary>
        public void RequestSave()
        {
            Interlocked.Increment(ref _requestCount);

            string json;
            try
            {
                json = _snapshot();
            }
            catch (SaveStateException ex)
            {
                // Wiring error: keep it visible - it cannot be fixed by retrying.
                _logger.Log(SaveLogLevel.Error, "Async save snapshot failed (wiring).", ex);
                return;
            }
            catch (Exception ex)
            {
                _logger.Log(SaveLogLevel.Error, "Async save snapshot failed; this save was skipped.", ex);
                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (_pending != null)
                {
                    // The previous snapshot has not been written yet: the newer one supersedes it.
                    Interlocked.Increment(ref _skippedCount);
                }

                _pending = json;

                if (!_writing)
                {
                    _writing = true;
                    _worker = Task.Run((Action)WorkerLoop);
                }
            }
        }

        /// <summary>
        /// Blocks until every pending snapshot has been written (quit flow, or a test that wants to assert
        /// the file content). Call it from the thread that owns the state.
        /// </summary>
        /// <returns>Whether there was still work to wait for.</returns>
        public bool Flush()
        {
            bool hadWork = false;

            while (true)
            {
                Task worker;
                lock (_gate)
                {
                    if (_pending == null && !_writing)
                    {
                        return hadWork;
                    }

                    hadWork = true;
                    worker = _worker;
                }

                if (!worker.IsCompleted)
                {
                    try
                    {
                        worker.Wait();
                    }
                    catch (AggregateException ex)
                    {
                        _logger.Log(
                            SaveLogLevel.Error,
                            "Async save worker failed.",
                            ex.InnerException ?? ex);
                    }
                }
                else
                {
                    // The worker is between "cleared _pending" and "returned": let it finish and re-check.
                    Thread.Sleep(0);
                }
            }
        }

        /// <summary>Flushes the pending snapshot and stops the scheduler.</summary>
        public void Dispose()
        {
            Flush();

            lock (_gate)
            {
                _disposed = true;
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                string? json;
                lock (_gate)
                {
                    json = _pending;
                    _pending = null;

                    if (json == null)
                    {
                        _writing = false;
                        Monitor.PulseAll(_gate);
                        return;
                    }
                }

                bool ok;
                try
                {
                    ok = _write(json);
                }
                catch (SaveStateException ex)
                {
                    _logger.Log(SaveLogLevel.Error, "Async save write failed (wiring).", ex);
                    ok = false;
                }
                catch (Exception ex)
                {
                    _logger.Log(SaveLogLevel.Error, "Async save write failed.", ex);
                    ok = false;
                }

                if (ok)
                {
                    Interlocked.Increment(ref _saveCount);
                }

                lock (_gate)
                {
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }
}
