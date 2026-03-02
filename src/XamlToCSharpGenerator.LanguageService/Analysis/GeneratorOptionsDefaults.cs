using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.LanguageService.Analysis;

internal static class GeneratorOptionsDefaults
{
    public static GeneratorOptions Create(Compilation? compilation, string? projectDirectory)
    {
        return new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: true,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: true,
            StrictMode: false,
            HotReloadEnabled: true,
            HotReloadErrorResilienceEnabled: true,
            IdeHotReloadEnabled: true,
            HotDesignEnabled: true,
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
            AllowImplicitXmlnsDeclaration: true,
            ImplicitStandardXmlnsPrefixesEnabled: true,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: true,
            ImplicitProjectNamespacesEnabled: true,
            GlobalXmlnsPrefixes: null,
            RootNamespace: null,
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: projectDirectory,
            Backend: "SourceGen",
            AssemblyName: compilation?.AssemblyName);
    }
}
