namespace SaveState.Policy
{
    /// <summary>
    /// Bind an <see cref="AsyncSaveScheduler"/> to a session: the snapshot half stays on the caller's thread,
    /// the write half goes to a worker.
    ///
    /// <code>
    /// using var async = session.ScheduleAsync(logger);
    ///
    /// // gameplay events (unlock, end of run) can fire as often as they like:
    /// async.RequestSave();
    ///
    /// // on quit:
    /// async.Flush();
    /// </code>
    /// </summary>
    public static class SaveSessionExtensions
    {
        /// <summary>Creates an asynchronous scheduler for this session's file.</summary>
        public static AsyncSaveScheduler ScheduleAsync<TFile>(
            this SaveFileSession<TFile> session,
            ISaveLogger? logger = null)
            where TFile : SaveFileDefinition, new()
        {
            if (session == null)
            {
                throw new System.ArgumentNullException(nameof(session));
            }

            return new AsyncSaveScheduler(session.SerializeSnapshot, session.WriteSnapshot, logger);
        }
    }
}
