using System;

namespace SaveState
{
    /// <summary>
    /// A single version migration: it upgrades the JSON of version <see cref="FromVersion"/> to <see cref="FromVersion"/> + 1.
    ///
    /// <para>A migration only changes the "shape" (renaming keys, moving sections, filling in defaults) and
    /// **never deserializes**: the load pipeline is "parse the JSON -> run the migrations in order -> deserialize
    /// into the current DTO -> fill missing sections -> restore", so migration code never has to know the old
    /// version's C# types (that whole set of types has no business staying in the code base).</para>
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>Target save file name (matches <see cref="SaveFileAttribute.FileName"/>).</summary>
        string FileName { get; }

        /// <summary>Starting version number; this migration is responsible for bumping it to +1.</summary>
        int FromVersion { get; }

        /// <summary>Modifies the save's JSON root object in place.</summary>
        void Apply(System.Text.Json.Nodes.JsonObject root);
    }
}
