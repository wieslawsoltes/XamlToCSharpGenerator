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
    private static readonly MarkupExpressionParser CanonicalMarkupExpressionParser =
        new(new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));

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
    private static readonly CSharpExpressionClassificationService ExpressionClassificationService = new(
        CanonicalMarkupExpressionParser,
        KnownMarkupExtensionNames,
        TryResolveMarkupExtensionType);
    private static readonly XamlTypeExpressionResolutionService TypeExpressionResolutionService = new(
        CanonicalMarkupExpressionParser,
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
    private static readonly RuntimeXamlFragmentDetectionService RuntimeXamlFragmentDetectionService = new();
    private static readonly CollectionAddBindingService CollectionAddService = new(
        ResolveTypeToken,
        IsTypeAssignableTo,
        TryGetCollectionElementType,
        TryConvertValueForCollectionAdd,
        Escape);
    private static readonly HotDesignArtifactClassificationService HotDesignArtifactClassificationService = new(
        IsTypeAssignableTo);

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

        try
        {
            ActiveGeneratorOptions.Value = options;
            ActiveTypeResolutionDiagnosticContext.Value = new TypeResolutionDiagnosticContext(
                diagnostics,
                document.FilePath,
                options.StrictMode);
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
        }
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
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(XmlNamespace),
                StringComparer.Ordinal.GetHashCode(XamlTypeName));
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

    private static ResolvedTransformExtensions BuildResolvedTransformExtensions(
        Compilation compilation,
        XamlTransformConfiguration configuration,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var typeAliases = new Dictionary<TypeAliasKey, INamedTypeSymbol>();
        var propertyAliases = new Dictionary<string, ResolvedPropertyAliasRule>(StringComparer.OrdinalIgnoreCase);
        var inputTypeAliases = EnumerateTypeAliases(configuration, compilation);
        var inputPropertyAliases = EnumeratePropertyAliases(configuration, compilation);

        foreach (var typeAlias in inputTypeAliases)
        {
            if (string.IsNullOrWhiteSpace(typeAlias.XmlNamespace) ||
                string.IsNullOrWhiteSpace(typeAlias.XamlTypeName) ||
                string.IsNullOrWhiteSpace(typeAlias.ClrTypeName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Type alias entries require non-empty XML namespace, XAML type name, and CLR type name.",
                    typeAlias.Source,
                    typeAlias.Line,
                    typeAlias.Column,
                    options.StrictMode));
                continue;
            }

            var clrTypeName = NormalizeMetadataTypeName(typeAlias.ClrTypeName);
            var clrType = compilation.GetTypeByMetadataName(clrTypeName);
            if (clrType is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0902",
                    $"Type alias '{typeAlias.XmlNamespace}:{typeAlias.XamlTypeName}' targets unknown CLR type '{typeAlias.ClrTypeName}'.",
                    typeAlias.Source,
                    typeAlias.Line,
                    typeAlias.Column,
                    options.StrictMode));
                continue;
            }

            var key = new TypeAliasKey(typeAlias.XmlNamespace.Trim(), typeAlias.XamlTypeName.Trim());
            if (typeAliases.TryGetValue(key, out var existing))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0903",
                    $"Type alias '{typeAlias.XmlNamespace}:{typeAlias.XamlTypeName}' overrides previous mapping to '{existing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    typeAlias.Source,
                    typeAlias.Line,
                    typeAlias.Column,
                    false));
            }

            typeAliases[key] = clrType;
        }

        foreach (var propertyAlias in inputPropertyAliases)
        {
            if (string.IsNullOrWhiteSpace(propertyAlias.XamlPropertyName) ||
                string.IsNullOrWhiteSpace(propertyAlias.TargetTypeName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Property alias entries require non-empty target type and XAML property token.",
                    propertyAlias.Source,
                    propertyAlias.Line,
                    propertyAlias.Column,
                    options.StrictMode));
                continue;
            }

            var targetTypeToken = propertyAlias.TargetTypeName.Trim();
            var targetTypeIsWildcard = targetTypeToken == "*";
            INamedTypeSymbol? targetTypeSymbol = null;
            if (!targetTypeIsWildcard)
            {
                targetTypeSymbol = compilation.GetTypeByMetadataName(NormalizeMetadataTypeName(targetTypeToken));
                if (targetTypeSymbol is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0902",
                        $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' targets unknown CLR type '{propertyAlias.TargetTypeName}'.",
                        propertyAlias.Source,
                        propertyAlias.Line,
                        propertyAlias.Column,
                        options.StrictMode));
                    continue;
                }
            }

            INamedTypeSymbol? avaloniaOwnerTypeSymbol = null;
            string? avaloniaOwnerTypeName = null;
            var avaloniaPropertyOwnerTypeFromPayload =
                propertyAlias.GetFrameworkPropertyOwnerTypeName(FrameworkProfileIds.Avalonia);
            var avaloniaPropertyFieldFromPayload =
                propertyAlias.GetFrameworkPropertyFieldName(FrameworkProfileIds.Avalonia);
            if (!string.IsNullOrWhiteSpace(avaloniaPropertyOwnerTypeFromPayload))
            {
                avaloniaOwnerTypeName = NormalizeMetadataTypeName(avaloniaPropertyOwnerTypeFromPayload!);
                avaloniaOwnerTypeSymbol = compilation.GetTypeByMetadataName(avaloniaOwnerTypeName);
                if (avaloniaOwnerTypeSymbol is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0902",
                        $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' targets unknown Avalonia property owner type '{avaloniaPropertyOwnerTypeFromPayload}'.",
                        propertyAlias.Source,
                        propertyAlias.Line,
                        propertyAlias.Column,
                        options.StrictMode));
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(propertyAlias.ClrPropertyName) &&
                (avaloniaOwnerTypeSymbol is null ||
                 string.IsNullOrWhiteSpace(avaloniaPropertyFieldFromPayload)))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' must declare either CLR property mapping or Avalonia owner+field mapping.",
                    propertyAlias.Source,
                    propertyAlias.Line,
                    propertyAlias.Column,
                    options.StrictMode));
                continue;
            }

            var key = BuildPropertyAliasLookupKey(targetTypeToken, propertyAlias.XamlPropertyName);
            var resolvedAlias = new ResolvedPropertyAliasRule(
                targetTypeToken,
                targetTypeSymbol,
                propertyAlias.XamlPropertyName.Trim(),
                string.IsNullOrWhiteSpace(propertyAlias.ClrPropertyName)
                    ? null
                    : NormalizePropertyName(propertyAlias.ClrPropertyName),
                avaloniaOwnerTypeName,
                avaloniaOwnerTypeSymbol,
                avaloniaPropertyFieldFromPayload,
                propertyAlias.Source,
                propertyAlias.Line,
                propertyAlias.Column);

            if (propertyAliases.ContainsKey(key))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0903",
                    $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' overrides an earlier mapping.",
                    propertyAlias.Source,
                    propertyAlias.Line,
                    propertyAlias.Column,
                    false));
            }

            propertyAliases[key] = resolvedAlias;
        }

        return new ResolvedTransformExtensions(
            typeAliases.ToImmutableDictionary(),
            propertyAliases.Values.ToImmutableArray());
    }

    private static IEnumerable<XamlTypeAliasRule> EnumerateTypeAliases(
        XamlTransformConfiguration configuration,
        Compilation compilation)
    {
        foreach (var alias in configuration.TypeAliases)
        {
            yield return alias;
        }

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsSourceGenTypeAliasAttribute(attribute.AttributeClass))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 3 ||
                    attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                    attribute.ConstructorArguments[1].Value is not string xamlTypeName ||
                    attribute.ConstructorArguments[2].Value is not string clrTypeName)
                {
                    continue;
                }

                var (source, line, column) = ResolveAttributeLocation(attribute);
                yield return new XamlTypeAliasRule(
                    xmlNamespace,
                    xamlTypeName,
                    clrTypeName,
                    source,
                    line,
                    column);
            }
        }
    }

    private static IEnumerable<XamlPropertyAliasRule> EnumeratePropertyAliases(
        XamlTransformConfiguration configuration,
        Compilation compilation)
    {
        foreach (var alias in configuration.PropertyAliases)
        {
            yield return alias;
        }

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (IsSourceGenPropertyAliasAttribute(attribute.AttributeClass))
                {
                    if (attribute.ConstructorArguments.Length < 3 ||
                        attribute.ConstructorArguments[0].Value is not string targetTypeName ||
                        attribute.ConstructorArguments[1].Value is not string xamlPropertyName ||
                        attribute.ConstructorArguments[2].Value is not string clrPropertyName)
                    {
                        continue;
                    }

                    var (source, line, column) = ResolveAttributeLocation(attribute);
                    yield return new XamlPropertyAliasRule(
                        targetTypeName,
                        xamlPropertyName,
                        clrPropertyName,
                        null,
                        null,
                        source,
                        line,
                        column);
                }
                else if (IsSourceGenAvaloniaPropertyAliasAttribute(attribute.AttributeClass))
                {
                    if (attribute.ConstructorArguments.Length < 4 ||
                        attribute.ConstructorArguments[0].Value is not string targetTypeName ||
                        attribute.ConstructorArguments[1].Value is not string xamlPropertyName ||
                        attribute.ConstructorArguments[2].Value is not string ownerTypeName ||
                        attribute.ConstructorArguments[3].Value is not string fieldName)
                    {
                        continue;
                    }

                    var (source, line, column) = ResolveAttributeLocation(attribute);
                    yield return new XamlPropertyAliasRule(
                        targetTypeName,
                        xamlPropertyName,
                        null,
                        ownerTypeName,
                        fieldName,
                        source,
                        line,
                        column);
                }
            }
        }
    }

    private static (string Source, int Line, int Column) ResolveAttributeLocation(AttributeData attribute)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
        if (location is null)
        {
            return ("<assembly>", 1, 1);
        }

        var lineSpan = location.GetLineSpan();
        return (
            lineSpan.Path ?? "<assembly>",
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    private static string NormalizeMetadataTypeName(string typeName)
    {
        return XamlTypeTokenSemantics.TrimGlobalQualifier(typeName);
    }

    private static bool IsSourceGenTypeAliasAttribute(INamedTypeSymbol? attributeType)
    {
        return string.Equals(
            attributeType?.ToDisplayString(),
            SourceGenXamlTypeAliasAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static bool IsSourceGenPropertyAliasAttribute(INamedTypeSymbol? attributeType)
    {
        return string.Equals(
            attributeType?.ToDisplayString(),
            SourceGenXamlPropertyAliasAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static bool IsSourceGenAvaloniaPropertyAliasAttribute(INamedTypeSymbol? attributeType)
    {
        return string.Equals(
            attributeType?.ToDisplayString(),
            SourceGenXamlAvaloniaPropertyAliasAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static string BuildPropertyAliasLookupKey(string targetTypeName, string xamlPropertyName)
    {
        return targetTypeName.Trim() + "|" + xamlPropertyName.Trim();
    }

    private static PropertyAliasResolution ResolvePropertyAlias(
        INamedTypeSymbol? targetType,
        string propertyToken)
    {
        var normalizedPropertyName = NormalizePropertyName(propertyToken);
        var extensions = ActiveTransformExtensions.Value;
        if (targetType is null ||
            extensions is null ||
            extensions.PropertyAliases.IsDefaultOrEmpty)
        {
            return new PropertyAliasResolution(normalizedPropertyName, null, null);
        }

        ResolvedPropertyAliasRule? matchedRule = null;
        var matchedScore = int.MinValue;
        foreach (var rule in extensions.PropertyAliases)
        {
            var targetMatchScore = GetPropertyAliasTargetMatchScore(rule, targetType);
            if (targetMatchScore < 0)
            {
                continue;
            }

            var tokenMatchScore = GetPropertyAliasTokenMatchScore(rule.XamlPropertyName, propertyToken, normalizedPropertyName);
            if (tokenMatchScore < 0)
            {
                continue;
            }

            var score = (targetMatchScore * 10) + tokenMatchScore;
            if (score <= matchedScore)
            {
                continue;
            }

            matchedScore = score;
            matchedRule = rule;
        }

        if (matchedRule is null)
        {
            return new PropertyAliasResolution(normalizedPropertyName, null, null);
        }

        var ruleValue = matchedRule.Value;
        if (!string.IsNullOrWhiteSpace(ruleValue.ClrPropertyName))
        {
            return new PropertyAliasResolution(
                NormalizePropertyName(ruleValue.ClrPropertyName!),
                null,
                null);
        }

        var avaloniaPropertyName = !string.IsNullOrWhiteSpace(ruleValue.AvaloniaPropertyFieldName)
            ? PropertyNameFromField(ruleValue.AvaloniaPropertyFieldName!)
            : normalizedPropertyName;
        return new PropertyAliasResolution(
            avaloniaPropertyName,
            ruleValue.AvaloniaPropertyOwnerType,
            ruleValue.AvaloniaPropertyFieldName);
    }

    private static int GetPropertyAliasTargetMatchScore(ResolvedPropertyAliasRule rule, INamedTypeSymbol targetType)
    {
        if (rule.TargetTypeName == "*")
        {
            return 1;
        }

        if (rule.TargetTypeSymbol is null)
        {
            return -1;
        }

        if (SymbolEqualityComparer.Default.Equals(targetType, rule.TargetTypeSymbol))
        {
            return 3;
        }

        return IsTypeAssignableTo(targetType, rule.TargetTypeSymbol)
            ? 2
            : -1;
    }

    private static int GetPropertyAliasTokenMatchScore(
        string configuredToken,
        string authoredToken,
        string normalizedAuthoredToken)
    {
        if (configuredToken.Equals(authoredToken, StringComparison.Ordinal))
        {
            return 3;
        }

        if (configuredToken.Equals(authoredToken, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        var normalizedConfigured = NormalizePropertyName(configuredToken);
        if (normalizedConfigured.Equals(normalizedAuthoredToken, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return -1;
    }

    private static string PropertyNameFromField(string fieldName)
    {
        return XamlTokenSplitSemantics.TrimTerminalSuffix(fieldName, "Property");
    }

    private sealed class BindCustomTransformsPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P000-BindCustomTransforms";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "TransformerConfiguration",
            "Avalonia.Metadata.XmlnsDefinitionAttribute"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.TransformExtensions = BuildResolvedTransformExtensions(
                context.Compilation,
                context.TransformConfiguration,
                context.Diagnostics,
                context.Options);
            ActiveTransformExtensions.Value = context.TransformExtensions;
        }
    }

    private sealed class BindNamedElementsPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P001-BindNamedElements";

        public ImmutableArray<string> UpstreamTransformerIds => ["AXSG-P010-BindRootObject", "XNameTransformer"];

        public void Execute(BindingTransformContext context)
        {
            context.NamedElements.Clear();

            if (context.RootObject is null)
            {
                return;
            }

            var fieldModifierLookup = BuildNamedFieldModifierLookup(context.Document.NamedElements);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            CollectResolvedNamedElements(
                context.RootObject,
                context.NamedElements,
                fieldModifierLookup,
                seenNames);
        }
    }

    private static Dictionary<string, string> BuildNamedFieldModifierLookup(
        ImmutableArray<XamlNamedElement> namedElements)
    {
        var fieldModifierLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var namedElement in namedElements)
        {
            if (string.IsNullOrWhiteSpace(namedElement.Name) ||
                fieldModifierLookup.ContainsKey(namedElement.Name))
            {
                continue;
            }

            fieldModifierLookup[namedElement.Name] = string.IsNullOrWhiteSpace(namedElement.FieldModifier)
                ? "internal"
                : namedElement.FieldModifier!;
        }

        return fieldModifierLookup;
    }

    private static void CollectResolvedNamedElements(
        ResolvedObjectNode node,
        ImmutableArray<ResolvedNamedElement>.Builder namedElements,
        IReadOnlyDictionary<string, string> fieldModifierLookup,
        HashSet<string> seenNames)
    {
        if (!string.IsNullOrWhiteSpace(node.Name) &&
            seenNames.Add(node.Name!))
        {
            var fieldModifier = fieldModifierLookup.TryGetValue(node.Name!, out var requestedModifier) &&
                                !string.IsNullOrWhiteSpace(requestedModifier)
                ? requestedModifier
                : "internal";
            namedElements.Add(new ResolvedNamedElement(
                Name: node.Name!,
                TypeName: node.TypeName,
                FieldModifier: fieldModifier,
                Line: node.Line,
                Column: node.Column));
        }

        foreach (var child in node.Children)
        {
            CollectResolvedNamedElements(child, namedElements, fieldModifierLookup, seenNames);
        }

        foreach (var propertyElementAssignment in node.PropertyElementAssignments)
        {
            foreach (var objectValue in propertyElementAssignment.ObjectValues)
            {
                CollectResolvedNamedElements(objectValue, namedElements, fieldModifierLookup, seenNames);
            }
        }
    }

    private sealed class BindRootObjectPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P010-BindRootObject";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlClassesTransformer",
            "AvaloniaXamlIlResolveClassesPropertiesTransformer",
            "AvaloniaXamlIlAvaloniaPropertyResolver",
            "AvaloniaXamlIlTransformInstanceAttachedProperties",
            "AvaloniaXamlIlTransformRoutedEvent"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.ClassSymbol = context.Document.IsClassBacked
                ? context.Compilation.GetTypeByMetadataName(context.Document.ClassFullName!)
                : null;
            context.ClassModifier = ResolveGeneratedClassModifier(
                context.Document,
                context.ClassSymbol,
                context.Diagnostics,
                context.Options);

            var rootDataType = ResolveTypeFromTypeExpression(
                context.Compilation,
                context.Document,
                context.Document.RootObject.DataType,
                context.Document.ClassNamespace);

            var rootCompileBindings = context.Document.RootObject.CompileBindings ?? context.Options.UseCompiledBindingsByDefault;

            context.RootObject = BindObjectNode(
                node: context.Document.RootObject,
                compilation: context.Compilation,
                diagnostics: context.Diagnostics,
                document: context.Document,
                options: context.Options,
                compiledBindings: context.CompiledBindings,
                inheritedCompileBindingsEnabled: rootCompileBindings,
                inheritedDataType: rootDataType,
                inheritedSetterTargetType: null,
                inheritedBindingPriorityScope: BindingPriorityScope.None,
                forcedType: context.ClassSymbol,
                rootTypeSymbol: context.ClassSymbol);
        }
    }

    private sealed class BindResourcesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P020-BindResources";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlResourceTransformer",
            "AvaloniaXamlIlEnsureResourceDictionaryCapacityTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Resources = BindResources(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options);
        }
    }

    private sealed class BindTemplatesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P030-BindTemplates";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlControlTemplateTargetTypeMetadataTransformer",
            "AvaloniaXamlIlControlTemplatePartsChecker",
            "AvaloniaXamlIlControlTemplatePriorityTransformer",
            "AvaloniaXamlIlDataTemplateWarningsTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Templates = BindTemplates(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options);
        }
    }

    private sealed class BindStylesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P040-BindStyles";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlSelectorTransformer",
            "AvaloniaXamlIlSetterTransformer",
            "AvaloniaXamlIlSetterTargetTypeMetadataTransformer",
            "AvaloniaXamlIlDuplicateSettersChecker",
            "AvaloniaXamlIlStyleValidatorTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Styles = BindStyles(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options,
                context.CompiledBindings);
        }
    }

    private sealed class BindControlThemesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P050-BindControlThemes";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlControlThemeTransformer",
            "AvaloniaXamlIlSetterTransformer",
            "AvaloniaXamlIlSetterTargetTypeMetadataTransformer",
            "AvaloniaXamlIlDuplicateSettersChecker"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.ControlThemes = BindControlThemes(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options,
                context.CompiledBindings);
        }
    }

    private sealed class BindIncludesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P060-BindIncludes";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIncludeTransformer",
            "XamlMergeResourceGroupTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Includes = BindIncludes(
                context.Document,
                context.Compilation,
                context.BuildUri,
                context.Diagnostics,
                context.Options);
        }
    }

    private sealed class FinalizeViewModelPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P900-Finalize";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AddNameScopeRegistration",
            "AvaloniaXamlIlRootObjectScope",
            "AvaloniaXamlIlAddSourceInfoTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            var root = context.RootObject ?? BindObjectNode(
                node: context.Document.RootObject,
                compilation: context.Compilation,
                diagnostics: context.Diagnostics,
                document: context.Document,
                options: context.Options,
                compiledBindings: context.CompiledBindings,
                inheritedCompileBindingsEnabled: context.Document.RootObject.CompileBindings ?? context.Options.UseCompiledBindingsByDefault,
                inheritedDataType: ResolveTypeFromTypeExpression(
                    context.Compilation,
                    context.Document,
                    context.Document.RootObject.DataType,
                    context.Document.ClassNamespace),
                inheritedSetterTargetType: null,
                inheritedBindingPriorityScope: BindingPriorityScope.None,
                forcedType: context.ClassSymbol,
                rootTypeSymbol: context.ClassSymbol);

            context.EmitNameScopeRegistration = context.Compilation.GetTypeByMetadataName("Avalonia.Controls.NameScope") is not null &&
                                                context.Compilation.GetTypeByMetadataName("Avalonia.StyledElement") is not null &&
                                                context.NamedElements.Count > 0;
            context.EmitStaticResourceResolver = RequiresStaticResourceResolver(
                root,
                context.Styles,
                context.ControlThemes);
            var hotDesignClassification = HotDesignArtifactClassificationService.Classify(
                context.Compilation,
                context.Document,
                context.ClassSymbol,
                context.Styles,
                context.ControlThemes,
                context.Templates);

            context.ViewModel = new ResolvedViewModel(
                Document: context.Document,
                BuildUri: context.BuildUri,
                ClassModifier: context.ClassModifier,
                CreateSourceInfo: context.Options.CreateSourceInfo,
                EnableHotReload: context.Options.HotReloadEnabled,
                EnableHotDesign: context.Options.HotDesignEnabled,
                PassExecutionTrace: context.Options.TracePasses
                    ? context.PassExecutionTrace.ToImmutableArray()
                    : ImmutableArray<string>.Empty,
                EmitNameScopeRegistration: context.EmitNameScopeRegistration,
                EmitStaticResourceResolver: context.EmitStaticResourceResolver,
                RootObject: root,
                NamedElements: context.NamedElements.ToImmutable(),
                Resources: context.Resources,
                Templates: context.Templates,
                CompiledBindings: context.CompiledBindings.ToImmutable(),
                Styles: context.Styles,
                ControlThemes: context.ControlThemes,
                Includes: context.Includes,
                HotDesignArtifactKind: hotDesignClassification.Kind,
                HotDesignScopeHints: hotDesignClassification.ScopeHints);
        }
    }

    private static string ResolveGeneratedClassModifier(
        XamlDocumentModel document,
        INamedTypeSymbol? classSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var normalizedRequested = NormalizeClassModifier(document.ClassModifier);
        if (!string.IsNullOrWhiteSpace(document.ClassModifier) && normalizedRequested is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0104",
                $"x:ClassModifier value '{document.ClassModifier}' is not supported.",
                document.FilePath,
                1,
                1,
                options.StrictMode));
        }

        if (classSymbol is not null)
        {
            var symbolModifier = ToCSharpClassModifier(classSymbol.DeclaredAccessibility);
            if (normalizedRequested is not null &&
                !normalizedRequested.Equals(symbolModifier, StringComparison.Ordinal))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0105",
                    $"x:ClassModifier '{normalizedRequested}' does not match class declaration accessibility '{symbolModifier}'.",
                    document.FilePath,
                    1,
                    1,
                    options.StrictMode));
            }

            return symbolModifier;
        }

        return normalizedRequested ?? "internal";
    }

    private static INamedTypeSymbol? ResolveCurrentSetterTargetType(
        INamedTypeSymbol? nodeType,
        XamlObjectNode node,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? inheritedSetterTargetType)
    {
        if (nodeType is null)
        {
            return inheritedSetterTargetType;
        }

        if (IsControlTemplateType(nodeType, compilation))
        {
            var targetTypeValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("TargetType", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetTypeValue))
            {
                var targetType = ResolveTypeFromTypeExpression(compilation, document, targetTypeValue, document.ClassNamespace);
                if (targetType is not null)
                {
                    return targetType;
                }
            }

            return inheritedSetterTargetType;
        }

        if (IsControlThemeType(nodeType, compilation))
        {
            var targetTypeValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("TargetType", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetTypeValue))
            {
                var targetType = ResolveTypeFromTypeExpression(compilation, document, targetTypeValue, document.ClassNamespace);
                if (targetType is not null)
                {
                    return targetType;
                }
            }

            return inheritedSetterTargetType;
        }

        if (IsStyleType(nodeType, compilation))
        {
            var selectorValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("Selector", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(selectorValue))
            {
                var selectorValidation = SelectorSyntaxValidator.Validate(selectorValue!);
                if (selectorValidation.IsValid)
                {
                    var selectorTargetType = AvaloniaSelectorSemanticAdapter.TryResolveSelectorTargetType(
                        selectorValidation.Branches,
                        typeToken => ResolveTypeToken(compilation, document, typeToken, document.ClassNamespace),
                        IsTypeAssignableTo,
                        out _,
                        out _);
                    if (selectorTargetType is not null)
                    {
                        return selectorTargetType;
                    }
                }
            }

            return inheritedSetterTargetType;
        }

        return inheritedSetterTargetType;
    }

    private static INamedTypeSymbol? ResolveObjectTypeSymbol(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node)
    {
        if (node.TypeArguments.IsDefaultOrEmpty)
        {
            return ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
        }

        var resolvedTypeArguments = new List<ITypeSymbol>(node.TypeArguments.Length);
        foreach (var argumentToken in node.TypeArguments)
        {
            var resolvedTypeArgument = ResolveTypeToken(compilation, document, argumentToken, document.ClassNamespace);
            if (resolvedTypeArgument is null)
            {
                return ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
            }

            resolvedTypeArguments.Add(resolvedTypeArgument);
        }

        var genericType = ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName, node.TypeArguments.Length) ??
                          ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
        if (genericType is null)
        {
            return null;
        }

        if (genericType.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return genericType.Construct(resolvedTypeArguments.ToArray());
        }

        if (genericType.OriginalDefinition.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return genericType.OriginalDefinition.Construct(resolvedTypeArguments.ToArray());
        }

        return genericType;
    }

    private static bool IsStyleType(INamedTypeSymbol type, Compilation compilation)
    {
        var styleType = compilation.GetTypeByMetadataName("Avalonia.Styling.Style");
        return styleType is not null && IsTypeAssignableTo(type, styleType);
    }

    private static bool IsControlThemeType(INamedTypeSymbol type, Compilation compilation)
    {
        var controlThemeType = compilation.GetTypeByMetadataName("Avalonia.Styling.ControlTheme");
        return controlThemeType is not null && IsTypeAssignableTo(type, controlThemeType);
    }

    private static bool IsControlTemplateType(INamedTypeSymbol type, Compilation compilation)
    {
        var markupControlTemplateType = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.Templates.ControlTemplate");
        if (markupControlTemplateType is not null && IsTypeAssignableTo(type, markupControlTemplateType))
        {
            return true;
        }

        var controlsControlTemplateType = compilation.GetTypeByMetadataName("Avalonia.Controls.Templates.ControlTemplate");
        if (controlsControlTemplateType is not null && IsTypeAssignableTo(type, controlsControlTemplateType))
        {
            return true;
        }

        var iControlTemplate = compilation.GetTypeByMetadataName("Avalonia.Controls.Templates.IControlTemplate");
        return iControlTemplate is not null && IsTypeAssignableTo(type, iControlTemplate);
    }

    private static bool IsTemplateScopeType(INamedTypeSymbol type, Compilation compilation)
    {
        if (IsControlTemplateType(type, compilation))
        {
            return true;
        }

        var itemsPanelTemplateType = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.Templates.ItemsPanelTemplate");
        if (itemsPanelTemplateType is not null && IsTypeAssignableTo(type, itemsPanelTemplateType))
        {
            return true;
        }

        var templateType = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.Templates.Template");
        return templateType is not null && IsTypeAssignableTo(type, templateType);
    }

    private static ResolvedObjectNode BindObjectNode(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? forcedType = null,
        INamedTypeSymbol? rootTypeSymbol = null)
    {
        var symbol = forcedType ?? ResolveObjectTypeSymbol(compilation, document, node);
        var typeName = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";

        if (IsXamlArrayNode(node))
        {
            return BindXamlArrayNode(
                node,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol);
        }

        var compileBindingsEnabled = node.CompileBindings ?? inheritedCompileBindingsEnabled;
        var nodeDataType = ResolveTypeFromTypeExpression(compilation, document, node.DataType, document.ClassNamespace) ?? inheritedDataType;
        var currentSetterTargetType = ResolveCurrentSetterTargetType(
            symbol,
            node,
            compilation,
            document,
            inheritedSetterTargetType);
        var currentBindingPriorityScope = ResolveCurrentBindingPriorityScope(
            symbol,
            compilation,
            inheritedBindingPriorityScope);
        var contentPropertyName = FindContentPropertyName(symbol);
        var inferredSetterValueType = TryResolveSetterValueType(
            symbol,
            node.PropertyAssignments,
            compilation,
            document,
            currentSetterTargetType);

        var assignments = ImmutableArray.CreateBuilder<ResolvedPropertyAssignment>();
        var propertyElementAssignments = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>();
        var eventSubscriptions = ImmutableArray.CreateBuilder<ResolvedEventSubscription>();
        foreach (var assignment in node.PropertyAssignments)
        {
            if (ShouldSkipConditionalBranch(
                    assignment.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            if (symbol is null)
            {
                continue;
            }

            if (IsDesignTimePropertyToken(assignment.PropertyName))
            {
                continue;
            }

            var propertyAlias = ResolvePropertyAlias(symbol, assignment.PropertyName);

            if (assignment.IsAttached || propertyAlias.HasAvaloniaPropertyAlias)
            {
                if (TryBindAttachedPropertyAssignment(
                        assignment,
                        symbol,
                        typeName,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol,
                        explicitOwnerType: propertyAlias.AvaloniaPropertyOwnerType,
                        explicitPropertyName: propertyAlias.ResolvedPropertyName,
                        explicitPropertyFieldName: propertyAlias.AvaloniaPropertyFieldName,
                        out var attachedAssignment))
                {
                    if (attachedAssignment is not null)
                    {
                        assignments.Add(attachedAssignment);
                    }

                    continue;
                }

                if (TryBindAttachedStaticSetterAssignment(
                        assignment,
                        symbol,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var staticSetterAssignment))
                {
                    if (staticSetterAssignment is not null)
                    {
                        assignments.Add(staticSetterAssignment);
                    }

                    continue;
                }

                if (TryBindAttachedClassPropertyAssignment(
                        assignment,
                        symbol,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var classPropertyAssignment))
                {
                    if (classPropertyAssignment is not null)
                    {
                        assignments.Add(classPropertyAssignment);
                    }

                    continue;
                }

                if (TryBindAttachedEventSubscription(
                        assignment,
                        compilation,
                        nodeDataType,
                        rootTypeSymbol,
                        diagnostics,
                        document,
                        options,
                        out var attachedEventSubscription))
                {
                    if (attachedEventSubscription is not null)
                    {
                        eventSubscriptions.Add(attachedEventSubscription);
                    }

                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Attached property '{assignment.PropertyName}' could not be resolved on this scope.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                continue;
            }

            var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
            var property = FindProperty(symbol, normalizedPropertyName);
            if (property is not null &&
                property.SetMethod is null &&
                TryBindCollectionLiteralPropertyAssignment(
                    symbol,
                    property,
                    assignment,
                    compilation,
                    out var collectionLiteralAssignment))
            {
                if (collectionLiteralAssignment is not null)
                {
                    propertyElementAssignments.Add(collectionLiteralAssignment);
                }

                continue;
            }

            if (property is not null && property.SetMethod is not null)
            {
                if (TryParseBindingMarkup(assignment.Value, out var bindingMarkup))
                {
                    if (TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode))
                    {
                        continue;
                    }

                    var shouldCompileBinding = CanUseCompiledBinding(bindingMarkup) &&
                                               (bindingMarkup.IsCompiledBinding || compileBindingsEnabled);
                    if (shouldCompileBinding)
                    {
                        if (nodeDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for '{property.Name}' requires x:DataType in scope.",
                                document.FilePath,
                                assignment.Line,
                                assignment.Column,
                                options.StrictMode));
                            continue;
                        }

                        if (!TryBuildCompiledBindingAccessorExpression(
                                compilation,
                                document,
                                nodeDataType,
                                bindingMarkup.Path,
                                out var accessorExpression,
                                out var normalizedPath,
                                out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{nodeDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                assignment.Line,
                                assignment.Column,
                                options.StrictMode));
                            continue;
                        }

                        compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                            TargetTypeName: typeName,
                            TargetPropertyName: property.Name,
                            Path: normalizedPath,
                            SourceTypeName: nodeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            AccessorExpression: accessorExpression,
                            IsSetterBinding: false,
                            Line: assignment.Line,
                            Column: assignment.Column));
                    }

                    if (TryBindAvaloniaPropertyAssignment(
                            symbol,
                            typeName,
                            normalizedPropertyName,
                            assignment,
                            compilation,
                            document,
                            options,
                            diagnostics,
                            compiledBindings,
                            compileBindingsEnabled,
                            nodeDataType,
                            property.Type,
                            currentBindingPriorityScope,
                            currentSetterTargetType,
                            rootTypeSymbol,
                            out var bindingAssignment,
                            allowCompiledBindingRegistration: false))
                    {
                        if (bindingAssignment is not null)
                        {
                            assignments.Add(bindingAssignment);
                        }

                        continue;
                    }

                    if (TryBuildBindingValueExpression(
                            compilation,
                            document,
                            bindingMarkup,
                            property.Type,
                            currentSetterTargetType,
                            currentBindingPriorityScope,
                            out var runtimeBindingExpression))
                    {
                        assignments.Add(new ResolvedPropertyAssignment(
                            PropertyName: property.Name,
                            ValueExpression: runtimeBindingExpression,
                            AvaloniaPropertyOwnerTypeName: null,
                            AvaloniaPropertyFieldName: null,
                            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            BindingPriorityExpression: null,
                            Line: assignment.Line,
                            Column: assignment.Column,
                            Condition: assignment.Condition,
                            ValueKind: ResolvedValueKind.Binding,
                            ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true)));
                        continue;
                    }

                    if (shouldCompileBinding)
                    {
                        continue;
                    }
                }

                if (currentBindingPriorityScope == BindingPriorityScope.Template &&
                    TryBindAvaloniaPropertyAssignment(
                        symbol,
                        typeName,
                        normalizedPropertyName,
                        assignment,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        property.Type,
                        currentBindingPriorityScope,
                        currentSetterTargetType,
                        rootTypeSymbol,
                        out var templatePriorityAssignment,
                        allowCompiledBindingRegistration: false))
                {
                    if (templatePriorityAssignment is not null)
                    {
                        assignments.Add(templatePriorityAssignment);
                    }

                    continue;
                }

                if (TryParseMarkupExtension(assignment.Value, out _) &&
                    TryBindAvaloniaPropertyAssignment(
                        symbol,
                        typeName,
                        normalizedPropertyName,
                        assignment,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        property.Type,
                        currentBindingPriorityScope,
                        currentSetterTargetType,
                        rootTypeSymbol,
                        out var markupExtensionAssignment,
                        allowCompiledBindingRegistration: false))
                {
                    if (markupExtensionAssignment is not null)
                    {
                        assignments.Add(markupExtensionAssignment);
                    }

                    continue;
                }

                var isSetterValueProperty = property.Name.Equals("Value", StringComparison.Ordinal) &&
                                            IsSetterType(symbol);
                var conversionTargetType = property.Type;
                if (isSetterValueProperty &&
                    inferredSetterValueType is not null)
                {
                    if (conversionTargetType.SpecialType == SpecialType.System_Object)
                    {
                        conversionTargetType = inferredSetterValueType;
                    }
                }

                var valueExpression = string.Empty;
                var valueKind = ResolvedValueKind.Literal;
                var requiresStaticResourceResolver = false;
                var valueRequirements = ResolvedValueRequirements.None;
                if (isSetterValueProperty &&
                    TryBuildRuntimeXamlFragmentExpression(
                        assignment.Value,
                        conversionTargetType,
                        document,
                        out var runtimeXamlSetterValueExpression))
                {
                    valueExpression = runtimeXamlSetterValueExpression;
                    valueKind = ResolvedValueKind.RuntimeXamlFallback;
                    valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
                }

                var selectorNestingTypeHint =
                    IsStyleType(symbol, compilation) &&
                    property.Name.Equals("Selector", StringComparison.Ordinal)
                        ? inheritedSetterTargetType
                        : null;

                if (valueExpression.Length == 0 &&
                    HasResolveByNameSemantics(symbol, property.Name) &&
                    TryBuildResolveByNameLiteralExpression(
                        assignment.Value,
                        conversionTargetType,
                        out var resolveByNameValueExpression))
                {
                    valueExpression = resolveByNameValueExpression;
                    valueKind = ResolvedValueKind.MarkupExtension;
                }

                if (valueExpression.Length == 0 &&
                    property.Type is INamedTypeSymbol delegateType &&
                    delegateType.TypeKind == TypeKind.Delegate &&
                    TryBuildDelegateMethodGroupValueExpression(
                        assignment.Value,
                        delegateType,
                        rootTypeSymbol,
                        out var delegateMethodExpression))
                {
                    valueExpression = delegateMethodExpression;
                }

                if (valueExpression.Length == 0 && isSetterValueProperty)
                {
                    if (!TryResolveSetterValueWithPolicy(
                            rawValue: assignment.Value,
                            conversionTargetType: conversionTargetType,
                            compilation: compilation,
                            document: document,
                            setterTargetType: currentSetterTargetType,
                            bindingPriorityScope: currentBindingPriorityScope,
                            strictMode: options.StrictMode,
                            preferTypedStaticResourceCoercion: true,
                            allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                            conversionTargetType.SpecialType == SpecialType.System_Object,
                            allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                     conversionTargetType.SpecialType == SpecialType.System_Object,
                            propertyName: property.Name,
                            ownerDisplayName: symbol.ToDisplayString(),
                            line: assignment.Line,
                            column: assignment.Column,
                            diagnostics: diagnostics,
                            resolution: out var setterResolution,
                            selectorNestingTypeHint: selectorNestingTypeHint,
                            setterContext: false))
                    {
                        continue;
                    }

                    valueExpression = setterResolution.Expression;
                    valueKind = setterResolution.ValueKind;
                    requiresStaticResourceResolver = setterResolution.RequiresStaticResourceResolver;
                    valueRequirements = setterResolution.ValueRequirements;
                }
                else if (valueExpression.Length == 0)
                {
                    if (!TryConvertValueConversion(
                            assignment.Value,
                            conversionTargetType,
                            compilation,
                            document,
                            currentSetterTargetType,
                            currentBindingPriorityScope,
                            out var convertedValue,
                            allowObjectStringLiteralFallback: !options.StrictMode &&
                                                              conversionTargetType.SpecialType == SpecialType.System_Object,
                            selectorNestingTypeHint: selectorNestingTypeHint))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0102",
                            $"Could not convert literal '{assignment.Value}' for '{property.Name}' on '{symbol.ToDisplayString()}'.",
                            document.FilePath,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode));
                        continue;
                    }

                    valueExpression = convertedValue.Expression;
                    valueKind = convertedValue.ValueKind;
                    requiresStaticResourceResolver = convertedValue.RequiresStaticResourceResolver;
                    valueRequirements = convertedValue.EffectiveRequirements;
                }

                assignments.Add(new ResolvedPropertyAssignment(
                    PropertyName: property.Name,
                    ValueExpression: valueExpression,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: valueKind,
                    RequiresStaticResourceResolver: requiresStaticResourceResolver,
                    ValueRequirements: valueRequirements));
                continue;
            }

            if (TryBindEventSubscription(
                    symbol,
                    assignment,
                    compilation,
                    nodeDataType,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    out var eventSubscription))
            {
                if (eventSubscription is not null)
                {
                    eventSubscriptions.Add(eventSubscription);
                }

                continue;
            }

            if (TryBindAvaloniaPropertyAssignment(
                    symbol,
                    typeName,
                    normalizedPropertyName,
                    assignment,
                    compilation,
                    document,
                    options,
                    diagnostics,
                    compiledBindings,
                    compileBindingsEnabled,
                    nodeDataType,
                    property?.Type,
                    currentBindingPriorityScope,
                    currentSetterTargetType,
                    rootTypeSymbol,
                    out var fallbackAssignment))
            {
                if (fallbackAssignment is not null)
                {
                    assignments.Add(fallbackAssignment);
                }

                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Property '{assignment.PropertyName}' was not found on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }

        TryAddTemplateDataTypeDirectiveAssignment(
            node,
            symbol,
            compilation,
            document,
            options,
            diagnostics,
            assignments);

        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>();
        foreach (var child in node.ChildObjects)
        {
            if (ShouldSkipConditionalBranch(
                    child.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            children.Add(BindObjectNode(
                child,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                compileBindingsEnabled,
                nodeDataType,
                currentSetterTargetType,
                currentBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol));
        }

        ResolvedChildAttachmentMode? explicitAttachment = null;
        string? explicitContentPropertyName = null;
        foreach (var propertyElement in node.PropertyElements)
        {
            if (ShouldSkipConditionalBranch(
                    propertyElement.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            if (IsDesignTimePropertyToken(propertyElement.PropertyName))
            {
                continue;
            }

            var propertyAlias = ResolvePropertyAlias(symbol, propertyElement.PropertyName);
            var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
            if (!string.IsNullOrWhiteSpace(contentPropertyName) &&
                normalizedPropertyName.Equals(contentPropertyName, StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.Content;
                explicitContentPropertyName = contentPropertyName;
                foreach (var value in propertyElement.ObjectValues)
                {
                    if (ShouldSkipConditionalBranch(
                            value.Condition,
                            compilation,
                            document,
                            diagnostics,
                            options))
                    {
                        continue;
                    }

                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol));
                }

                continue;
            }

            if (normalizedPropertyName.Equals("Children", StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.ChildrenCollection;
                foreach (var value in propertyElement.ObjectValues)
                {
                    if (ShouldSkipConditionalBranch(
                            value.Condition,
                            compilation,
                            document,
                            diagnostics,
                            options))
                    {
                        continue;
                    }

                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol));
                }

                continue;
            }

            if (normalizedPropertyName.Equals("Items", StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.ItemsCollection;
                foreach (var value in propertyElement.ObjectValues)
                {
                    if (ShouldSkipConditionalBranch(
                            value.Condition,
                            compilation,
                            document,
                            diagnostics,
                            options))
                    {
                        continue;
                    }

                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol));
                }

                continue;
            }

            if (symbol is null)
            {
                continue;
            }

            if (propertyElement.ObjectValues.Length == 0)
            {
                continue;
            }

            var elementValues = ImmutableArray.CreateBuilder<ResolvedObjectNode>(propertyElement.ObjectValues.Length);
            foreach (var value in propertyElement.ObjectValues)
            {
                if (ShouldSkipConditionalBranch(
                        value.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                elementValues.Add(BindObjectNode(
                    value,
                    compilation,
                    diagnostics,
                    document,
                    options,
                    compiledBindings,
                    compileBindingsEnabled,
                    nodeDataType,
                    currentSetterTargetType,
                    currentBindingPriorityScope,
                    rootTypeSymbol: rootTypeSymbol));
            }

            var elementValuesArray = elementValues.ToImmutable();

            if (propertyAlias.HasAvaloniaPropertyAlias &&
                propertyAlias.AvaloniaPropertyOwnerType is not null &&
                TryFindAvaloniaPropertyField(
                    propertyAlias.AvaloniaPropertyOwnerType,
                    normalizedPropertyName,
                    out var aliasedOwnerType,
                    out var aliasedPropertyField,
                    propertyAlias.AvaloniaPropertyFieldName))
            {
                var aliasedAssignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                    TryGetAvaloniaPropertyValueType(aliasedPropertyField.Type),
                    elementValuesArray,
                    compilation,
                    document,
                    propertyElement.Line,
                    propertyElement.Column);

                if (aliasedAssignmentValues.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Aliased Avalonia property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: normalizedPropertyName,
                    AvaloniaPropertyOwnerTypeName: aliasedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: aliasedPropertyField.Name,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        symbol,
                        aliasedPropertyField,
                        compilation,
                        currentBindingPriorityScope),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: aliasedAssignmentValues,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition));
                continue;
            }

            if (TrySplitOwnerQualifiedPropertyToken(
                    propertyElement.PropertyName,
                    out var attachedOwnerToken,
                    out var attachedPropertyName))
            {
                var attachedOwnerType = ResolveTypeToken(
                    compilation,
                    document,
                    attachedOwnerToken,
                    document.ClassNamespace);
                if (attachedOwnerType is not null &&
                    TryFindAvaloniaPropertyField(
                        attachedOwnerType,
                        attachedPropertyName,
                        out var attachedResolvedOwnerType,
                        out var attachedPropertyField))
                {
                    var attachedAssignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                        TryGetAvaloniaPropertyValueType(attachedPropertyField.Type),
                        elementValuesArray,
                        compilation,
                        document,
                        propertyElement.Line,
                        propertyElement.Column);

                    if (attachedAssignmentValues.Length != 1)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0103",
                            $"Attached property element '{propertyElement.PropertyName}' requires exactly one object value.",
                            document.FilePath,
                            propertyElement.Line,
                            propertyElement.Column,
                            options.StrictMode));
                        continue;
                    }

                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: attachedPropertyName,
                        AvaloniaPropertyOwnerTypeName: attachedResolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        AvaloniaPropertyFieldName: attachedPropertyField.Name,
                        ClrPropertyOwnerTypeName: null,
                        ClrPropertyTypeName: null,
                        BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                            symbol,
                            attachedPropertyField,
                            compilation,
                            currentBindingPriorityScope),
                        IsCollectionAdd: false,
                        IsDictionaryMerge: false,
                        ObjectValues: attachedAssignmentValues,
                        Line: propertyElement.Line,
                        Column: propertyElement.Column,
                            Condition: propertyElement.Condition));
                    continue;
                }

                if (attachedOwnerType is not null &&
                    TryBindAttachedSetterPropertyElementAssignment(
                        targetType: symbol,
                        ownerType: attachedOwnerType,
                        attachedPropertyName: attachedPropertyName,
                        propertyElement: propertyElement,
                        objectValues: elementValuesArray,
                        compilation: compilation,
                        document: document,
                        options: options,
                        diagnostics: diagnostics,
                        out var attachedSetterAssignment))
                {
                    if (attachedSetterAssignment is not null)
                    {
                        propertyElementAssignments.Add(attachedSetterAssignment);
                    }

                    continue;
                }
            }

            var property = FindProperty(symbol, normalizedPropertyName);
            if (property is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Property element '{propertyElement.PropertyName}' was not found on '{symbol.ToDisplayString()}'.",
                    document.FilePath,
                    propertyElement.Line,
                    propertyElement.Column,
                    options.StrictMode));
                continue;
            }

            ValidateItemContainerInsideTemplateWarning(
                symbol,
                property,
                propertyElement,
                compilation,
                document,
                diagnostics,
                options);

            var canMergeDictionaryProperty = CanMergeDictionaryProperty(symbol, property.Name);
            if (canMergeDictionaryProperty &&
                TryBuildKeyedDictionaryMergeContainer(
                    property,
                    elementValuesArray,
                    propertyElement.Line,
                    propertyElement.Column,
                    out var dictionaryMergeContainer))
            {
                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: ImmutableArray.Create(dictionaryMergeContainer),
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition));
                continue;
            }

            if (CanAddToCollectionProperty(symbol, property.Name))
            {
                var collectionAddInstructions = CollectionAddService.ResolveCollectionAddInstructionsForValues(
                    property.Type,
                    elementValuesArray,
                    compilation,
                    document);
                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: true,
                    IsDictionaryMerge: false,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition,
                    CollectionAddInstructions: collectionAddInstructions));
                continue;
            }

            if (TryFindAvaloniaPropertyField(symbol, property.Name, out var ownerType, out var propertyField))
            {
                var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                    property.Type,
                    elementValuesArray,
                    compilation,
                    document,
                    propertyElement.Line,
                    propertyElement.Column);

                if (assignmentValues.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Avalonia property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        symbol,
                        propertyField,
                        compilation,
                        currentBindingPriorityScope),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: assignmentValues,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition));
                continue;
            }

            if (property.SetMethod is not null)
            {
                var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                    property.Type,
                    elementValuesArray,
                    compilation,
                    document,
                    propertyElement.Line,
                    propertyElement.Column);

                if (assignmentValues.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: assignmentValues,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition));
                continue;
            }

            if (canMergeDictionaryProperty)
            {
                if (elementValuesArray.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Dictionary property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition));
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Property element '{propertyElement.PropertyName}' is not supported on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                propertyElement.Line,
                propertyElement.Column,
                options.StrictMode));
        }

        string? factoryExpression = null;
        var factoryValueRequirements = ResolvedValueRequirements.None;
        if (TryBuildExplicitConstructionExpression(
                node,
                symbol,
                typeName,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                compileBindingsEnabled,
                nodeDataType,
                currentSetterTargetType,
                currentBindingPriorityScope,
                rootTypeSymbol,
                out var explicitConstructionExpression))
        {
            factoryExpression = explicitConstructionExpression;
            factoryValueRequirements = ResolvedValueRequirements.None;
        }

        if (symbol is not null &&
            string.IsNullOrWhiteSpace(factoryExpression) &&
            !string.IsNullOrWhiteSpace(node.TextContent) &&
            children.Count == 0)
        {
            var inlineTextContent = node.TextContent!.Trim();
            var handledAsContentProperty = false;
            if (!string.IsNullOrWhiteSpace(contentPropertyName) &&
                !HasResolvedPropertyAssignment(assignments, contentPropertyName!) &&
                !HasResolvedPropertyElementAssignment(propertyElementAssignments, contentPropertyName!))
            {
                var contentProperty = FindProperty(symbol, contentPropertyName!);
                if (contentProperty?.SetMethod is not null &&
                    TryConvertValueConversion(
                        inlineTextContent,
                        contentProperty.Type,
                        compilation,
                        document,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var inlineContentConversion,
                        allowObjectStringLiteralFallback: !options.StrictMode))
                {
                    assignments.Add(new ResolvedPropertyAssignment(
                        PropertyName: contentProperty.Name,
                        ValueExpression: inlineContentConversion.Expression,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: contentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: contentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        Line: node.Line,
                        Column: node.Column,
                        Condition: null,
                        ValueKind: inlineContentConversion.ValueKind,
                        ValueRequirements: inlineContentConversion.EffectiveRequirements));
                    handledAsContentProperty = true;
                }
                else if (contentProperty is not null &&
                         CanAddToCollectionProperty(symbol, contentProperty.Name) &&
                         CollectionAddService.TryCreateCollectionContentValue(
                             inlineTextContent,
                             contentProperty.Type,
                             compilation,
                             document,
                             currentSetterTargetType,
                             (int)currentBindingPriorityScope,
                             !options.StrictMode,
                             node.Line,
                             node.Column,
                             out var inlineContentValue,
                             out var inlineContentAddInstruction))
                {
                    var inlineContentValues = ImmutableArray.Create(inlineContentValue);
                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: contentProperty.Name,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: contentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: contentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        IsCollectionAdd: true,
                        IsDictionaryMerge: false,
                        ObjectValues: inlineContentValues,
                        Line: node.Line,
                        Column: node.Column,
                        Condition: null,
                        CollectionAddInstructions: ImmutableArray.Create(inlineContentAddInstruction)));
                    handledAsContentProperty = true;
                }
            }

            if (!handledAsContentProperty &&
                assignments.Count == 0 &&
                propertyElementAssignments.Count == 0 &&
                TryConvertValueConversion(
                    inlineTextContent,
                    symbol,
                    compilation,
                    document,
                    currentSetterTargetType,
                    currentBindingPriorityScope,
                    out var inlineFactoryConversion))
            {
                factoryExpression = inlineFactoryConversion.Expression;
                factoryValueRequirements = inlineFactoryConversion.EffectiveRequirements;
            }
        }

        var (defaultAttachmentMode, defaultContentPropertyName) = DetermineChildAttachment(symbol);
        var attachmentMode = explicitAttachment ?? defaultAttachmentMode;
        var resolvedContentPropertyName = attachmentMode == ResolvedChildAttachmentMode.Content
            ? explicitContentPropertyName ?? defaultContentPropertyName
            : null;

        if (attachmentMode == ResolvedChildAttachmentMode.Content &&
            children.Count > 0 &&
            symbol is not null &&
            !string.IsNullOrWhiteSpace(resolvedContentPropertyName))
        {
            var resolvedContentProperty = FindProperty(symbol, resolvedContentPropertyName!);
            if (resolvedContentProperty is not null)
            {
                var contentChildrenValues = children.ToImmutableArray();
                var useCollectionAddForContent =
                    CanAddToCollectionProperty(symbol, resolvedContentProperty.Name) &&
                    ShouldUseCollectionAddForContentProperty(
                        resolvedContentProperty,
                        contentChildrenValues,
                        compilation,
                        document);
                if (useCollectionAddForContent)
                {
                    var collectionAddInstructions = CollectionAddService.ResolveCollectionAddInstructionsForValues(
                        resolvedContentProperty.Type,
                        contentChildrenValues,
                        compilation,
                        document);
                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: resolvedContentProperty.Name,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: resolvedContentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: resolvedContentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        IsCollectionAdd: true,
                        IsDictionaryMerge: false,
                        ObjectValues: contentChildrenValues,
                        Line: node.Line,
                        Column: node.Column,
                        Condition: node.Condition,
                        CollectionAddInstructions: collectionAddInstructions));

                    children.Clear();
                    attachmentMode = ResolvedChildAttachmentMode.None;
                    resolvedContentPropertyName = null;
                }
                else if (CanMergeDictionaryProperty(symbol, resolvedContentProperty.Name) &&
                         ShouldUseDictionaryMergeForContentProperty(
                             resolvedContentProperty,
                             contentChildrenValues) &&
                         TryBuildKeyedDictionaryMergeContainer(
                             resolvedContentProperty,
                             contentChildrenValues,
                             node.Line,
                             node.Column,
                             out var contentDictionaryMergeContainer))
                {
                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: resolvedContentProperty.Name,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: resolvedContentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: resolvedContentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        IsCollectionAdd: false,
                        IsDictionaryMerge: true,
                        ObjectValues: ImmutableArray.Create(contentDictionaryMergeContainer),
                        Line: node.Line,
                        Column: node.Column,
                        Condition: node.Condition));

                    children.Clear();
                    attachmentMode = ResolvedChildAttachmentMode.None;
                    resolvedContentPropertyName = null;
                }
            }
        }

        if (attachmentMode == ResolvedChildAttachmentMode.Content && children.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0103",
                "More than one child object was found for a Content attachment target. Only the first child will be attached.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
        }

        if (attachmentMode == ResolvedChildAttachmentMode.DictionaryAdd)
        {
            foreach (var child in children)
            {
                if (!string.IsNullOrWhiteSpace(child.KeyExpression))
                {
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Type '{typeName}' requires x:Key for child objects added via dictionary Add(key, value).",
                    document.FilePath,
                    node.Line,
                    node.Column,
                    options.StrictMode));
                break;
            }
        }

        var useServiceProviderConstructor = ShouldUseServiceProviderConstructor(symbol);
        var useTopDownInitialization = IsUsableDuringInitialization(symbol);
        var normalizedNodeName = ResolveObjectNodeNameScopeRegistration(node, symbol, compilation);
        var resolvedChildren = children.ToImmutable();
        var childAddInstructions = ResolveChildAddInstructions(
            symbol,
            attachmentMode,
            resolvedChildren,
            compilation,
            document);

        return new ResolvedObjectNode(
            KeyExpression: BuildObjectNodeKeyExpression(node.Key, compilation, document),
            Name: normalizedNodeName,
            TypeName: typeName,
            IsBindingObjectNode: IsBindingObjectType(symbol, compilation),
            FactoryExpression: factoryExpression,
            FactoryValueRequirements: factoryValueRequirements,
            UseServiceProviderConstructor: useServiceProviderConstructor,
            UseTopDownInitialization: useTopDownInitialization,
            PropertyAssignments: assignments.ToImmutable(),
            PropertyElementAssignments: propertyElementAssignments.ToImmutable(),
            EventSubscriptions: eventSubscriptions.ToImmutable(),
            Children: resolvedChildren,
            ChildAttachmentMode: attachmentMode,
            ContentPropertyName: resolvedContentPropertyName,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition,
            ChildAddInstructions: childAddInstructions);
    }

    private static void TryAddTemplateDataTypeDirectiveAssignment(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments)
    {
        if (symbol is null ||
            !IsDataTemplateNode(node) ||
            string.IsNullOrWhiteSpace(node.DataType))
        {
            return;
        }

        if (node.PropertyAssignments.Any(static assignment =>
                !assignment.IsAttached &&
                NormalizePropertyName(assignment.PropertyName).Equals("DataType", StringComparison.Ordinal)))
        {
            return;
        }

        var dataTypeProperty = FindProperty(symbol, "DataType");
        if (dataTypeProperty is null || dataTypeProperty.SetMethod is null)
        {
            return;
        }

        var resolvedDataType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            node.DataType,
            document.ClassNamespace);
        if (resolvedDataType is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Template x:DataType '{node.DataType}' could not be resolved for runtime DataType assignment.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return;
        }

        assignments.Add(new ResolvedPropertyAssignment(
            PropertyName: dataTypeProperty.Name,
            ValueExpression: "typeof(" + resolvedDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            ClrPropertyOwnerTypeName: dataTypeProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: dataTypeProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            BindingPriorityExpression: null,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition));
    }

    private static bool IsXamlArrayNode(XamlObjectNode node)
    {
        return node.XmlNamespace == Xaml2006.NamespaceName &&
               node.XmlTypeName.Equals("Array", StringComparison.Ordinal);
    }

    private static ResolvedObjectNode BindXamlArrayNode(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol)
    {
        var elementType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            node.ArrayItemType,
            document.ClassNamespace);
        if (elementType is null && node.TypeArguments.Length > 0)
        {
            elementType = ResolveTypeToken(compilation, document, node.TypeArguments[0], document.ClassNamespace);
        }

        var elementTypeName = elementType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
        var valueExpressions = new List<string>(node.ChildObjects.Length);

        foreach (var child in node.ChildObjects)
        {
            if (ShouldSkipConditionalBranch(
                    child.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var boundChild = BindObjectNode(
                child,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol);

            if (!TryBuildInlineResolvedObjectExpression(boundChild, out var inlineChildExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0108",
                    "x:Array values must be inline-constructable objects when used in source-generated construction.",
                    document.FilePath,
                    child.Line,
                    child.Column,
                    options.StrictMode));
                continue;
            }

            valueExpressions.Add(inlineChildExpression);
        }

        var factoryExpression = valueExpressions.Count == 0
            ? "global::System.Array.Empty<" + elementTypeName + ">()"
            : "new " + elementTypeName + "[] { " + string.Join(", ", valueExpressions) + " }";
        var normalizedNodeName = NormalizeObjectNodeName(node.Name);

        return new ResolvedObjectNode(
            KeyExpression: BuildObjectNodeKeyExpression(node.Key, compilation, document),
            Name: normalizedNodeName,
            TypeName: "global::System.Array",
            IsBindingObjectNode: false,
            FactoryExpression: factoryExpression,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition);
    }

    private static string? BuildObjectNodeKeyExpression(
        string? rawKey,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        if (TryBuildResourceKeyExpression(rawKey!, compilation, document, out var keyExpression))
        {
            return keyExpression.Expression;
        }

        return "\"" + Escape(rawKey!.Trim()) + "\"";
    }

    private static bool TryBuildExplicitConstructionExpression(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        string typeName,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        out string? expression)
    {
        expression = null;
        var hasConstructionDirectives = node.ConstructorArguments.Length > 0 ||
                                        !string.IsNullOrWhiteSpace(node.FactoryMethod);
        if (!hasConstructionDirectives)
        {
            return false;
        }

        if (symbol is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0106",
                $"Could not resolve type '{node.XmlTypeName}' for construction directives.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return true;
        }

        var arguments = new List<string>(node.ConstructorArguments.Length);
        foreach (var argumentNode in node.ConstructorArguments)
        {
            if (ShouldSkipConditionalBranch(
                    argumentNode.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var resolvedArgument = BindObjectNode(
                argumentNode,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol);

            if (!TryBuildInlineResolvedObjectExpression(resolvedArgument, out var argumentExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0106",
                    "x:Arguments value is not inline-constructable for source-generated construction.",
                    document.FilePath,
                    argumentNode.Line,
                    argumentNode.Column,
                    options.StrictMode));
                return true;
            }

            arguments.Add(argumentExpression);
        }

        if (!string.IsNullOrWhiteSpace(node.FactoryMethod))
        {
            var factoryMethod = TryFindMatchingFactoryMethod(symbol, node.FactoryMethod!, arguments.Count);
            if (factoryMethod is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0107",
                    $"x:FactoryMethod '{node.FactoryMethod}' with {arguments.Count} argument(s) was not found on '{symbol.ToDisplayString()}'.",
                    document.FilePath,
                    node.Line,
                    node.Column,
                    options.StrictMode));
                return true;
            }

            expression = factoryMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "." +
                         factoryMethod.Name +
                         "(" +
                         string.Join(", ", arguments) +
                         ")";
            return true;
        }

        var constructor = TryFindMatchingConstructor(symbol, arguments.Count);
        if (constructor is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0107",
                $"No constructor with {arguments.Count} argument(s) was found on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return true;
        }

        expression = "new " + typeName + "(" + string.Join(", ", arguments) + ")";
        return true;
    }

    private static bool TryBuildInlineResolvedObjectExpression(
        ResolvedObjectNode node,
        out string expression)
    {
        expression = string.Empty;
        if (node.Condition is not null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(node.FactoryExpression))
        {
            if (node.FactoryValueRequirements.RequiresMarkupContext)
            {
                return false;
            }

            expression = node.FactoryExpression!;
            return true;
        }

        if (node.UseServiceProviderConstructor ||
            node.Children.Length > 0 ||
            node.PropertyElementAssignments.Length > 0 ||
            node.EventSubscriptions.Length > 0)
        {
            return false;
        }

        if (node.PropertyAssignments.Length == 0)
        {
            expression = "new " + node.TypeName + "()";
            return true;
        }

        var initializers = new List<string>(node.PropertyAssignments.Length);
        foreach (var assignment in node.PropertyAssignments)
        {
            if (!string.IsNullOrWhiteSpace(assignment.AvaloniaPropertyOwnerTypeName) ||
                !string.IsNullOrWhiteSpace(assignment.AvaloniaPropertyFieldName) ||
                assignment.Condition is not null ||
                assignment.ValueRequirements.RequiresMarkupContext)
            {
                return false;
            }

            initializers.Add(assignment.PropertyName + " = " + assignment.ValueExpression);
        }

        expression = "new " + node.TypeName + "() { " + string.Join(", ", initializers) + " }";
        return true;
    }

    private static bool ShouldSkipConditionalBranch(
        ConditionalXamlExpression? condition,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        if (condition is null)
        {
            return false;
        }

        if (TryEvaluateConditionalExpression(
                condition,
                compilation,
                out var result,
                out var errorMessage))
        {
            return !result;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0120",
            $"Invalid conditional XAML expression '{condition.RawExpression}': {errorMessage}",
            document.FilePath,
            condition.Line,
            condition.Column,
            options.StrictMode));
        return false;
    }

    private static bool TryEvaluateConditionalExpression(
        ConditionalXamlExpression condition,
        Compilation compilation,
        out bool result,
        out string errorMessage)
    {
        result = false;
        errorMessage = string.Empty;

        var args = condition.Arguments;
        switch (condition.MethodName)
        {
            case "IsTypePresent":
            case "IsTypeNotPresent":
            {
                if (args.Length != 1)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 1 argument.";
                    return false;
                }

                var isPresent = ResolveConditionalTypeSymbol(compilation, args[0]) is not null;
                result = condition.MethodName == "IsTypePresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsPropertyPresent":
            case "IsPropertyNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.GetMembers(args[1]).OfType<IPropertySymbol>().Any();
                result = condition.MethodName == "IsPropertyPresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsMethodPresent":
            case "IsMethodNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.GetMembers(args[1]).OfType<IMethodSymbol>().Any(method =>
                                    method.MethodKind == MethodKind.Ordinary);
                result = condition.MethodName == "IsMethodPresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsEventPresent":
            case "IsEventNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.GetMembers(args[1]).OfType<IEventSymbol>().Any();
                result = condition.MethodName == "IsEventPresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsEnumNamedValuePresent":
            case "IsEnumNamedValueNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.TypeKind == TypeKind.Enum &&
                                type.GetMembers(args[1]).OfType<IFieldSymbol>().Any(field => field.HasConstantValue);
                result = condition.MethodName == "IsEnumNamedValuePresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsApiContractPresent":
            case "IsApiContractNotPresent":
            {
                if (args.Length < 1 || args.Length > 3)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects between 1 and 3 arguments.";
                    return false;
                }

                var contractType = ResolveConditionalTypeSymbol(compilation, args[0]);
                var hasContract = contractType is not null;
                if (!hasContract)
                {
                    result = condition.MethodName == "IsApiContractNotPresent";
                    return true;
                }

                if (args.Length == 1)
                {
                    result = condition.MethodName == "IsApiContractPresent";
                    return true;
                }

                if (!TryParseNonNegativeInt(args[1], out var requiredMajor))
                {
                    errorMessage = $"Contract major version '{args[1]}' is not a valid non-negative integer.";
                    return false;
                }

                var requiredMinor = 0;
                if (args.Length > 2 && !TryParseNonNegativeInt(args[2], out requiredMinor))
                {
                    errorMessage = $"Contract minor version '{args[2]}' is not a valid non-negative integer.";
                    return false;
                }

                var actualMajor = 1;
                var actualMinor = 0;
                if (TryGetContractVersion(contractType!, out var parsedMajor, out var parsedMinor))
                {
                    actualMajor = parsedMajor;
                    actualMinor = parsedMinor;
                }

                var versionSatisfied = actualMajor > requiredMajor ||
                                       (actualMajor == requiredMajor && actualMinor >= requiredMinor);
                var contractPresent = hasContract && versionSatisfied;
                result = condition.MethodName == "IsApiContractPresent" ? contractPresent : !contractPresent;
                return true;
            }
            default:
                errorMessage = $"Unsupported conditional method '{condition.MethodName}'.";
                return false;
        }
    }

    private static INamedTypeSymbol? ResolveConditionalTypeSymbol(Compilation compilation, string rawTypeToken)
    {
        if (string.IsNullOrWhiteSpace(rawTypeToken))
        {
            return null;
        }

        var token = rawTypeToken.Trim();
        token = XamlTypeTokenSemantics.TrimGlobalQualifier(token);

        if (XamlTokenSplitSemantics.TrySplitAtFirstSeparator(
                token,
                ',',
                out var typeToken,
                out _))
        {
            token = typeToken;
        }

        if (token.Length == 0)
        {
            return null;
        }

        var resolved = compilation.GetTypeByMetadataName(token);
        if (resolved is not null)
        {
            return resolved;
        }

        return TryFindTypeByFullName(compilation.Assembly.GlobalNamespace, token);
    }

    private static INamedTypeSymbol? TryFindTypeByFullName(INamespaceSymbol namespaceSymbol, string fullTypeName)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            if (string.Equals(
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal),
                    fullTypeName,
                    StringComparison.Ordinal))
            {
                return type;
            }
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            var resolved = TryFindTypeByFullName(childNamespace, fullTypeName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool TryGetContractVersion(INamedTypeSymbol contractType, out int major, out int minor)
    {
        major = 1;
        minor = 0;

        foreach (var attribute in contractType.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (attributeName is not ("Windows.Foundation.Metadata.ContractVersionAttribute" or "Windows.Foundation.Metadata.VersionAttribute") &&
                !string.Equals(attribute.AttributeClass?.Name, "ContractVersionAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0)
            {
                continue;
            }

            if (TryDecodeContractVersion(attribute.ConstructorArguments[^1], out major, out minor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeContractVersion(
        TypedConstant argument,
        out int major,
        out int minor)
    {
        major = 1;
        minor = 0;

        if (argument.Value is null)
        {
            return false;
        }

        switch (argument.Value)
        {
            case ushort ushortVersion:
                major = ushortVersion;
                minor = 0;
                return true;
            case int intVersion:
                if (intVersion < 0)
                {
                    return false;
                }

                major = intVersion >> 16;
                minor = intVersion & 0xFFFF;
                return true;
            case uint uintVersion:
                major = (int)(uintVersion >> 16);
                minor = (int)(uintVersion & 0xFFFF);
                return true;
            case long longVersion:
                if (longVersion < 0)
                {
                    return false;
                }

                major = (int)(longVersion >> 16);
                minor = (int)(longVersion & 0xFFFF);
                return true;
            case ulong ulongVersion:
                major = (int)(ulongVersion >> 16);
                minor = (int)(ulongVersion & 0xFFFF);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseNonNegativeInt(string token, out int value)
    {
        if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static IMethodSymbol? TryFindMatchingFactoryMethod(INamedTypeSymbol type, string methodName, int argumentCount)
    {
        return type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                !method.IsGenericMethod &&
                !method.ReturnsVoid)
            .Where(method => method.Parameters.Length == argumentCount)
            .OrderBy(static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IMethodSymbol? TryFindMatchingConstructor(INamedTypeSymbol type, int argumentCount)
    {
        return type.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                !constructor.IsStatic)
            .Where(constructor => constructor.Parameters.Length == argumentCount)
            .OrderBy(static constructor => constructor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
