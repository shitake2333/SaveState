using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SaveState.Generator
{
    /// <summary>One <c>[SaveService]</c> class (its <c>[SavedState]</c> members take part in saving). Immutable and value-comparable.</summary>
    internal sealed class ServiceModel : IEquatable<ServiceModel>
    {
        private ServiceModel(string namespaceName, string className, string classFullName, string modifiers)
        {
            Namespace = namespaceName;
            ClassName = className;
            ClassFullName = classFullName;
            Modifiers = modifiers;
        }

        public string Namespace { get; }

        public string ClassName { get; }

        public string ClassFullName { get; }

        public string Modifiers { get; }

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

        /// <summary>Information-only factory (no diagnostics): used by section models to point at their service.</summary>
        public static ServiceModel FromType(INamedTypeSymbol type)
        {
            return new ServiceModel(
                type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString(),
                type.Name,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Syntax.ModifiersOf(type));
        }

        public static ServiceModel? Create(GeneratorAttributeSyntaxContext context)
        {
            if (context.TargetSymbol is not INamedTypeSymbol type)
            {
                return null;
            }

            Location? location = context.TargetNode.GetLocation();
            ServiceModel model = FromType(type);

            if (type.ContainingType != null || type.IsGenericType)
            {
                model.Issues.Add(new IssueInfo(Diagnostics.NestedTypesAreNotSupported, location, type.Name));
            }
            else if (!Syntax.IsPartial(type, location))
            {
                model.Issues.Add(new IssueInfo(Diagnostics.ServiceMustBePartial, location, type.Name));
            }

            return model;
        }

        public bool Equals(ServiceModel? other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
                   && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
                   && string.Equals(ClassFullName, other.ClassFullName, StringComparison.Ordinal)
                   && string.Equals(Modifiers, other.Modifiers, StringComparison.Ordinal)
                   && Models.IssuesEqual(Issues, other.Issues);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ServiceModel);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Namespace.GetHashCode();
                hash = (hash * 31) + ClassName.GetHashCode();
                hash = (hash * 31) + ClassFullName.GetHashCode();
                hash = (hash * 31) + Modifiers.GetHashCode();
                return (hash * 31) + Models.IssuesHash(Issues);
            }
        }
    }
}
