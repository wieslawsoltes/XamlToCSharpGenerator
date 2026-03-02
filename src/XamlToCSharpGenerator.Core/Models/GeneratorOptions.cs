using System;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;
using XamlToCSharpGenerator.Core.Configuration;

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
    bool IosHotReloadEnabled,
    bool IosHotReloadUseInterpreter,
    bool DotNetWatchBuild,
    bool BuildingInsideVisualStudio,
    bool BuildingByReSharper,
    bool TracePasses,
    bool MetricsEnabled,
    bool MetricsDetailed,
    bool MarkupParserLegacyInvalidNamedArgumentFallbackEnabled,
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
    private const string RawGlobalXmlnsPrefixesAdditionalPropertyKey = "RawGlobalXmlnsPrefixes";

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
        var strictMode = GetBool(globalOptions, "build_property.AvaloniaSourceGenStrictMode", false);
        var compatibilityFallbackEnabled = TryGetBool(
            globalOptions,
            "build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled",
            out var configuredFallbackEnabled)
            ? configuredFallbackEnabled
            : false;

        return new GeneratorOptions(
            IsEnabled: explicitEnable || backend.Equals("SourceGen", System.StringComparison.OrdinalIgnoreCase),
            UseCompiledBindingsByDefault: GetBool(globalOptions, "build_property.AvaloniaSourceGenUseCompiledBindingsByDefault", false),
            CSharpExpressionsEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenCSharpExpressionsEnabled", true),
            ImplicitCSharpExpressionsEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenImplicitCSharpExpressionsEnabled", true),
            CreateSourceInfo: GetBool(globalOptions, "build_property.AvaloniaSourceGenCreateSourceInfo", false),
            StrictMode: strictMode,
            HotReloadEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotReloadEnabled", true),
            HotReloadErrorResilienceEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotReloadErrorResilienceEnabled", true),
            IdeHotReloadEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenIdeHotReloadEnabled", true),
            HotDesignEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotDesignEnabled", false),
            IosHotReloadEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenIosHotReloadEnabled", false),
            IosHotReloadUseInterpreter: GetBool(globalOptions, "build_property.AvaloniaSourceGenIosHotReloadUseInterpreter", false),
            DotNetWatchBuild: GetBool(globalOptions, "build_property.DotNetWatchBuild", false),
            BuildingInsideVisualStudio: GetBool(globalOptions, "build_property.BuildingInsideVisualStudio", false),
            BuildingByReSharper: GetBool(globalOptions, "build_property.BuildingByReSharper", false),
            TracePasses: GetBool(globalOptions, "build_property.AvaloniaSourceGenTracePasses", false),
            MetricsEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenMetricsEnabled", false),
            MetricsDetailed: GetBool(globalOptions, "build_property.AvaloniaSourceGenMetricsDetailed", false),
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled", false),
            TypeResolutionCompatibilityFallbackEnabled: compatibilityFallbackEnabled,
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

    public static GeneratorOptions FromConfiguration(
        XamlSourceGenConfiguration configuration,
        AnalyzerConfigOptions globalOptions,
        string? assemblyName)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (globalOptions is null)
        {
            throw new ArgumentNullException(nameof(globalOptions));
        }

        var globalXmlnsPrefixes = ResolveGlobalXmlnsPrefixes(configuration);

        return new GeneratorOptions(
            IsEnabled: configuration.Build.IsEnabled ||
                       string.Equals(configuration.Build.Backend, "SourceGen", StringComparison.OrdinalIgnoreCase),
            UseCompiledBindingsByDefault: configuration.Binding.UseCompiledBindingsByDefault,
            CSharpExpressionsEnabled: configuration.Binding.CSharpExpressionsEnabled,
            ImplicitCSharpExpressionsEnabled: configuration.Binding.ImplicitCSharpExpressionsEnabled,
            CreateSourceInfo: configuration.Emitter.CreateSourceInfo,
            StrictMode: configuration.Build.StrictMode,
            HotReloadEnabled: configuration.Build.HotReloadEnabled,
            HotReloadErrorResilienceEnabled: configuration.Build.HotReloadErrorResilienceEnabled,
            IdeHotReloadEnabled: configuration.Build.IdeHotReloadEnabled,
            HotDesignEnabled: configuration.Build.HotDesignEnabled,
            IosHotReloadEnabled: configuration.Build.IosHotReloadEnabled,
            IosHotReloadUseInterpreter: configuration.Build.IosHotReloadUseInterpreter,
            DotNetWatchBuild: configuration.Build.DotNetWatchBuild,
            BuildingInsideVisualStudio: configuration.Build.BuildingInsideVisualStudio,
            BuildingByReSharper: configuration.Build.BuildingByReSharper,
            TracePasses: configuration.Emitter.TracePasses,
            MetricsEnabled: configuration.Emitter.MetricsEnabled,
            MetricsDetailed: configuration.Emitter.MetricsDetailed,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled:
                configuration.Binding.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled,
            TypeResolutionCompatibilityFallbackEnabled:
                configuration.Binding.TypeResolutionCompatibilityFallbackEnabled,
            AllowImplicitXmlnsDeclaration: configuration.Parser.AllowImplicitXmlnsDeclaration,
            ImplicitStandardXmlnsPrefixesEnabled: configuration.Parser.ImplicitStandardXmlnsPrefixesEnabled,
            ImplicitDefaultXmlns: configuration.Parser.ImplicitDefaultXmlns,
            InferClassFromPath: configuration.Parser.InferClassFromPath,
            ImplicitProjectNamespacesEnabled: configuration.Parser.ImplicitProjectNamespacesEnabled,
            GlobalXmlnsPrefixes: globalXmlnsPrefixes,
            RootNamespace: GetNullable(globalOptions, "build_property.RootNamespace"),
            IntermediateOutputPath: GetNullable(globalOptions, "build_property.IntermediateOutputPath"),
            BaseIntermediateOutputPath: GetNullable(globalOptions, "build_property.BaseIntermediateOutputPath"),
            ProjectDirectory: GetNullable(globalOptions, "build_property.MSBuildProjectDirectory"),
            Backend: configuration.Build.Backend,
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

    private static bool TryGetBool(AnalyzerConfigOptions options, string key, out bool value)
    {
        value = false;
        if (!options.TryGetValue(key, out var text))
        {
            return false;
        }

        if (!bool.TryParse(text, out value))
        {
            value = false;
            return false;
        }

        return true;
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

    private static string? ResolveGlobalXmlnsPrefixes(XamlSourceGenConfiguration configuration)
    {
        if (configuration.Parser.AdditionalProperties.TryGetValue(
                RawGlobalXmlnsPrefixesAdditionalPropertyKey,
                out var rawGlobalXmlnsPrefixes) &&
            !string.IsNullOrWhiteSpace(rawGlobalXmlnsPrefixes))
        {
            return rawGlobalXmlnsPrefixes;
        }

        if (configuration.Parser.GlobalXmlnsPrefixes.Count == 0)
        {
            return null;
        }

        return string.Join(
            ";",
            configuration.Parser.GlobalXmlnsPrefixes
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Key + "=" + pair.Value));
    }
}
