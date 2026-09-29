using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaveState
{
    /// <summary>
    /// The read/write session for one save file: it wires <typeparamref name="TFile"/> (the generated DTO, see
    /// <see cref="SaveFileDefinition"/>) to an <see cref="ISaveStore"/> + <see cref="SaveRegistry"/>.
    ///
    /// <para><b>The read/write pipeline</b></para>
    /// <list type="number">
    ///   <item><see cref="Load"/>: read the file -> read the version number -> run the migrations in order -> deserialize ->
    ///         <c>FillMissingDefaults()</c> (fill missing sections with defaults, <b>never write the service state as null</b>) -> <c>RestoreTo()</c>.</item>
    ///   <item><see cref="Save"/>: <c>CollectFrom()</c> (collect the current state from the services) -> serialize -> atomic write.</item>
    ///   <item><see cref="EnsureCreated"/>: Missing / Repaired -> write one; Loaded -> do nothing;
    ///         Failed -> <b>leave the file alone</b> (that is data, not garbage).</item>
    /// </list>
    ///
    /// <para><b>The write-to-disk policy is not here</b>: when to save (first launch / unlock / end of a round /
    /// settings screen, plus merging and debouncing) is the host's decision - this class only provides the
    /// primitives. The old implementation mixed "generate one on first launch" and "write to disk on unlock" into
    /// the business services, which is one of the reasons it was hard to reuse.</para>
    ///
    /// <para><b>Error classification</b>: data/IO problems always degrade gracefully (return a <see cref="LoadResult"/>/
    /// <c>false</c> and log, never throw); wiring errors (a service is not registered, a file name contains a path)
    /// throw <see cref="SaveStateException"/> and surface immediately.</para>
    /// </summary>
    public sealed class SaveFileSession<TFile>
        where TFile : SaveFileDefinition, new()
    {
        private readonly ISaveStore _store;
        private readonly SaveRegistry _registry;
        private readonly ISaveLogger _logger;
        private readonly IReadOnlyList<ISaveMigration> _migrations;
        private readonly JsonSerializerOptions _options;
        private readonly int _targetVersion;

        /// <param name="store">Where the bytes end up (file system / memory / engine implementation).</param>
        /// <param name="registry">The service registry taking part in this file.</param>
        /// <param name="logger">Sink for non-fatal problems (write failure, missing sections, a gap in the migrations); silent by default.</param>
        /// <param name="migrations">The version migrations (only the ones whose <see cref="ISaveMigration.FileName"/> matches this file are used).</param>
        /// <param name="options">JSON options; defaults to <see cref="SaveJson.DefaultOptions"/>.</param>
        /// <param name="fileName">
        /// Explicit file name, for hosts where the <b>same shape</b> serves several files - save slots are the
        /// classic case (<c>save_0.json</c>, <c>save_1.json</c>, … share one DTO and one version).
        /// <c>null</c> (default) uses the name declared by <c>[SaveFile("…")]</c>.
        /// </param>
        public SaveFileSession(
            ISaveStore store,
            SaveRegistry registry,
            ISaveLogger? logger = null,
            IEnumerable<ISaveMigration>? migrations = null,
            JsonSerializerOptions? options = null,
            string? fileName = null)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            _store = store;
            _registry = registry;
            _logger = logger ?? NullSaveLogger.Instance;
            _options = options ?? SaveJson.DefaultOptions;

            TFile probe = new TFile();
            FileName = string.IsNullOrWhiteSpace(fileName) ? probe.GetFileName() : fileName!.Trim();
            _targetVersion = probe.Version;

            if (string.IsNullOrWhiteSpace(FileName))
            {
                throw new SaveStateException(
                    typeof(TFile).FullName + " has no file name: declare it in [SaveFile(\"...\")]"
                    + " or pass one to the SaveFileSession constructor (save slots share one DTO across files).");
            }

            var list = new List<ISaveMigration>();
            if (migrations != null)
            {
                foreach (ISaveMigration migration in migrations)
                {
                    if (migration != null && string.Equals(migration.FileName, FileName, StringComparison.Ordinal))
                    {
                        list.Add(migration);
                    }
                }
            }

            list.Sort(static (a, b) => a.FromVersion.CompareTo(b.FromVersion));
            _migrations = list;
        }

        /// <summary>The save file name this session is responsible for.</summary>
        public string FileName { get; }

        /// <summary>The current version number (the value the generated DTO reports).</summary>
        public int TargetVersion
        {
            get { return _targetVersion; }
        }

        /// <summary>Whether the file exists.</summary>
        public bool Exists()
        {
            return _store.Exists(FileName);
        }

        /// <summary>Deletes the save file; returns whether it was really deleted.</summary>
        public bool Delete()
        {
            return _store.Delete(FileName);
        }

        /// <summary>Loads the save and restores the state (see the pipeline and the error classification in the class remarks).</summary>
        public LoadResult Load()
        {
            if (!_store.Exists(FileName))
            {
                return LoadResult.Missing();
            }

            string json;
            try
            {
                json = _store.Read(FileName);
            }
            catch (Exception ex)
            {
                return Fail("Read failed", ex);
            }

            try
            {
                json = ApplyMigrations(json);

                TFile? dto = JsonSerializer.Deserialize<TFile>(json, _options);
                if (dto == null)
                {
                    return Fail("deserialized to null", null);
                }

                bool repaired = dto.FillMissingDefaults(out IReadOnlyList<string> missing);
                if (repaired)
                {
                    _logger.Log(
                        SaveLogLevel.Warn,
                        FileName + " is missing " + missing.Count + " section(s) (" + string.Join(", ", missing)
                        + ") - filled with default instances; call EnsureCreated() to write the repaired file back.");
                }

                // Wiring errors (a service is not registered, etc.) are not swallowed here: that is a startup wiring bug and must surface immediately.
                dto.RestoreTo(_registry);

                return repaired ? LoadResult.Repaired(missing) : LoadResult.Loaded();
            }
            catch (SaveStateException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                return Fail("JSON parse failed", ex);
            }
            catch (Exception ex)
            {
                return Fail("Load failed", ex);
            }
        }

        /// <summary>
        /// Ensures a complete save exists on disk: Missing (first launch) / Repaired (missing sections) -> write one;
        /// Loaded -> do nothing; Failed (corrupt) -> leave the file alone.
        /// </summary>
        /// <returns>Whether anything was **written to disk**.</returns>
        public bool EnsureCreated()
        {
            LoadResult result = Load();

            switch (result.Status)
            {
                case LoadStatus.Loaded:
                    return false;

                case LoadStatus.Failed:
                    _logger.Log(
                        SaveLogLevel.Warn,
                        "Failed to read " + FileName + " - refusing to overwrite it with defaults (that would destroy data): "
                        + _store.Describe(FileName));
                    return false;

                default:
                    _logger.Log(
                        SaveLogLevel.Info,
                        result.Status == LoadStatus.Missing
                            ? "First launch: creating default save file " + FileName + "."
                            : "Save file " + FileName + " had missing sections; writing the repaired file back.");
                    return Save();
            }
        }

        /// <summary>
        /// Collects the current state and serializes it into the save file's JSON text.
        ///
        /// <para>This is the half of a save that <b>must run on the thread that owns the state</b>:
        /// collecting walks the service state (lists, sets, dictionaries), so doing it on a background
        /// thread while the game keeps mutating that state can tear the snapshot or throw
        /// "collection was modified". <see cref="WriteSnapshot"/> is the half that may move to a worker
        /// (see <c>SaveState.Policy.AsyncSaveScheduler</c>).</para>
        /// </summary>
        /// <exception cref="SaveStateException">Wiring errors (a service is not registered).</exception>
        public string SerializeSnapshot()
        {
            TFile dto = new TFile();
            dto.Version = _targetVersion;
            dto.CollectFrom(_registry);
            return JsonSerializer.Serialize(dto, _options);
        }

        /// <summary>
        /// Writes a snapshot produced by <see cref="SerializeSnapshot"/> (atomic write through the store).
        /// A failure is logged and reported as <c>false</c> - it is never thrown, because a failed save
        /// must not interrupt play.
        /// </summary>
        public bool WriteSnapshot(string json)
        {
            try
            {
                _store.Write(FileName, json);
                return true;
            }
            catch (SaveStateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Log(
                    SaveLogLevel.Error,
                    "Failed to write: " + _store.Describe(FileName),
                    ex);
                return false;
            }
        }

        /// <summary>
        /// Writes to disk right now: <see cref="WriteSnapshot"/> of <see cref="SerializeSnapshot"/>.
        /// On failure it only logs and returns false.
        /// </summary>
        public bool Save()
        {
            try
            {
                return WriteSnapshot(SerializeSnapshot());
            }
            catch (SaveStateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Log(
                    SaveLogLevel.Error,
                    "Failed to write: " + _store.Describe(FileName),
                    ex);
                return false;
            }
        }

        private LoadResult Fail(string what, Exception? ex)
        {
            _logger.Log(
                SaveLogLevel.Error,
                what + ": " + _store.Describe(FileName),
                ex);

            return LoadResult.Failed(
                ex ?? new SaveStateException(what + ": " + FileName));
        }

        /// <summary>
        /// Runs the migrations one version at a time (v -> v+1). When a migration is missing it **logs a warning and
        /// carries on**: better to let the player keep playing with the part that can still be read than to declare the
        /// whole file dead (the same stance as "fill missing sections with defaults").
        /// </summary>
        private string ApplyMigrations(string json)
        {
            int fromVersion = ReadVersion(json);
            if (fromVersion >= _targetVersion)
            {
                return json;
            }

            JsonNode? node = JsonNode.Parse(
                json,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

            JsonObject? root = node as JsonObject;
            if (root == null)
            {
                _logger.Log(SaveLogLevel.Warn, FileName + " root is not a JSON object; skipping migrations.");
                return json;
            }

            for (int version = fromVersion; version < _targetVersion; version++)
            {
                ISaveMigration? migration = FindMigration(version);
                if (migration == null)
                {
                    _logger.Log(
                        SaveLogLevel.Warn,
                        FileName + " has no migration for v" + version + " -> v" + (version + 1)
                        + "; loading the rest as-is (fields may be lost).");
                    break;
                }

                migration.Apply(root);
                root["Version"] = version + 1;
                _logger.Log(
                    SaveLogLevel.Info,
                    "Migrated " + FileName + ": v" + version + " -> v" + (version + 1) + ".");

                // A migration that could not find what it was told to migrate (usually a typo in the old field
                // name) is otherwise invisible: the load succeeds with defaults and the player quietly loses
                // progress. Report it loudly.
                if (migration is ISaveMigrationDiagnostics diagnostics && diagnostics.Warnings.Count > 0)
                {
                    foreach (string warning in diagnostics.Warnings)
                    {
                        _logger.Log(
                            SaveLogLevel.Warn,
                            FileName + " migration v" + version + " -> v" + (version + 1) + ": " + warning + ".");
                    }
                }
            }

            return root.ToJsonString(_options);
        }

        private ISaveMigration? FindMigration(int fromVersion)
        {
            for (int i = 0; i < _migrations.Count; i++)
            {
                if (_migrations[i].FromVersion == fromVersion)
                {
                    return _migrations[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the version number declared in the file; when the key is absent it is treated as 1 (documented
        /// convention: the version number has been written from the very beginning, so a missing key can only come
        /// from hand-editing or from an even earlier save).
        /// </summary>
        private static int ReadVersion(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(
                       json,
                       new JsonDocumentOptions
                       {
                           AllowTrailingCommas = true,
                           CommentHandling = JsonCommentHandling.Skip,
                       }))
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("Version", out JsonElement versionElement)
                    && versionElement.ValueKind == JsonValueKind.Number
                    && versionElement.TryGetInt32(out int version))
                {
                    return version;
                }
            }

            return 1;
        }
    }
}
