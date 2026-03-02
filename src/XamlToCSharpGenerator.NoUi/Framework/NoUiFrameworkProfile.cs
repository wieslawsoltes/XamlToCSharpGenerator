using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.NoUi.Binding;
using XamlToCSharpGenerator.NoUi.Emission;

namespace XamlToCSharpGenerator.NoUi.Framework;

public sealed class NoUiFrameworkProfile : IXamlFrameworkProfile
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string NoUiImplicitDefaultXmlNamespace = "urn:noui";
    private static readonly IXamlFrameworkBuildContract BuildContractInstance = new NoUiFrameworkBuildContract();
    private static readonly IXamlFrameworkTransformProvider TransformProviderInstance = new NoUiFrameworkTransformProvider();

    public static NoUiFrameworkProfile Instance { get; } = new();

    private NoUiFrameworkProfile()
    {
    }

    public string Id => FrameworkProfileIds.NoUi;

    public IXamlFrameworkBuildContract BuildContract => BuildContractInstance;

    public IXamlFrameworkTransformProvider TransformProvider => TransformProviderInstance;

    public IXamlFrameworkSemanticBinder CreateSemanticBinder()
    {
        return new NoUiSemanticBinder();
    }

    public IXamlFrameworkEmitter CreateEmitter()
    {
        return new NoUiCodeEmitter();
    }

    public ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers()
    {
        return ImmutableArray<IXamlDocumentEnricher>.Empty;
    }

    public XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options)
    {
        _ = compilation;
        _ = options;
        var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        globalPrefixes["x"] = Xaml2006Namespace;
        return new XamlFrameworkParserSettings(
            globalPrefixes.ToImmutable(),
            allowImplicitDefaultXmlns: true,
            implicitDefaultXmlns: NoUiImplicitDefaultXmlNamespace);
    }

    private sealed class NoUiFrameworkBuildContract : IXamlFrameworkBuildContract
    {
        public string SourceItemGroupMetadataName => "build_metadata.AdditionalFiles.SourceItemGroup";

        public string TargetPathMetadataName => "build_metadata.AdditionalFiles.TargetPath";

        public string XamlSourceItemGroup => "NoUiXaml";

        public string TransformRuleSourceItemGroup => "NoUiSourceGenTransformRule";

        public bool IsXamlPath(string path)
        {
            return path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsXamlSourceItemGroup(string? sourceItemGroup)
        {
            return string.Equals(sourceItemGroup, XamlSourceItemGroup, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsTransformRuleSourceItemGroup(string? sourceItemGroup)
        {
            return string.Equals(sourceItemGroup, TransformRuleSourceItemGroup, StringComparison.OrdinalIgnoreCase);
        }

        public string NormalizeSourceItemGroup(string? sourceItemGroup)
        {
            return string.IsNullOrWhiteSpace(sourceItemGroup)
                ? XamlSourceItemGroup
                : sourceItemGroup!.Trim();
        }
    }

    private sealed class NoUiFrameworkTransformProvider : IXamlFrameworkTransformProvider
    {
        public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input)
        {
            _ = input;
            return new XamlFrameworkTransformRuleResult(
                input.FilePath,
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }

        public XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
            ImmutableArray<XamlFrameworkTransformRuleResult> files)
        {
            if (files.IsDefaultOrEmpty)
            {
                return new XamlFrameworkTransformRuleAggregateResult(
                    XamlTransformConfiguration.Empty,
                    ImmutableArray<DiagnosticInfo>.Empty);
            }

            var diagnostics = files
                .SelectMany(static file => file.Diagnostics)
                .ToImmutableArray();
            return new XamlFrameworkTransformRuleAggregateResult(
                XamlTransformConfiguration.Empty,
                diagnostics);
        }
    }
}
