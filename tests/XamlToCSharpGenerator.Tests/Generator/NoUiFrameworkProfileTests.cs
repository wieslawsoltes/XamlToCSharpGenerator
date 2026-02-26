using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.NoUi.Framework;

namespace XamlToCSharpGenerator.Tests.Generator;

public class NoUiFrameworkProfileTests
{
    [Fact]
    public void BuildContract_Exposes_NoUi_SourceItemGroups()
    {
        IXamlFrameworkProfile profile = NoUiFrameworkProfile.Instance;
        var contract = profile.BuildContract;

        Assert.Equal("build_metadata.AdditionalFiles.SourceItemGroup", contract.SourceItemGroupMetadataName);
        Assert.Equal("build_metadata.AdditionalFiles.TargetPath", contract.TargetPathMetadataName);
        Assert.Equal("NoUiXaml", contract.NormalizeSourceItemGroup(null));
        Assert.True(contract.IsXamlSourceItemGroup("NoUiXaml"));
        Assert.False(contract.IsXamlSourceItemGroup("AvaloniaXaml"));
        Assert.False(contract.IsXamlSourceItemGroup(null));
        Assert.True(contract.IsTransformRuleSourceItemGroup("NoUiSourceGenTransformRule"));
        Assert.True(contract.IsXamlPath("View.xaml"));
        Assert.False(contract.IsXamlPath("View.axaml"));
    }

    [Fact]
    public void BuildParserSettings_Provides_XPrefix_And_Implicit_Default_Namespace()
    {
        IXamlFrameworkProfile profile = NoUiFrameworkProfile.Instance;
        var compilation = CSharpCompilation.Create(
            assemblyName: "NoUi.Tests",
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
            ImplicitDefaultXmlns: "urn:noui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: "NoUi.Tests",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "NoUi.Tests");

        var settings = profile.BuildParserSettings(compilation, options);

        Assert.True(settings.AllowImplicitDefaultXmlns);
        Assert.Equal("urn:noui", settings.ImplicitDefaultXmlns);
        Assert.Equal("http://schemas.microsoft.com/winfx/2006/xaml", settings.GlobalXmlnsPrefixes["x"]);
    }

    [Fact]
    public void TransformProvider_Returns_Empty_Configuration()
    {
        IXamlFrameworkProfile profile = NoUiFrameworkProfile.Instance;
        var provider = profile.TransformProvider;
        var first = provider.ParseTransformRule(new XamlFrameworkTransformRuleInput("one.json", """{ "anything": true }"""));
        var second = provider.ParseTransformRule(new XamlFrameworkTransformRuleInput("two.json", """{ "else": "value" }"""));
        var aggregate = provider.MergeTransformRules([first, second]);

        Assert.Empty(first.Configuration.TypeAliases);
        Assert.Empty(first.Configuration.PropertyAliases);
        Assert.Empty(aggregate.Configuration.TypeAliases);
        Assert.Empty(aggregate.Configuration.PropertyAliases);
        Assert.Empty(aggregate.Diagnostics);
    }
}
