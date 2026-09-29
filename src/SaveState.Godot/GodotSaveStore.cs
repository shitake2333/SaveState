using Godot;

namespace SaveState.Godot
{
    /// <summary>
    /// An <see cref="ISaveStore"/> rooted at Godot's <c>user://</c> directory.
    ///
    /// <para><c>user://</c> is the only writable location that behaves the same in the editor, in an
    /// exported build and on every platform, so it is the right default for save files. Godot resolves
    /// it to a real directory via <c>ProjectSettings.GlobalizePath</c>; everything below that is the
    /// engine-agnostic <see cref="FileSystemStore"/> (including its atomic write).</para>
    ///
    /// <para>Pass <paramref name="subdirectory"/> to keep saves in their own folder
    /// (for example <c>new GodotSaveStore("saves")</c> → <c>user://saves/</c>).</para>
    /// </summary>
    public sealed class GodotSaveStore : ISaveStore
    {
        private readonly FileSystemStore _inner;

        /// <param name="subdirectory">
        /// Optional subdirectory below <c>user://</c> (no leading slash needed; empty means the user
        /// directory itself). It must not escape the user directory.
        /// </param>
        public GodotSaveStore(string subdirectory = "")
        {
            string normalized = (subdirectory ?? string.Empty).Trim();
            if (normalized.Length > 0
                && (normalized.IndexOf("..", System.StringComparison.Ordinal) >= 0
                    || normalized.IndexOf(':') >= 0))
            {
                throw new SaveStateException(
                    "The save subdirectory must stay inside user:// (got \"" + subdirectory + "\").");
            }

            if (normalized.Length > 0 && normalized[0] == '/')
            {
                normalized = normalized.Substring(1);
            }

            if (normalized.Length == 0)
            {
                Root = ProjectSettings.GlobalizePath("user://");
            }
            else
            {
                Root = ProjectSettings.GlobalizePath("user://" + normalized.TrimEnd('/') + "/");
            }

            _inner = new FileSystemStore(Root);
        }

        /// <summary>Absolute directory that backs <c>user://…</c> for this store.</summary>
        public string Root { get; }

        /// <inheritdoc />
        public bool Exists(string fileName)
        {
            return _inner.Exists(fileName);
        }

        /// <inheritdoc />
        public string Read(string fileName)
        {
            return _inner.Read(fileName);
        }

        /// <inheritdoc />
        public void Write(string fileName, string contents)
        {
            _inner.Write(fileName, contents);
        }

        /// <inheritdoc />
        public bool Delete(string fileName)
        {
            return _inner.Delete(fileName);
        }

        /// <inheritdoc />
        public string Describe(string fileName)
        {
            return _inner.Describe(fileName);
        }
    }
}
