using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CompilationTypeSymbolCatalogTests
{
    [Fact]
    public void Avalonia_Default_Contract_Map_Defines_Expected_Core_Contracts()
    {
        var map = SemanticContractMaps.AvaloniaDefault;

        var styledElement = map.GetTypeContract(TypeContractId.StyledElement);
        Assert.Equal("Avalonia.Default", map.MapId);
        Assert.Equal("Avalonia", map.FrameworkId);
        Assert.Contains("Avalonia.StyledElement", styledElement.MetadataNames);
        Assert.Contains("Avalonia.Controls.Control", map.GetTypeContract(TypeContractId.AvaloniaControl).MetadataNames);
        Assert.Contains("Avalonia.Controls.Panel", map.GetTypeContract(TypeContractId.AvaloniaPanel).MetadataNames);
        Assert.Contains("Avalonia.Markup.Xaml.Styling.ResourceInclude", map.GetTypeContract(TypeContractId.ResourceInclude).MetadataNames);
        Assert.Contains("Avalonia.Markup.Xaml.Styling.MergeResourceInclude", map.GetTypeContract(TypeContractId.MergeResourceInclude).MetadataNames);
        Assert.Contains("Avalonia.Markup.Xaml.Styling.StyleInclude", map.GetTypeContract(TypeContractId.StyleInclude).MetadataNames);
        Assert.Contains("Avalonia.Input.KeyGesture", map.GetTypeContract(TypeContractId.AvaloniaKeyGesture).MetadataNames);
        Assert.Contains("Avalonia.Input.Key", map.GetTypeContract(TypeContractId.AvaloniaKey).MetadataNames);
        Assert.Contains("Avalonia.Input.KeyModifiers", map.GetTypeContract(TypeContractId.AvaloniaKeyModifiers).MetadataNames);
    }

    [Fact]
    public void NoUi_Default_Contract_Map_Contains_Bcl_Contracts_Only()
    {
        var map = SemanticContractMaps.NoUiDefault;

        Assert.Equal("NoUi.Default", map.MapId);
        Assert.Equal("NoUi", map.FrameworkId);
        Assert.Contains("System.Object", map.GetTypeContract(TypeContractId.SystemObject).MetadataNames);
        Assert.DoesNotContain(
            map.TypeContracts,
            contract => contract.MetadataNames.Any(name => name.StartsWith("Avalonia.", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void Catalog_Uses_Fallback_Metadata_Names_When_Primary_Is_Missing()
    {
        var map = new SemanticContractMap(
            mapId: "Test.Fallback",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.StyledElement,
                    ImmutableArray.Create("Demo.DoesNotExist", "Demo.StyledElement"),
                    IsRequired: true,
                    FeatureTag: "tests")
            ]);

        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public class StyledElement
                {
                }
            }
            """);

        var catalog = CompilationTypeSymbolCatalog.Create(compilation, map);

        Assert.True(catalog.TryGet(TypeContractId.StyledElement, out var symbol));
        Assert.Equal("Demo.StyledElement", symbol!.ToDisplayString());
        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    public void Catalog_Emits_Diagnostic_For_Missing_Required_Contract()
    {
        var map = new SemanticContractMap(
            mapId: "Test.RequiredMissing",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.MissingType"),
                    IsRequired: true,
                    FeatureTag: "tests")
            ]);
        var compilation = CreateCompilation("namespace Demo { public class Placeholder { } }");

        var catalog = CompilationTypeSymbolCatalog.Create(compilation, map);
        var diagnostic = Assert.Single(catalog.Diagnostics);

        Assert.Equal("AXSG0910", diagnostic.Code);
        Assert.Equal(TypeContractId.Style, diagnostic.ContractId);
        Assert.True(diagnostic.IsRequired);
    }

    [Fact]
    public void Catalog_Does_Not_Emit_Diagnostic_For_Missing_Optional_Contract()
    {
        var map = new SemanticContractMap(
            mapId: "Test.OptionalMissing",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.MissingType"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);
        var compilation = CreateCompilation("namespace Demo { public class Placeholder { } }");

        var catalog = CompilationTypeSymbolCatalog.Create(compilation, map);

        Assert.False(catalog.TryGet(TypeContractId.Style, out _));
        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    public void Catalog_Is_Cached_Per_Compilation_And_MapId()
    {
        var map = new SemanticContractMap(
            mapId: "Test.Cache",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.StyledElement,
                    ImmutableArray.Create("Demo.StyledElement"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);
        var compilation = CreateCompilation("namespace Demo { public class StyledElement { } }");

        var first = CompilationTypeSymbolCatalog.Create(compilation, map);
        var second = CompilationTypeSymbolCatalog.Create(compilation, map);

        Assert.Same(first, second);
    }

    [Fact]
    public void Catalog_Does_Not_ReUse_Cache_For_Different_Map_Content_With_Same_MapId()
    {
        var firstMap = new SemanticContractMap(
            mapId: "Test.Cache.Collision",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.StyledElement,
                    ImmutableArray.Create("Demo.StyledElement"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        var secondMap = new SemanticContractMap(
            mapId: "Test.Cache.Collision",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Style"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public class StyledElement
                {
                }

                public class Style
                {
                }
            }
            """);

        var first = CompilationTypeSymbolCatalog.Create(compilation, firstMap);
        var second = CompilationTypeSymbolCatalog.Create(compilation, secondMap);

        Assert.NotSame(first, second);
        Assert.True(first.TryGet(TypeContractId.StyledElement, out _));
        Assert.False(first.TryGet(TypeContractId.Style, out _));
        Assert.True(second.TryGet(TypeContractId.Style, out _));
        Assert.False(second.TryGet(TypeContractId.StyledElement, out _));
    }

    [Fact]
    public void Catalog_ReUses_Cache_For_Equivalent_Map_Content_With_Different_Map_Instances()
    {
        var firstMap = new SemanticContractMap(
            mapId: "Test.Cache.Equivalent",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.StyledElement,
                    ImmutableArray.Create("Demo.StyledElement"),
                    IsRequired: false,
                    FeatureTag: "tests"),
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Style"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        var secondMap = new SemanticContractMap(
            mapId: "Test.Cache.Equivalent",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Style"),
                    IsRequired: false,
                    FeatureTag: "tests"),
                new SemanticTypeContract(
                    TypeContractId.StyledElement,
                    ImmutableArray.Create("Demo.StyledElement"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public class StyledElement
                {
                }

                public class Style
                {
                }
            }
            """);

        var first = CompilationTypeSymbolCatalog.Create(compilation, firstMap);
        var second = CompilationTypeSymbolCatalog.Create(compilation, secondMap);

        Assert.Same(first, second);
        Assert.True(second.TryGet(TypeContractId.StyledElement, out _));
        Assert.True(second.TryGet(TypeContractId.Style, out _));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            assemblyName: "SemanticContractCatalogTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
