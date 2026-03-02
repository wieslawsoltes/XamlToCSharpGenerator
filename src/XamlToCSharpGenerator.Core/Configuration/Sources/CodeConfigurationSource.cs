using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.Core.Configuration.Sources;

public sealed class CodeConfigurationSource : IXamlSourceGenConfigurationSource
{
    private const string AssemblyMetadataAttributeMetadataName = "System.Reflection.AssemblyMetadataAttribute";
    private const string KeyPrefix = "XamlSourceGen.";
    private readonly Compilation _compilation;

    public CodeConfigurationSource(
        Compilation compilation,
        int precedence = 300,
        string? name = null)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        Precedence = precedence;
        Name = string.IsNullOrWhiteSpace(name) ? "Code" : name!;
    }

    public string Name { get; }

    public int Precedence { get; }

    public XamlSourceGenConfigurationSourceResult Load(XamlSourceGenConfigurationSourceContext context)
    {
        _ = context;
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();

        ConfigValue<bool> buildIsEnabled = default;
        ConfigValue<string> buildBackend = default;
        ConfigValue<bool> buildStrictMode = default;
        ConfigValue<bool> buildHotReloadEnabled = default;
        ConfigValue<bool> buildHotReloadErrorResilienceEnabled = default;
        ConfigValue<bool> buildIdeHotReloadEnabled = default;
        ConfigValue<bool> buildHotDesignEnabled = default;
        ConfigValue<bool> buildIosHotReloadEnabled = default;
        ConfigValue<bool> buildIosHotReloadUseInterpreter = default;
        ConfigValue<bool> buildDotNetWatchBuild = default;
        ConfigValue<bool> buildBuildingInsideVisualStudio = default;
        ConfigValue<bool> buildBuildingByReSharper = default;

        ConfigValue<bool> parserAllowImplicitXmlnsDeclaration = default;
        ConfigValue<bool> parserImplicitStandardXmlnsPrefixesEnabled = default;
        ConfigValue<string> parserImplicitDefaultXmlns = default;
        ConfigValue<bool> parserInferClassFromPath = default;
        ConfigValue<bool> parserImplicitProjectNamespacesEnabled = default;

        ConfigValue<bool> bindingUseCompiledBindingsByDefault = default;
        ConfigValue<bool> bindingCSharpExpressionsEnabled = default;
        ConfigValue<bool> bindingImplicitCSharpExpressionsEnabled = default;
        ConfigValue<bool> bindingLegacyInvalidNamedArgumentFallbackEnabled = default;
        ConfigValue<bool> bindingTypeResolutionCompatibilityFallbackEnabled = default;

        ConfigValue<bool> emitterCreateSourceInfo = default;
        ConfigValue<bool> emitterTracePasses = default;
        ConfigValue<bool> emitterMetricsEnabled = default;
        ConfigValue<bool> emitterMetricsDetailed = default;

        ConfigValue<bool> diagnosticsTreatWarningsAsErrors = default;

        var parserGlobalXmlnsPrefixes = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        var semanticTypeContracts = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        var semanticPropertyContracts = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        var semanticEventContracts = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        var transformRawDocuments = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        var severityOverrides = ImmutableDictionary.CreateBuilder<string, XamlSourceGenConfigurationIssueSeverity?>(StringComparer.Ordinal);
        var frameworkSections = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, string?>>(StringComparer.Ordinal);

        foreach (var attribute in _compilation.Assembly.GetAttributes())
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    AssemblyMetadataAttributeMetadataName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 2 ||
                attribute.ConstructorArguments[0].Value is not string rawKey)
            {
                continue;
            }

            var rawValue = attribute.ConstructorArguments[1].Value as string;
            if (!rawKey.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = rawKey.Substring(KeyPrefix.Length).Trim();
            if (path.Length == 0)
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    Code: "AXSG0930",
                    Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                    Message: "AssemblyMetadata key '" + rawKey + "' is missing the configuration path.",
                    SourceName: Name));
                continue;
            }

            if (TryReadBooleanKey(path, "Build.IsEnabled", rawValue, issues, ref buildIsEnabled) ||
                TryReadStringKey(path, "Build.Backend", rawValue, issues, ref buildBackend) ||
                TryReadBooleanKey(path, "Build.StrictMode", rawValue, issues, ref buildStrictMode) ||
                TryReadBooleanKey(path, "Build.HotReloadEnabled", rawValue, issues, ref buildHotReloadEnabled) ||
                TryReadBooleanKey(path, "Build.HotReloadErrorResilienceEnabled", rawValue, issues, ref buildHotReloadErrorResilienceEnabled) ||
                TryReadBooleanKey(path, "Build.IdeHotReloadEnabled", rawValue, issues, ref buildIdeHotReloadEnabled) ||
                TryReadBooleanKey(path, "Build.HotDesignEnabled", rawValue, issues, ref buildHotDesignEnabled) ||
                TryReadBooleanKey(path, "Build.IosHotReloadEnabled", rawValue, issues, ref buildIosHotReloadEnabled) ||
                TryReadBooleanKey(path, "Build.IosHotReloadUseInterpreter", rawValue, issues, ref buildIosHotReloadUseInterpreter) ||
                TryReadBooleanKey(path, "Build.DotNetWatchBuild", rawValue, issues, ref buildDotNetWatchBuild) ||
                TryReadBooleanKey(path, "Build.BuildingInsideVisualStudio", rawValue, issues, ref buildBuildingInsideVisualStudio) ||
                TryReadBooleanKey(path, "Build.BuildingByReSharper", rawValue, issues, ref buildBuildingByReSharper) ||
                TryReadBooleanKey(path, "Parser.AllowImplicitXmlnsDeclaration", rawValue, issues, ref parserAllowImplicitXmlnsDeclaration) ||
                TryReadBooleanKey(path, "Parser.ImplicitStandardXmlnsPrefixesEnabled", rawValue, issues, ref parserImplicitStandardXmlnsPrefixesEnabled) ||
                TryReadStringKey(path, "Parser.ImplicitDefaultXmlns", rawValue, issues, ref parserImplicitDefaultXmlns) ||
                TryReadBooleanKey(path, "Parser.InferClassFromPath", rawValue, issues, ref parserInferClassFromPath) ||
                TryReadBooleanKey(path, "Parser.ImplicitProjectNamespacesEnabled", rawValue, issues, ref parserImplicitProjectNamespacesEnabled) ||
                TryReadBooleanKey(path, "Binding.UseCompiledBindingsByDefault", rawValue, issues, ref bindingUseCompiledBindingsByDefault) ||
                TryReadBooleanKey(path, "Binding.CSharpExpressionsEnabled", rawValue, issues, ref bindingCSharpExpressionsEnabled) ||
                TryReadBooleanKey(path, "Binding.ImplicitCSharpExpressionsEnabled", rawValue, issues, ref bindingImplicitCSharpExpressionsEnabled) ||
                TryReadBooleanKey(path, "Binding.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled", rawValue, issues, ref bindingLegacyInvalidNamedArgumentFallbackEnabled) ||
                TryReadBooleanKey(path, "Binding.TypeResolutionCompatibilityFallbackEnabled", rawValue, issues, ref bindingTypeResolutionCompatibilityFallbackEnabled) ||
                TryReadBooleanKey(path, "Emitter.CreateSourceInfo", rawValue, issues, ref emitterCreateSourceInfo) ||
                TryReadBooleanKey(path, "Emitter.TracePasses", rawValue, issues, ref emitterTracePasses) ||
                TryReadBooleanKey(path, "Emitter.MetricsEnabled", rawValue, issues, ref emitterMetricsEnabled) ||
                TryReadBooleanKey(path, "Emitter.MetricsDetailed", rawValue, issues, ref emitterMetricsDetailed) ||
                TryReadBooleanKey(path, "Diagnostics.TreatWarningsAsErrors", rawValue, issues, ref diagnosticsTreatWarningsAsErrors))
            {
                continue;
            }

            if (TryReadMapKey(path, "Parser.GlobalXmlnsPrefixes.", rawValue, parserGlobalXmlnsPrefixes) ||
                TryReadMapKey(path, "SemanticContract.TypeContracts.", rawValue, semanticTypeContracts) ||
                TryReadMapKey(path, "SemanticContract.PropertyContracts.", rawValue, semanticPropertyContracts) ||
                TryReadMapKey(path, "SemanticContract.EventContracts.", rawValue, semanticEventContracts) ||
                TryReadMapKey(path, "Transform.RawTransformDocuments.", rawValue, transformRawDocuments))
            {
                continue;
            }

            if (TryReadSeverityMapKey(path, rawValue, issues, severityOverrides))
            {
                continue;
            }

            if (TryReadFrameworkSectionKey(path, rawValue, frameworkSections))
            {
                continue;
            }

            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0932",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "AssemblyMetadata key '" + rawKey + "' is not a recognized configuration path.",
                SourceName: Name));
        }

        var patch = new XamlSourceGenConfigurationPatch
        {
            Build = new XamlSourceGenBuildOptionsPatch
            {
                IsEnabled = buildIsEnabled,
                Backend = buildBackend,
                StrictMode = buildStrictMode,
                HotReloadEnabled = buildHotReloadEnabled,
                HotReloadErrorResilienceEnabled = buildHotReloadErrorResilienceEnabled,
                IdeHotReloadEnabled = buildIdeHotReloadEnabled,
                HotDesignEnabled = buildHotDesignEnabled,
                IosHotReloadEnabled = buildIosHotReloadEnabled,
                IosHotReloadUseInterpreter = buildIosHotReloadUseInterpreter,
                DotNetWatchBuild = buildDotNetWatchBuild,
                BuildingInsideVisualStudio = buildBuildingInsideVisualStudio,
                BuildingByReSharper = buildBuildingByReSharper
            },
            Parser = new XamlSourceGenParserOptionsPatch
            {
                AllowImplicitXmlnsDeclaration = parserAllowImplicitXmlnsDeclaration,
                ImplicitStandardXmlnsPrefixesEnabled = parserImplicitStandardXmlnsPrefixesEnabled,
                ImplicitDefaultXmlns = parserImplicitDefaultXmlns,
                InferClassFromPath = parserInferClassFromPath,
                ImplicitProjectNamespacesEnabled = parserImplicitProjectNamespacesEnabled,
                GlobalXmlnsPrefixes = parserGlobalXmlnsPrefixes.Count == 0
                    ? XamlSourceGenConfigurationCollections.EmptyNullableStringMap
                    : parserGlobalXmlnsPrefixes.ToImmutable()
            },
            SemanticContract = new XamlSourceGenSemanticContractOptionsPatch
            {
                TypeContracts = semanticTypeContracts.Count == 0
                    ? XamlSourceGenConfigurationCollections.EmptyNullableStringMap
                    : semanticTypeContracts.ToImmutable(),
                PropertyContracts = semanticPropertyContracts.Count == 0
                    ? XamlSourceGenConfigurationCollections.EmptyNullableStringMap
                    : semanticPropertyContracts.ToImmutable(),
                EventContracts = semanticEventContracts.Count == 0
                    ? XamlSourceGenConfigurationCollections.EmptyNullableStringMap
                    : semanticEventContracts.ToImmutable()
            },
            Binding = new XamlSourceGenBindingOptionsPatch
            {
                UseCompiledBindingsByDefault = bindingUseCompiledBindingsByDefault,
                CSharpExpressionsEnabled = bindingCSharpExpressionsEnabled,
                ImplicitCSharpExpressionsEnabled = bindingImplicitCSharpExpressionsEnabled,
                MarkupParserLegacyInvalidNamedArgumentFallbackEnabled = bindingLegacyInvalidNamedArgumentFallbackEnabled,
                TypeResolutionCompatibilityFallbackEnabled = bindingTypeResolutionCompatibilityFallbackEnabled
            },
            Emitter = new XamlSourceGenEmitterOptionsPatch
            {
                CreateSourceInfo = emitterCreateSourceInfo,
                TracePasses = emitterTracePasses,
                MetricsEnabled = emitterMetricsEnabled,
                MetricsDetailed = emitterMetricsDetailed
            },
            Transform = new XamlSourceGenTransformOptionsPatch
            {
                RawTransformDocuments = transformRawDocuments.Count == 0
                    ? XamlSourceGenConfigurationCollections.EmptyNullableStringMap
                    : transformRawDocuments.ToImmutable()
            },
            Diagnostics = new XamlSourceGenDiagnosticsOptionsPatch
            {
                TreatWarningsAsErrors = diagnosticsTreatWarningsAsErrors,
                SeverityOverrides = severityOverrides.Count == 0
                    ? XamlSourceGenConfigurationCollections.EmptyNullableSeverityMap
                    : severityOverrides.ToImmutable()
            },
            FrameworkExtras = new XamlSourceGenFrameworkExtrasPatch
            {
                Sections = frameworkSections.Count == 0
                    ? XamlSourceGenConfigurationCollections.EmptySectionPatchMap
                    : frameworkSections.ToImmutable()
            }
        };

        return new XamlSourceGenConfigurationSourceResult
        {
            Patch = patch,
            Issues = issues.ToImmutable()
        };
    }

    private bool TryReadBooleanKey(
        string path,
        string expectedPath,
        string? rawValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues,
        ref ConfigValue<bool> target)
    {
        if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!bool.TryParse(rawValue, out var parsed))
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0931",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "AssemblyMetadata key '" + KeyPrefix + expectedPath + "' expects a boolean value.",
                SourceName: Name));
            return true;
        }

        target = parsed;
        return true;
    }

    private bool TryReadStringKey(
        string path,
        string expectedPath,
        string? rawValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues,
        ref ConfigValue<string> target)
    {
        if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rawValue is null)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0931",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "AssemblyMetadata key '" + KeyPrefix + expectedPath + "' expects a non-null string value.",
                SourceName: Name));
            return true;
        }

        target = rawValue;
        return true;
    }

    private static bool TryReadMapKey(
        string path,
        string prefix,
        string? rawValue,
        ImmutableDictionary<string, string?>.Builder target)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = path.Substring(prefix.Length).Trim();
        if (key.Length == 0)
        {
            return true;
        }

        target[key] = rawValue;
        return true;
    }

    private bool TryReadSeverityMapKey(
        string path,
        string? rawValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues,
        ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity?>.Builder target)
    {
        const string prefix = "Diagnostics.SeverityOverrides.";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var diagnosticCode = path.Substring(prefix.Length).Trim();
        if (diagnosticCode.Length == 0)
        {
            return true;
        }

        if (rawValue is null)
        {
            target[diagnosticCode] = null;
            return true;
        }

        if (Enum.TryParse(rawValue, ignoreCase: true, out XamlSourceGenConfigurationIssueSeverity severity))
        {
            target[diagnosticCode] = severity;
            return true;
        }

        issues.Add(new XamlSourceGenConfigurationIssue(
            Code: "AXSG0931",
            Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
            Message: "AssemblyMetadata key '" + KeyPrefix + prefix + diagnosticCode +
                     "' expects 'Info', 'Warning', or 'Error'.",
            SourceName: Name));
        return true;
    }

    private static bool TryReadFrameworkSectionKey(
        string path,
        string? rawValue,
        ImmutableDictionary<string, ImmutableDictionary<string, string?>>.Builder sections)
    {
        const string prefix = "FrameworkExtras.";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = path.Substring(prefix.Length);
        var separatorIndex = suffix.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= suffix.Length - 1)
        {
            return true;
        }

        var sectionName = suffix.Substring(0, separatorIndex).Trim();
        var entryName = suffix.Substring(separatorIndex + 1).Trim();
        if (sectionName.Length == 0 || entryName.Length == 0)
        {
            return true;
        }

        if (!sections.TryGetValue(sectionName, out var existingSection))
        {
            existingSection = ImmutableDictionary<string, string?>.Empty.WithComparers(StringComparer.Ordinal);
        }

        sections[sectionName] = existingSection.SetItem(entryName, rawValue);
        return true;
    }
}
