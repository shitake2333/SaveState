using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveState
{
    /// <summary>
    /// The one JSON policy for save files. **Defined in exactly one place in the library** (the old implementation
    /// wrote a copy into each generated file, so changing the policy meant changing the generator).
    ///
    /// <para><b>Deliberately does not ignore defaults / null</b> (<see cref="JsonIgnoreCondition.Never"/>):
    /// "missing key" and "value is null" must be two distinguishable concepts - the former is an old save or a
    /// hand-edited file (to be repaired as a missing section), the latter is an explicitly written empty value.
    /// The old implementation used <c>WhenWritingDefault</c> to save bytes, and the result was that the broken
    /// shape of a save with missing sections stuck around forever (it was left out again on write-back).</para>
    /// </summary>
    public static class SaveJson
    {
        private static readonly JsonSerializerOptions s_options = CreateDefaultOptions();

        /// <summary>Default serialization options (a shared read-only instance: mutating it is pointless; to customize, call <see cref="CreateDefaultOptions"/> and pass the result in).</summary>
        public static JsonSerializerOptions DefaultOptions
        {
            get { return s_options; }
        }

        /// <summary>Creates a fresh set of default options (the host can adjust them and then hand them to the save session).</summary>
        public static JsonSerializerOptions CreateDefaultOptions()
        {
            return new JsonSerializerOptions
            {
                // Hand-edited files often differ in casing: read leniently, write canonically.
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = null,
                WriteIndented = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            };
        }
    }
}
