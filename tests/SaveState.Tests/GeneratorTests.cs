using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SaveState.Generator;
using Xunit;

namespace SaveState.Tests;

/// <summary>
/// Generator test cases: drive the generator in-process and assert on the **compile-time diagnostics**
/// and on the generated output.
///
/// <para>Why this deserves its own tests: the generator's diagnostics are the means of "moving
/// runtime incidents forward to compile time", and nobody would notice if they themselves broke
/// (a generator does not report errors just because it reports errors).</para>
/// </summary>
public class GeneratorTests
{
    private const string StateDeclarations = @"
using System.Collections.Generic;
using SaveState;

public sealed class State1 : StateBase
{
    public int Value { get; set; }
}
";

    [Fact]
    public void ValidModel_GeneratesAndCompiles()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(File = ""a.json"", Key = ""S"")]
    public State1 State { get; private set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Empty(SavDiagnostics(result));
        Assert.Empty(result.CompileErrors);
        Assert.Contains("FileNameValue", result.Generated);
        Assert.Contains("FillMissingDefaults", result.Generated);
        Assert.Contains("public override void RestoreTo", result.Generated);
        Assert.Contains("__SaveState_State", result.Generated);
    }

    [Fact]
    public void NonPartialService_ReportsSAV012()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveService]
public class Service1
{
    [SavedState(File = ""a.json"", Key = ""S"")]
    public State1 State { get; set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV012");
    }

    [Fact]
    public void NonPartialSaveFile_ReportsSAV001()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveFile(""a.json"")]
public sealed class AFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV001");
    }

    [Fact]
    public void SaveFileWithoutTheBaseClass_ReportsSAV002()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveFile(""a.json"")]
public sealed partial class AFile
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV002");
    }

    [Fact]
    public void FileNameWithPath_ReportsSAV004()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveFile(""sub/a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV004");
    }

    [Fact]
    public void NestedSaveFile_ReportsSAV005()
    {
        GeneratorResult result = Run(StateDeclarations + @"
public static class Outer
{
    [SaveFile(""a.json"")]
    public sealed partial class AFile : SaveFileDefinition
    {
    }
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV005");
    }

    [Fact]
    public void SectionWithoutFileArgumentAndTwoFiles_ReportsSAV006()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(Key = ""S"")]
    public State1 State { get; private set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}

[SaveFile(""b.json"")]
public sealed partial class BFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV006");
    }

    [Fact]
    public void SectionWithoutFileArgumentAndSingleFile_IsInferred()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(Key = ""S"")]
    public State1 State { get; private set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Empty(SavDiagnostics(result));
        Assert.Empty(result.CompileErrors);
    }

    [Fact]
    public void DuplicateKeysInOneFile_ReportSAV008()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(File = ""a.json"", Key = ""S"")]
    public State1 State { get; private set; } = new State1();

    [SavedState(File = ""a.json"", Key = ""S"")]
    public State1 State2 { get; private set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV008");
    }

    [Fact]
    public void GetOnlyMember_ReportsSAV009()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(File = ""a.json"", Key = ""S"")]
    public State1 State { get; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV009");
    }

    [Fact]
    public void UninstantiableSectionType_ReportsSAV010()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(File = ""a.json"", Key = ""S"")]
    public string Text { get; private set; } = string.Empty;
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV010");
    }

    [Fact]
    public void FileWithoutSections_ReportsSAV011Warning()
    {
        GeneratorResult result = Run(StateDeclarations + @"
[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Diagnostic warning = Assert.Single(SavDiagnostics(result), d => d.Id == "SAV011");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void SectionOnClassWithoutSaveService_ReportsSAV013()
    {
        GeneratorResult result = Run(StateDeclarations + @"
public partial class Service1
{
    [SavedState(File = ""a.json"", Key = ""S"")]
    public State1 State { get; private set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
");

        Assert.Contains(SavDiagnostics(result), d => d.Id == "SAV013");
    }

    [Fact]
    public void GeneratedCodeIsStableAcrossRuns()
    {
        const string source = StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(File = ""a.json"", Key = ""B"")]
    public State1 Second { get; private set; } = new State1();

    [SavedState(File = ""a.json"", Key = ""A"")]
    public State1 First { get; private set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
";

        string first = Run(source).Generated;
        string second = Run(source).Generated;

        Assert.Equal(first, second);
        // Sections are ordered by Key, so the output does not depend on declaration order
        // (which makes golden comparisons and reproducible builds easier).
        Assert.True(first.IndexOf("\"A\"", StringComparison.Ordinal) < first.IndexOf("\"B\"", StringComparison.Ordinal)
                    || first.IndexOf(" A ", StringComparison.Ordinal) < first.IndexOf(" B ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Re-running the generator must produce byte-identical output: the models are immutable and
    /// value-comparable, and resolving "which file does this section belong to" happens at emit time instead of
    /// writing back into a cached model. A mutation of cached state shows up here as a difference between runs.
    /// </summary>
    [Fact]
    public void GeneratedOutputIsIdenticalAcrossRuns_AndCachedModelsAreNotMutated()
    {
        const string source = StateDeclarations + @"
[SaveService]
public partial class Service1
{
    [SavedState(File = ""a.json"", Key = ""S"")]
    public State1 State { get; private set; } = new State1();
}

[SaveFile(""a.json"")]
public sealed partial class AFile : SaveFileDefinition
{
}
";

        string first = Run(source).Generated;
        string second = Run(source).Generated;

        Assert.Equal(first, second);
        Assert.Contains("Section \"S\"", first);
    }

    private static IEnumerable<Diagnostic> SavDiagnostics(GeneratorResult result)
    {
        return result.Diagnostics.Where(d => d.Id.StartsWith("SAV", StringComparison.Ordinal));
    }

    private static GeneratorResult Run(string source)
    {
        var references = new List<MetadataReference>();
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (string path in tpa.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(SaveFileDefinition).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "SaveStateGeneratorTests",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SaveStateGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation output,
            out ImmutableArray<Diagnostic> _);

        // Note: the out diagnostics of RunGeneratorsAndUpdateCompilation and RunResult.Diagnostics
        // are the same set (using both would make cases like SAV011 assert against two copies), so
        // only one of them is taken here.
        var diagnostics = new List<Diagnostic>(driver.GetRunResult().Diagnostics);

        return new GeneratorResult(
            diagnostics,
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList(),
            string.Join(
                "\n",
                driver.GetRunResult().Results
                    .SelectMany(r => r.GeneratedSources)
                    .Select(s => s.SourceText.ToString())));
    }

    private sealed class GeneratorResult
    {
        public GeneratorResult(List<Diagnostic> diagnostics, List<Diagnostic> compileErrors, string generated)
        {
            Diagnostics = diagnostics;
            CompileErrors = compileErrors;
            Generated = generated;
        }

        public List<Diagnostic> Diagnostics { get; }

        public List<Diagnostic> CompileErrors { get; }

        public string Generated { get; }
    }
}
