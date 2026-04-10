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
        var defaults = XamlSourceGenConfiguration.Default;
        var backend = GetOrDefault(
            globalOptions,
            "build_property.XamlSourceGenBackend",
            defaults.Build.Backend);
        var explicitEnable = GetBool(globalOptions, "build_property.XamlSourceGenEnabled", defaults.Build.IsEnabled);
        var strictMode = GetBool(globalOptions, "build_property.XamlSourceGenStrictMode", defaults.Build.StrictMode);
        var compatibilityFallbackEnabled = TryGetBool(
            globalOptions,
            "build_property.XamlSourceGenTypeResolutionCompatibilityFallbackEnabled",
            out var configuredFallbackEnabled)
            ? configuredFallbackEnabled
            : defaults.Binding.TypeResolutionCompatibilityFallbackEnabled;

        return new GeneratorOptions(
            IsEnabled: explicitEnable || backend.Equals("SourceGen", System.StringComparison.OrdinalIgnoreCase),
            UseCompiledBindingsByDefault: GetBool(globalOptions, "build_property.XamlSourceGenUseCompiledBindingsByDefault", defaults.Binding.UseCompiledBindingsByDefault),
            CSharpExpressionsEnabled: GetBool(globalOptions, "build_property.XamlSourceGenCSharpExpressionsEnabled", defaults.Binding.CSharpExpressionsEnabled),
            ImplicitCSharpExpressionsEnabled: GetBool(globalOptions, "build_property.XamlSourceGenImplicitCSharpExpressionsEnabled", defaults.Binding.ImplicitCSharpExpressionsEnabled),
            CreateSourceInfo: GetBool(globalOptions, "build_property.XamlSourceGenCreateSourceInfo", defaults.Emitter.CreateSourceInfo),
            StrictMode: strictMode,
            HotReloadEnabled: GetBool(globalOptions, "build_property.XamlSourceGenHotReloadEnabled", defaults.Build.HotReloadEnabled),
            HotReloadErrorResilienceEnabled: GetBool(globalOptions, "build_property.XamlSourceGenHotReloadErrorResilienceEnabled", defaults.Build.HotReloadErrorResilienceEnabled),
            IdeHotReloadEnabled: GetBool(globalOptions, "build_property.XamlSourceGenIdeHotReloadEnabled", defaults.Build.IdeHotReloadEnabled),
            HotDesignEnabled: GetBool(globalOptions, "build_property.XamlSourceGenHotDesignEnabled", defaults.Build.HotDesignEnabled),
            IosHotReloadEnabled: GetBool(globalOptions, "build_property.XamlSourceGenIosHotReloadEnabled", defaults.Build.IosHotReloadEnabled),
            IosHotReloadUseInterpreter: GetBool(globalOptions, "build_property.XamlSourceGenIosHotReloadUseInterpreter", defaults.Build.IosHotReloadUseInterpreter),
            DotNetWatchBuild: GetBool(globalOptions, "build_property.DotNetWatchBuild", false),
            BuildingInsideVisualStudio: GetBool(globalOptions, "build_property.BuildingInsideVisualStudio", false),
            BuildingByReSharper: GetBool(globalOptions, "build_property.BuildingByReSharper", false),
            TracePasses: GetBool(globalOptions, "build_property.XamlSourceGenTracePasses", defaults.Emitter.TracePasses),
            MetricsEnabled: GetBool(globalOptions, "build_property.XamlSourceGenMetricsEnabled", defaults.Emitter.MetricsEnabled),
            MetricsDetailed: GetBool(globalOptions, "build_property.XamlSourceGenMetricsDetailed", defaults.Emitter.MetricsDetailed),
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: GetBool(globalOptions, "build_property.XamlSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled", defaults.Binding.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled),
            TypeResolutionCompatibilityFallbackEnabled: compatibilityFallbackEnabled,
            AllowImplicitXmlnsDeclaration: GetBool(globalOptions, "build_property.XamlSourceGenAllowImplicitXmlnsDeclaration", defaults.Parser.AllowImplicitXmlnsDeclaration),
            ImplicitStandardXmlnsPrefixesEnabled: GetBool(globalOptions, "build_property.XamlSourceGenImplicitStandardXmlnsPrefixesEnabled", defaults.Parser.ImplicitStandardXmlnsPrefixesEnabled),
            ImplicitDefaultXmlns: GetOrDefault(globalOptions, "build_property.XamlSourceGenImplicitDefaultXmlns", defaults.Parser.ImplicitDefaultXmlns),
            InferClassFromPath: GetBool(globalOptions, "build_property.XamlSourceGenInferClassFromPath", defaults.Parser.InferClassFromPath),
            ImplicitProjectNamespacesEnabled: GetBool(globalOptions, "build_property.XamlSourceGenImplicitProjectNamespacesEnabled", defaults.Parser.ImplicitProjectNamespacesEnabled),
            GlobalXmlnsPrefixes: GetNullable(globalOptions, "build_property.XamlSourceGenGlobalXmlnsPrefixes"),
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
