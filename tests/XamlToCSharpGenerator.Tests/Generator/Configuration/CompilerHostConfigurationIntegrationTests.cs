using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Generator;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CompilerHostConfigurationIntegrationTests
{
    [Fact]
    public void File_Configuration_Can_Enable_Generator_When_MsBuild_Disables_It()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="Hello" />
            </UserControl>
            """;

        const string configurationJson = """
            {
              "schemaVersion": 1,
              "build": {
                "isEnabled": true,
                "backend": "SourceGen"
              }
            }
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGeneratorWithOptions(
            compilation,
            [
                ("/tmp/MainView.axaml", xaml, "AvaloniaXaml", "MainView.axaml"),
                ("/tmp/xaml-sourcegen.config.json", configurationJson, "None", "xaml-sourcegen.config.json")
            ],
            Array.Empty<KeyValuePair<string, string>>());

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generatedSource = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__PopulateGeneratedObjectGraph", generatedSource);
    }

    [Fact]
    public void Code_Configuration_Can_Enable_Generator_When_MsBuild_Disables_It()
    {
        const string code = """
            using System.Reflection;

            [assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "SourceGen")]
            [assembly: AssemblyMetadata("XamlSourceGen.Build.IsEnabled", "true")]

            namespace Demo
            {
                public partial class MainView
                {
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="Hello from code config" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGeneratorWithOptions(
            compilation,
            [
                ("/tmp/MainView.axaml", xaml, "AvaloniaXaml", "MainView.axaml")
            ],
            Array.Empty<KeyValuePair<string, string>>());

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generatedSource = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__PopulateGeneratedObjectGraph", generatedSource);
    }

    [Fact]
    public void ConfigurationPrecedence_Can_Promote_File_Source_Above_Code_Source()
    {
        const string code = """
            using System.Reflection;

            [assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "SourceGen")]
            [assembly: AssemblyMetadata("XamlSourceGen.Build.IsEnabled", "true")]

            namespace Demo
            {
                public partial class MainView
                {
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="Should not generate when file precedence is highest" />
            </UserControl>
            """;

        const string configurationJson = """
            {
              "schemaVersion": 1,
              "build": {
                "isEnabled": false,
                "backend": "XamlIl"
              }
            }
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGeneratorWithOptions(
            compilation,
            [
                ("/tmp/MainView.axaml", xaml, "AvaloniaXaml", "MainView.axaml"),
                ("/tmp/xaml-sourcegen.config.json", configurationJson, "None", "xaml-sourcegen.config.json")
            ],
            [
                new KeyValuePair<string, string>(
                    "build_property.XamlSourceGenConfigurationPrecedence",
                    "ProjectDefaultFile=80;MsBuild=200;Code=300;File=400")
            ]);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(updatedCompilation.SyntaxTrees);
    }

    [Fact]
    public void ConfigurationPrecedence_Invalid_Format_Reports_Warning_Diagnostic()
    {
        const string code = """
            using System.Reflection;

            [assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "SourceGen")]
            [assembly: AssemblyMetadata("XamlSourceGen.Build.IsEnabled", "true")]

            namespace Demo
            {
                public partial class MainView
                {
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="Hello from precedence warning test" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGeneratorWithOptions(
            compilation,
            [
                ("/tmp/MainView.axaml", xaml, "AvaloniaXaml", "MainView.axaml")
            ],
            [
                new KeyValuePair<string, string>(
                    "build_property.XamlSourceGenConfigurationPrecedence",
                    "File=NaN;Unknown=123;BrokenSegment")
            ]);

        Assert.Contains(diagnostics, d => d.Id == "AXSG0933");
        var generatedSource = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__PopulateGeneratedObjectGraph", generatedSource);
    }

    private static CSharpCompilation CreateCompilation(string code)
    {
        return CSharpCompilation.Create(
            assemblyName: "CompilerHostConfigurationIntegrationTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(code)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Reflection.AssemblyMetadataAttribute).Assembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (Compilation UpdatedCompilation, ImmutableArray<Diagnostic> Diagnostics) RunGeneratorWithOptions(
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Text, string SourceItemGroup, string TargetPath)> additionalFiles,
        IReadOnlyList<KeyValuePair<string, string>> options)
    {
        var additionalTexts = additionalFiles
            .Select(static file => new InMemoryAdditionalText(file.Path, file.Text))
            .ToImmutableArray();
        var additionalFileMetadata = additionalFiles
            .Select(static file =>
            {
                var metadata = new List<KeyValuePair<string, string>>
                {
                    new("build_metadata.AdditionalFiles.SourceItemGroup", file.SourceItemGroup),
                    new("build_metadata.AdditionalFiles.TargetPath", file.TargetPath)
                };

                return (file.Path, Values: (IEnumerable<KeyValuePair<string, string>>)metadata);
            })
            .ToImmutableArray();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AvaloniaXamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(options, additionalFileMetadata));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        return (updatedCompilation, diagnostics);
    }
}
