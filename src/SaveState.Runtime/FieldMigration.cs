using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SaveState
{
    /// <summary>
    /// The declarative way to upgrade old save data: <b>old field → new field (+ a conversion function)</b>.
    ///
    /// <para>Save a file's <c>Version</c> is bumped whenever its shape changes (see
    /// <see cref="SaveFileAttribute.Version"/>), and the host registers one <see cref="FieldMigration"/> per version
    /// step. The framework applies them to the JSON before deserializing, so old files turn into the current shape
    /// and load normally:</para>
    ///
    /// <code>
    /// var migrations = new ISaveMigration[]
    /// {
    ///     // v1 -> v2: the section was renamed, and a field changed type.
    ///     new FieldMigration("profile.json", fromVersion: 1)
    ///         .Rename("OldProfile", "Profile")
    ///         .Convert("Profile/PlayTimeSeconds", "Profile/PlayTime", JsonUpgrade.ToTimeSpanText())
    ///         .SetDefault("Profile/Title", JsonValue.Create("Newcomer")),
    /// };
    /// </code>
    ///
    /// <para><b>Paths</b> are <c>'/'</c>-separated object paths inside the file, e.g. <c>"Profile/Coins"</c>.
    /// A leading slash is allowed. Dots are <em>not</em> separators on purpose: keys such as mod package names
    /// (<c>com.example.mod</c>) contain dots. Arrays are not traversed - migrate the field that holds the array.</para>
    ///
    /// <para><b>Missing sources</b> are reported (see <see cref="Warnings"/>) and the target is left untouched, so a
    /// typo in the old name is visible instead of silently resetting progress. A <see cref="Convert"/> whose source
    /// is missing still runs with <c>null</c> and writes its result when that result is not null - that lets a host
    /// derive a value from scratch.</para>
    ///
    /// <para>Intermediate objects of a new target path are created as needed (moving a field into a new nested object
    /// is a normal shape change); existing objects are reused, never replaced.</para>
    /// </summary>
    public sealed class FieldMigration : ISaveMigration, ISaveMigrationDiagnostics
    {
        private enum OperationKind
        {
            /// <summary>Move the value as-is (a rename, or a move to another place).</summary>
            Rename,

            /// <summary>Move the value through a conversion function (a type or shape change).</summary>
            Convert,

            /// <summary>Delete a field that no longer exists in the new shape.</summary>
            Remove,

            /// <summary>Add a value when the (new) field is absent - the "new field in an old file" case.</summary>
            SetDefault,
        }

        private readonly List<Operation> _operations = new List<Operation>();
        private readonly List<string> _warnings = new List<string>();

        /// <param name="fileName">The save file to migrate (matches <see cref="SaveFileAttribute.FileName"/>).</param>
        /// <param name="fromVersion">The version this migration upgrades <b>from</b> (it produces <c>fromVersion + 1</c>).</param>
        public FieldMigration(string fileName, int fromVersion)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new SaveStateException("A field migration needs the save file name it applies to.");
            }

            if (fromVersion < 1)
            {
                throw new SaveStateException(
                    "A field migration upgrades from version 1 or later (got " + fromVersion + ").");
            }

            FileName = fileName;
            FromVersion = fromVersion;
        }

        /// <inheritdoc />
        public string FileName { get; }

        /// <inheritdoc />
        public int FromVersion { get; }

        /// <summary>What the last <see cref="Apply"/> could not do (see <see cref="ISaveMigrationDiagnostics"/>).</summary>
        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>Report a missing source field in <see cref="Warnings"/> (default <c>true</c>).</summary>
        public bool WarnWhenSourceIsMissing { get; set; } = true;

        /// <summary>Moves a value unchanged: the classic "this field was renamed" (or moved somewhere else).</summary>
        /// <param name="sourcePath">Old location, e.g. <c>"OldProfile"</c> or <c>"Profile/OldCoins"</c>.</param>
        /// <param name="targetPath">New location, e.g. <c>"Profile"</c> or <c>"Profile/Coins"</c>.</param>
        public FieldMigration Rename(string sourcePath, string targetPath)
        {
            return Add(OperationKind.Rename, sourcePath, targetPath, null);
        }

        /// <summary>
        /// Moves a value through <paramref name="convert"/>, which is where type and shape changes happen
        /// (a number becoming a string, a scalar becoming an array, two fields merging…).
        ///
        /// <para><paramref name="convert"/> receives the old node (or <c>null</c> when the source is absent) and
        /// returns the new node (<c>null</c> leaves the target untouched). <see cref="JsonUpgrade"/> has ready-made
        /// conversions for the common type changes.</para>
        /// </summary>
        public FieldMigration Convert(string sourcePath, string targetPath, Func<JsonNode?, JsonNode?> convert)
        {
            if (convert == null)
            {
                throw new ArgumentNullException(nameof(convert));
            }

            return Add(OperationKind.Convert, sourcePath, targetPath, convert);
        }

        /// <summary>Drops a field that the new shape does not have any more.</summary>
        public FieldMigration Remove(string path)
        {
            return Add(OperationKind.Remove, path, null, null);
        }

        /// <summary>
        /// Adds <paramref name="value"/> when <paramref name="path"/> is absent - for fields that only exist from
        /// this version on. An existing value is never overwritten (an old file that already has the field keeps it).
        /// </summary>
        public FieldMigration SetDefault(string path, JsonNode? value)
        {
            return Add(OperationKind.SetDefault, path, null, _ => value);
        }

        /// <inheritdoc />
        public void Apply(JsonObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            _warnings.Clear();

            foreach (Operation operation in _operations)
            {
                ApplyOne(root, operation);
            }
        }

        private FieldMigration Add(
            OperationKind kind,
            string sourcePath,
            string? targetPath,
            Func<JsonNode?, JsonNode?>? convert)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new SaveStateException("A field migration operation needs a field path.");
            }

            if (kind != OperationKind.Remove && kind != OperationKind.SetDefault && string.IsNullOrWhiteSpace(targetPath))
            {
                throw new SaveStateException("A field migration operation needs a target path.");
            }

            _operations.Add(new Operation(kind, PathOf(sourcePath), targetPath == null ? null : PathOf(targetPath), convert));
            return this;
        }

        private void ApplyOne(JsonObject root, Operation operation)
        {
            switch (operation.Kind)
            {
                case OperationKind.Remove:
                    Remove(root, operation.Source);
                    break;

                case OperationKind.SetDefault:
                    if (!TryGet(root, operation.Source, out _))
                    {
                        Set(root, operation.Source, operation.Convert!(null));
                    }

                    break;

                default:
                    ApplyMove(root, operation);
                    break;
            }
        }

        private void ApplyMove(JsonObject root, Operation operation)
        {
            string sourceText = string.Join("/", operation.Source);

            if (!TryGet(root, operation.Source, out JsonNode? value))
            {
                if (operation.Kind == OperationKind.Convert)
                {
                    // A conversion may legitimately work from nothing (deriving a value from scratch).
                    JsonNode? created = operation.Convert!(null);
                    if (created != null)
                    {
                        Set(root, operation.Target!, Detached(created));
                    }
                }

                if (WarnWhenSourceIsMissing)
                {
                    _warnings.Add("field \"" + sourceText + "\" was not found");
                }

                return;
            }

            JsonNode? result = operation.Kind == OperationKind.Convert ? operation.Convert!(value) : value;
            bool samePath = PathsEqual(operation.Source, operation.Target!);

            if (result == null)
            {
                // The conversion decided to drop the field; remove the old one so the shape stays consistent.
                Remove(root, operation.Source);
                return;
            }

            // Write a copy: the node we read is still attached to its old parent, and a JSON node can only live in
            // one place at a time (assigning it elsewhere throws). Only a freshly created node is written as-is.
            Set(root, operation.Target!, Detached(result));

            // Drop the old field - unless the target *is* the source (a type change in place) or lives *inside* it
            // (moving a value into a new object under the same name); removing then would delete what we just wrote.
            if (!samePath && !IsPrefixOf(operation.Source, operation.Target!))
            {
                Remove(root, operation.Source);
            }
        }

        /// <summary>Returns a node that is safe to attach elsewhere (a copy when it already has a parent).</summary>
        private static JsonNode Detached(JsonNode node)
        {
            return node.Parent == null ? node : node.DeepClone();
        }

        // ── JSON path plumbing (objects only; arrays are opaque on purpose) ──────────────────────

        private static string[] PathOf(string path)
        {
            string trimmed = path.Trim();
            if (trimmed.Length > 0 && trimmed[0] == '/')
            {
                trimmed = trimmed.Substring(1);
            }

            string[] segments = trimmed.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = segments[i].Trim();
                if (segments[i].Length == 0)
                {
                    throw new SaveStateException("Field path \"" + path + "\" contains an empty segment.");
                }
            }

            return segments;
        }

        private static bool TryGet(JsonObject root, string[] path, out JsonNode? value)
        {
            JsonNode? current = root;

            for (int i = 0; i < path.Length; i++)
            {
                if (current is not JsonObject obj || !obj.TryGetPropertyValue(path[i], out current))
                {
                    value = null;
                    return false;
                }
            }

            value = current;
            return true;
        }

        private static void Set(JsonObject root, string[] path, JsonNode? value)
        {
            JsonObject parent = root;

            for (int i = 0; i < path.Length - 1; i++)
            {
                if (parent[path[i]] is JsonObject existing)
                {
                    parent = existing;
                    continue;
                }

                var created = new JsonObject();
                parent[path[i]] = created;
                parent = created;
            }

            parent[path[path.Length - 1]] = value;
        }

        private static void Remove(JsonObject root, string[] path)
        {
            JsonObject parent = root;

            for (int i = 0; i < path.Length - 1; i++)
            {
                if (parent[path[i]] is not JsonObject next)
                {
                    return;
                }

                parent = next;
            }

            parent.Remove(path[path.Length - 1]);
        }

        private static bool IsPrefixOf(string[] source, string[] target)
        {
            if (target.Length <= source.Length)
            {
                return false;
            }

            for (int i = 0; i < source.Length; i++)
            {
                if (!string.Equals(source[i], target[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PathsEqual(string[] left, string[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct Operation
        {
            public Operation(OperationKind kind, string[] source, string[]? target, Func<JsonNode?, JsonNode?>? convert)
            {
                Kind = kind;
                Source = source;
                Target = target;
                Convert = convert;
            }

            public OperationKind Kind { get; }

            public string[] Source { get; }

            public string[]? Target { get; }

            public Func<JsonNode?, JsonNode?>? Convert { get; }
        }
    }
}
