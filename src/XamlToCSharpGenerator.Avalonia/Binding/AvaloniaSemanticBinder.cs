using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private const string AvaloniaDefaultXmlNamespaceWithSlash = "https://github.com/avaloniaui/";
    private const string AvaloniaXmlnsDefinitionAttributeMetadataName = "Avalonia.Metadata.XmlnsDefinitionAttribute";
    private const string SourceGenXmlnsDefinitionAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXmlnsDefinitionAttribute";
    private const string SourceGenXamlTypeAliasAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXamlTypeAliasAttribute";
    private const string SourceGenXamlPropertyAliasAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXamlPropertyAliasAttribute";
    private const string SourceGenXamlAvaloniaPropertyAliasAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXamlAvaloniaPropertyAliasAttribute";
    private const string MarkupContextServiceProviderToken = "__AXSG_CTX_SERVICE_PROVIDER__";
    private const string MarkupContextRootObjectToken = "__AXSG_CTX_ROOT_OBJECT__";
    private const string MarkupContextIntermediateRootObjectToken = "__AXSG_CTX_INTERMEDIATE_ROOT_OBJECT__";
    private const string MarkupContextTargetObjectToken = "__AXSG_CTX_TARGET_OBJECT__";
    private const string MarkupContextTargetPropertyToken = "__AXSG_CTX_TARGET_PROPERTY__";
    private const string MarkupContextBaseUriToken = "__AXSG_CTX_BASE_URI__";
    private const string MarkupContextParentStackToken = "__AXSG_CTX_PARENT_STACK__";
    private const string ExpressionSourceParameterName = "source";
    private static readonly MarkupExpressionParser StrictMarkupExpressionParser = new(
        new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: false));
    private static readonly MarkupExpressionParser LegacyMarkupExpressionParser = new(
        new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));

    private static readonly ImmutableHashSet<string> KnownMarkupExtensionNames = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Binding",
        "CompiledBinding",
        "ReflectionBinding",
        "StaticResource",
        "DynamicResource",
        "TemplateBinding",
        "RelativeSource",
        "OnPlatform",
        "OnFormFactor",
        "x:Reference",
        "Reference",
        "ResolveByName",
        "CSharp",
        "x:Static",
        "Static",
        "x:Type",
        "Type",
        "x:Null",
        "Null",
        "x:String",
        "String",
        "x:Char",
        "Char",
        "x:Byte",
        "Byte",
        "x:SByte",
        "SByte",
        "x:Int16",
        "Int16",
        "x:UInt16",
        "UInt16",
        "x:Int32",
        "Int32",
        "x:UInt32",
        "UInt32",
        "x:Int64",
        "Int64",
        "x:UInt64",
        "UInt64",
        "x:Single",
        "Single",
        "x:Double",
        "Double",
        "x:Decimal",
        "Decimal",
        "x:DateTime",
        "DateTime",
        "x:TimeSpan",
        "TimeSpan",
        "x:Uri",
        "Uri",
        "x:Array",
        "Array");

    private static readonly string[] AvaloniaDefaultNamespaceCandidateSeed =
    [
        "Avalonia.Controls.",
        "Avalonia.Controls.Primitives.",
        "Avalonia.Controls.Presenters.",
        "Avalonia.Controls.Shapes.",
        "Avalonia.Controls.Documents.",
        "Avalonia.Controls.Chrome.",
        "Avalonia.Controls.Embedding.",
        "Avalonia.Controls.Notifications.",
        "Avalonia.Controls.Converters.",
        "Avalonia.Markup.Xaml.Templates.",
        "Avalonia.Markup.Xaml.Styling.",
        "Avalonia.Markup.Xaml.MarkupExtensions.",
        "Avalonia.Styling.",
        "Avalonia.Controls.Templates.",
        "Avalonia.Input.",
        "Avalonia.Automation.",
        "Avalonia.Dialogs.",
        "Avalonia.Dialogs.Internal.",
        "Avalonia.Layout.",
        "Avalonia.Media.",
        "Avalonia.Media.Transformation.",
        "Avalonia.Media.Imaging.",
        "Avalonia.Animation.",
        "Avalonia.Animation.Easings.",
        "Avalonia."
    ];

    private static readonly ConditionalWeakTable<Compilation, AvaloniaNamespaceCandidateCacheEntry>
        AvaloniaNamespaceCandidateCache = new();
    private static readonly ConditionalWeakTable<Compilation, XmlnsDefinitionCacheEntry>
        XmlnsDefinitionCache = new();
    private static readonly ConditionalWeakTable<Compilation, SourceAssemblyNamespaceCacheEntry>
        SourceAssemblyNamespaceCache = new();
    private static readonly AsyncLocal<ResolvedTransformExtensions?> ActiveTransformExtensions = new();
    private static readonly AsyncLocal<GeneratorOptions?> ActiveGeneratorOptions = new();
    private static readonly AsyncLocal<TypeResolutionDiagnosticContext?> ActiveTypeResolutionDiagnosticContext = new();
    private static readonly AsyncLocal<ITypeSymbolCatalog?> ActiveTypeSymbolCatalog = new();
    private static readonly SemanticContractMap AvaloniaSemanticContractMap = SemanticContractMaps.AvaloniaDefault;
    private static readonly CSharpExpressionClassificationService ExpressionClassificationService = new(
        TryParseMarkupExtension,
        KnownMarkupExtensionNames,
        TryResolveMarkupExtensionType);
    private static readonly XamlTypeExpressionResolutionService TypeExpressionResolutionService = new(
        TryParseMarkupExtension,
        ResolveTypeToken);
    private static readonly TypeResolutionPolicyService TypeResolutionPolicyService = new(
        TryResolveTypeFromNamespacePrefixes,
        TryGetImplicitProjectNamespaceRoot,
        GetProjectNamespaceCandidates,
        GetAvaloniaDefaultNamespaceCandidates,
        ResolveTypeSymbol,
        IsTypeResolutionCompatibilityFallbackEnabled,
        IsStrictTypeResolutionMode,
        IsAvaloniaDefaultXmlNamespace);
    private static readonly MarkupObjectElementTypeResolutionService MarkupObjectElementTypeResolutionService = new(
        IsAvaloniaDefaultXmlNamespace,
        Xaml2006.NamespaceName);
    private static readonly RuntimeXamlFragmentDetectionService RuntimeXamlFragmentDetectionService = new();
    private static readonly CollectionAddBindingService CollectionAddService = new(
        ResolveTypeToken,
        IsTypeAssignableTo,
        TryGetCollectionElementType,
        TryConvertValueForCollectionAdd,
        Escape);
    private static readonly NameScopeRegistrationSemanticsService NameScopeRegistrationSemanticsService = new(
        IsTypeAssignableTo);
    private static readonly HotDesignArtifactClassificationService HotDesignArtifactClassificationService = new(
        IsTypeAssignableTo);

    private static bool IsMarkupParserLegacyFallbackEnabled()
    {
        var options = ActiveGeneratorOptions.Value;
        return options?.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled == true;
    }

    private static MarkupExpressionParser GetActiveMarkupExpressionParser()
    {
        return IsMarkupParserLegacyFallbackEnabled()
            ? LegacyMarkupExpressionParser
            : StrictMarkupExpressionParser;
    }

    private static readonly ItemContainerTypeMapping[] KnownItemContainerTypeMappings =
    [
        new("Avalonia.Controls.ListBox", "Avalonia.Controls.ListBoxItem"),
        new("Avalonia.Controls.ComboBox", "Avalonia.Controls.ComboBoxItem"),
        new("Avalonia.Controls.Menu", "Avalonia.Controls.MenuItem"),
        new("Avalonia.Controls.MenuItem", "Avalonia.Controls.MenuItem"),
        new("Avalonia.Controls.Primitives.TabStrip", "Avalonia.Controls.Primitives.TabStripItem"),
        new("Avalonia.Controls.TabControl", "Avalonia.Controls.TabItem"),
        new("Avalonia.Controls.TreeView", "Avalonia.Controls.TreeViewItem")
    ];

    private static readonly ImmutableArray<IAvaloniaTransformPass> TransformPasses =
    [
        new BindCustomTransformsPass(),
        new BindRootObjectPass(),
        new BindNamedElementsPass(),
        new BindResourcesPass(),
        new BindTemplatesPass(),
        new BindStylesPass(),
        new BindControlThemesPass(),
        new BindIncludesPass(),
        new FinalizeViewModelPass()
    ];

    public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
        XamlDocumentModel document,
        Compilation compilation,
        GeneratorOptions options,
        XamlTransformConfiguration transformConfiguration)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assemblyName = options.AssemblyName ?? compilation.AssemblyName ?? "UnknownAssembly";
        var uri = "avares://" + assemblyName + "/" + document.TargetPath;
        var previousTransformExtensions = ActiveTransformExtensions.Value;
        var previousGeneratorOptions = ActiveGeneratorOptions.Value;
        var previousTypeResolutionDiagnostics = ActiveTypeResolutionDiagnosticContext.Value;
        var previousTypeSymbolCatalog = ActiveTypeSymbolCatalog.Value;

        try
        {
            ActiveGeneratorOptions.Value = options;
            ActiveTypeResolutionDiagnosticContext.Value = new TypeResolutionDiagnosticContext(
                diagnostics,
                document.FilePath,
                options.StrictMode);
            var typeSymbolCatalog = CompilationTypeSymbolCatalog.Create(compilation, AvaloniaSemanticContractMap);
            ActiveTypeSymbolCatalog.Value = typeSymbolCatalog;
            foreach (var contractDiagnostic in typeSymbolCatalog.Diagnostics)
            {
                diagnostics.Add(new DiagnosticInfo(
                    contractDiagnostic.Code,
                    contractDiagnostic.Message,
                    document.FilePath,
                    1,
                    1,
                    true));
            }
            var context = new BindingTransformContext(
                document,
                compilation,
                options,
                transformConfiguration,
                uri,
                diagnostics);

            ExecuteTransformPasses(context);

            return (context.ViewModel, diagnostics.ToImmutable());
        }
        finally
        {
            ActiveTransformExtensions.Value = previousTransformExtensions;
            ActiveGeneratorOptions.Value = previousGeneratorOptions;
            ActiveTypeResolutionDiagnosticContext.Value = previousTypeResolutionDiagnostics;
            ActiveTypeSymbolCatalog.Value = previousTypeSymbolCatalog;
        }
    }

    private static ITypeSymbolCatalog GetActiveTypeSymbolCatalog(Compilation compilation)
    {
        var catalog = ActiveTypeSymbolCatalog.Value;
        if (catalog is not null &&
            ReferenceEquals(catalog.Compilation, compilation))
        {
            return catalog;
        }

        return CompilationTypeSymbolCatalog.Create(compilation, AvaloniaSemanticContractMap);
    }

    private static INamedTypeSymbol? ResolveContractType(
        Compilation compilation,
        TypeContractId contractId)
    {
        var catalog = GetActiveTypeSymbolCatalog(compilation);
        if (catalog.TryGet(contractId, out var symbol))
        {
            return symbol;
        }

        return null;
    }

    private static void ExecuteTransformPasses(BindingTransformContext context)
    {
        foreach (var pass in TransformPasses)
        {
            if (context.Options.TracePasses)
            {
                var upstream = pass.UpstreamTransformerIds.Length == 0
                    ? "none"
                    : string.Join(", ", pass.UpstreamTransformerIds);
                context.PassExecutionTrace.Add(pass.PassId + " => " + upstream);
            }

            pass.Execute(context);
        }
    }

    private interface IAvaloniaTransformPass
    {
        string PassId { get; }

        ImmutableArray<string> UpstreamTransformerIds { get; }

        void Execute(BindingTransformContext context);
    }

    private sealed class BindingTransformContext
    {
        public BindingTransformContext(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            XamlTransformConfiguration transformConfiguration,
            string buildUri,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics)
        {
            Document = document;
            Compilation = compilation;
            Options = options;
            TransformConfiguration = transformConfiguration;
            BuildUri = buildUri;
            Diagnostics = diagnostics;
            NamedElements = ImmutableArray.CreateBuilder<ResolvedNamedElement>(document.NamedElements.Length);
            CompiledBindings = ImmutableArray.CreateBuilder<ResolvedCompiledBindingDefinition>();
            Resources = ImmutableArray<ResolvedResourceDefinition>.Empty;
            Templates = ImmutableArray<ResolvedTemplateDefinition>.Empty;
            Styles = ImmutableArray<ResolvedStyleDefinition>.Empty;
            ControlThemes = ImmutableArray<ResolvedControlThemeDefinition>.Empty;
            Includes = ImmutableArray<ResolvedIncludeDefinition>.Empty;
        }

        public XamlDocumentModel Document { get; }

        public Compilation Compilation { get; }

        public GeneratorOptions Options { get; }

        public XamlTransformConfiguration TransformConfiguration { get; }

        public string BuildUri { get; }

        public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

        public ImmutableArray<ResolvedNamedElement>.Builder NamedElements { get; }

        public ImmutableArray<ResolvedCompiledBindingDefinition>.Builder CompiledBindings { get; }

        public List<string> PassExecutionTrace { get; } = new();

        public INamedTypeSymbol? ClassSymbol { get; set; }

        public string ClassModifier { get; set; } = "internal";

        public ResolvedObjectNode? RootObject { get; set; }

        public ImmutableArray<ResolvedResourceDefinition> Resources { get; set; }

        public ImmutableArray<ResolvedTemplateDefinition> Templates { get; set; }

        public ImmutableArray<ResolvedStyleDefinition> Styles { get; set; }

        public ImmutableArray<ResolvedControlThemeDefinition> ControlThemes { get; set; }

        public ImmutableArray<ResolvedIncludeDefinition> Includes { get; set; }

        public bool EmitNameScopeRegistration { get; set; }

        public bool EmitStaticResourceResolver { get; set; }

        public ResolvedViewModel? ViewModel { get; set; }

        public ResolvedTransformExtensions TransformExtensions { get; set; } = ResolvedTransformExtensions.Empty;
    }

    private sealed class ResolvedTransformExtensions
    {
        public static ResolvedTransformExtensions Empty { get; } = new(
            ImmutableDictionary<TypeAliasKey, INamedTypeSymbol>.Empty,
            ImmutableArray<ResolvedPropertyAliasRule>.Empty);

        public ResolvedTransformExtensions(
            ImmutableDictionary<TypeAliasKey, INamedTypeSymbol> typeAliases,
            ImmutableArray<ResolvedPropertyAliasRule> propertyAliases)
        {
            TypeAliases = typeAliases;
            PropertyAliases = propertyAliases;
        }

        public ImmutableDictionary<TypeAliasKey, INamedTypeSymbol> TypeAliases { get; }

        public ImmutableArray<ResolvedPropertyAliasRule> PropertyAliases { get; }
    }

    private readonly struct TypeAliasKey : IEquatable<TypeAliasKey>
    {
        public TypeAliasKey(string xmlNamespace, string xamlTypeName)
        {
            XmlNamespace = xmlNamespace;
            XamlTypeName = xamlTypeName;
        }

        public string XmlNamespace { get; }

        public string XamlTypeName { get; }

        public bool Equals(TypeAliasKey other)
        {
            return string.Equals(XmlNamespace, other.XmlNamespace, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(XamlTypeName, other.XamlTypeName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is TypeAliasKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var xmlNamespaceHash = StringComparer.OrdinalIgnoreCase.GetHashCode(XmlNamespace ?? string.Empty);
                var typeNameHash = StringComparer.Ordinal.GetHashCode(XamlTypeName ?? string.Empty);
                return (xmlNamespaceHash * 397) ^ typeNameHash;
            }
        }
    }

    private readonly struct ResolvedPropertyAliasRule
    {
        public ResolvedPropertyAliasRule(
            string targetTypeName,
            INamedTypeSymbol? targetTypeSymbol,
            string xamlPropertyName,
            string? clrPropertyName,
            string? avaloniaPropertyOwnerTypeName,
            INamedTypeSymbol? avaloniaPropertyOwnerType,
            string? avaloniaPropertyFieldName,
            string source,
            int line,
            int column)
        {
            TargetTypeName = targetTypeName;
            TargetTypeSymbol = targetTypeSymbol;
            XamlPropertyName = xamlPropertyName;
            ClrPropertyName = clrPropertyName;
            AvaloniaPropertyOwnerTypeName = avaloniaPropertyOwnerTypeName;
            AvaloniaPropertyOwnerType = avaloniaPropertyOwnerType;
            AvaloniaPropertyFieldName = avaloniaPropertyFieldName;
            Source = source;
            Line = line;
            Column = column;
        }

        public string TargetTypeName { get; }

        public INamedTypeSymbol? TargetTypeSymbol { get; }

        public string XamlPropertyName { get; }

        public string? ClrPropertyName { get; }

        public string? AvaloniaPropertyOwnerTypeName { get; }

        public INamedTypeSymbol? AvaloniaPropertyOwnerType { get; }

        public string? AvaloniaPropertyFieldName { get; }

        public string Source { get; }

        public int Line { get; }

        public int Column { get; }
    }

    private readonly struct PropertyAliasResolution
    {
        public PropertyAliasResolution(
            string resolvedPropertyName,
            INamedTypeSymbol? avaloniaPropertyOwnerType,
            string? avaloniaPropertyFieldName)
        {
            ResolvedPropertyName = resolvedPropertyName;
            AvaloniaPropertyOwnerType = avaloniaPropertyOwnerType;
            AvaloniaPropertyFieldName = avaloniaPropertyFieldName;
        }

        public string ResolvedPropertyName { get; }

        public INamedTypeSymbol? AvaloniaPropertyOwnerType { get; }

        public string? AvaloniaPropertyFieldName { get; }

        public bool HasAvaloniaPropertyAlias => AvaloniaPropertyOwnerType is not null;
    }
}
