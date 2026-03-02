using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.InteropServices;
using XamlToCSharpGenerator.Avalonia.Framework;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaFrameworkProfileTests
{
    [Fact]
    public void SemanticBinder_And_Emitter_Are_Reused_As_Singletons()
    {
        IXamlFrameworkProfile profile = AvaloniaFrameworkProfile.Instance;

        var firstBinder = profile.CreateSemanticBinder();
        var secondBinder = profile.CreateSemanticBinder();
        var firstEmitter = profile.CreateEmitter();
        var secondEmitter = profile.CreateEmitter();

        Assert.Same(firstBinder, secondBinder);
        Assert.Same(firstEmitter, secondEmitter);
    }

    [Fact]
    public void DocumentEnrichers_Are_Reused_As_Shared_Array_Instance()
    {
        IXamlFrameworkProfile profile = AvaloniaFrameworkProfile.Instance;

        var first = profile.CreateDocumentEnrichers();
        var second = profile.CreateDocumentEnrichers();

        var firstArray = ImmutableCollectionsMarshal.AsArray(first);
        var secondArray = ImmutableCollectionsMarshal.AsArray(second);
        Assert.NotNull(firstArray);
        Assert.NotNull(secondArray);
        Assert.Same(firstArray, secondArray);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void BuildContract_Exposes_Avalonia_SourceItemGroups()
    {
        IXamlFrameworkProfile profile = AvaloniaFrameworkProfile.Instance;
        var contract = profile.BuildContract;

        Assert.Equal("build_metadata.AdditionalFiles.SourceItemGroup", contract.SourceItemGroupMetadataName);
        Assert.Equal("build_metadata.AdditionalFiles.TargetPath", contract.TargetPathMetadataName);
        Assert.Equal("AvaloniaXaml", contract.NormalizeSourceItemGroup(null));
        Assert.True(contract.IsXamlSourceItemGroup(null));
        Assert.True(contract.IsXamlSourceItemGroup("AvaloniaXaml"));
        Assert.False(contract.IsXamlSourceItemGroup("OtherXaml"));
        Assert.True(contract.IsTransformRuleSourceItemGroup("AvaloniaSourceGenTransformRule"));
        Assert.True(contract.IsXamlPath("View.axaml"));
        Assert.True(contract.IsXamlPath("View.xaml"));
        Assert.False(contract.IsXamlPath("View.cs"));
    }

    [Fact]
    public void TransformProvider_Merges_Duplicate_Aliases_With_Diagnostics()
    {
        IXamlFrameworkProfile profile = AvaloniaFrameworkProfile.Instance;
        var provider = profile.TransformProvider;
        var first = provider.ParseTransformRule(new XamlFrameworkTransformRuleInput(
            "one.json",
            """
            {
              "typeAliases": [
                { "xmlNamespace": "https://github.com/avaloniaui", "xamlType": "Demo", "clrType": "App.DemoA" }
              ]
            }
            """));
        var second = provider.ParseTransformRule(new XamlFrameworkTransformRuleInput(
            "two.json",
            """
            {
              "typeAliases": [
                { "xmlNamespace": "https://github.com/avaloniaui", "xamlType": "Demo", "clrType": "App.DemoB" }
              ]
            }
            """));

        var aggregate = provider.MergeTransformRules([first, second]);

        Assert.Single(aggregate.Configuration.TypeAliases);
        Assert.Contains(aggregate.Diagnostics, diagnostic => diagnostic.Id == "AXSG0903");
    }

    [Fact]
    public void BuildParserSettings_Adds_Implicit_Standard_Prefixes_When_Enabled()
    {
        IXamlFrameworkProfile profile = AvaloniaFrameworkProfile.Instance;
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText("class C {}")],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var options = new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: false,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: false,
            HotReloadEnabled: false,
            HotReloadErrorResilienceEnabled: false,
            IdeHotReloadEnabled: false,
            HotDesignEnabled: false,
            IosHotReloadEnabled: false,
            IosHotReloadUseInterpreter: false,
            DotNetWatchBuild: false,
            BuildingInsideVisualStudio: false,
            BuildingByReSharper: false,
            TracePasses: false,
            MetricsEnabled: false,
            MetricsDetailed: false,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: false,
            TypeResolutionCompatibilityFallbackEnabled: true,
            AllowImplicitXmlnsDeclaration: true,
            ImplicitStandardXmlnsPrefixesEnabled: true,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: "Sample",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "TestAssembly");

        var settings = profile.BuildParserSettings(compilation, options);

        Assert.True(settings.AllowImplicitDefaultXmlns);
        Assert.Equal("https://github.com/avaloniaui", settings.ImplicitDefaultXmlns);
        Assert.Equal("http://schemas.microsoft.com/winfx/2006/xaml", settings.GlobalXmlnsPrefixes["x"]);
        Assert.Equal("http://schemas.microsoft.com/expression/blend/2008", settings.GlobalXmlnsPrefixes["d"]);
        Assert.Equal("http://schemas.openxmlformats.org/markup-compatibility/2006", settings.GlobalXmlnsPrefixes["mc"]);
        Assert.Equal("https://github.com/avaloniaui", settings.GlobalXmlnsPrefixes[string.Empty]);
    }

    [Fact]
    public void BuildParserSettings_Parses_GlobalXmlnsPrefixes_With_Mixed_Delimiters_And_Invalid_Entries()
    {
        IXamlFrameworkProfile profile = AvaloniaFrameworkProfile.Instance;
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText("class C {}")],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var options = new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: false,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: false,
            HotReloadEnabled: false,
            HotReloadErrorResilienceEnabled: false,
            IdeHotReloadEnabled: false,
            HotDesignEnabled: false,
            IosHotReloadEnabled: false,
            IosHotReloadUseInterpreter: false,
            DotNetWatchBuild: false,
            BuildingInsideVisualStudio: false,
            BuildingByReSharper: false,
            TracePasses: false,
            MetricsEnabled: false,
            MetricsDetailed: false,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: false,
            TypeResolutionCompatibilityFallbackEnabled: true,
            AllowImplicitXmlnsDeclaration: true,
            ImplicitStandardXmlnsPrefixesEnabled: true,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: " foo = https://example.com/foo ;invalidEntry,bar=https://example.com/bar\r\nbaz=https://example.com/baz",
            RootNamespace: "Sample",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "TestAssembly");

        var settings = profile.BuildParserSettings(compilation, options);

        Assert.Equal("https://example.com/foo", settings.GlobalXmlnsPrefixes["foo"]);
        Assert.Equal("https://example.com/bar", settings.GlobalXmlnsPrefixes["bar"]);
        Assert.Equal("https://example.com/baz", settings.GlobalXmlnsPrefixes["baz"]);
        Assert.False(settings.GlobalXmlnsPrefixes.ContainsKey("invalidEntry"));
    }

    [Fact]
    public void BuildParserSettings_GlobalXmlnsPrefixes_Override_Implicit_Standard_Prefixes()
    {
        IXamlFrameworkProfile profile = AvaloniaFrameworkProfile.Instance;
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText("class C {}")],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var options = new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: false,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: false,
            HotReloadEnabled: false,
            HotReloadErrorResilienceEnabled: false,
            IdeHotReloadEnabled: false,
            HotDesignEnabled: false,
            IosHotReloadEnabled: false,
            IosHotReloadUseInterpreter: false,
            DotNetWatchBuild: false,
            BuildingInsideVisualStudio: false,
            BuildingByReSharper: false,
            TracePasses: false,
            MetricsEnabled: false,
            MetricsDetailed: false,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: false,
            TypeResolutionCompatibilityFallbackEnabled: true,
            AllowImplicitXmlnsDeclaration: true,
            ImplicitStandardXmlnsPrefixesEnabled: true,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: "x=https://custom.example/x;mc=https://custom.example/mc",
            RootNamespace: "Sample",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "TestAssembly");

        var settings = profile.BuildParserSettings(compilation, options);

        Assert.Equal("https://custom.example/x", settings.GlobalXmlnsPrefixes["x"]);
        Assert.Equal("https://custom.example/mc", settings.GlobalXmlnsPrefixes["mc"]);
        Assert.Equal("http://schemas.microsoft.com/expression/blend/2008", settings.GlobalXmlnsPrefixes["d"]);
    }
}
