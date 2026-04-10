using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed class PassiveXamlFrameworkProfile : IXamlFrameworkProfile
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string BlendDesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
    private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private readonly IXamlFrameworkBuildContract _buildContract;

    public PassiveXamlFrameworkProfile(
        string id,
        string defaultXmlNamespace,
        string preferredProjectXamlItemName,
        ImmutableArray<string> projectXamlItemNames)
    {
        Id = id;
        DefaultXmlNamespace = defaultXmlNamespace;
        _buildContract = new PassiveBuildContract(preferredProjectXamlItemName, projectXamlItemNames);
        BaseConfiguration = XamlSourceGenConfiguration.Default with
        {
            Parser = XamlSourceGenConfiguration.Default.Parser with
            {
                AllowImplicitXmlnsDeclaration = true,
                ImplicitDefaultXmlns = defaultXmlNamespace
            }
        };
        SemanticContractMap = new SemanticContractMap(
            mapId: id + ".Passive",
            frameworkId: id,
            typeContracts: Array.Empty<SemanticTypeContract>());
        DocumentUriResolver = new PassiveDocumentUriResolver(id);
    }

    public string Id { get; }

    public string DefaultXmlNamespace { get; }

    public XamlSourceGenConfiguration BaseConfiguration { get; }

    public XamlFrameworkMsBuildSettings MsBuildSettings { get; } = new(
    [
        new KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>(
            XamlFrameworkMsBuildSettingKey.Backend,
            new[] { "XamlSourceGenBackend" }),
        new KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>(
            XamlFrameworkMsBuildSettingKey.IsEnabled,
            new[] { "XamlSourceGenEnabled" }),
        new KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>(
            XamlFrameworkMsBuildSettingKey.ConfigurationPrecedence,
            new[] { "XamlSourceGenConfigurationPrecedence" })
    ]);

    public SemanticContractMap SemanticContractMap { get; }

    public XamlFrameworkSemanticConventions SemanticConventions { get; } = XamlFrameworkSemanticConventions.Empty;

    public IXamlFrameworkBuildContract BuildContract => _buildContract;

    public IXamlFrameworkDocumentUriResolver DocumentUriResolver { get; }

    public IXamlFrameworkTransformProvider TransformProvider { get; } = new PassiveTransformProvider();

    public IXamlFrameworkSemanticBinder CreateSemanticBinder()
    {
        return PassiveSemanticBinder.Instance;
    }

    public IXamlFrameworkEmitter CreateEmitter()
    {
        return PassiveEmitter.Instance;
    }

    public ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers()
    {
        return ImmutableArray<IXamlDocumentEnricher>.Empty;
    }

    public XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options)
    {
        _ = compilation;
        var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        globalPrefixes["x"] = Xaml2006Namespace;
        globalPrefixes["d"] = BlendDesignNamespace;
        globalPrefixes["mc"] = MarkupCompatibilityNamespace;

        return new XamlFrameworkParserSettings(
            globalPrefixes.ToImmutable(),
            allowImplicitDefaultXmlns: true,
            implicitDefaultXmlns: string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns)
                ? DefaultXmlNamespace
                : options.ImplicitDefaultXmlns);
    }

    public string? BuildHotReloadAssemblyMetadataHandlerSource(bool hasXamlInputs, GeneratorOptions options)
    {
        _ = hasXamlInputs;
        _ = options;
        return null;
    }

    private sealed class PassiveBuildContract : IXamlFrameworkBuildContract
    {
        private readonly ImmutableHashSet<string> _projectXamlItemNames;
        private readonly string _preferredProjectXamlItemName;

        public PassiveBuildContract(string preferredProjectXamlItemName, ImmutableArray<string> projectXamlItemNames)
        {
            _preferredProjectXamlItemName = preferredProjectXamlItemName;
            _projectXamlItemNames = projectXamlItemNames.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public string SourceItemGroupMetadataName => "build_metadata.AdditionalFiles.SourceItemGroup";

        public string TargetPathMetadataName => "build_metadata.AdditionalFiles.TargetPath";

        public string XamlSourceItemGroup => _preferredProjectXamlItemName;

        public string TransformRuleSourceItemGroup => _preferredProjectXamlItemName + "SourceGenTransformRule";

        public bool IsXamlPath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsXamlSourceItemGroup(string? sourceItemGroup)
        {
            return sourceItemGroup is not null && _projectXamlItemNames.Contains(sourceItemGroup.Trim());
        }

        public bool IsTransformRuleSourceItemGroup(string? sourceItemGroup)
        {
            return string.Equals(sourceItemGroup, TransformRuleSourceItemGroup, StringComparison.OrdinalIgnoreCase);
        }

        public string NormalizeSourceItemGroup(string? sourceItemGroup)
        {
            if (string.IsNullOrWhiteSpace(sourceItemGroup))
            {
                return _preferredProjectXamlItemName;
            }

            var normalized = sourceItemGroup.Trim();
            return _projectXamlItemNames.Contains(normalized)
                ? normalized
                : _preferredProjectXamlItemName;
        }
    }

    private sealed class PassiveTransformProvider : IXamlFrameworkTransformProvider
    {
        public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input)
        {
            return new XamlFrameworkTransformRuleResult(
                input.FilePath,
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }

        public XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
            ImmutableArray<XamlFrameworkTransformRuleResult> files)
        {
            var diagnostics = files.IsDefaultOrEmpty
                ? ImmutableArray<DiagnosticInfo>.Empty
                : files.SelectMany(static item => item.Diagnostics).ToImmutableArray();

            return new XamlFrameworkTransformRuleAggregateResult(
                XamlTransformConfiguration.Empty,
                diagnostics);
        }
    }

    private sealed class PassiveSemanticBinder : IXamlFrameworkSemanticBinder
    {
        public static PassiveSemanticBinder Instance { get; } = new();

        public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            XamlTransformConfiguration transformConfiguration)
        {
            _ = document;
            _ = compilation;
            _ = options;
            _ = transformConfiguration;
            return (null, ImmutableArray<DiagnosticInfo>.Empty);
        }
    }

    private sealed class PassiveEmitter : IXamlFrameworkEmitter
    {
        public static PassiveEmitter Instance { get; } = new();

        public (string HintName, string Source) Emit(ResolvedViewModel viewModel)
        {
            throw new NotSupportedException("Passive language-service framework profiles do not emit source.");
        }
    }

    private sealed class PassiveDocumentUriResolver : IXamlFrameworkDocumentUriResolver
    {
        private readonly string _schemePrefix;

        public PassiveDocumentUriResolver(string frameworkId)
        {
            _schemePrefix = frameworkId.ToLowerInvariant() + "://";
        }

        public string BuildDocumentUri(string assemblyName, string normalizedTargetPath)
        {
            return _schemePrefix + assemblyName + "/" + normalizedTargetPath;
        }

        public bool TryResolveIncludeUri(
            string includeSource,
            string currentTargetPath,
            string currentDocumentUri,
            out string resolvedUri,
            out bool isProjectLocal)
        {
            _ = currentTargetPath;
            _ = currentDocumentUri;
            resolvedUri = includeSource;
            isProjectLocal = false;
            return !string.IsNullOrWhiteSpace(includeSource);
        }
    }
}
