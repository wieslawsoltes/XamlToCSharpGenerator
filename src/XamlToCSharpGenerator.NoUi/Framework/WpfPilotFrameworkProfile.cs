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

public sealed class WpfPilotFrameworkProfile : IXamlFrameworkProfile
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string PresentationXmlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly IXamlFrameworkBuildContract BuildContractInstance =
        new XamlFrameworkBuildContract(
            xamlSourceItemGroup: "Page",
            transformRuleSourceItemGroup: "PageSourceGenTransformRule",
            extensions: new[] { ".xaml" },
            allowMissingSourceItemGroup: false);

    private static readonly IXamlFrameworkDocumentUriResolver DocumentUriResolverInstance =
        new SchemeBasedDocumentUriResolver("wpfpilot");

    private static readonly IXamlFrameworkTransformProvider TransformProviderInstance =
        new XamlJsonTransformProvider(
            defaultFrameworkId: FrameworkProfileIds.WpfPilot,
            defaultXmlNamespace: PresentationXmlNamespace);

    private static readonly IXamlFrameworkSemanticBinder SemanticBinderInstance = new NoUiSemanticBinder();
    private static readonly IXamlFrameworkEmitter EmitterInstance = new WpfPilotCodeEmitter();

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
                ImplicitDefaultXmlns = PresentationXmlNamespace
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

    public static WpfPilotFrameworkProfile Instance { get; } = new();

    private WpfPilotFrameworkProfile()
    {
    }

    public string Id => FrameworkProfileIds.WpfPilot;

    public XamlSourceGenConfiguration BaseConfiguration => BaseConfigurationInstance;

    public XamlFrameworkMsBuildSettings MsBuildSettings => MsBuildSettingsInstance;

    public SemanticContractMap SemanticContractMap => NoUiSemanticContractMap.Instance;

    public XamlFrameworkSemanticConventions SemanticConventions => NoUiFrameworkSemanticConventions.Instance;

    public IXamlFrameworkBuildContract BuildContract => BuildContractInstance;

    public IXamlFrameworkDocumentUriResolver DocumentUriResolver => DocumentUriResolverInstance;

    public IXamlFrameworkTransformProvider TransformProvider => TransformProviderInstance;

    public IXamlFrameworkSemanticBinder CreateSemanticBinder() => SemanticBinderInstance;

    public IXamlFrameworkEmitter CreateEmitter() => EmitterInstance;

    public ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers() => ImmutableArray<IXamlDocumentEnricher>.Empty;

    public XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options)
    {
        _ = compilation;

        var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(System.StringComparer.Ordinal);
        globalPrefixes["x"] = Xaml2006Namespace;

        return new XamlFrameworkParserSettings(
            globalPrefixes.ToImmutable(),
            allowImplicitDefaultXmlns: true,
            implicitDefaultXmlns: string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns)
                ? PresentationXmlNamespace
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
