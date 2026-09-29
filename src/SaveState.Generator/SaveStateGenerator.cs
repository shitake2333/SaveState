using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SaveState.Generator
{
    /// <summary>
    /// SaveState's compile-time source generator.
    ///
    /// <para><b>Input</b></para>
    /// <list type="bullet">
    ///   <item><c>[SaveFile("global.json")]</c>: placed on a partial class (which must derive from <c>SaveFileDefinition</c>) → one save file.</item>
    ///   <item><c>[SaveService]</c> + <c>[SavedState(File=…, Key=…)]</c>: which members of a service go into which file.</item>
    /// </list>
    ///
    /// <para><b>Output (its own partial per class)</b></para>
    /// <list type="bullet">
    ///   <item>File class: <c>FileName</c> / <c>Version</c> / one property per section /
    ///         <c>CollectFrom</c> / <c>RestoreTo</c> (null-checking every section) / <c>FillMissingDefaults</c> (fills in defaults for missing sections).</item>
    ///   <item>Service class: one <c>internal</c> accessor per <c>[SavedState]</c> member
    ///         (so the member can stay <c>private set</c> without widening its visibility just for saving).</item>
    /// </list>
    ///
    /// <para><b>Why "kinds" are host-declared classes instead of an enum inside the generator</b>: the generator does not need
    /// to know how many save files the host has. Adding a save file (machine save / replay / several presets) only changes host
    /// code - in the old implementation that meant "edit the generator + ship a new version".</para>
    ///
    /// <para><b>Incremental correctness</b> (see also <see cref="EquatableArray{T}"/>): the models are immutable and
    /// value-comparable, and a transform never mutates a model afterwards. Resolving which file a section belongs to
    /// therefore happens at emit time into a local <c>ResolvedSection</c>, not by writing back into the cached
    /// model - mutating a cached value is exactly the kind of bug that makes a generator's output depend on how many
    /// times it ran.</para>
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class SaveStateGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<FileModel?> files = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    WellKnownNames.SaveFileAttribute,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) => CreateFile(ctx))
                .Where(static model => model is not null);

            IncrementalValuesProvider<SectionModel?> sections = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    WellKnownNames.SavedStateAttribute,
                    predicate: static (node, _) =>
                        node is PropertyDeclarationSyntax
                        || node is FieldDeclarationSyntax
                        || node is VariableDeclaratorSyntax,
                    transform: static (ctx, _) => SectionModel.Create(ctx))
                .Where(static model => model is not null);

            IncrementalValuesProvider<ServiceModel?> services = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    WellKnownNames.SaveServiceAttribute,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) => ServiceModel.Create(ctx))
                .Where(static model => model is not null);

            IncrementalValueProvider<Materials> materials = files.Collect()
                .Combine(sections.Collect())
                .Combine(services.Collect())
                .Select(static (tuple, _) => Materials.Create(tuple.Left.Left, tuple.Left.Right, tuple.Right));

            context.RegisterSourceOutput(materials, static (spc, model) => Emit(spc, model));
        }

        private static FileModel? CreateFile(GeneratorAttributeSyntaxContext context)
        {
            string fileName = string.Empty;
            int version = 1;

            if (context.Attributes.Length > 0)
            {
                AttributeData attribute = context.Attributes[0];
                if (attribute.ConstructorArguments.Length > 0)
                {
                    fileName = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
                }

                foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
                {
                    if (named.Key == "Version" && named.Value.Value is int declared && declared > 0)
                    {
                        version = declared;
                    }
                }
            }

            return FileModel.Create(context, fileName, version);
        }

        private static void Emit(SourceProductionContext context, Materials materials)
        {
            foreach (FileModel file in materials.Files)
            {
                Report(context, file.Issues);
            }

            foreach (ServiceModel service in materials.Services)
            {
                Report(context, service.Issues);
            }

            foreach (SectionModel section in materials.Sections)
            {
                Report(context, section.Issues);
            }

            List<FileModel> usableFiles = materials.Files.Where(static f => f.Usable).ToList();
            List<ServiceModel> usableServices = materials.Services.Where(static s => s.Usable).ToList();

            // (1) Resolve which file every section belongs to (when File is omitted: auto-assign only if there is exactly one
            // file, otherwise report an error - never guess).
            List<ResolvedSection> resolved = ResolveSections(context, materials.Sections, usableFiles);

            // (2) Group by file and check for duplicate keys within a file.
            var sectionsByFile = new Dictionary<string, List<SectionModel>>(StringComparer.Ordinal);
            foreach (ResolvedSection entry in resolved)
            {
                if (!sectionsByFile.TryGetValue(entry.FileName, out List<SectionModel>? list))
                {
                    list = new List<SectionModel>();
                    sectionsByFile.Add(entry.FileName, list);
                }

                list.Add(entry.Section);
            }

            foreach (KeyValuePair<string, List<SectionModel>> pair in sectionsByFile)
            {
                foreach (IGrouping<string, SectionModel> group in pair.Value.GroupBy(static s => s.Key, StringComparer.Ordinal))
                {
                    if (group.Count() <= 1)
                    {
                        continue;
                    }

                    foreach (SectionModel duplicated in group.Skip(1))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.DuplicateSectionKey,
                            duplicated.Location,
                            pair.Key,
                            duplicated.Key));
                    }
                }
            }

            // (3) Generate.
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("// Generated by SaveState.Generator. Changes will be overwritten on the next build.");
            sb.AppendLine();

            var handled = new HashSet<string>(StringComparer.Ordinal);

            foreach (FileModel file in usableFiles)
            {
                List<SectionModel> own;
                if (!sectionsByFile.TryGetValue(file.FileName, out own))
                {
                    own = new List<SectionModel>();
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.EmptySaveFile,
                        file.Location,
                        file.FileName));
                }

                // Keep only one entry per unique key (duplicates are already reported, so do not emit two properties for them).
                List<SectionModel> emitted = own
                    .GroupBy(static s => s.Key, StringComparer.Ordinal)
                    .Select(static g => g.First())
                    .OrderBy(static s => s.Key, StringComparer.Ordinal)
                    .ToList();

                EmitFileClass(sb, file, emitted);
                handled.Add(file.FileName);
            }

            // A section points at a file that does not exist (or whose file class is unusable) → already reported during
            // validation, so generate nothing here.
            foreach (SectionModel section in materials.Sections)
            {
                if (!section.Usable)
                {
                    continue;
                }

                ResolvedSection? match = resolved
                    .Cast<ResolvedSection?>()
                    .FirstOrDefault(entry => ReferenceEquals(entry!.Value.Section, section));

                if (match == null || !handled.Contains(match.Value.FileName))
                {
                    string target = match == null ? "<unresolved>" : match.Value.FileName;
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.SaveFileNotFound,
                        section.Location,
                        section.MemberName,
                        "'" + target + "' has no usable [SaveFile] class"));
                }
            }

            // Service accessors: only generate for a usable service and the sections that belong to it.
            foreach (ServiceModel service in usableServices)
            {
                List<SectionModel> own = resolved
                    .Where(entry => entry.Section.Service.ClassFullName == service.ClassFullName
                                    && handled.Contains(entry.FileName))
                    .Select(static entry => entry.Section)
                    .OrderBy(static s => s.MemberName, StringComparer.Ordinal)
                    .ToList();

                if (own.Count == 0)
                {
                    continue;
                }

                EmitServiceAccessors(sb, service, own);
            }

            context.AddSource("SaveState.Generated.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Pairs every usable section with the file it belongs to. Returns a fresh list - the cached section models
        /// are never modified.
        /// </summary>
        private static List<ResolvedSection> ResolveSections(
            SourceProductionContext context,
            IReadOnlyList<SectionModel> sections,
            List<FileModel> usableFiles)
        {
            var resolved = new List<ResolvedSection>(sections.Count);

            foreach (SectionModel section in sections)
            {
                if (!section.Usable)
                {
                    continue;
                }

                if (section.DeclaredFileName != null)
                {
                    resolved.Add(new ResolvedSection(section, section.DeclaredFileName));
                    continue;
                }

                if (usableFiles.Count == 1)
                {
                    resolved.Add(new ResolvedSection(section, usableFiles[0].FileName));
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.SaveFileNotFound,
                    section.Location,
                    section.MemberName,
                    usableFiles.Count == 0
                        ? "the project contains no [SaveFile] class"
                        : "there are " + usableFiles.Count + " save files, so the target must be named in [SavedState(File = \"...\")]"));
            }

            return resolved;
        }

        private static void EmitFileClass(StringBuilder sb, FileModel file, List<SectionModel> sections)
        {
            OpenNamespace(sb, file.Namespace);

            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Generated implementation of save file \"" + file.FileName + "\" (triggered by [SaveFile]).");
            sb.AppendLine("/// </summary>");
            sb.AppendLine(file.Modifiers + "partial class " + file.ClassName);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Save file name (generated from [SaveFile]).</summary>");
            sb.AppendLine("    public const string FileNameValue = \"" + file.FileName + "\";");
            sb.AppendLine();
            sb.AppendLine("    /// <inheritdoc />");
            sb.AppendLine("    public override string GetFileName()");
            sb.AppendLine("    {");
            sb.AppendLine("        return FileNameValue;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Save version (migration anchor; bump it by 1 and add an ISaveMigration when the structure changes).</summary>");
            sb.AppendLine("    public override int Version { get; set; } = " + file.Version + ";");
            sb.AppendLine();

            foreach (SectionModel section in sections)
            {
                sb.AppendLine("    /// <summary>Section \"" + section.Key + "\" from " + section.Service.ClassName + "." + section.MemberName + ".</summary>");
                sb.AppendLine("    public " + section.TypeFullName + " " + section.Key + " { get; set; } = null!;");
                sb.AppendLine();
            }

            // CollectFrom
            sb.AppendLine("    /// <inheritdoc />");
            sb.AppendLine("    public override void CollectFrom(" + WellKnownNames.SaveRegistryFullyQualified + " registry)");
            sb.AppendLine("    {");
            foreach (SectionModel section in sections)
            {
                sb.AppendLine("        " + section.Service.ClassFullName + " service_" + Sanitize(section.Key)
                              + " = registry.Get<" + section.Service.ClassFullName + ">();");
                sb.AppendLine("        " + section.Key + " = service_" + Sanitize(section.Key) + "." + section.AccessorName + ";");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // RestoreTo
            sb.AppendLine("    /// <inheritdoc />");
            sb.AppendLine("    public override void RestoreTo(" + WellKnownNames.SaveRegistryFullyQualified + " registry)");
            sb.AppendLine("    {");
            sb.AppendLine("        // Null-check every section: even a hand-built DTO cannot write null into the service state.");
            foreach (ServiceModel service in ServicesOf(sections))
            {
                sb.AppendLine("        // " + service.ClassName);
                foreach (SectionModel section in SectionsOf(sections, service))
                {
                    sb.AppendLine("        if (" + section.Key + " != null)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            " + service.ClassFullName + " service_" + Sanitize(section.Key)
                                  + " = registry.Get<" + service.ClassFullName + ">();");
                    sb.AppendLine("            service_" + Sanitize(section.Key) + "." + section.AccessorName + " = " + section.Key + ";");
                    sb.AppendLine("        }");
                }

                // One callback per service, after its own sections are in place: reconnect derived data,
                // re-register event handlers. It also fires when a section was missing and got a default
                // instance - the state was replaced, so re-registration matters even more.
                sb.AppendLine("        registry.Get<" + service.ClassFullName + ">().__InvokeAfterRestore();");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // FillMissingDefaults
            sb.AppendLine("    /// <inheritdoc />");
            sb.AppendLine("    public override bool FillMissingDefaults(out " + WellKnownNames.ReadOnlyListOfString + " missingSections)");
            sb.AppendLine("    {");
            sb.AppendLine("        " + WellKnownNames.ListOfString + " missing = new " + WellKnownNames.ListOfString + "(" + sections.Count + ");");
            foreach (SectionModel section in sections)
            {
                sb.AppendLine("        if (" + section.Key + " == null)");
                sb.AppendLine("        {");
                sb.AppendLine("            " + section.Key + " = " + section.DefaultInstanceExpression + ";");
                sb.AppendLine("            missing.Add(nameof(" + section.Key + "));");
                sb.AppendLine("        }");
            }

            sb.AppendLine();
            sb.AppendLine("        missingSections = missing;");
            sb.AppendLine("        return missing.Count > 0;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            CloseNamespace(sb, file.Namespace);
        }

        private static void EmitServiceAccessors(StringBuilder sb, ServiceModel service, List<SectionModel> sections)
        {
            OpenNamespace(sb, service.Namespace);

            sb.AppendLine("/// <summary>Save accessors for " + service.ClassName + " (generated from [SavedState]; accessible within the same assembly).</summary>");
            sb.AppendLine(service.Modifiers + "partial class " + service.ClassName);
            sb.AppendLine("{");
            foreach (SectionModel section in sections)
            {
                sb.AppendLine("    /// <summary>Accesses " + section.MemberName + " (section \"" + section.Key + "\").</summary>");
                sb.AppendLine("    internal " + section.TypeFullName + " " + section.AccessorName);
                sb.AppendLine("    {");
                sb.AppendLine("        get { return this." + section.MemberName + "; }");
                sb.AppendLine("        set { this." + section.MemberName + " = value; }");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Called after this service's sections have been restored from a save file.");
            sb.AppendLine("    /// Implement it in your own partial to reconnect derived data or re-register event handlers.");
            sb.AppendLine("    /// Optional: an unimplemented <c>partial void</c> is removed by the compiler.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    partial void OnAfterRestore();");
            sb.AppendLine();
            sb.AppendLine("    internal void __InvokeAfterRestore() => OnAfterRestore();");
            sb.AppendLine("}");
            sb.AppendLine();

            CloseNamespace(sb, service.Namespace);
        }

        /// <summary>The distinct services of a file, in a deterministic order (also the hook call order).</summary>
        private static List<ServiceModel> ServicesOf(List<SectionModel> sections)
        {
            return sections
                .Select(static s => s.Service)
                .GroupBy(static s => s.ClassFullName, StringComparer.Ordinal)
                .Select(static g => g.First())
                .OrderBy(static s => s.ClassFullName, StringComparer.Ordinal)
                .ToList();
        }

        private static IEnumerable<SectionModel> SectionsOf(List<SectionModel> sections, ServiceModel service)
        {
            foreach (SectionModel section in sections)
            {
                if (string.Equals(section.Service.ClassFullName, service.ClassFullName, StringComparison.Ordinal))
                {
                    yield return section;
                }
            }
        }

        private static void Report(SourceProductionContext context, List<IssueInfo> issues)
        {
            foreach (IssueInfo issue in issues)
            {
                context.ReportDiagnostic(issue.ToDiagnostic());
            }
        }

        private static void OpenNamespace(StringBuilder sb, string namespaceName)
        {
            if (namespaceName.Length == 0)
            {
                return;
            }

            sb.Append("namespace ").Append(namespaceName).AppendLine();
            sb.AppendLine("{");
        }

        private static void CloseNamespace(StringBuilder sb, string namespaceName)
        {
            if (namespaceName.Length == 0)
            {
                sb.AppendLine();
                return;
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        /// <summary>Turns a section key into characters usable as a local-variable suffix (a key may legally contain <c>@</c>).</summary>
        private static string Sanitize(string value)
        {
            return value.Replace("@", string.Empty).Replace(".", "_");
        }

        /// <summary>A section together with the save file it belongs to (resolved at emit time; see the class remarks).</summary>
        private readonly struct ResolvedSection
        {
            public ResolvedSection(SectionModel section, string fileName)
            {
                Section = section;
                FileName = fileName;
            }

            public SectionModel Section { get; }

            public string FileName { get; }
        }

        /// <summary>The inputs of a single generation run (files / sections / services), comparable for caching.</summary>
        private sealed class Materials : IEquatable<Materials>
        {
            private Materials(EquatableArray<FileModel> files, EquatableArray<SectionModel> sections, EquatableArray<ServiceModel> services)
            {
                Files = files;
                Sections = sections;
                Services = services;
            }

            public EquatableArray<FileModel> Files { get; }

            public EquatableArray<SectionModel> Sections { get; }

            public EquatableArray<ServiceModel> Services { get; }

            public static Materials Create(
                ImmutableArray<FileModel?> files,
                ImmutableArray<SectionModel?> sections,
                ImmutableArray<ServiceModel?> services)
            {
                return new Materials(
                    new EquatableArray<FileModel>(NotNull(files).OrderBy(static m => m.ClassFullName, StringComparer.Ordinal)),
                    new EquatableArray<SectionModel>(NotNull(sections)
                        .OrderBy(static m => m.Service.ClassFullName, StringComparer.Ordinal)
                        .ThenBy(static m => m.Key, StringComparer.Ordinal)),
                    new EquatableArray<ServiceModel>(NotNull(services).OrderBy(static m => m.ClassFullName, StringComparer.Ordinal)));
            }

            private static IEnumerable<T> NotNull<T>(ImmutableArray<T?> items)
                where T : class
            {
                foreach (T? item in items)
                {
                    if (item is not null)
                    {
                        yield return item;
                    }
                }
            }

            public bool Equals(Materials? other)
            {
                return other != null
                       && Files.Equals(other.Files)
                       && Sections.Equals(other.Sections)
                       && Services.Equals(other.Services);
            }

            public override bool Equals(object? obj)
            {
                return Equals(obj as Materials);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Files.GetHashCode();
                    hash = (hash * 31) + Sections.GetHashCode();
                    return (hash * 31) + Services.GetHashCode();
                }
            }
        }
    }
}
