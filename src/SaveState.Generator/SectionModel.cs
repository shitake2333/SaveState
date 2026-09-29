using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace SaveState.Generator
{
    /// <summary>
    /// One <c>[SavedState]</c> member (= one section of a save file). Immutable and value-comparable.
    ///
    /// <para>The file this section belongs to is <b>not</b> stored here: the transform does not know the other
    /// models, so resolution happens at emit time and produces a local <c>ResolvedSection</c> instead of mutating
    /// the cached model.</para>
    /// </summary>
    internal sealed class SectionModel : IEquatable<SectionModel>
    {
        private SectionModel(
            string? declaredFileName,
            string key,
            string memberName,
            string typeFullName,
            string defaultInstanceExpression,
            ServiceModel service,
            Location? location)
        {
            DeclaredFileName = declaredFileName;
            Key = key;
            MemberName = memberName;
            TypeFullName = typeFullName;
            DefaultInstanceExpression = defaultInstanceExpression;
            Service = service;
            Location = location;
        }

        /// <summary>The <c>File</c> given in the attribute (<c>null</c> = "auto-assign the only file").</summary>
        public string? DeclaredFileName { get; }

        /// <summary>Section key = the property name of the generated DTO.</summary>
        public string Key { get; }

        /// <summary>The member name on the service.</summary>
        public string MemberName { get; }

        public string TypeFullName { get; }

        /// <summary>Expression that creates a default instance when the section is missing (arrays use an empty array).</summary>
        public string DefaultInstanceExpression { get; }

        public ServiceModel Service { get; }

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

        /// <summary>Name of the accessor generated inside the service's partial class (per member, so it is unique in the class).</summary>
        public string AccessorName
        {
            get { return "__SaveState_" + MemberName; }
        }

        public static SectionModel? Create(GeneratorAttributeSyntaxContext context)
        {
            ISymbol member = context.TargetSymbol;
            INamedTypeSymbol declaringType = member.ContainingType;
            if (declaringType == null)
            {
                return null;
            }

            Location? location = context.TargetNode.GetLocation();

            string? declaredFile = null;
            string? keyOverride = null;
            if (context.Attributes.Length > 0)
            {
                foreach (KeyValuePair<string, TypedConstant> named in context.Attributes[0].NamedArguments)
                {
                    if (named.Key == "File" && named.Value.Value is string file && !string.IsNullOrWhiteSpace(file))
                    {
                        declaredFile = file;
                    }
                    else if (named.Key == "Key" && named.Value.Value is string keyName && !string.IsNullOrWhiteSpace(keyName))
                    {
                        keyOverride = keyName;
                    }
                }
            }

            string key = keyOverride ?? member.Name;

            ITypeSymbol? memberType = member switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null,
            };

            if (memberType == null)
            {
                return null;
            }

            ServiceModel service = ServiceModel.FromType(declaringType);

            var model = new SectionModel(
                declaredFile,
                key,
                member.Name,
                memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                BuildDefaultInstanceExpression(memberType),
                service,
                location);

            model.Validate(member, memberType, location);
            return model;
        }

        private void Validate(ISymbol member, ITypeSymbol memberType, Location? location)
        {
            if (member.ContainingType == null || member.ContainingType.ContainingType != null || member.ContainingType.IsGenericType)
            {
                Issues.Add(new IssueInfo(
                    Diagnostics.NestedTypesAreNotSupported,
                    location,
                    member.ContainingType == null ? member.Name : member.ContainingType.Name));
            }

            if (!HasSaveServiceAttribute(member.ContainingType))
            {
                Issues.Add(new IssueInfo(
                    Diagnostics.ServiceMustBeMarkedSaveService,
                    location,
                    member.Name,
                    member.ContainingType?.Name ?? "?"));
            }

            if (!Syntax.IsValidIdentifier(Key))
            {
                Issues.Add(new IssueInfo(Diagnostics.SectionKeyIsNotIdentifier, location, Key, member.Name));
            }

            if (!IsWritable(member))
            {
                Issues.Add(new IssueInfo(Diagnostics.SectionMemberIsNotWritable, location, member.Name));
            }

            string expression = BuildDefaultInstanceExpression(memberType);
            if (expression.Length == 0)
            {
                Issues.Add(new IssueInfo(
                    Diagnostics.SectionTypeIsNotInstantiable,
                    location,
                    member.Name,
                    memberType.ToDisplayString()));
            }
        }

        private static bool HasSaveServiceAttribute(INamedTypeSymbol? type)
        {
            if (type == null)
            {
                return false;
            }

            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == WellKnownNames.SaveServiceAttribute)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A member can be restored when it has a setter (including <c>private set</c>); fields must not be readonly/const.</summary>
        private static bool IsWritable(ISymbol member)
        {
            switch (member)
            {
                case IPropertySymbol property:
                    return property.SetMethod != null;
                case IFieldSymbol field:
                    return !field.IsReadOnly && !field.IsConst;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The default-instance expression used when a section is missing; an empty string means "cannot be
        /// instantiated" (and is reported as a diagnostic).
        ///
        /// <para>Arrays must use <c>new T[0]</c> - <c>new T[]()</c> is not legal (CS1586). That was a real bug
        /// (a <c>ChipState[]</c> section in the slot save).</para>
        /// </summary>
        private static string BuildDefaultInstanceExpression(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return "new " + array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "[0]";
            }

            string fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (type.TypeKind == TypeKind.Enum || type.IsValueType)
            {
                return "new " + fullName + "()";
            }

            if (type.TypeKind == TypeKind.Class && !type.IsAbstract && HasPublicParameterlessConstructor(type))
            {
                return "new " + fullName + "()";
            }

            return string.Empty;
        }

        private static bool HasPublicParameterlessConstructor(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named)
            {
                return false;
            }

            foreach (IMethodSymbol constructor in named.InstanceConstructors)
            {
                if (constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Equals(SectionModel? other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Models.StringEquals(DeclaredFileName, other.DeclaredFileName)
                   && string.Equals(Key, other.Key, StringComparison.Ordinal)
                   && string.Equals(MemberName, other.MemberName, StringComparison.Ordinal)
                   && string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal)
                   && string.Equals(DefaultInstanceExpression, other.DefaultInstanceExpression, StringComparison.Ordinal)
                   && Service.Equals(other.Service)
                   && Models.IssuesEqual(Issues, other.Issues);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as SectionModel);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = DeclaredFileName == null ? 0 : DeclaredFileName.GetHashCode();
                hash = (hash * 31) + Key.GetHashCode();
                hash = (hash * 31) + MemberName.GetHashCode();
                hash = (hash * 31) + TypeFullName.GetHashCode();
                hash = (hash * 31) + DefaultInstanceExpression.GetHashCode();
                hash = (hash * 31) + Service.GetHashCode();
                return (hash * 31) + Models.IssuesHash(Issues);
            }
        }
    }
}
