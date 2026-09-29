using System.Collections.Generic;

namespace SaveState
{
    /// <summary>
    /// Optional companion to <see cref="ISaveMigration"/>: a migration that wants to report what it could not do.
    ///
    /// <para><b>Why this exists.</b> The most common migration failure is a silent no-op: the old field was named
    /// something slightly different from what the migration expects, so it matches nothing, migrates nothing, and the
    /// load "succeeds" with defaults. That is very hard to notice from the outside. A migration that records those
    /// misses lets <c>SaveFileSession</c> log them (the host already has a logger there) instead of leaving the
    /// player with quietly reset progress.</para>
    /// </summary>
    public interface ISaveMigrationDiagnostics
    {
        /// <summary>
        /// Problems from the **most recent** <see cref="ISaveMigration.Apply"/> call (for example
        /// "the source field was not there"), in a human-readable, log-ready form. Empty when the migration did
        /// everything it was asked to do.
        /// </summary>
        IReadOnlyList<string> Warnings { get; }
    }
}
