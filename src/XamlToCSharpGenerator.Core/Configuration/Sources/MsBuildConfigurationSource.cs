using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace XamlToCSharpGenerator.Core.Configuration.Sources;

public sealed class MsBuildConfigurationSource : IXamlSourceGenConfigurationSource
{
    private const string BuildPropertyPrefix = "build_property.";
    private const string RawGlobalXmlnsPrefixesAdditionalPropertyKey = "RawGlobalXmlnsPrefixes";
    private static readonly ImmutableDictionary<XamlFrameworkMsBuildSettingKey, ImmutableArray<string>> DefaultFallbackAliases =
        ImmutableDictionary.CreateRange(new[]
        {
            Alias(XamlFrameworkMsBuildSettingKey.Backend, "XamlSourceGenBackend", "AvaloniaXamlCompilerBackend"),
            Alias(XamlFrameworkMsBuildSettingKey.IsEnabled, "XamlSourceGenEnabled", "AvaloniaSourceGenCompilerEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.ConfigurationPrecedence, "XamlSourceGenConfigurationPrecedence", "AvaloniaSourceGenConfigurationPrecedence"),
            Alias(XamlFrameworkMsBuildSettingKey.StrictMode, "AvaloniaSourceGenStrictMode"),
            Alias(XamlFrameworkMsBuildSettingKey.HotReloadEnabled, "XamlSourceGenHotReloadEnabled", "AvaloniaSourceGenHotReloadEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.HotReloadErrorResilienceEnabled, "AvaloniaSourceGenHotReloadErrorResilienceEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.IdeHotReloadEnabled, "XamlSourceGenIdeHotReloadEnabled", "AvaloniaSourceGenIdeHotReloadEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.HotDesignEnabled, "XamlSourceGenHotDesignEnabled", "AvaloniaSourceGenHotDesignEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.IosHotReloadEnabled, "AvaloniaSourceGenIosHotReloadEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.IosHotReloadUseInterpreter, "AvaloniaSourceGenIosHotReloadUseInterpreter"),
            Alias(XamlFrameworkMsBuildSettingKey.AllowImplicitXmlnsDeclaration, "AvaloniaSourceGenAllowImplicitXmlnsDeclaration"),
            Alias(XamlFrameworkMsBuildSettingKey.ImplicitStandardXmlnsPrefixesEnabled, "AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.ImplicitDefaultXmlns, "AvaloniaSourceGenImplicitDefaultXmlns"),
            Alias(XamlFrameworkMsBuildSettingKey.InferClassFromPath, "AvaloniaSourceGenInferClassFromPath"),
            Alias(XamlFrameworkMsBuildSettingKey.ImplicitProjectNamespacesEnabled, "AvaloniaSourceGenImplicitProjectNamespacesEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.GlobalXmlnsPrefixes, "AvaloniaSourceGenGlobalXmlnsPrefixes"),
            Alias(XamlFrameworkMsBuildSettingKey.UseCompiledBindingsByDefault, "XamlSourceGenUseCompiledBindingsByDefault", "AvaloniaSourceGenUseCompiledBindingsByDefault"),
            Alias(XamlFrameworkMsBuildSettingKey.CSharpExpressionsEnabled, "AvaloniaSourceGenCSharpExpressionsEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.ImplicitCSharpExpressionsEnabled, "AvaloniaSourceGenImplicitCSharpExpressionsEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled, "AvaloniaSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.TypeResolutionCompatibilityFallbackEnabled, "AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.CreateSourceInfo, "XamlSourceGenCreateSourceInfo", "AvaloniaSourceGenCreateSourceInfo"),
            Alias(XamlFrameworkMsBuildSettingKey.TracePasses, "AvaloniaSourceGenTracePasses"),
            Alias(XamlFrameworkMsBuildSettingKey.MetricsEnabled, "AvaloniaSourceGenMetricsEnabled"),
            Alias(XamlFrameworkMsBuildSettingKey.MetricsDetailed, "AvaloniaSourceGenMetricsDetailed")
        });
    private readonly AnalyzerConfigOptions _globalOptions;
    private readonly XamlSourceGenConfiguration _baseConfiguration;
    private readonly XamlFrameworkMsBuildSettings _frameworkMsBuildSettings;

    public MsBuildConfigurationSource(
        AnalyzerConfigOptions globalOptions,
        XamlSourceGenConfiguration? baseConfiguration = null,
        XamlFrameworkMsBuildSettings? frameworkMsBuildSettings = null,
        int precedence = 200,
        string? name = null)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _baseConfiguration = baseConfiguration ?? XamlSourceGenConfiguration.Default;
        _frameworkMsBuildSettings = frameworkMsBuildSettings ?? new XamlFrameworkMsBuildSettings(Array.Empty<KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>>());
        Precedence = precedence;
        Name = string.IsNullOrWhiteSpace(name) ? "MsBuild" : name!;
    }

    public string Name { get; }

    public int Precedence { get; }

    public XamlSourceGenConfigurationSourceResult Load(XamlSourceGenConfigurationSourceContext context)
    {
        _ = context;
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();

        var backend = ReadStringOverrideBySettingKey(
            XamlFrameworkMsBuildSettingKey.Backend,
            _baseConfiguration.Build.Backend);
        var explicitEnable = ReadBooleanOverrideBySettingKey(
            XamlFrameworkMsBuildSettingKey.IsEnabled,
            _baseConfiguration.Build.IsEnabled,
            issues);

        var isEnabled = default(ConfigValue<bool>);
        if (explicitEnable.HasValue)
        {
            isEnabled = explicitEnable.Value;
        }

        if (backend.HasValue && string.Equals(backend.Value, "SourceGen", StringComparison.OrdinalIgnoreCase))
        {
            isEnabled = true;
        }

        var rawGlobalXmlnsPrefixes = GetNullableBySettingKey(XamlFrameworkMsBuildSettingKey.GlobalXmlnsPrefixes);
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
                StrictMode = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.StrictMode,
                    _baseConfiguration.Build.StrictMode,
                    issues),
                HotReloadEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.HotReloadEnabled,
                    _baseConfiguration.Build.HotReloadEnabled,
                    issues),
                HotReloadErrorResilienceEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.HotReloadErrorResilienceEnabled,
                    _baseConfiguration.Build.HotReloadErrorResilienceEnabled,
                    issues),
                IdeHotReloadEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.IdeHotReloadEnabled,
                    _baseConfiguration.Build.IdeHotReloadEnabled,
                    issues),
                HotDesignEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.HotDesignEnabled,
                    _baseConfiguration.Build.HotDesignEnabled,
                    issues),
                IosHotReloadEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.IosHotReloadEnabled,
                    _baseConfiguration.Build.IosHotReloadEnabled,
                    issues),
                IosHotReloadUseInterpreter = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.IosHotReloadUseInterpreter,
                    _baseConfiguration.Build.IosHotReloadUseInterpreter,
                    issues),
                DotNetWatchBuild = ReadBooleanOverrideByPropertyName(
                    "DotNetWatchBuild",
                    _baseConfiguration.Build.DotNetWatchBuild,
                    issues),
                BuildingInsideVisualStudio = ReadBooleanOverrideByPropertyName(
                    "BuildingInsideVisualStudio",
                    _baseConfiguration.Build.BuildingInsideVisualStudio,
                    issues),
                BuildingByReSharper = ReadBooleanOverrideByPropertyName(
                    "BuildingByReSharper",
                    _baseConfiguration.Build.BuildingByReSharper,
                    issues)
            },
            Parser = new XamlSourceGenParserOptionsPatch
            {
                AllowImplicitXmlnsDeclaration = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.AllowImplicitXmlnsDeclaration,
                    _baseConfiguration.Parser.AllowImplicitXmlnsDeclaration,
                    issues),
                ImplicitStandardXmlnsPrefixesEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.ImplicitStandardXmlnsPrefixesEnabled,
                    _baseConfiguration.Parser.ImplicitStandardXmlnsPrefixesEnabled,
                    issues),
                ImplicitDefaultXmlns = ReadStringOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.ImplicitDefaultXmlns,
                    _baseConfiguration.Parser.ImplicitDefaultXmlns),
                InferClassFromPath = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.InferClassFromPath,
                    _baseConfiguration.Parser.InferClassFromPath,
                    issues),
                ImplicitProjectNamespacesEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.ImplicitProjectNamespacesEnabled,
                    _baseConfiguration.Parser.ImplicitProjectNamespacesEnabled,
                    issues),
                GlobalXmlnsPrefixes = globalXmlnsPrefixes,
                AdditionalProperties = parserAdditionalProperties.ToImmutable()
            },
            Binding = new XamlSourceGenBindingOptionsPatch
            {
                UseCompiledBindingsByDefault = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.UseCompiledBindingsByDefault,
                    _baseConfiguration.Binding.UseCompiledBindingsByDefault,
                    issues),
                CSharpExpressionsEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.CSharpExpressionsEnabled,
                    _baseConfiguration.Binding.CSharpExpressionsEnabled,
                    issues),
                ImplicitCSharpExpressionsEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.ImplicitCSharpExpressionsEnabled,
                    _baseConfiguration.Binding.ImplicitCSharpExpressionsEnabled,
                    issues),
                MarkupParserLegacyInvalidNamedArgumentFallbackEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled,
                    _baseConfiguration.Binding.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled,
                    issues),
                TypeResolutionCompatibilityFallbackEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.TypeResolutionCompatibilityFallbackEnabled,
                    _baseConfiguration.Binding.TypeResolutionCompatibilityFallbackEnabled,
                    issues)
            },
            Emitter = new XamlSourceGenEmitterOptionsPatch
            {
                CreateSourceInfo = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.CreateSourceInfo,
                    _baseConfiguration.Emitter.CreateSourceInfo,
                    issues),
                TracePasses = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.TracePasses,
                    _baseConfiguration.Emitter.TracePasses,
                    issues),
                MetricsEnabled = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.MetricsEnabled,
                    _baseConfiguration.Emitter.MetricsEnabled,
                    issues),
                MetricsDetailed = ReadBooleanOverrideBySettingKey(
                    XamlFrameworkMsBuildSettingKey.MetricsDetailed,
                    _baseConfiguration.Emitter.MetricsDetailed,
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

    private ConfigValue<bool> ReadBooleanOverrideBySettingKey(
        XamlFrameworkMsBuildSettingKey key,
        bool defaultValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        return ReadBooleanOverrideByPropertyNames(GetPropertyNames(key), defaultValue, issues);
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

    private ConfigValue<string> ReadStringOverrideBySettingKey(
        XamlFrameworkMsBuildSettingKey key,
        string defaultValue)
    {
        return ReadStringOverrideByPropertyNames(GetPropertyNames(key), defaultValue);
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

    private string? GetNullableBySettingKey(XamlFrameworkMsBuildSettingKey key)
    {
        foreach (var name in GetPropertyNames(key))
        {
            var value = GetNullableByPropertyName(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetPropertyNames(XamlFrameworkMsBuildSettingKey key)
    {
        var configuredAliases = _frameworkMsBuildSettings.GetAliases(key);
        if (configuredAliases.Count > 0)
        {
            return configuredAliases;
        }

        return DefaultFallbackAliases.TryGetValue(key, out var fallbackAliases)
            ? fallbackAliases
            : ImmutableArray<string>.Empty;
    }

    private static KeyValuePair<XamlFrameworkMsBuildSettingKey, ImmutableArray<string>> Alias(
        XamlFrameworkMsBuildSettingKey key,
        params string[] aliases)
    {
        return new KeyValuePair<XamlFrameworkMsBuildSettingKey, ImmutableArray<string>>(
            key,
            aliases.ToImmutableArray());
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
