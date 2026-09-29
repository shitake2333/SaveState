using System;
using System.Collections.Generic;

namespace SaveState
{
    /// <summary>
    /// In-memory store: zero IO, for unit tests and embedded scenarios.
    ///
    /// <para>It doubles as the <b>test seam</b>: the old implementation could only be tested by overriding a
    /// "save root directory" string, whereas with an interface the test cases can run entirely in memory
    /// (and they can additionally assert "how many times it wrote", which is what verifies the save policy's
    /// merging / debouncing).</para>
    /// </summary>
    public sealed class InMemoryStore : ISaveStore
    {
        private readonly Dictionary<string, string> _files =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Number of writes (including overwrites) - the basis for assertions in policy / debounce test cases.</summary>
        public int WriteCount { get; private set; }

        /// <summary>All current files (read-only snapshot).</summary>
        public IReadOnlyDictionary<string, string> Files
        {
            get { return _files; }
        }

        public bool Exists(string fileName)
        {
            return _files.ContainsKey(fileName);
        }

        public string Read(string fileName)
        {
            string? contents;
            if (!_files.TryGetValue(fileName, out contents))
            {
                throw new SaveStateException("No such file in the in-memory store: " + fileName);
            }

            return contents;
        }

        public void Write(string fileName, string contents)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new SaveStateException("Save file name must not be empty.");
            }

            _files[fileName] = contents ?? string.Empty;
            WriteCount++;
        }

        public bool Delete(string fileName)
        {
            return _files.Remove(fileName);
        }

        public string Describe(string fileName)
        {
            return "memory://" + fileName;
        }
    }
}
