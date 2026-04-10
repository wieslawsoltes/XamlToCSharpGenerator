using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.Framework.Shared.Configuration;
using XamlToCSharpGenerator.Framework.Shared.Runtime;
using XamlToCSharpGenerator.NoUi.Binding;
using XamlToCSharpGenerator.NoUi.Emission;

namespace XamlToCSharpGenerator.NoUi.Framework;

public sealed class NoUiFrameworkProfile : IXamlFrameworkProfile
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string NoUiImplicitDefaultXmlNamespace = "urn:noui";

    private static readonly IXamlFrameworkBuildContract BuildContractInstance =
        new XamlFrameworkBuildContract(
            xamlSourceItemGroup: "NoUiXaml",
            transformRuleSourceItemGroup: "NoUiSourceGenTransformRule",
            extensions: new[] { ".xaml" },
            allowMissingSourceItemGroup: false);

    private static readonly IXamlFrameworkDocumentUriResolver DocumentUriResolverInstance =
        new SchemeBasedDocumentUriResolver("noui");

    private static readonly IXamlFrameworkTransformProvider TransformProviderInstance =
        new XamlJsonTransformProvider(
            defaultFrameworkId: FrameworkProfileIds.NoUi,
            defaultXmlNamespace: NoUiImplicitDefaultXmlNamespace);

    private static readonly IXamlFrameworkSemanticBinder SemanticBinderInstance = new NoUiSemanticBinder();
    private static readonly IXamlFrameworkEmitter EmitterInstance = new NoUiCodeEmitter();

    private static readonly XamlSourceGenConfiguration BaseConfigurationInstance =
        XamlSourceGenConfiguration.Default with
        {
            Build = XamlSourceGenConfiguration.Default.Build with
            {
                HotReloadEnabled = false,
                IdeHotReloadEnabled = false,
                HotDesignEnabled = false
            },
            Parser = XamlSourceGenConfiguration.Default.Parser with
            {
                AllowImplicitXmlnsDeclaration = true,
                ImplicitDefaultXmlns = NoUiImplicitDefaultXmlNamespace
            }
        };

    private static readonly XamlFrameworkMsBuildSettings MsBuildSettingsInstance = new(
    [
        Alias(XamlFrameworkMsBuildSettingKey.Backend, "XamlSourceGenBackend"),
        Alias(XamlFrameworkMsBuildSettingKey.IsEnabled, "XamlSourceGenEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.ConfigurationPrecedence, "XamlSourceGenConfigurationPrecedence"),
        Alias(XamlFrameworkMsBuildSettingKey.UseCompiledBindingsByDefault, "XamlSourceGenUseCompiledBindingsByDefault"),
        Alias(XamlFrameworkMsBuildSettingKey.CreateSourceInfo, "XamlSourceGenCreateSourceInfo"),
        Alias(XamlFrameworkMsBuildSettingKey.HotReloadEnabled, "XamlSourceGenHotReloadEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.IdeHotReloadEnabled, "XamlSourceGenIdeHotReloadEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.HotDesignEnabled, "XamlSourceGenHotDesignEnabled")
    ]);

    public static NoUiFrameworkProfile Instance { get; } = new();

    private NoUiFrameworkProfile()
    {
    }

    public string Id => FrameworkProfileIds.NoUi;

    public XamlSourceGenConfiguration BaseConfiguration => BaseConfigurationInstance;

    public XamlFrameworkMsBuildSettings MsBuildSettings => MsBuildSettingsInstance;

    public SemanticContractMap SemanticContractMap => NoUiSemanticContractMap.Instance;

    public XamlFrameworkSemanticConventions SemanticConventions => NoUiFrameworkSemanticConventions.Instance;

    public IXamlFrameworkBuildContract BuildContract => BuildContractInstance;

    public IXamlFrameworkDocumentUriResolver DocumentUriResolver => DocumentUriResolverInstance;

    public IXamlFrameworkTransformProvider TransformProvider => TransformProviderInstance;

    public IXamlFrameworkSemanticBinder CreateSemanticBinder()
    {
        return SemanticBinderInstance;
    }

    public IXamlFrameworkEmitter CreateEmitter()
    {
        return EmitterInstance;
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
        if (!string.IsNullOrWhiteSpace(options.GlobalXmlnsPrefixes))
        {
            foreach (var entry in XamlGlobalXmlnsPrefixParser.Parse(options.GlobalXmlnsPrefixes))
            {
                globalPrefixes[entry.Key] = entry.Value;
            }
        }

        return new XamlFrameworkParserSettings(
            globalPrefixes.ToImmutable(),
            allowImplicitDefaultXmlns: true,
            implicitDefaultXmlns: string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns)
                ? NoUiImplicitDefaultXmlNamespace
                : options.ImplicitDefaultXmlns);
    }

    public string? BuildHotReloadAssemblyMetadataHandlerSource(bool hasXamlInputs, GeneratorOptions options)
    {
        _ = hasXamlInputs;
        _ = options;
        return null;
    }

    private static KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>> Alias(
        XamlFrameworkMsBuildSettingKey key,
        params string[] propertyNames)
    {
        return new KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>(key, propertyNames);
    }
}
