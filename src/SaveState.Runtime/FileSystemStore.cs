using System;
using System.IO;
using System.Text;

namespace SaveState
{
    /// <summary>
    /// File-system store: a root directory plus a plain file name. **Atomic write** (temp file + replace),
    /// so a "crash halfway through the write" cannot destroy the existing save.
    ///
    /// <para>The file name is whitelist-validated (no path separators, no <c>..</c>): <see cref="SaveFileAttribute.FileName"/>
    /// is a host constant, but this validation makes accidents such as "the save is written outside the root
    /// directory" impossible.</para>
    ///
    /// <para>The root directory comes from the host (Godot uses <c>ProjectSettings.GlobalizePath("user://")</c>,
    /// Unity uses <c>Application.persistentDataPath</c>).</para>
    /// </summary>
    public sealed class FileSystemStore : ISaveStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public FileSystemStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Save root directory must not be empty.", nameof(rootDirectory));
            }

            Root = rootDirectory;
        }

        /// <summary>The save root directory (an absolute path, or the virtual path handed over by the engine).</summary>
        public string Root { get; }

        public bool Exists(string fileName)
        {
            return File.Exists(PathFor(fileName));
        }

        public string Read(string fileName)
        {
            return File.ReadAllText(PathFor(fileName), Encoding.UTF8);
        }

        public void Write(string fileName, string contents)
        {
            string path = PathFor(fileName);
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (directory.Length > 0 && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Atomic write: write .tmp first, then replace the target.
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents ?? string.Empty, Utf8NoBom);

            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                // File.Replace is a real replace on both Windows and POSIX, but some network / virtual file systems do not support it.
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            catch (Exception)
            {
                if (!File.Exists(tempPath))
                {
                    throw;
                }

                // Fallback: delete the old file, then rename. The window is tiny, and by then .tmp already holds the complete contents.
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }

        public bool Delete(string fileName)
        {
            string path = PathFor(fileName);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }

        public string Describe(string fileName)
        {
            return PathFor(fileName);
        }

        private string PathFor(string fileName)
        {
            ValidateFileName(fileName);
            return Path.Combine(Root, fileName);
        }

        /// <summary>The save file name must be a "plain file name": no directory separators, no <c>..</c>, no absolute path.</summary>
        private static void ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new SaveStateException("Save file name must not be empty.");
            }

            if (fileName.IndexOf('/') >= 0
                || fileName.IndexOf('\\') >= 0
                || fileName.IndexOf("..", StringComparison.Ordinal) >= 0
                || Path.IsPathRooted(fileName))
            {
                throw new SaveStateException(
                    "Save file name must be a plain file name (no path separators): \"" + fileName + "\".");
            }
        }
    }
}
