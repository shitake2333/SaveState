namespace SaveState.Generator
{
    /// <summary>Full names of the types/attributes the generator knows about (kept in one place so the strings do not scatter).</summary>
    internal static class WellKnownNames
    {
        public const string SaveFileAttribute = "SaveState.SaveFileAttribute";
        public const string SaveServiceAttribute = "SaveState.SaveServiceAttribute";
        public const string SavedStateAttribute = "SaveState.SavedStateAttribute";

        public const string SaveFileDefinition = "SaveState.SaveFileDefinition";
        public const string SaveRegistry = "SaveState.SaveRegistry";

        public const string SaveFileDefinitionFullyQualified = "global::SaveState.SaveFileDefinition";
        public const string SaveRegistryFullyQualified = "global::SaveState.SaveRegistry";
        public const string ReadOnlyListOfString = "global::System.Collections.Generic.IReadOnlyList<string>";
        public const string ListOfString = "global::System.Collections.Generic.List<string>";
    }
}
