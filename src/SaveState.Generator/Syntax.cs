using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SaveState.Generator
{
    /// <summary>Small syntax/symbol helpers (internal to the generator).</summary>
    internal static class Syntax
    {
        /// <summary>Whether the type declaration carries <c>partial</c> (checking the first declaration is enough: C# requires all parts to agree).</summary>
        public static bool IsPartial(INamedTypeSymbol type, Location? _)
        {
            foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is TypeDeclarationSyntax declaration
                    && HasModifier(declaration, SyntaxKind.PartialKeyword))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasModifier(TypeDeclarationSyntax declaration, SyntaxKind keyword)
        {
            foreach (SyntaxToken modifier in declaration.Modifiers)
            {
                if (modifier.IsKind(keyword))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Copies the host's modifiers into the generated partial declaration (accessibility + sealed/abstract).
        /// Without the copy, some combinations make the two partial halves conflict (CS0262 and friends).
        /// </summary>
        public static string ModifiersOf(INamedTypeSymbol type)
        {
            var sb = new StringBuilder();

            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    sb.Append("public ");
                    break;
                case Accessibility.Protected:
                    sb.Append("protected ");
                    break;
                case Accessibility.ProtectedOrInternal:
                    sb.Append("protected internal ");
                    break;
                case Accessibility.ProtectedAndInternal:
                    sb.Append("private protected ");
                    break;
                default:
                    // internal / private / file all collapse to internal: a top-level type can only be public or internal.
                    sb.Append("internal ");
                    break;
            }

            if (type.IsAbstract && !type.IsSealed)
            {
                sb.Append("abstract ");
            }

            if (type.IsSealed && !type.IsAbstract)
            {
                sb.Append("sealed ");
            }

            return sb.ToString();
        }

        /// <summary>A save file name must be a plain file name (path traversal is not allowed).</summary>
        public static bool IsValidFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            return fileName.IndexOf('/') < 0
                   && fileName.IndexOf('\\') < 0
                   && fileName.IndexOf("..", System.StringComparison.Ordinal) < 0;
        }

        /// <summary>Whether the name can be used as a generated property name (keywords are rejected too).</summary>
        public static bool IsValidIdentifier(string name)
        {
            return !string.IsNullOrEmpty(name)
                   && SyntaxFacts.IsValidIdentifier(name)
                   && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
        }
    }
}
