using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace XamlToCSharpGenerator.Core.Configuration.Sources;

public sealed class MsBuildConfigurationSource : IXamlSourceGenConfigurationSource
{
    private const string BuildPropertyPrefix = "build_property.";
    private const string RawGlobalXmlnsPrefixesAdditionalPropertyKey = "RawGlobalXmlnsPrefixes";
    private readonly AnalyzerConfigOptions _globalOptions;

    public MsBuildConfigurationSource(
        AnalyzerConfigOptions globalOptions,
        int precedence = 200,
        string? name = null)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        Precedence = precedence;
        Name = string.IsNullOrWhiteSpace(name) ? "MsBuild" : name!;
    }

    public string Name { get; }

    public int Precedence { get; }

    public XamlSourceGenConfigurationSourceResult Load(XamlSourceGenConfigurationSourceContext context)
    {
        _ = context;
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();

        var backend = ReadStringOverrideByPropertyNames(
            names: new[] { "XamlSourceGenBackend", "AvaloniaXamlCompilerBackend" },
            defaultValue: XamlSourceGenConfiguration.Default.Build.Backend);
        var explicitEnable = ReadBooleanOverrideByPropertyNames(
            names: new[] { "XamlSourceGenEnabled", "AvaloniaSourceGenCompilerEnabled" },
            defaultValue: XamlSourceGenConfiguration.Default.Build.IsEnabled,
            issues: issues);

        var isEnabled = default(ConfigValue<bool>);
        if (explicitEnable.HasValue)
        {
            isEnabled = explicitEnable.Value;
        }

        if (backend.HasValue && string.Equals(backend.Value, "SourceGen", StringComparison.OrdinalIgnoreCase))
        {
            isEnabled = true;
        }

        var rawGlobalXmlnsPrefixes = GetNullableByPropertyName("AvaloniaSourceGenGlobalXmlnsPrefixes");
        var globalXmlnsPrefixes = ParseGlobalXmlnsPrefixes(rawGlobalXmlnsPrefixes, issues);

        var parserAdditionalProperties = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(rawGlobalXmlnsPrefixes))
        {
            parserAdditionalProperties[RawGlobalXmlnsPrefixesAdditionalPropertyKey] = rawGlobalXmlnsPrefixes;
        }

        var patch = new XamlSourceGenConfigurationPatch
        {
            Build = new XamlSourceGenBuildOptionsPatch
            {
                IsEnabled = isEnabled,
                Backend = backend,
                StrictMode = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenStrictMode",
                    XamlSourceGenConfiguration.Default.Build.StrictMode,
                    issues),
                HotReloadEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenHotReloadEnabled",
                    XamlSourceGenConfiguration.Default.Build.HotReloadEnabled,
                    issues),
                HotReloadErrorResilienceEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenHotReloadErrorResilienceEnabled",
                    XamlSourceGenConfiguration.Default.Build.HotReloadErrorResilienceEnabled,
                    issues),
                IdeHotReloadEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenIdeHotReloadEnabled",
                    XamlSourceGenConfiguration.Default.Build.IdeHotReloadEnabled,
                    issues),
                HotDesignEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenHotDesignEnabled",
                    XamlSourceGenConfiguration.Default.Build.HotDesignEnabled,
                    issues),
                IosHotReloadEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenIosHotReloadEnabled",
                    XamlSourceGenConfiguration.Default.Build.IosHotReloadEnabled,
                    issues),
                IosHotReloadUseInterpreter = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenIosHotReloadUseInterpreter",
                    XamlSourceGenConfiguration.Default.Build.IosHotReloadUseInterpreter,
                    issues),
                DotNetWatchBuild = ReadBooleanOverrideByPropertyName(
                    "DotNetWatchBuild",
                    XamlSourceGenConfiguration.Default.Build.DotNetWatchBuild,
                    issues),
                BuildingInsideVisualStudio = ReadBooleanOverrideByPropertyName(
                    "BuildingInsideVisualStudio",
                    XamlSourceGenConfiguration.Default.Build.BuildingInsideVisualStudio,
                    issues),
                BuildingByReSharper = ReadBooleanOverrideByPropertyName(
                    "BuildingByReSharper",
                    XamlSourceGenConfiguration.Default.Build.BuildingByReSharper,
                    issues)
            },
            Parser = new XamlSourceGenParserOptionsPatch
            {
                AllowImplicitXmlnsDeclaration = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenAllowImplicitXmlnsDeclaration",
                    XamlSourceGenConfiguration.Default.Parser.AllowImplicitXmlnsDeclaration,
                    issues),
                ImplicitStandardXmlnsPrefixesEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled",
                    XamlSourceGenConfiguration.Default.Parser.ImplicitStandardXmlnsPrefixesEnabled,
                    issues),
                ImplicitDefaultXmlns = ReadStringOverrideByPropertyName(
                    "AvaloniaSourceGenImplicitDefaultXmlns",
                    XamlSourceGenConfiguration.Default.Parser.ImplicitDefaultXmlns),
                InferClassFromPath = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenInferClassFromPath",
                    XamlSourceGenConfiguration.Default.Parser.InferClassFromPath,
                    issues),
                ImplicitProjectNamespacesEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenImplicitProjectNamespacesEnabled",
                    XamlSourceGenConfiguration.Default.Parser.ImplicitProjectNamespacesEnabled,
                    issues),
                GlobalXmlnsPrefixes = globalXmlnsPrefixes,
                AdditionalProperties = parserAdditionalProperties.ToImmutable()
            },
            Binding = new XamlSourceGenBindingOptionsPatch
            {
                UseCompiledBindingsByDefault = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenUseCompiledBindingsByDefault",
                    XamlSourceGenConfiguration.Default.Binding.UseCompiledBindingsByDefault,
                    issues),
                CSharpExpressionsEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenCSharpExpressionsEnabled",
                    XamlSourceGenConfiguration.Default.Binding.CSharpExpressionsEnabled,
                    issues),
                ImplicitCSharpExpressionsEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenImplicitCSharpExpressionsEnabled",
                    XamlSourceGenConfiguration.Default.Binding.ImplicitCSharpExpressionsEnabled,
                    issues),
                MarkupParserLegacyInvalidNamedArgumentFallbackEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled",
                    XamlSourceGenConfiguration.Default.Binding.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled,
                    issues),
                TypeResolutionCompatibilityFallbackEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled",
                    XamlSourceGenConfiguration.Default.Binding.TypeResolutionCompatibilityFallbackEnabled,
                    issues)
            },
            Emitter = new XamlSourceGenEmitterOptionsPatch
            {
                CreateSourceInfo = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenCreateSourceInfo",
                    XamlSourceGenConfiguration.Default.Emitter.CreateSourceInfo,
                    issues),
                TracePasses = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenTracePasses",
                    XamlSourceGenConfiguration.Default.Emitter.TracePasses,
                    issues),
                MetricsEnabled = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenMetricsEnabled",
                    XamlSourceGenConfiguration.Default.Emitter.MetricsEnabled,
                    issues),
                MetricsDetailed = ReadBooleanOverrideByPropertyName(
                    "AvaloniaSourceGenMetricsDetailed",
                    XamlSourceGenConfiguration.Default.Emitter.MetricsDetailed,
                    issues)
            }
        };

        return new XamlSourceGenConfigurationSourceResult
        {
            Patch = patch,
            Issues = issues.ToImmutable()
        };
    }

    private ConfigValue<bool> ReadBooleanOverrideByPropertyName(
        string name,
        bool defaultValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        return ReadBooleanOverrideByPropertyNames(new[] { name }, defaultValue, issues);
    }

    private ConfigValue<bool> ReadBooleanOverrideByPropertyNames(
        IReadOnlyList<string> names,
        bool defaultValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        foreach (var name in names)
        {
            var key = BuildPropertyPrefix + name;
            if (!_globalOptions.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                // Unset MSBuild properties can surface as empty strings through analyzer config.
                continue;
            }

            if (bool.TryParse(rawValue, out var parsedValue))
            {
                if (parsedValue == defaultValue)
                {
                    return default;
                }

                return parsedValue;
            }

            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0911",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Invalid boolean value '" + rawValue + "' for MSBuild property '" + name + "'.",
                SourceName: Name));
            return default;
        }

        return default;
    }

    private ConfigValue<string> ReadStringOverrideByPropertyName(string name, string defaultValue)
    {
        return ReadStringOverrideByPropertyNames(new[] { name }, defaultValue);
    }

    private ConfigValue<string> ReadStringOverrideByPropertyNames(
        IReadOnlyList<string> names,
        string defaultValue)
    {
        foreach (var name in names)
        {
            var key = BuildPropertyPrefix + name;
            if (!_globalOptions.TryGetValue(key, out var rawValue))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rawValue))
            {
                if (string.Equals(rawValue, defaultValue, StringComparison.Ordinal))
                {
                    return default;
                }

                return rawValue;
            }
        }

        return default;
    }

    private string? GetNullableByPropertyName(string name)
    {
        var key = BuildPropertyPrefix + name;
        if (!_globalOptions.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private ImmutableDictionary<string, string?> ParseGlobalXmlnsPrefixes(
        string? rawValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
        }

        var mapBuilder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        var entries = rawValue!
            .Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    Code: "AXSG0912",
                    Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                    Message: "Invalid global xmlns prefix entry '" + entry.Trim() + "'. Expected 'prefix=namespace'.",
                    SourceName: Name));
                continue;
            }

            var prefix = entry.Substring(0, separatorIndex).Trim();
            var xmlNamespace = entry.Substring(separatorIndex + 1).Trim();
            if (prefix.Length == 0 || xmlNamespace.Length == 0)
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    Code: "AXSG0912",
                    Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                    Message: "Invalid global xmlns prefix entry '" + entry.Trim() + "'. Expected non-empty prefix and namespace.",
                    SourceName: Name));
                continue;
            }

            mapBuilder[prefix] = xmlNamespace;
        }

        return mapBuilder.Count == 0
            ? XamlSourceGenConfigurationCollections.EmptyNullableStringMap
            : mapBuilder.ToImmutable();
    }
}
