using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Configuration;

public readonly struct ConfigValue<T> : IEquatable<ConfigValue<T>>
{
    private readonly T _value;

    public ConfigValue(T value)
    {
        _value = value;
        HasValue = true;
    }

    public bool HasValue { get; }

    public T Value
    {
        get
        {
            if (!HasValue)
            {
                throw new InvalidOperationException("Configuration value is not specified.");
            }

            return _value;
        }
    }

    public T GetValueOrDefault(T fallback)
    {
        return HasValue ? _value : fallback;
    }

    public bool Equals(ConfigValue<T> other)
    {
        if (HasValue != other.HasValue)
        {
            return false;
        }

        if (!HasValue)
        {
            return true;
        }

        return EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    public override bool Equals(object? obj)
    {
        return obj is ConfigValue<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (!HasValue)
        {
            return 0;
        }

        return _value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value);
    }

    public override string ToString()
    {
        return HasValue ? _value?.ToString() ?? "<null>" : "<unspecified>";
    }

    public static implicit operator ConfigValue<T>(T value)
    {
        return new ConfigValue<T>(value);
    }
}

public sealed record XamlSourceGenConfigurationPatch
{
    public static XamlSourceGenConfigurationPatch Empty { get; } = new();

    public XamlSourceGenBuildOptionsPatch Build { get; init; } = XamlSourceGenBuildOptionsPatch.Empty;

    public XamlSourceGenParserOptionsPatch Parser { get; init; } = XamlSourceGenParserOptionsPatch.Empty;

    public XamlSourceGenSemanticContractOptionsPatch SemanticContract { get; init; } = XamlSourceGenSemanticContractOptionsPatch.Empty;

    public XamlSourceGenBindingOptionsPatch Binding { get; init; } = XamlSourceGenBindingOptionsPatch.Empty;

    public XamlSourceGenEmitterOptionsPatch Emitter { get; init; } = XamlSourceGenEmitterOptionsPatch.Empty;

    public XamlSourceGenTransformOptionsPatch Transform { get; init; } = XamlSourceGenTransformOptionsPatch.Empty;

    public XamlSourceGenDiagnosticsOptionsPatch Diagnostics { get; init; } = XamlSourceGenDiagnosticsOptionsPatch.Empty;

    public XamlSourceGenFrameworkExtrasPatch FrameworkExtras { get; init; } = XamlSourceGenFrameworkExtrasPatch.Empty;
}

public sealed record XamlSourceGenBuildOptionsPatch
{
    public static XamlSourceGenBuildOptionsPatch Empty { get; } = new();

    public ConfigValue<bool> IsEnabled { get; init; }

    public ConfigValue<string> Backend { get; init; }

    public ConfigValue<bool> StrictMode { get; init; }

    public ConfigValue<bool> HotReloadEnabled { get; init; }

    public ConfigValue<bool> HotReloadErrorResilienceEnabled { get; init; }

    public ConfigValue<bool> IdeHotReloadEnabled { get; init; }

    public ConfigValue<bool> HotDesignEnabled { get; init; }

    public ConfigValue<bool> IosHotReloadEnabled { get; init; }

    public ConfigValue<bool> IosHotReloadUseInterpreter { get; init; }

    public ConfigValue<bool> DotNetWatchBuild { get; init; }

    public ConfigValue<bool> BuildingInsideVisualStudio { get; init; }

    public ConfigValue<bool> BuildingByReSharper { get; init; }

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}

public sealed record XamlSourceGenParserOptionsPatch
{
    public static XamlSourceGenParserOptionsPatch Empty { get; } = new();

    public ConfigValue<bool> AllowImplicitXmlnsDeclaration { get; init; }

    public ConfigValue<bool> ImplicitStandardXmlnsPrefixesEnabled { get; init; }

    public ConfigValue<string> ImplicitDefaultXmlns { get; init; }

    public ConfigValue<bool> InferClassFromPath { get; init; }

    public ConfigValue<bool> ImplicitProjectNamespacesEnabled { get; init; }

    public ImmutableDictionary<string, string?> GlobalXmlnsPrefixes { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}

public sealed record XamlSourceGenSemanticContractOptionsPatch
{
    public static XamlSourceGenSemanticContractOptionsPatch Empty { get; } = new();

    public ImmutableDictionary<string, string?> TypeContracts { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;

    public ImmutableDictionary<string, string?> PropertyContracts { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;

    public ImmutableDictionary<string, string?> EventContracts { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}

public sealed record XamlSourceGenBindingOptionsPatch
{
    public static XamlSourceGenBindingOptionsPatch Empty { get; } = new();

    public ConfigValue<bool> UseCompiledBindingsByDefault { get; init; }

    public ConfigValue<bool> CSharpExpressionsEnabled { get; init; }

    public ConfigValue<bool> ImplicitCSharpExpressionsEnabled { get; init; }

    public ConfigValue<bool> MarkupParserLegacyInvalidNamedArgumentFallbackEnabled { get; init; }

    public ConfigValue<bool> TypeResolutionCompatibilityFallbackEnabled { get; init; }

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}

public sealed record XamlSourceGenEmitterOptionsPatch
{
    public static XamlSourceGenEmitterOptionsPatch Empty { get; } = new();

    public ConfigValue<bool> CreateSourceInfo { get; init; }

    public ConfigValue<bool> TracePasses { get; init; }

    public ConfigValue<bool> MetricsEnabled { get; init; }

    public ConfigValue<bool> MetricsDetailed { get; init; }

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}

public sealed record XamlSourceGenTransformOptionsPatch
{
    public static XamlSourceGenTransformOptionsPatch Empty { get; } = new();

    public ConfigValue<XamlTransformConfiguration> Configuration { get; init; }

    public ImmutableDictionary<string, string?> RawTransformDocuments { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}

public sealed record XamlSourceGenDiagnosticsOptionsPatch
{
    public static XamlSourceGenDiagnosticsOptionsPatch Empty { get; } = new();

    public ConfigValue<bool> TreatWarningsAsErrors { get; init; }

    public ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity?> SeverityOverrides { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableSeverityMap;

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}

public sealed record XamlSourceGenFrameworkExtrasPatch
{
    public static XamlSourceGenFrameworkExtrasPatch Empty { get; } = new();

    public ImmutableDictionary<string, ImmutableDictionary<string, string?>> Sections { get; init; } =
        XamlSourceGenConfigurationCollections.EmptySectionPatchMap;

    public ImmutableDictionary<string, string?> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
}
