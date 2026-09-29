using System.Collections.Generic;

namespace SaveState
{
    /// <summary>
    /// The data shape of a save file (declared by the host, implemented by the generator).
    ///
    /// <para>The three abstract methods here are all "plumbing the generator fills in"; the host does not need to (and
    /// should not) write them by hand:</para>
    /// <list type="bullet">
    ///   <item><see cref="CollectFrom"/>: collects the current state from the services in the registry into this object (called before writing to disk).</item>
    ///   <item><see cref="RestoreTo"/>: restores this object's sections back into the services (called after reading from disk; <b>checking each section for null</b>).</item>
    ///   <item><see cref="FillMissingDefaults"/>: fills missing (null) sections in with default instances and reports what was missing.</item>
    /// </list>
    ///
    /// <para><b>The core contract (the reason this framework exists)</b>: <see cref="SaveFileSession{TFile}.Load"/> will
    /// never write a service's state to <c>null</c> just because "the file is missing a section" - a missing section is
    /// filled with a default instance and <see cref="LoadStatus.Repaired"/> is returned, leaving it to the host to decide
    /// whether to write the file back. This is exactly the fix for the project's original
    /// <c>NullReferenceException</c> (missing section → null state → the first business action blew up).</para>
    /// </summary>
    public abstract class SaveFileDefinition
    {
        /// <summary>
        /// The save file name (filled in by the generator from <see cref="SaveFileAttribute.FileName"/>).
        ///
        /// <para><b>Why a method rather than a property</b>: a property would be written into the save by
        /// System.Text.Json (and reading it back would serve no purpose), whereas the "file name" is host configuration
        /// and not part of the save contents - keeping a read-once piece of plumbing in a method leaves the save shape
        /// determined solely by <see cref="Version"/> and the sections.</para>
        /// </summary>
        public abstract string GetFileName();

        /// <summary>
        /// The save version (the generator gives the current value; when loading, this field is also read back from the
        /// file). Migration is anchored to it, see <see cref="ISaveMigration"/>.
        /// </summary>
        public abstract int Version { get; set; }

        /// <summary>Collects the current state from the services in the registry (generated).</summary>
        public abstract void CollectFrom(SaveRegistry registry);

        /// <summary>Restores the sections back into the services in the registry (generated; checks each section for null).</summary>
        public abstract void RestoreTo(SaveRegistry registry);

        /// <summary>
        /// Fills missing (null) sections in with default instances; returns whether anything was filled in.
        /// </summary>
        /// <param name="missingSections">The names of the sections that were filled in (in declaration order); an empty list when nothing was missing.</param>
        public abstract bool FillMissingDefaults(out IReadOnlyList<string> missingSections);
    }
}
