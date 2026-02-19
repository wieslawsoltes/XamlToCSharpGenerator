using Microsoft.CodeAnalysis.Diagnostics;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record GeneratorOptions(
    bool IsEnabled,
    bool UseCompiledBindingsByDefault,
    bool CreateSourceInfo,
    bool StrictMode,
    bool HotReloadEnabled,
    bool HotReloadErrorResilienceEnabled,
    bool DotNetWatchBuild,
    bool TracePasses,
    string Backend,
    string? AssemblyName)
{
    public static GeneratorOptions From(AnalyzerConfigOptions globalOptions, string? assemblyName)
    {
        var backend = GetOrDefault(globalOptions, "build_property.AvaloniaXamlCompilerBackend", "XamlIl");
        var explicitEnable = GetBool(globalOptions, "build_property.AvaloniaSourceGenCompilerEnabled", false);

        return new GeneratorOptions(
            IsEnabled: explicitEnable || backend.Equals("SourceGen", System.StringComparison.OrdinalIgnoreCase),
            UseCompiledBindingsByDefault: GetBool(globalOptions, "build_property.AvaloniaSourceGenUseCompiledBindingsByDefault", false),
            CreateSourceInfo: GetBool(globalOptions, "build_property.AvaloniaSourceGenCreateSourceInfo", false),
            StrictMode: GetBool(globalOptions, "build_property.AvaloniaSourceGenStrictMode", false),
            HotReloadEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotReloadEnabled", true),
            HotReloadErrorResilienceEnabled: GetBool(globalOptions, "build_property.AvaloniaSourceGenHotReloadErrorResilienceEnabled", true),
            DotNetWatchBuild: GetBool(globalOptions, "build_property.DotNetWatchBuild", false),
            TracePasses: GetBool(globalOptions, "build_property.AvaloniaSourceGenTracePasses", false),
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
}
