using Microsoft.CodeAnalysis.Diagnostics;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record GeneratorOptions(
    bool IsEnabled,
    bool UseCompiledBindingsByDefault,
    bool CSharpExpressionsEnabled,
    bool ImplicitCSharpExpressionsEnabled,
    bool CreateSourceInfo,
    bool StrictMode,
    bool HotReloadEnabled,
    bool HotReloadErrorResilienceEnabled,
    bool IdeHotReloadEnabled,
    bool HotDesignEnabled,
    bool DotNetWatchBuild,
    bool BuildingInsideVisualStudio,
    bool BuildingByReSharper,
    bool TracePasses,
    bool MetricsEnabled,
    bool MetricsDetailed,
    bool TypeResolutionCompatibilityFallbackEnabled,
    bool AllowImplicitXmlnsDeclaration,
    bool ImplicitStandardXmlnsPrefixesEnabled,
    string ImplicitDefaultXmlns,
    bool InferClassFromPath,
    bool ImplicitProjectNamespacesEnabled,
    string? GlobalXmlnsPrefixes,
    string? RootNamespace,
    string? IntermediateOutputPath,
    string? BaseIntermediateOutputPath,
    string? ProjectDirectory,
    string Backend,
    string? AssemblyName)
{
    public static GeneratorOptions From(AnalyzerConfigOptions globalOptions, string? assemblyName)
    {
        var backend = GetOrDefault(
            globalOptions,
            "build_property.XamlSourceGenBackend",
            GetOrDefault(globalOptions, "build_property.AvaloniaXamlCompilerBackend", "XamlIl"));
        var explicitEnable = GetBool(
            globalOptions,
            "build_property.XamlSourceGenEnabled",
            GetBool(globalOptions, "build_property.AvaloniaSourceGenCompilerEnabled", false));

        return new GeneratorOptions(
            IsEnabled: explicitEnable || backend.Equals("SourceGen", System.StringComparison.OrdinalIgnoreCase),
            UseCompiledBindingsByDefault: GetBool(globalOptions, "build_property.AvaloniaSourceGenUseCompiledBindingsByDefault", false),
            CSharpExpressionsEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenCSharpExpressionsEnabled", true),
            ImplicitCSharpExpressionsEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenImplicitCSharpExpressionsEnabled", true),
            CreateSourceInfo: GetBool(globalOptions, "build_property.AvaloniaSourceGenCreateSourceInfo", false),
            StrictMode: GetBool(globalOptions, "build_property.AvaloniaSourceGenStrictMode", false),
            HotReloadEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotReloadEnabled", true),
            HotReloadErrorResilienceEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotReloadErrorResilienceEnabled", true),
            IdeHotReloadEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenIdeHotReloadEnabled", true),
            HotDesignEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotDesignEnabled", false),
            DotNetWatchBuild: GetBool(globalOptions, "build_property.DotNetWatchBuild", false),
            BuildingInsideVisualStudio: GetBool(globalOptions, "build_property.BuildingInsideVisualStudio", false),
            BuildingByReSharper: GetBool(globalOptions, "build_property.BuildingByReSharper", false),
            TracePasses: GetBool(globalOptions, "build_property.AvaloniaSourceGenTracePasses", false),
            MetricsEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenMetricsEnabled", false),
            MetricsDetailed: GetBool(globalOptions, "build_property.AvaloniaSourceGenMetricsDetailed", false),
            TypeResolutionCompatibilityFallbackEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", true),
            AllowImplicitXmlnsDeclaration: GetBool(globalOptions, "build_property.AvaloniaSourceGenAllowImplicitXmlnsDeclaration", false),
            ImplicitStandardXmlnsPrefixesEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled", true),
            ImplicitDefaultXmlns: GetOrDefault(globalOptions, "build_property.AvaloniaSourceGenImplicitDefaultXmlns", "https://github.com/avaloniaui"),
            InferClassFromPath: GetBool(globalOptions, "build_property.AvaloniaSourceGenInferClassFromPath", false),
            ImplicitProjectNamespacesEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenImplicitProjectNamespacesEnabled", false),
            GlobalXmlnsPrefixes: GetNullable(globalOptions, "build_property.AvaloniaSourceGenGlobalXmlnsPrefixes"),
            RootNamespace: GetNullable(globalOptions, "build_property.RootNamespace"),
            IntermediateOutputPath: GetNullable(globalOptions, "build_property.IntermediateOutputPath"),
            BaseIntermediateOutputPath: GetNullable(globalOptions, "build_property.BaseIntermediateOutputPath"),
            ProjectDirectory: GetNullable(globalOptions, "build_property.MSBuildProjectDirectory"),
            Backend: backend,
            AssemblyName: assemblyName);
    }

    private static bool GetBool(AnalyzerConfigOptions options, string key, bool fallback)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string GetOrDefault(AnalyzerConfigOptions options, string key, string fallback)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value;
    }

    private static string? GetNullable(AnalyzerConfigOptions options, string key)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }
}
