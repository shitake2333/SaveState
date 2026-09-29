using System;

namespace SaveState
{
    /// <summary>
    /// Declares a save file.
    ///
    /// <para>Usage: put it on a <c>partial</c> class, which must derive from <see cref="SaveFileDefinition"/>;
    /// the source generator then fills in <c>Version</c>, the per-section properties and
    /// <see cref="SaveFileDefinition.CollectFrom"/> /
    /// <see cref="SaveFileDefinition.RestoreTo"/> / <see cref="SaveFileDefinition.FillMissingDefaults"/>.</para>
    ///
    /// <code>
    /// [SaveFile("global.json")]
    /// public sealed partial class GlobalSaveFile : SaveFileDefinition;
    /// </code>
    ///
    /// <para><b>Why the save category is "a class declared by the host" instead of an enum in the library</b>: the
    /// library should not know how many save files the host has or what they are called. Adding a save file (a machine
    /// save, replays, several sets of settings...) then only touches host code, with no need to wait for a library
    /// release.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SaveFileAttribute : Attribute
    {
        /// <param name="fileName">
        /// The save file name (relative to the root directory of <see cref="ISaveStore"/>). It must be a plain file name
        /// containing no path separators, e.g. <c>"global.json"</c>; a name with a path in it is rejected at runtime.
        /// </param>
        public SaveFileAttribute(string fileName)
        {
            FileName = fileName;
        }

        /// <summary>The save file name (relative to the <see cref="ISaveStore"/> root directory).</summary>
        public string FileName { get; }

        /// <summary>
        /// The current schema version (written into the save's <c>Version</c> field).
        ///
        /// <para>Bump it by one when the structure changes (a section is renamed, a field inside a section changes
        /// meaning) and add an <see cref="ISaveMigration"/> that brings old saves up; when loading, the framework
        /// migrates them one version at a time.</para>
        /// </summary>
        public int Version { get; init; } = 1;
    }
}
