using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Configuration;

public sealed record XamlSourceGenConfiguration
{
    public static XamlSourceGenConfiguration Default { get; } = new();

    public XamlSourceGenBuildOptions Build { get; init; } = XamlSourceGenBuildOptions.Default;

    public XamlSourceGenParserOptions Parser { get; init; } = XamlSourceGenParserOptions.Default;

    public XamlSourceGenSemanticContractOptions SemanticContract { get; init; } = XamlSourceGenSemanticContractOptions.Default;

    public XamlSourceGenBindingOptions Binding { get; init; } = XamlSourceGenBindingOptions.Default;

    public XamlSourceGenEmitterOptions Emitter { get; init; } = XamlSourceGenEmitterOptions.Default;

    public XamlSourceGenTransformOptions Transform { get; init; } = XamlSourceGenTransformOptions.Default;

    public XamlSourceGenDiagnosticsOptions Diagnostics { get; init; } = XamlSourceGenDiagnosticsOptions.Default;

    public XamlSourceGenFrameworkExtras FrameworkExtras { get; init; } = XamlSourceGenFrameworkExtras.Default;

    public XamlSourceGenConfiguration ApplyPatch(XamlSourceGenConfigurationPatch patch)
    {
        if (patch is null)
        {
            throw new ArgumentNullException(nameof(patch));
        }

        var buildPatch = patch.Build ?? XamlSourceGenBuildOptionsPatch.Empty;
        var parserPatch = patch.Parser ?? XamlSourceGenParserOptionsPatch.Empty;
        var semanticPatch = patch.SemanticContract ?? XamlSourceGenSemanticContractOptionsPatch.Empty;
        var bindingPatch = patch.Binding ?? XamlSourceGenBindingOptionsPatch.Empty;
        var emitterPatch = patch.Emitter ?? XamlSourceGenEmitterOptionsPatch.Empty;
        var transformPatch = patch.Transform ?? XamlSourceGenTransformOptionsPatch.Empty;
        var diagnosticsPatch = patch.Diagnostics ?? XamlSourceGenDiagnosticsOptionsPatch.Empty;
        var frameworkExtrasPatch = patch.FrameworkExtras ?? XamlSourceGenFrameworkExtrasPatch.Empty;

        return this with
        {
            Build = Build.ApplyPatch(buildPatch),
            Parser = Parser.ApplyPatch(parserPatch),
            SemanticContract = SemanticContract.ApplyPatch(semanticPatch),
            Binding = Binding.ApplyPatch(bindingPatch),
            Emitter = Emitter.ApplyPatch(emitterPatch),
            Transform = Transform.ApplyPatch(transformPatch),
            Diagnostics = Diagnostics.ApplyPatch(diagnosticsPatch),
            FrameworkExtras = FrameworkExtras.ApplyPatch(frameworkExtrasPatch)
        };
    }
}

public sealed record XamlSourceGenBuildOptions
{
    public static XamlSourceGenBuildOptions Default { get; } = new();

    public bool IsEnabled { get; init; }

    public string Backend { get; init; } = "XamlIl";

    public bool StrictMode { get; init; }

    public bool HotReloadEnabled { get; init; } = true;

    public bool HotReloadErrorResilienceEnabled { get; init; } = true;

    public bool IdeHotReloadEnabled { get; init; } = true;

    public bool HotDesignEnabled { get; init; }

    public bool IosHotReloadEnabled { get; init; }

    public bool IosHotReloadUseInterpreter { get; init; }

    public bool DotNetWatchBuild { get; init; }

    public bool BuildingInsideVisualStudio { get; init; }

    public bool BuildingByReSharper { get; init; }

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenBuildOptions ApplyPatch(XamlSourceGenBuildOptionsPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            IsEnabled = patch.IsEnabled.GetValueOrDefault(IsEnabled),
            Backend = patch.Backend.GetValueOrDefault(Backend),
            StrictMode = patch.StrictMode.GetValueOrDefault(StrictMode),
            HotReloadEnabled = patch.HotReloadEnabled.GetValueOrDefault(HotReloadEnabled),
            HotReloadErrorResilienceEnabled = patch.HotReloadErrorResilienceEnabled.GetValueOrDefault(HotReloadErrorResilienceEnabled),
            IdeHotReloadEnabled = patch.IdeHotReloadEnabled.GetValueOrDefault(IdeHotReloadEnabled),
            HotDesignEnabled = patch.HotDesignEnabled.GetValueOrDefault(HotDesignEnabled),
            IosHotReloadEnabled = patch.IosHotReloadEnabled.GetValueOrDefault(IosHotReloadEnabled),
            IosHotReloadUseInterpreter = patch.IosHotReloadUseInterpreter.GetValueOrDefault(IosHotReloadUseInterpreter),
            DotNetWatchBuild = patch.DotNetWatchBuild.GetValueOrDefault(DotNetWatchBuild),
            BuildingInsideVisualStudio = patch.BuildingInsideVisualStudio.GetValueOrDefault(BuildingInsideVisualStudio),
            BuildingByReSharper = patch.BuildingByReSharper.GetValueOrDefault(BuildingByReSharper),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

public sealed record XamlSourceGenParserOptions
{
    public static XamlSourceGenParserOptions Default { get; } = new();

    public bool AllowImplicitXmlnsDeclaration { get; init; }

    public bool ImplicitStandardXmlnsPrefixesEnabled { get; init; } = true;

    public string ImplicitDefaultXmlns { get; init; } = "https://github.com/avaloniaui";

    public bool InferClassFromPath { get; init; }

    public bool ImplicitProjectNamespacesEnabled { get; init; }

    public ImmutableDictionary<string, string> GlobalXmlnsPrefixes { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenParserOptions ApplyPatch(XamlSourceGenParserOptionsPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            AllowImplicitXmlnsDeclaration = patch.AllowImplicitXmlnsDeclaration.GetValueOrDefault(AllowImplicitXmlnsDeclaration),
            ImplicitStandardXmlnsPrefixesEnabled = patch.ImplicitStandardXmlnsPrefixesEnabled.GetValueOrDefault(ImplicitStandardXmlnsPrefixesEnabled),
            ImplicitDefaultXmlns = patch.ImplicitDefaultXmlns.GetValueOrDefault(ImplicitDefaultXmlns),
            InferClassFromPath = patch.InferClassFromPath.GetValueOrDefault(InferClassFromPath),
            ImplicitProjectNamespacesEnabled = patch.ImplicitProjectNamespacesEnabled.GetValueOrDefault(ImplicitProjectNamespacesEnabled),
            GlobalXmlnsPrefixes = XamlSourceGenConfigurationMerge.ApplyStringPatch(GlobalXmlnsPrefixes, patch.GlobalXmlnsPrefixes),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

public sealed record XamlSourceGenSemanticContractOptions
{
    public static XamlSourceGenSemanticContractOptions Default { get; } = new();

    public ImmutableDictionary<string, string> TypeContracts { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    public ImmutableDictionary<string, string> PropertyContracts { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    public ImmutableDictionary<string, string> EventContracts { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenSemanticContractOptions ApplyPatch(XamlSourceGenSemanticContractOptionsPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            TypeContracts = XamlSourceGenConfigurationMerge.ApplyStringPatch(TypeContracts, patch.TypeContracts),
            PropertyContracts = XamlSourceGenConfigurationMerge.ApplyStringPatch(PropertyContracts, patch.PropertyContracts),
            EventContracts = XamlSourceGenConfigurationMerge.ApplyStringPatch(EventContracts, patch.EventContracts),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

public sealed record XamlSourceGenBindingOptions
{
    public static XamlSourceGenBindingOptions Default { get; } = new();

    public bool UseCompiledBindingsByDefault { get; init; }

    public bool CSharpExpressionsEnabled { get; init; } = true;

    public bool ImplicitCSharpExpressionsEnabled { get; init; } = true;

    public bool MarkupParserLegacyInvalidNamedArgumentFallbackEnabled { get; init; }

    public bool TypeResolutionCompatibilityFallbackEnabled { get; init; }

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenBindingOptions ApplyPatch(XamlSourceGenBindingOptionsPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            UseCompiledBindingsByDefault = patch.UseCompiledBindingsByDefault.GetValueOrDefault(UseCompiledBindingsByDefault),
            CSharpExpressionsEnabled = patch.CSharpExpressionsEnabled.GetValueOrDefault(CSharpExpressionsEnabled),
            ImplicitCSharpExpressionsEnabled = patch.ImplicitCSharpExpressionsEnabled.GetValueOrDefault(ImplicitCSharpExpressionsEnabled),
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled = patch.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled.GetValueOrDefault(MarkupParserLegacyInvalidNamedArgumentFallbackEnabled),
            TypeResolutionCompatibilityFallbackEnabled = patch.TypeResolutionCompatibilityFallbackEnabled.GetValueOrDefault(TypeResolutionCompatibilityFallbackEnabled),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

public sealed record XamlSourceGenEmitterOptions
{
    public static XamlSourceGenEmitterOptions Default { get; } = new();

    public bool CreateSourceInfo { get; init; }

    public bool TracePasses { get; init; }

    public bool MetricsEnabled { get; init; }

    public bool MetricsDetailed { get; init; }

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenEmitterOptions ApplyPatch(XamlSourceGenEmitterOptionsPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            CreateSourceInfo = patch.CreateSourceInfo.GetValueOrDefault(CreateSourceInfo),
            TracePasses = patch.TracePasses.GetValueOrDefault(TracePasses),
            MetricsEnabled = patch.MetricsEnabled.GetValueOrDefault(MetricsEnabled),
            MetricsDetailed = patch.MetricsDetailed.GetValueOrDefault(MetricsDetailed),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

public sealed record XamlSourceGenTransformOptions
{
    public static XamlSourceGenTransformOptions Default { get; } = new();

    public XamlTransformConfiguration Configuration { get; init; } = XamlTransformConfiguration.Empty;

    public ImmutableDictionary<string, string> RawTransformDocuments { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenTransformOptions ApplyPatch(XamlSourceGenTransformOptionsPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            Configuration = patch.Configuration.GetValueOrDefault(Configuration),
            RawTransformDocuments = XamlSourceGenConfigurationMerge.ApplyStringPatch(RawTransformDocuments, patch.RawTransformDocuments),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

public sealed record XamlSourceGenDiagnosticsOptions
{
    public static XamlSourceGenDiagnosticsOptions Default { get; } = new();

    public bool TreatWarningsAsErrors { get; init; }

    public ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity> SeverityOverrides { get; init; } =
        XamlSourceGenConfigurationCollections.EmptySeverityMap;

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenDiagnosticsOptions ApplyPatch(XamlSourceGenDiagnosticsOptionsPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            TreatWarningsAsErrors = patch.TreatWarningsAsErrors.GetValueOrDefault(TreatWarningsAsErrors),
            SeverityOverrides = XamlSourceGenConfigurationMerge.ApplySeverityPatch(SeverityOverrides, patch.SeverityOverrides),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

public sealed record XamlSourceGenFrameworkExtras
{
    public static XamlSourceGenFrameworkExtras Default { get; } = new();

    public ImmutableDictionary<string, ImmutableDictionary<string, string>> Sections { get; init; } =
        XamlSourceGenConfigurationCollections.EmptySectionMap;

    public ImmutableDictionary<string, string> AdditionalProperties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;

    internal XamlSourceGenFrameworkExtras ApplyPatch(XamlSourceGenFrameworkExtrasPatch patch)
    {
        if (patch is null)
        {
            return this;
        }

        return this with
        {
            Sections = XamlSourceGenConfigurationMerge.ApplyFrameworkSectionPatch(Sections, patch.Sections),
            AdditionalProperties = XamlSourceGenConfigurationMerge.ApplyStringPatch(AdditionalProperties, patch.AdditionalProperties)
        };
    }
}

internal static class XamlSourceGenConfigurationCollections
{
    public static ImmutableDictionary<string, string> EmptyStringMap { get; } =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public static ImmutableDictionary<string, string?> EmptyNullableStringMap { get; } =
        ImmutableDictionary.Create<string, string?>(StringComparer.Ordinal);

    public static ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity> EmptySeverityMap { get; } =
        ImmutableDictionary.Create<string, XamlSourceGenConfigurationIssueSeverity>(StringComparer.Ordinal);

    public static ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity?> EmptyNullableSeverityMap { get; } =
        ImmutableDictionary.Create<string, XamlSourceGenConfigurationIssueSeverity?>(StringComparer.Ordinal);

    public static ImmutableDictionary<string, ImmutableDictionary<string, string>> EmptySectionMap { get; } =
        ImmutableDictionary.Create<string, ImmutableDictionary<string, string>>(StringComparer.Ordinal);

    public static ImmutableDictionary<string, ImmutableDictionary<string, string?>> EmptySectionPatchMap { get; } =
        ImmutableDictionary.Create<string, ImmutableDictionary<string, string?>>(StringComparer.Ordinal);
}

internal static class XamlSourceGenConfigurationMerge
{
    public static ImmutableDictionary<string, string> ApplyStringPatch(
        ImmutableDictionary<string, string> current,
        ImmutableDictionary<string, string?> patch)
    {
        if (patch is null || patch.Count == 0)
        {
            return current;
        }

        var builder = current.ToBuilder();
        foreach (var pair in patch)
        {
            if (pair.Value is null)
            {
                builder.Remove(pair.Key);
            }
            else
            {
                builder[pair.Key] = pair.Value;
            }
        }

        return builder.ToImmutable();
    }

    public static ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity> ApplySeverityPatch(
        ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity> current,
        ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity?> patch)
    {
        if (patch is null || patch.Count == 0)
        {
            return current;
        }

        var builder = current.ToBuilder();
        foreach (var pair in patch)
        {
            if (!pair.Value.HasValue)
            {
                builder.Remove(pair.Key);
            }
            else
            {
                builder[pair.Key] = pair.Value.Value;
            }
        }

        return builder.ToImmutable();
    }

    public static ImmutableDictionary<string, ImmutableDictionary<string, string>> ApplyFrameworkSectionPatch(
        ImmutableDictionary<string, ImmutableDictionary<string, string>> current,
        ImmutableDictionary<string, ImmutableDictionary<string, string?>> patch)
    {
        if (patch is null || patch.Count == 0)
        {
            return current;
        }

        var sectionBuilder = current.ToBuilder();
        foreach (var sectionPair in patch)
        {
            var currentSection = sectionBuilder.TryGetValue(sectionPair.Key, out var sectionValues)
                ? sectionValues
                : XamlSourceGenConfigurationCollections.EmptyStringMap;
            var nextSection = ApplyStringPatch(
                currentSection,
                sectionPair.Value ?? XamlSourceGenConfigurationCollections.EmptyNullableStringMap);
            if (nextSection.Count == 0)
            {
                sectionBuilder.Remove(sectionPair.Key);
            }
            else
            {
                sectionBuilder[sectionPair.Key] = nextSection;
            }
        }

        return sectionBuilder.ToImmutable();
    }
}
