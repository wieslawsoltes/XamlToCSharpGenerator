using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Generator;
using XamlToCSharpGenerator.Avalonia.Binding;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator;

public sealed class AvaloniaExternalXmlnsResolutionTests
{
    [Fact]
    public void Resolves_DataGrid_From_External_Assembly_Xmlns_Definition_Without_Compatibility_Fallback()
    {
        _ = typeof(DataGrid);

        const string code = """
            namespace Demo
            {
                public partial class MainWindow : global::Avalonia.Controls.Window
                {
                }
            }
            """;

        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Demo.MainWindow">
                <DataGrid AutoGenerateColumns="False">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Name"
                                            Binding="{Binding Name}" />
                    </DataGrid.Columns>
                </DataGrid>
            </Window>
            """;

        var compilation = CreateCompilationWithLoadedAssemblyReferences(code);
        var dataGridAssembly = compilation.SourceModule.ReferencedAssemblySymbols
            .FirstOrDefault(static assembly => assembly.Name == "Avalonia.Controls.DataGrid");

        Assert.NotNull(dataGridAssembly);
        Assert.NotNull(dataGridAssembly!.GetTypeByMetadataName("Avalonia.Controls.DataGrid"));
        Assert.Contains(
            dataGridAssembly.GetAttributes(),
            static attribute =>
                string.Equals(attribute.AttributeClass?.ToDisplayString(), "Avalonia.Metadata.XmlnsDefinitionAttribute", StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length >= 2 &&
                string.Equals(attribute.ConstructorArguments[0].Value as string, "https://github.com/avaloniaui", StringComparison.Ordinal) &&
                string.Equals(attribute.ConstructorArguments[1].Value as string, "Avalonia.Controls", StringComparison.Ordinal));

        var resolveTypeSymbol = typeof(AvaloniaSemanticBinder)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(static method => method.Name == "ResolveTypeSymbol" && method.GetParameters().Length == 3);
        var resolvedDataGridType = (INamedTypeSymbol?)resolveTypeSymbol.Invoke(
            null,
            [compilation, "https://github.com/avaloniaui", "DataGrid"]);

        Assert.NotNull(resolvedDataGridType);
        Assert.Equal("Avalonia.Controls.DataGrid", resolvedDataGridType!.ToDisplayString());

        var parser = new SimpleXamlDocumentParser();
        var (document, parseDiagnostics) = parser.Parse(new XamlFileInput("MainWindow.axaml", "MainWindow.axaml", "AvaloniaXaml", xaml));
        Assert.Empty(parseDiagnostics.Where(static diagnostic => diagnostic.IsError));
        Assert.NotNull(document);
        Assert.Single(document!.RootObject.ChildObjects);
        Assert.Equal("https://github.com/avaloniaui", document.RootObject.ChildObjects[0].XmlNamespace);
        Assert.Equal("DataGrid", document.RootObject.ChildObjects[0].XmlTypeName);

        var (updatedCompilation, diagnostics, _) = FrameworkGeneratorTestHarness.RunGenerator(
            new AvaloniaXamlSourceGenerator(),
            compilation,
            [("MainWindow.axaml", xaml, "AvaloniaXaml", "MainWindow.axaml")],
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaXamlCompilerBackend", "SourceGen"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenCompilerEnabled", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "false")
            ]);

        Assert.Empty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainWindow");
        Assert.DoesNotContain("new global::System.Object();", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Avalonia.Controls.DataGrid();", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Deduplicates_Equivalent_Type_Candidates_From_Duplicate_Metadata_References()
    {
        const string code = """
            namespace Demo
            {
                public partial class MainWindow : global::Avalonia.Controls.Window
                {
                }
            }
            """;

        var compilation = CreateCompilationWithPackageReferenceAssemblies(code);

        var dataGridResolvers = compilation.SourceModule.ReferencedAssemblySymbols
            .Select(static assembly => new
            {
                AssemblyIdentity = assembly.Identity.ToString(),
                Candidate = assembly.GetTypeByMetadataName("Avalonia.Controls.DataGrid")
            })
            .Where(static entry => entry.Candidate is not null)
            .Select(static entry => entry.AssemblyIdentity + " -> " + entry.Candidate!.ContainingAssembly?.Identity)
            .ToArray();

        Assert.NotEmpty(dataGridResolvers);

        var candidates = DeterministicTypeResolutionSemantics.CollectCandidatesFromNamespacePrefixes(
            compilation,
            ["Avalonia.Controls"],
            "DataGrid");

        Assert.True(
            candidates.Length == 1,
            string.Join(Environment.NewLine, dataGridResolvers));
        Assert.Equal("Avalonia.Controls.DataGrid", candidates[0].ToDisplayString());
    }

    [Fact]
    public void Resolves_DataGrid_Without_Xmlns_Ambiguity_When_Avalonia_Diagnostics_Is_Referenced()
    {
        const string code = """
            namespace Demo
            {
                public partial class MainWindow : global::Avalonia.Controls.Window
                {
                }
            }
            """;

        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Demo.MainWindow">
                <DataGrid AutoGenerateColumns="False">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="Name"
                                            Binding="{Binding Name}" />
                    </DataGrid.Columns>
                </DataGrid>
            </Window>
            """;

        var compilation = CreateCompilationWithPackageReferenceAssemblies(code, includeDiagnostics: true);

        var (updatedCompilation, diagnostics, _) = FrameworkGeneratorTestHarness.RunGenerator(
            new AvaloniaXamlSourceGenerator(),
            compilation,
            [("MainWindow.axaml", xaml, "AvaloniaXaml", "MainWindow.axaml")],
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaXamlCompilerBackend", "SourceGen"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenCompilerEnabled", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "false")
            ]);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Id == "AXSG0112" && diagnostic.GetMessage().Contains("ambiguous", StringComparison.OrdinalIgnoreCase));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainWindow");
        Assert.DoesNotContain("new global::System.Object();", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Avalonia.Controls.DataGrid();", generated, StringComparison.Ordinal);
    }

    private static CSharpCompilation CreateCompilationWithLoadedAssemblyReferences(string code)
    {
        var referenceLocations = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Where(static location => File.Exists(location))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        referenceLocations.Add(typeof(global::Avalonia.AvaloniaObject).Assembly.Location);
        referenceLocations.Add(typeof(Window).Assembly.Location);
        referenceLocations.Add(typeof(DataGrid).Assembly.Location);

        var loadedReferences = referenceLocations
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToArray();

        return CreateCompilation(code, loadedReferences);
    }

    private static CSharpCompilation CreateCompilationWithPackageReferenceAssemblies(
        string code,
        bool includeDiagnostics = false)
    {
        var avaloniaPackageVersion = GetPackageVersion(typeof(Window).Assembly);
        var dataGridPackageVersion = GetPackageVersion(typeof(DataGrid).Assembly);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(ResolvePackageAssetPath("avalonia", avaloniaPackageVersion, "ref", "net8.0", "Avalonia.Base.dll")),
            MetadataReference.CreateFromFile(ResolvePackageAssetPath("avalonia", avaloniaPackageVersion, "ref", "net8.0", "Avalonia.Controls.dll")),
            MetadataReference.CreateFromFile(ResolvePackageAssetPath("avalonia.controls.datagrid", dataGridPackageVersion, "lib", "net8.0", "Avalonia.Controls.DataGrid.dll"))
        };

        if (includeDiagnostics)
        {
            references.Add(MetadataReference.CreateFromFile(
                ResolvePackageAssetPath("avalonia.diagnostics", avaloniaPackageVersion, "lib", "net8.0", "Avalonia.Diagnostics.dll")));
        }

        return CreateCompilation(code, references.ToArray());
    }

    private static CSharpCompilation CreateCompilation(string code, params MetadataReference[] additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        foreach (var additionalReference in additionalReferences)
        {
            references.Add(additionalReference);
        }

        return CSharpCompilation.Create(
            assemblyName: "Demo.Assembly",
            syntaxTrees: [syntaxTree],
            references: references.ToImmutable(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string GetGeneratedPartialClassSource(Compilation compilation, string className)
    {
        var sources = compilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .ToArray();

        var generated = sources.FirstOrDefault(source =>
            source.StartsWith("// <auto-generated />", StringComparison.Ordinal) &&
            source.Contains($"partial class {className}", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(generated))
        {
            return generated;
        }

        return sources.First(source => source.Contains($"partial class {className}", StringComparison.Ordinal));
    }

    private static string ResolvePackageAssetPath(string packageId, string packageVersion, params string[] relativeSegments)
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        var pathSegments = new string[relativeSegments.Length + 3];
        pathSegments[0] = packageRoot;
        pathSegments[1] = packageId;
        pathSegments[2] = packageVersion;
        Array.Copy(relativeSegments, 0, pathSegments, 3, relativeSegments.Length);

        var assetPath = Path.Combine(pathSegments);
        Assert.True(File.Exists(assetPath), $"Missing package asset: {assetPath}");
        return assetPath;
    }

    private static string GetPackageVersion(Assembly assembly)
    {
        var version = assembly.GetName().Version;
        Assert.NotNull(version);
        return version!.Revision > 0
            ? version.ToString()
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
