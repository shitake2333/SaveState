using Microsoft.CodeAnalysis;

namespace SaveState.Generator
{
    /// <summary>
    /// Compile-time diagnostics reported by the generator.
    ///
    /// <para>Design principle: **never leave a compile-time error to runtime**. Every diagnostic
    /// here corresponds to an incident in the old implementation that only blew up at runtime and
    /// whose symptom was far away from its cause (for example: a section type that cannot be
    /// instantiated → NRE while writing the file; ambiguous file ownership → a section silently
    /// written into another file).</para>
    /// </summary>
    internal static class Diagnostics
    {
        private const string Category = "SaveState";

        public static readonly DiagnosticDescriptor FileMustBePartial = new DiagnosticDescriptor(
            id: "SAV001",
            title: "[SaveFile] class must be partial",
            messageFormat: "Save file class '{0}' must be declared partial - the generator adds Version, section properties, and the collect/restore implementations to it",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FileMustDeriveFromSaveFileDefinition = new DiagnosticDescriptor(
            id: "SAV002",
            title: "[SaveFile] class must derive from SaveFileDefinition",
            messageFormat: "Save file class '{0}' must derive from global::SaveState.SaveFileDefinition",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FileMustHavePublicParameterlessConstructor = new DiagnosticDescriptor(
            id: "SAV003",
            title: "[SaveFile] class needs a public parameterless constructor",
            messageFormat: "Save file class '{0}' needs a public parameterless constructor (loading creates an instance to deserialize into)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FileNameIsInvalid = new DiagnosticDescriptor(
            id: "SAV004",
            title: "Invalid save file name",
            messageFormat: "Save file name '{0}' is invalid: it must be a plain file name (not empty, no '/' '\\' or '..')",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NestedTypesAreNotSupported = new DiagnosticDescriptor(
            id: "SAV005",
            title: "Nested or generic types are not supported",
            messageFormat: "'{0}' is a nested or generic type: save files and services must be top-level, non-generic types"
                           + " (the generator emits the partial declaration from namespace + class name)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ServiceMustBeMarkedSaveService = new DiagnosticDescriptor(
            id: "SAV013",
            title: "Service class is missing [SaveService]",
            messageFormat: "Member '{0}' has [SavedState] but its class '{1}' is not marked [SaveService] - "
                           + "the generator does not know which partial class to add the accessor to",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SaveFileNotFound = new DiagnosticDescriptor(
            id: "SAV006",
            title: "Save file not found",
            messageFormat: "The save file for member '{0}' cannot be determined: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SectionKeyIsNotIdentifier = new DiagnosticDescriptor(
            id: "SAV007",
            title: "Section key is not a valid identifier",
            messageFormat: "Section key '{0}' (member '{1}') must be a valid C# identifier - it becomes the property name of the generated DTO",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateSectionKey = new DiagnosticDescriptor(
            id: "SAV008",
            title: "Duplicate section key in the same save file",
            messageFormat: "Section key '{1}' appears more than once in save file '{0}' - later ones would be overwritten; distinguish them with [SavedState(Key = \"...\")]",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SectionMemberIsNotWritable = new DiagnosticDescriptor(
            id: "SAV009",
            title: "Section member is not writable",
            messageFormat: "Member '{0}' cannot be restored by the save system (read-only field or no setter) - give it a setter (private set is fine)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SectionTypeIsNotInstantiable = new DiagnosticDescriptor(
            id: "SAV010",
            title: "Section type cannot be instantiated",
            messageFormat: "The type '{1}' of member '{0}' cannot be instantiated (no public parameterless constructor / abstract / interface) - "
                           + "a default instance is needed when a section is missing from the file",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor EmptySaveFile = new DiagnosticDescriptor(
            id: "SAV011",
            title: "Save file has no sections",
            messageFormat: "Save file '{0}' has no [SavedState] sections: it will be written as an empty file containing only the version",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ServiceMustBePartial = new DiagnosticDescriptor(
            id: "SAV012",
            title: "[SaveService] class must be partial",
            messageFormat: "Save service '{0}' must be declared partial - the generator adds the accessors for its [SavedState] members to it",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
