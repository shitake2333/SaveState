using System;
using System.Collections.Generic;

namespace SaveState
{
    /// <summary>Status of a load operation.</summary>
    public enum LoadStatus
    {
        /// <summary>The file does not exist (first launch) - the caller will usually want to write a default save (see <c>EnsureCreated</c>).</summary>
        Missing,

        /// <summary>Loaded successfully and in full.</summary>
        Loaded,

        /// <summary>Loaded successfully but **with missing sections**, which were filled in with default instances (see <see cref="LoadResult.MissingSections"/>).</summary>
        Repaired,

        /// <summary>The load failed (corrupt or inaccessible file) - do **not** overwrite it with defaults, that is data.</summary>
        Failed,
    }

    /// <summary>
    /// The result of one load: status + list of missing sections + failure reason.
    ///
    /// <para>Why not a <c>bool</c>: the caller has to tell apart three completely different responses - "first launch, so
    /// generate one", "sections are missing, so write the repair back" and "the file is broken, so do not overwrite it".
    /// The old implementation used a single <c>bool</c> (and treated "corrupt" as "does not exist" as well), which is
    /// exactly what caused trouble in both directions: a missing section writing a broken state, and a corrupt save being
    /// overwritten by defaults.</para>
    /// </summary>
    public readonly struct LoadResult
    {
        private LoadResult(LoadStatus status, IReadOnlyList<string> missingSections, Exception? error)
        {
            Status = status;
            MissingSections = missingSections;
            Error = error;
        }

        /// <summary>The result status.</summary>
        public LoadStatus Status { get; }

        /// <summary>The names of the sections that were filled with defaults (non-empty when <see cref="LoadStatus.Repaired"/>).</summary>
        public IReadOnlyList<string> MissingSections { get; }

        /// <summary>The failure reason (non-null when <see cref="LoadStatus.Failed"/>).</summary>
        public Exception? Error { get; }

        /// <summary>Whether the file needs to be written back (= missing sections have been filled with defaults).</summary>
        public bool NeedsWriteBack
        {
            get { return Status == LoadStatus.Repaired; }
        }

        public static LoadResult Missing()
        {
            return new LoadResult(LoadStatus.Missing, EmptySectionList, null);
        }

        public static LoadResult Loaded()
        {
            return new LoadResult(LoadStatus.Loaded, EmptySectionList, null);
        }

        public static LoadResult Repaired(IReadOnlyList<string> missingSections)
        {
            return new LoadResult(
                LoadStatus.Repaired,
                missingSections ?? EmptySectionList,
                null);
        }

        public static LoadResult Failed(Exception error)
        {
            return new LoadResult(LoadStatus.Failed, EmptySectionList, error);
        }

        private static readonly string[] EmptySectionList = new string[0];

        public override string ToString()
        {
            switch (Status)
            {
                case LoadStatus.Repaired:
                    return "Repaired(missing: " + string.Join(", ", MissingSections) + ")";
                case LoadStatus.Failed:
                    return "Failed(" + (Error == null ? "unknown" : Error.GetType().Name + ": " + Error.Message) + ")";
                default:
                    return Status.ToString();
            }
        }
    }
}
