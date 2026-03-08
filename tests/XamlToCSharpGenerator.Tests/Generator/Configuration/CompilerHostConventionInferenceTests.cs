using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CompilerHostConventionInferenceTests
{
    [Theory]
    [InlineData(" Demo . 1Shell . Views-Root ", "Demo._1Shell.ViewsRoot")]
    [InlineData(".. Demo .. <Inner> .. 42Child ..", "Demo.Inner._42Child")]
    [InlineData("   ", "")]
    public void NormalizeRootNamespace_Normalizes_Segments_Without_Split_Artifacts(
        string rawNamespace,
        string expected)
    {
        var normalized = XamlSourceGeneratorCompilerHost.NormalizeRootNamespace(rawNamespace);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizeNamespaceSegments_Returns_Normalized_Immutable_Segments()
    {
        var segments = XamlSourceGeneratorCompilerHost.NormalizeNamespaceSegments(" Demo . Views-Root . 9Detail ");

        Assert.Collection(
            segments,
            value => Assert.Equal("Demo", value),
            value => Assert.Equal("ViewsRoot", value),
            value => Assert.Equal("_9Detail", value));
    }

    [Fact]
    public void TryInferClassNameFromTargetPath_Appends_Normalized_Directory_Segments()
    {
        var inferred = XamlSourceGeneratorCompilerHost.TryInferClassNameFromTargetPath(
            "Views/Sub-View/Main Page.axaml",
            CreateOptions());

        Assert.Equal("Demo.App.Views.SubView.MainPage", inferred);
    }

    [Fact]
    public void TryInferClassNameFromTargetPath_Rooted_Path_Uses_File_Name_Only()
    {
        var inferred = XamlSourceGeneratorCompilerHost.TryInferClassNameFromTargetPath(
            "/tmp/generated/Views/Main Page.axaml",
            CreateOptions());

        Assert.Equal("Demo.App.MainPage", inferred);
    }

    [Fact]
    public void ApplyDocumentConventions_Inferred_Class_Removes_Missing_Class_Diagnostic_When_Type_Exists()
    {
        var document = CreateDocument(targetPath: "Views/Main Page.axaml");
        var diagnostics = ImmutableArray.Create(
            new DiagnosticInfo("AXSG0002", "Missing class", "Views/Main Page.axaml", 1, 1, false),
            new DiagnosticInfo("AXSG9999", "Keep me", "Views/Main Page.axaml", 1, 1, false));

        var result = XamlSourceGeneratorCompilerHost.ApplyDocumentConventions(
            new XamlFileInput("Views/Main Page.axaml", "Views/Main Page.axaml", "AvaloniaXaml", "<UserControl />"),
            document,
            diagnostics,
            CreateCompilation("namespace Demo.App.Views { public sealed class MainPage { } }"),
            CreateOptions());

        Assert.NotNull(result.Document);
        Assert.Equal("Demo.App.Views.MainPage", result.Document!.ClassFullName);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AXSG0002");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "AXSG9999");
    }

    [Fact]
    public void ApplyDocumentConventions_Does_Not_Remove_Missing_Class_Diagnostic_When_Type_Is_Absent()
    {
        var document = CreateDocument(targetPath: "Views/Main Page.axaml");
        var diagnostics = ImmutableArray.Create(
            new DiagnosticInfo("AXSG0002", "Missing class", "Views/Main Page.axaml", 1, 1, false));

        var result = XamlSourceGeneratorCompilerHost.ApplyDocumentConventions(
            new XamlFileInput("Views/Main Page.axaml", "Views/Main Page.axaml", "AvaloniaXaml", "<UserControl />"),
            document,
            diagnostics,
            CreateCompilation("namespace Demo.App.Views { public sealed class OtherPage { } }"),
            CreateOptions());

        Assert.NotNull(result.Document);
        Assert.Null(result.Document!.ClassFullName);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "AXSG0002");
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
            InferClassFromPath: true,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: "Demo.App",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "DemoAssembly");
    }

    private static Compilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            "ConventionInferenceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static XamlDocumentModel CreateDocument(string targetPath)
    {
        return new XamlDocumentModel(
            FilePath: targetPath,
            TargetPath: targetPath,
            ClassFullName: null,
            ClassModifier: "public",
            Precompile: true,
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
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);
    }
}
