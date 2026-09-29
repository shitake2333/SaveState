using System;

namespace SaveState
{
    /// <summary>
    /// Where the save bytes end up. <b>This is the only contact surface between this framework and the engine/platform</b>:
    /// the Godot implementation is <c>ProjectSettings.GlobalizePath("user://")</c> plus the file system,
    /// Unity uses <c>Application.persistentDataPath</c>, a server uses some directory, and tests use an in-memory dictionary.
    ///
    /// <para>Implementation requirement: <see cref="Write"/> must **either write the whole thing or leave the original
    /// file untouched** (an atomic write) - crashing halfway through a save destroys the entire save file. The format
    /// itself is the business of the runtime layer; this interface only deals in bytes.</para>
    /// </summary>
    public interface ISaveStore
    {
        /// <summary>Whether the file exists.</summary>
        bool Exists(string fileName);

        /// <summary>Reads back the whole contents (UTF-8 text). Behavior is undefined when the file does not exist - the caller should check <see cref="Exists"/> first.</summary>
        string Read(string fileName);

        /// <summary>Writes the whole contents atomically.</summary>
        void Write(string fileName, string contents);

        /// <summary>Deletes the file; returns whether it was really deleted.</summary>
        bool Delete(string fileName);

        /// <summary>The actual location of the file (for logs or error pages, e.g. an absolute path).</summary>
        string Describe(string fileName);
    }
}
