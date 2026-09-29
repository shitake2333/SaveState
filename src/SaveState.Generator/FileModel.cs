using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SaveState.Generator
{
    /// <summary>
    /// One <c>[SaveFile]</c> class (= one save file).
    ///
    /// <para>Models are <b>immutable and value-comparable</b>: the generator never mutates a model after a transform
    /// produced it (the incremental cache hands the same instance to later runs), and equality is what lets Roslyn
    /// skip re-emitting when nothing relevant changed.</para>
    /// </summary>
    internal sealed class FileModel : IEquatable<FileModel>
    {
        private FileModel(
            string fileName,
            int version,
            string namespaceName,
            string className,
            string classFullName,
            string modifiers,
            Location? location)
        {
            FileName = fileName;
            Version = version;
            Namespace = namespaceName;
            ClassName = className;
            ClassFullName = classFullName;
            Modifiers = modifiers;
            Location = location;
        }

        public string FileName { get; }

        /// <summary>The current schema version (from <c>[SaveFile(Version = …)]</c>).</summary>
        public int Version { get; }

        /// <summary>Includes the namespace; empty for the global namespace.</summary>
        public string Namespace { get; }

        public string ClassName { get; }

        /// <summary>Like <c>global::A.B.C</c>.</summary>
        public string ClassFullName { get; }

        /// <summary>The modifiers matching the host declaration (<c>public sealed </c> and so on), used for the generated half of the partial class.</summary>
        public string Modifiers { get; }

        /// <summary>Where to report problems (not part of equality).</summary>
        public Location? Location { get; }

        public List<IssueInfo> Issues { get; } = new List<IssueInfo>();

        public bool Usable
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity == DiagnosticSeverity.Error)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public static FileModel? Create(GeneratorAttributeSyntaxContext context, string fileName, int version)
        {
            if (context.TargetSymbol is not INamedTypeSymbol type)
            {
                return null;
            }

            Location? location = context.TargetNode.GetLocation();
            var model = new FileModel(
                fileName,
                version,
                type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString(),
                type.Name,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Syntax.ModifiersOf(type),
                location);

            model.Validate(type, location);
            return model;
        }

        private void Validate(INamedTypeSymbol type, Location? location)
        {
            if (type.ContainingType != null || type.IsGenericType)
            {
                Issues.Add(new IssueInfo(Diagnostics.NestedTypesAreNotSupported, location, type.Name));
                return;
            }

            if (!Syntax.IsPartial(type, location))
            {
                Issues.Add(new IssueInfo(Diagnostics.FileMustBePartial, location, type.Name));
            }

            string baseType = type.BaseType == null
                ? string.Empty
                : type.BaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (baseType != "global::" + WellKnownNames.SaveFileDefinition)
            {
                Issues.Add(new IssueInfo(Diagnostics.FileMustDeriveFromSaveFileDefinition, location, type.Name));
            }

            if (!HasPublicParameterlessConstructor(type) || type.IsAbstract)
            {
                Issues.Add(new IssueInfo(
                    Diagnostics.FileMustHavePublicParameterlessConstructor,
                    location,
                    type.Name));
            }

            if (!Syntax.IsValidFileName(FileName))
            {
                Issues.Add(new IssueInfo(Diagnostics.FileNameIsInvalid, location, FileName));
            }
        }

        private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
        {
            foreach (IMethodSymbol constructor in type.InstanceConstructors)
            {
                if (constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Equals(FileModel? other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Version == other.Version
                   && string.Equals(FileName, other.FileName, StringComparison.Ordinal)
                   && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
                   && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
                   && string.Equals(ClassFullName, other.ClassFullName, StringComparison.Ordinal)
                   && string.Equals(Modifiers, other.Modifiers, StringComparison.Ordinal)
                   && Models.IssuesEqual(Issues, other.Issues);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as FileModel);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Version;
                hash = (hash * 31) + FileName.GetHashCode();
                hash = (hash * 31) + Namespace.GetHashCode();
                hash = (hash * 31) + ClassName.GetHashCode();
                hash = (hash * 31) + ClassFullName.GetHashCode();
                hash = (hash * 31) + Modifiers.GetHashCode();
                return (hash * 31) + Models.IssuesHash(Issues);
            }
        }
    }
}
