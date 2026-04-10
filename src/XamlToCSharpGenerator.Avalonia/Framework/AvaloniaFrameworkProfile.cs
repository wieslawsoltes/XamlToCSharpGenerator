using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Avalonia.Binding;
using XamlToCSharpGenerator.Avalonia.Emission;
using XamlToCSharpGenerator.Avalonia.Parsing;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.Framework.Shared.Configuration;
using XamlToCSharpGenerator.Framework.Shared.Runtime;

namespace XamlToCSharpGenerator.Avalonia.Framework;

public sealed class AvaloniaFrameworkProfile : IXamlFrameworkProfile
{
    private const string AvaloniaXmlnsPrefixAttributeMetadataName = "Avalonia.Metadata.XmlnsPrefixAttribute";
    private const string SourceGenGlobalXmlnsPrefixAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenGlobalXmlnsPrefixAttribute";
    private const string SourceGenAllowImplicitXmlnsDeclarationAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenAllowImplicitXmlnsDeclarationAttribute";
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string BlendDesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
    private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private static readonly ConcurrentDictionary<string, ImmutableDictionary<string, string>> GlobalXmlnsPrefixPropertyCache =
        new(StringComparer.Ordinal);

    private static readonly IXamlFrameworkBuildContract BuildContractInstance =
        new XamlFrameworkBuildContract(
            xamlSourceItemGroup: "AvaloniaXaml",
            transformRuleSourceItemGroup: "AvaloniaSourceGenTransformRule",
            extensions: new[] { ".axaml", ".xaml", ".paml" },
            allowMissingSourceItemGroup: true);

    private static readonly IXamlFrameworkDocumentUriResolver DocumentUriResolverInstance =
        new SchemeBasedDocumentUriResolver("avares");

    private static readonly IXamlFrameworkTransformProvider TransformProviderInstance =
        new XamlJsonTransformProvider(
            defaultFrameworkId: FrameworkProfileIds.Avalonia,
            defaultXmlNamespace: AvaloniaDefaultXmlNamespace,
            legacyOwnerPropertyNames: new[] { "avaloniaPropertyOwnerType", "avaloniaPropertyOwnerTypeName" },
            legacyFieldPropertyNames: new[] { "avaloniaPropertyField", "avaloniaPropertyFieldName" });

    private static readonly Lazy<IXamlFrameworkSemanticBinder> SemanticBinderInstance =
        new(
            static () => new AvaloniaFrameworkSemanticBinder(new AvaloniaSemanticBinder()),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IXamlFrameworkEmitter> EmitterInstance =
        new(
            static () => new AvaloniaFrameworkEmitter(new AvaloniaCodeEmitter()),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly ImmutableArray<IXamlDocumentEnricher> DocumentEnricherInstances =
        ImmutableArray.Create<IXamlDocumentEnricher>(AvaloniaDocumentFeatureEnricher.Instance);

    private static readonly XamlSourceGenConfiguration BaseConfigurationInstance =
        XamlSourceGenConfiguration.Default with
        {
            Parser = XamlSourceGenConfiguration.Default.Parser with
            {
                ImplicitDefaultXmlns = AvaloniaDefaultXmlNamespace
            }
        };

    private static readonly XamlFrameworkMsBuildSettings MsBuildSettingsInstance = new(
    [
        Alias(XamlFrameworkMsBuildSettingKey.Backend, "XamlSourceGenBackend", "AvaloniaXamlCompilerBackend"),
        Alias(XamlFrameworkMsBuildSettingKey.IsEnabled, "XamlSourceGenEnabled", "AvaloniaSourceGenCompilerEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.ConfigurationPrecedence, "XamlSourceGenConfigurationPrecedence", "AvaloniaSourceGenConfigurationPrecedence"),
        Alias(XamlFrameworkMsBuildSettingKey.StrictMode, "AvaloniaSourceGenStrictMode"),
        Alias(XamlFrameworkMsBuildSettingKey.HotReloadEnabled, "AvaloniaSourceGenHotReloadEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.HotReloadErrorResilienceEnabled, "AvaloniaSourceGenHotReloadErrorResilienceEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.IdeHotReloadEnabled, "AvaloniaSourceGenIdeHotReloadEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.HotDesignEnabled, "AvaloniaSourceGenHotDesignEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.IosHotReloadEnabled, "AvaloniaSourceGenIosHotReloadEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.IosHotReloadUseInterpreter, "AvaloniaSourceGenIosHotReloadUseInterpreter"),
        Alias(XamlFrameworkMsBuildSettingKey.AllowImplicitXmlnsDeclaration, "AvaloniaSourceGenAllowImplicitXmlnsDeclaration"),
        Alias(XamlFrameworkMsBuildSettingKey.ImplicitStandardXmlnsPrefixesEnabled, "AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.ImplicitDefaultXmlns, "AvaloniaSourceGenImplicitDefaultXmlns"),
        Alias(XamlFrameworkMsBuildSettingKey.InferClassFromPath, "AvaloniaSourceGenInferClassFromPath"),
        Alias(XamlFrameworkMsBuildSettingKey.ImplicitProjectNamespacesEnabled, "AvaloniaSourceGenImplicitProjectNamespacesEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.GlobalXmlnsPrefixes, "AvaloniaSourceGenGlobalXmlnsPrefixes"),
        Alias(XamlFrameworkMsBuildSettingKey.UseCompiledBindingsByDefault, "AvaloniaSourceGenUseCompiledBindingsByDefault"),
        Alias(XamlFrameworkMsBuildSettingKey.CSharpExpressionsEnabled, "AvaloniaSourceGenCSharpExpressionsEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.ImplicitCSharpExpressionsEnabled, "AvaloniaSourceGenImplicitCSharpExpressionsEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled, "AvaloniaSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.TypeResolutionCompatibilityFallbackEnabled, "AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.CreateSourceInfo, "AvaloniaSourceGenCreateSourceInfo"),
        Alias(XamlFrameworkMsBuildSettingKey.TracePasses, "AvaloniaSourceGenTracePasses"),
        Alias(XamlFrameworkMsBuildSettingKey.MetricsEnabled, "AvaloniaSourceGenMetricsEnabled"),
        Alias(XamlFrameworkMsBuildSettingKey.MetricsDetailed, "AvaloniaSourceGenMetricsDetailed")
    ]);

    public static AvaloniaFrameworkProfile Instance { get; } = new();

    private AvaloniaFrameworkProfile()
    {
    }

    public string Id => FrameworkProfileIds.Avalonia;

    public XamlSourceGenConfiguration BaseConfiguration => BaseConfigurationInstance;

    public XamlFrameworkMsBuildSettings MsBuildSettings => MsBuildSettingsInstance;

    public SemanticContractMap SemanticContractMap => AvaloniaSemanticContractMap.Instance;

    public XamlFrameworkSemanticConventions SemanticConventions => AvaloniaFrameworkSemanticConventions.Instance;

    public IXamlFrameworkBuildContract BuildContract => BuildContractInstance;

    public IXamlFrameworkDocumentUriResolver DocumentUriResolver => DocumentUriResolverInstance;

    public IXamlFrameworkTransformProvider TransformProvider => TransformProviderInstance;

    public IXamlFrameworkSemanticBinder CreateSemanticBinder()
    {
        return SemanticBinderInstance.Value;
    }

    public IXamlFrameworkEmitter CreateEmitter()
    {
        return EmitterInstance.Value;
    }

    public ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers()
    {
        return DocumentEnricherInstances;
    }

    public XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options)
    {
        var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (IsXmlnsPrefixAttribute(attribute))
                {
                    if (attribute.ConstructorArguments.Length < 2 ||
                        attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                        attribute.ConstructorArguments[1].Value is not string prefix ||
                        string.IsNullOrWhiteSpace(prefix) ||
                        string.IsNullOrWhiteSpace(xmlNamespace))
                    {
                        continue;
                    }

                    globalPrefixes[prefix.Trim()] = xmlNamespace.Trim();
                    continue;
                }

                if (IsSourceGenAllowImplicitXmlnsDeclarationAttribute(attribute))
                {
                    if (attribute.ConstructorArguments.Length == 0)
                    {
                        options = options with { AllowImplicitXmlnsDeclaration = true };
                    }
                    else if (attribute.ConstructorArguments[0].Value is bool allowImplicit)
                    {
                        options = options with { AllowImplicitXmlnsDeclaration = allowImplicit };
                    }
                }
            }
        }

        foreach (var entry in ParseGlobalXmlnsPrefixesProperty(options.GlobalXmlnsPrefixes))
        {
            globalPrefixes[entry.Key] = entry.Value;
        }

        if (options.AllowImplicitXmlnsDeclaration &&
            options.ImplicitStandardXmlnsPrefixesEnabled)
        {
            AddImplicitPrefix(globalPrefixes, "x", Xaml2006Namespace);
            AddImplicitPrefix(globalPrefixes, "d", BlendDesignNamespace);
            AddImplicitPrefix(globalPrefixes, "mc", MarkupCompatibilityNamespace);
        }

        if (options.AllowImplicitXmlnsDeclaration &&
            !string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns) &&
            !globalPrefixes.ContainsKey(string.Empty))
        {
            globalPrefixes[string.Empty] = options.ImplicitDefaultXmlns;
        }

        return new XamlFrameworkParserSettings(
            globalPrefixes.ToImmutable(),
            options.AllowImplicitXmlnsDeclaration,
            options.ImplicitDefaultXmlns);
    }

    public string? BuildHotReloadAssemblyMetadataHandlerSource(bool hasXamlInputs, GeneratorOptions options)
    {
        if (!hasXamlInputs ||
            !options.IsEnabled ||
            !options.HotReloadEnabled)
        {
            return null;
        }

        var preserveIosDebugEntryPointsSource = options.IosHotReloadEnabled
            ? """
#if NET6_0_OR_GREATER && DEBUG && IOS
namespace XamlToCSharpGenerator.Generated
{
    [global::System.Runtime.CompilerServices.CompilerGenerated]
    internal static class __SourceGenHotReloadLinkerHints
    {
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        [global::System.Diagnostics.CodeAnalysis.DynamicDependency(nameof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.ClearCache), typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]
        [global::System.Diagnostics.CodeAnalysis.DynamicDependency(nameof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.UpdateApplication), typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]
        internal static void Initialize()
        {
        }
    }
}
#endif
"""
            : string.Empty;

        return """
#if NET6_0_OR_GREATER
[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]
#endif
""" + preserveIosDebugEntryPointsSource;
    }

    private static KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>> Alias(
        XamlFrameworkMsBuildSettingKey key,
        params string[] propertyNames)
    {
        return new KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>(key, propertyNames);
    }

    private static void AddImplicitPrefix(
        ImmutableDictionary<string, string>.Builder globalPrefixes,
        string prefix,
        string xmlNamespace)
    {
        if (!globalPrefixes.ContainsKey(prefix))
        {
            globalPrefixes[prefix] = xmlNamespace;
        }
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referencedAssembly is not null && visited.Add(referencedAssembly))
            {
                yield return referencedAssembly;
            }
        }

        if (visited.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }
    }

    private static bool IsXmlnsPrefixAttribute(AttributeData attribute)
    {
        var metadataName = attribute.AttributeClass?.ToDisplayString();
        return string.Equals(metadataName, AvaloniaXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal) ||
               string.Equals(metadataName, SourceGenGlobalXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal);
    }

    private static bool IsSourceGenAllowImplicitXmlnsDeclarationAttribute(AttributeData attribute)
    {
        return string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            SourceGenAllowImplicitXmlnsDeclarationAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> ParseGlobalXmlnsPrefixesProperty(string? rawValue)
    {
        if (rawValue is null)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var trimmedRawValue = rawValue.Trim();
        if (trimmedRawValue.Length == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        return GlobalXmlnsPrefixPropertyCache.GetOrAdd(trimmedRawValue, static value => XamlGlobalXmlnsPrefixParser.Parse(value));
    }

    private sealed class AvaloniaFrameworkSemanticBinder : IXamlFrameworkSemanticBinder
    {
        private readonly IXamlSemanticBinder _innerBinder;

        public AvaloniaFrameworkSemanticBinder(IXamlSemanticBinder innerBinder)
        {
            _innerBinder = innerBinder;
        }

        public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            XamlTransformConfiguration transformConfiguration)
        {
            return _innerBinder.Bind(document, compilation, options, transformConfiguration);
        }
    }

    private sealed class AvaloniaFrameworkEmitter : IXamlFrameworkEmitter
    {
        private readonly IXamlCodeEmitter _innerEmitter;

        public AvaloniaFrameworkEmitter(IXamlCodeEmitter innerEmitter)
        {
            _innerEmitter = innerEmitter;
        }

        public (string HintName, string Source) Emit(ResolvedViewModel viewModel)
        {
            return _innerEmitter.Emit(viewModel);
        }
    }
}
