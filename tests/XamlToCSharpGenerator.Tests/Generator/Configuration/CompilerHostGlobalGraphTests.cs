using System.Collections.Immutable;
using System.IO;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CompilerHostGlobalGraphTests
{
    [Fact]
    public void AnalyzeGlobalDocumentGraph_Reports_Missing_Project_Local_Include()
    {
        var documents = ImmutableArray.Create(
            CreateDocument(
                "Views/MainView.axaml",
                "Views/MainView.axaml",
                includes: ImmutableArray.Create(
                    new XamlIncludeDefinition(
                        "ResourceInclude",
                        "/Missing.axaml",
                        "MergedDictionaries",
                        string.Empty,
                        4,
                        9))));

        var diagnostics = XamlSourceGeneratorCompilerHost.AnalyzeGlobalDocumentGraph(documents, CreateOptions());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AXSG0403", diagnostic.Id);
    }

    [Fact]
    public void AnalyzeGlobalDocumentGraph_Reports_Project_Local_Include_Cycle()
    {
        var documents = ImmutableArray.Create(
            CreateDocument(
                "Views/A.axaml",
                "Views/A.axaml",
                includes: ImmutableArray.Create(
                    new XamlIncludeDefinition("ResourceInclude", "/Views/B.axaml", "MergedDictionaries", string.Empty, 4, 9))),
            CreateDocument(
                "Views/B.axaml",
                "Views/B.axaml",
                includes: ImmutableArray.Create(
                    new XamlIncludeDefinition("ResourceInclude", "/Views/A.axaml", "MergedDictionaries", string.Empty, 4, 9))));

        var diagnostics = XamlSourceGeneratorCompilerHost.AnalyzeGlobalDocumentGraph(documents, CreateOptions());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AXSG0404", diagnostic.Id);
    }

    [Fact]
    public void AnalyzeGlobalDocumentGraph_Reports_Duplicate_Generated_Target()
    {
        var documents = ImmutableArray.Create(
            CreateDocument("FolderA/Main.axaml", "Main.axaml"),
            CreateDocument("FolderB/Main.axaml", "Main.axaml"));

        var diagnostics = XamlSourceGeneratorCompilerHost.AnalyzeGlobalDocumentGraph(documents, CreateOptions());

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("AXSG0601", diagnostic.Id));
    }

    [Fact]
    public void NormalizeIncludePath_Removes_Dot_Segments_And_Normalizes_Separators()
    {
        var normalized = XamlSourceGeneratorCompilerHost.NormalizeIncludePath(
            @"./Views\Shared/../Cards//CardView.axaml");

        Assert.Equal("Views/Cards/CardView.axaml", normalized);
    }

    [Fact]
    public void NormalizeIncludeSource_Extracts_Unquoted_Uri_From_XUri_Markup()
    {
        var normalized = XamlSourceGeneratorCompilerHost.NormalizeIncludeSource(
            "{x:Uri /Assets/Theme.axaml}");

        Assert.Equal("/Assets/Theme.axaml", normalized);
    }

    [Fact]
    public void NormalizeIncludeSource_Extracts_Quoted_Uri_Argument()
    {
        var normalized = XamlSourceGeneratorCompilerHost.NormalizeIncludeSource(
            "{x:Uri Uri='/Assets/Theme.axaml', Relative=true}");

        Assert.Equal("/Assets/Theme.axaml", normalized);
    }

    [Fact]
    public void NormalizeIncludeSource_Preserves_NonUri_Markup_Extension_Text()
    {
        var normalized = XamlSourceGeneratorCompilerHost.NormalizeIncludeSource(
            " {Binding ThemeSource} ");

        Assert.Equal("{Binding ThemeSource}", normalized);
    }

    [Fact]
    public void TryResolveIncludeUri_Resolves_Project_Local_Relative_Include()
    {
        var resolved = XamlSourceGeneratorCompilerHost.TryResolveIncludeUri(
            "../Shared/Theme.axaml",
            "Views/Pages/MainView.axaml",
            "DemoAssembly",
            out var resolvedUri,
            out var isProjectLocal);

        Assert.True(resolved);
        Assert.True(isProjectLocal);
        Assert.Equal("avares://DemoAssembly/Views/Shared/Theme.axaml", resolvedUri);
    }

    [Fact]
    public void TryResolveIncludeUri_Resolves_Project_Local_Avares_Uri()
    {
        var resolved = XamlSourceGeneratorCompilerHost.TryResolveIncludeUri(
            "avares://DemoAssembly/Styles/../Themes/Fluent.axaml",
            "Views/MainView.axaml",
            "DemoAssembly",
            out var resolvedUri,
            out var isProjectLocal);

        Assert.True(resolved);
        Assert.True(isProjectLocal);
        Assert.Equal("avares://DemoAssembly/Themes/Fluent.axaml", resolvedUri);
    }

    [Fact]
    public void TryResolveIncludeUri_Preserves_External_Avares_Uri()
    {
        var resolved = XamlSourceGeneratorCompilerHost.TryResolveIncludeUri(
            "avares://External.Library/Themes/Fluent.axaml",
            "Views/MainView.axaml",
            "DemoAssembly",
            out var resolvedUri,
            out var isProjectLocal);

        Assert.True(resolved);
        Assert.False(isProjectLocal);
        Assert.Equal("avares://External.Library/Themes/Fluent.axaml", resolvedUri);
    }

    private static GeneratorOptions CreateOptions()
    {
        return new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: true,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: false,
            HotReloadEnabled: true,
            HotReloadErrorResilienceEnabled: true,
            IdeHotReloadEnabled: true,
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
            TypeResolutionCompatibilityFallbackEnabled: false,
            AllowImplicitXmlnsDeclaration: false,
            ImplicitStandardXmlnsPrefixesEnabled: true,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: "Demo",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "DemoAssembly");
    }

    private static XamlDocumentModel CreateDocument(
        string filePath,
        string targetPath,
        ImmutableArray<XamlIncludeDefinition> includes = default,
        bool? precompile = true)
    {
        return new XamlDocumentModel(
            FilePath: filePath,
            TargetPath: targetPath,
            ClassFullName: "Demo." + Path.GetFileNameWithoutExtension(filePath),
            ClassModifier: "public",
            Precompile: precompile,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: new XamlObjectNode(
                XmlNamespace: "https://github.com/avaloniaui",
                XmlTypeName: "UserControl",
                Key: null,
                Name: null,
                FieldModifier: null,
                DataType: null,
                CompileBindings: null,
                FactoryMethod: null,
                TypeArguments: ImmutableArray<string>.Empty,
                ArrayItemType: null,
                ConstructorArguments: ImmutableArray<XamlObjectNode>.Empty,
                TextContent: null,
                PropertyAssignments: ImmutableArray<XamlPropertyAssignment>.Empty,
                ChildObjects: ImmutableArray<XamlObjectNode>.Empty,
                PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
                Line: 1,
                Column: 1),
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: includes.IsDefault ? ImmutableArray<XamlIncludeDefinition>.Empty : includes,
            IsValid: true);
    }
}
