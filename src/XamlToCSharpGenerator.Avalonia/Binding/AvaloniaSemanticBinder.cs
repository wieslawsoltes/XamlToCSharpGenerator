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

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed class AvaloniaSemanticBinder : IXamlSemanticBinder
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

    private static readonly string[] ExpressionOperatorAliases =
    [
        "AND",
        "OR",
        "LT",
        "GT",
        "LTE",
        "GTE"
    ];

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
        var trimmed = typeName.Trim();
        return trimmed.StartsWith("global::", StringComparison.Ordinal)
            ? trimmed.Substring("global::".Length)
            : trimmed;
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
        var trimmed = fieldName.Trim();
        if (trimmed.EndsWith("Property", StringComparison.Ordinal) &&
            trimmed.Length > "Property".Length)
        {
            return trimmed.Substring(0, trimmed.Length - "Property".Length);
        }

        return trimmed;
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
                Includes: context.Includes);
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
                var selectorTypeToken = TryExtractSelectorTypeToken(selectorValue!);
                if (!string.IsNullOrWhiteSpace(selectorTypeToken))
                {
                    var selectorType = ResolveTypeToken(compilation, document, selectorTypeToken!, document.ClassNamespace);
                    if (selectorType is not null)
                    {
                        return selectorType;
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
                    Condition: propertyElement.Condition));
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
            children.Count == 0 &&
            propertyElementAssignments.Count == 0)
        {
            var inlineTextContent = node.TextContent!.Trim();
            var handledAsContentProperty = false;
            if (!string.IsNullOrWhiteSpace(contentPropertyName) &&
                !HasResolvedPropertyAssignment(assignments, contentPropertyName!))
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
            }

            if (!handledAsContentProperty &&
                assignments.Count == 0 &&
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
                if (CanAddToCollectionProperty(symbol, resolvedContentProperty.Name))
                {
                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: resolvedContentProperty.Name,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: resolvedContentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: resolvedContentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        IsCollectionAdd: true,
                        IsDictionaryMerge: false,
                        ObjectValues: children.ToImmutableArray(),
                        Line: node.Line,
                        Column: node.Column,
                        Condition: node.Condition));

                    children.Clear();
                    attachmentMode = ResolvedChildAttachmentMode.None;
                    resolvedContentPropertyName = null;
                }
                else if (CanMergeDictionaryProperty(symbol, resolvedContentProperty.Name) &&
                         TryBuildKeyedDictionaryMergeContainer(
                             resolvedContentProperty,
                             children.ToImmutableArray(),
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
        var normalizedNodeName = NormalizeObjectNodeName(node.Name);

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
            Children: children.ToImmutable(),
            ChildAttachmentMode: attachmentMode,
            ContentPropertyName: resolvedContentPropertyName,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition);
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
        if (token.StartsWith("global::", StringComparison.Ordinal))
        {
            token = token.Substring("global::".Length);
        }

        var assemblySeparatorIndex = token.IndexOf(',');
        if (assemblySeparatorIndex > 0)
        {
            token = token.Substring(0, assemblySeparatorIndex).Trim();
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

    private static ImmutableArray<ResolvedStyleDefinition> BindStyles(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings)
    {
        var styles = ImmutableArray.CreateBuilder<ResolvedStyleDefinition>(document.Styles.Length);

        foreach (var style in document.Styles)
        {
            if (ShouldSkipConditionalBranch(
                    style.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var selector = style.Selector.Trim();
            INamedTypeSymbol? targetType = null;

            if (string.IsNullOrWhiteSpace(selector))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0300",
                    "Style selector cannot be empty.",
                    document.FilePath,
                    style.SelectorLine,
                    style.SelectorColumn,
                    options.StrictMode));
            }
            else
            {
                var selectorValidation = SelectorSyntaxValidator.Validate(selector);
                if (!selectorValidation.IsValid)
                {
                    var (line, column) = AdvanceLineAndColumn(
                        style.SelectorLine,
                        style.SelectorColumn,
                        selector,
                        selectorValidation.ErrorOffset);
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0300",
                        "Unable to parse selector: " + selectorValidation.ErrorMessage,
                        document.FilePath,
                        line,
                        column,
                        options.StrictMode));
                }
                else
                {
                    targetType = TryResolveSelectorTargetType(
                        compilation,
                        document,
                        selectorValidation.Branches,
                        out var unresolvedTypeToken,
                        out var unresolvedTypeOffset);
                    if (!string.IsNullOrWhiteSpace(unresolvedTypeToken))
                    {
                        var (line, column) = AdvanceLineAndColumn(
                            style.SelectorLine,
                            style.SelectorColumn,
                            selector,
                            unresolvedTypeOffset);
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0300",
                            $"Style selector target '{unresolvedTypeToken}' could not be resolved.",
                            document.FilePath,
                            line,
                            column,
                            options.StrictMode));
                    }
                }
            }

            var styleDataType = ResolveTypeFromTypeExpression(compilation, document, style.DataType, document.ClassNamespace);
            var compileBindingsEnabled = style.CompileBindings ?? options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(style.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in style.Setters)
            {
                if (ShouldSkipConditionalBranch(
                        setter.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                var propertyAlias = ResolvePropertyAlias(targetType, setter.PropertyName);
                var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
                var resolvedPropertyName = normalizedPropertyName;
                IPropertySymbol? targetProperty = null;
                string? setterPropertyOwnerTypeName = null;
                string? setterPropertyFieldName = null;
                ITypeSymbol? setterValueType = null;

                if (propertyAlias.HasAvaloniaPropertyAlias &&
                    propertyAlias.AvaloniaPropertyOwnerType is not null &&
                    TryFindAvaloniaPropertyField(
                        propertyAlias.AvaloniaPropertyOwnerType,
                        propertyAlias.ResolvedPropertyName,
                        out var aliasedOwnerType,
                        out var aliasedPropertyField,
                        propertyAlias.AvaloniaPropertyFieldName))
                {
                    resolvedPropertyName = propertyAlias.ResolvedPropertyName;
                    setterPropertyOwnerTypeName = aliasedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = aliasedPropertyField.Name;
                    setterValueType = TryGetAvaloniaPropertyValueType(aliasedPropertyField.Type);
                }

                if (targetType is not null &&
                    TrySplitOwnerQualifiedPropertyToken(
                        setter.PropertyName,
                        out var ownerToken,
                        out var attachedPropertyName))
                {
                    var explicitOwnerType = ResolveTypeToken(
                        compilation,
                        document,
                        ownerToken,
                        document.ClassNamespace);
                    if (explicitOwnerType is not null &&
                        TryFindAvaloniaPropertyField(
                            explicitOwnerType,
                            attachedPropertyName,
                            out var attachedOwnerType,
                            out var attachedPropertyField))
                    {
                        resolvedPropertyName = attachedPropertyName;
                        setterPropertyOwnerTypeName =
                            attachedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        setterPropertyFieldName = attachedPropertyField.Name;
                        setterValueType = TryGetAvaloniaPropertyValueType(attachedPropertyField.Type);
                    }
                }

                if (targetType is not null)
                {
                    targetProperty = FindProperty(targetType, normalizedPropertyName);
                    if (targetProperty is not null)
                    {
                        resolvedPropertyName = targetProperty.Name;
                    }
                }

                setterValueType ??= targetProperty?.Type;
                if (targetType is not null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                    TryFindAvaloniaPropertyField(targetType, resolvedPropertyName, out var stylePropertyOwnerType, out var stylePropertyField))
                {
                    setterPropertyOwnerTypeName =
                        stylePropertyOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = stylePropertyField.Name;
                    setterValueType ??= TryGetAvaloniaPropertyValueType(stylePropertyField.Type);
                }

                if (targetType is not null &&
                    targetProperty is null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0301",
                        $"Style setter property '{setter.PropertyName}' was not found on '{targetType.ToDisplayString()}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var duplicatePropertyKey = !string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                                           !string.IsNullOrWhiteSpace(setterPropertyFieldName)
                    ? setterPropertyOwnerTypeName + "." + setterPropertyFieldName
                    : resolvedPropertyName;
                if (!seenSetterProperties.Add(duplicatePropertyKey))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"Style setter property '{resolvedPropertyName}' is duplicated in selector '{selector}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var valueExpression = string.Empty;
                var valueResolvedFromMarkup = false;
                var valueKind = ResolvedValueKind.Literal;
                var requiresStaticResourceResolver = false;
                var valueRequirements = ResolvedValueRequirements.None;

                var isCompiledBinding = false;
                string? compiledBindingPath = null;
                string? compiledBindingSourceType = null;
                if (TryConvertCSharpExpressionMarkupToBindingExpression(
                        setter.Value,
                        compilation,
                        document,
                        options,
                        styleDataType,
                        out var isExpressionMarkup,
                        out var expressionBindingValueExpression,
                        out var expressionAccessorExpression,
                        out var normalizedExpression,
                        out var expressionErrorCode,
                        out var expressionErrorMessage))
                {
                    var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                    var expressionPath = "{= " + normalizedExpression + " }";
                    var sourceTypeName = styleDataType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: resolvedPropertyName,
                        Path: expressionPath,
                        SourceTypeName: sourceTypeName,
                        AccessorExpression: expressionAccessorExpression,
                        IsSetterBinding: true,
                        Line: setter.Line,
                        Column: setter.Column));

                    setters.Add(new ResolvedSetterDefinition(
                        PropertyName: resolvedPropertyName,
                        ValueExpression: expressionBindingValueExpression,
                        IsCompiledBinding: true,
                        CompiledBindingPath: expressionPath,
                        CompiledBindingSourceTypeName: sourceTypeName,
                        AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                        AvaloniaPropertyFieldName: setterPropertyFieldName,
                        Line: setter.Line,
                        Column: setter.Column,
                        Condition: setter.Condition,
                        ValueKind: ResolvedValueKind.Binding,
                        ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true)));
                    continue;
                }

                if (isExpressionMarkup)
                {
                    var message = expressionErrorCode == "AXSG0110"
                        ? $"Expression binding for style setter '{setter.PropertyName}' requires x:DataType on the style."
                        : $"Expression binding '{setter.Value}' is invalid for source type '{styleDataType?.ToDisplayString() ?? "unknown"}': {expressionErrorMessage}";
                    diagnostics.Add(new DiagnosticInfo(
                        expressionErrorCode,
                        message,
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                    continue;
                }

                if (TryParseBindingMarkup(setter.Value, out var bindingMarkup))
                {
                    if (!TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            setter.Line,
                            setter.Column,
                            options.StrictMode) &&
                        CanUseCompiledBinding(bindingMarkup) &&
                        (bindingMarkup.IsCompiledBinding || compileBindingsEnabled))
                    {
                        if (styleDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for style setter '{setter.PropertyName}' requires x:DataType on the style.",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else if (!TryBuildCompiledBindingAccessorExpression(
                                     compilation,
                                     document,
                                     styleDataType,
                                     bindingMarkup.Path,
                                     out var accessorExpression,
                                     out var normalizedPath,
                                     out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{styleDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else
                        {
                            var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                            compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                                TargetTypeName: targetTypeName,
                                TargetPropertyName: resolvedPropertyName,
                                Path: normalizedPath,
                                SourceTypeName: styleDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                AccessorExpression: accessorExpression,
                                IsSetterBinding: true,
                                Line: setter.Line,
                                Column: setter.Column));

                            isCompiledBinding = true;
                            compiledBindingPath = normalizedPath;
                            compiledBindingSourceType = styleDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                    }

                    if (!isCompiledBinding &&
                        !bindingMarkup.HasSourceConflict &&
                        TryBuildRuntimeBindingExpression(
                            compilation,
                            document,
                            bindingMarkup,
                            targetType,
                            BindingPriorityScope.Style,
                            out var runtimeBindingExpression))
                    {
                        valueExpression = runtimeBindingExpression;
                        valueResolvedFromMarkup = true;
                        valueKind = ResolvedValueKind.Binding;
                        valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
                    }
                }

                var conversionTargetType = setterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                if (!valueResolvedFromMarkup &&
                    TryResolveSetterValueWithPolicy(
                        rawValue: setter.Value,
                        conversionTargetType: conversionTargetType,
                        compilation: compilation,
                        document: document,
                        setterTargetType: targetType,
                        bindingPriorityScope: BindingPriorityScope.Style,
                        strictMode: options.StrictMode,
                        preferTypedStaticResourceCoercion: string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName),
                        allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                        conversionTargetType.SpecialType == SpecialType.System_Object,
                        allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                 conversionTargetType.SpecialType == SpecialType.System_Object,
                        propertyName: resolvedPropertyName,
                        ownerDisplayName: targetType?.ToDisplayString() ?? "style",
                        line: setter.Line,
                        column: setter.Column,
                        diagnostics: diagnostics,
                        resolution: out var styleSetterResolution,
                        setterContext: true))
                {
                    valueExpression = styleSetterResolution.Expression;
                    valueResolvedFromMarkup = true;
                    valueKind = styleSetterResolution.ValueKind;
                    requiresStaticResourceResolver = styleSetterResolution.RequiresStaticResourceResolver;
                    valueRequirements = styleSetterResolution.ValueRequirements;
                }

                if (!valueResolvedFromMarkup)
                {
                    continue;
                }

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: resolvedPropertyName,
                    ValueExpression: valueExpression,
                    IsCompiledBinding: isCompiledBinding,
                    CompiledBindingPath: compiledBindingPath,
                    CompiledBindingSourceTypeName: compiledBindingSourceType,
                    AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                    AvaloniaPropertyFieldName: setterPropertyFieldName,
                    Line: setter.Line,
                    Column: setter.Column,
                    Condition: setter.Condition,
                    ValueKind: isCompiledBinding
                        ? ResolvedValueKind.Binding
                        : valueKind,
                    RequiresStaticResourceResolver: requiresStaticResourceResolver,
                    ValueRequirements: valueRequirements));
            }

            styles.Add(new ResolvedStyleDefinition(
                Key: style.Key,
                Selector: selector,
                TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Setters: setters.ToImmutable(),
                RawXaml: style.RawXaml,
                Line: style.Line,
                Column: style.Column,
                Condition: style.Condition));
        }

        return styles.ToImmutable();
    }

    private static ImmutableArray<ResolvedControlThemeDefinition> BindControlThemes(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings)
    {
        var controlThemes = ImmutableArray.CreateBuilder<ResolvedControlThemeDefinition>(document.ControlThemes.Length);

        foreach (var controlTheme in document.ControlThemes)
        {
            if (ShouldSkipConditionalBranch(
                    controlTheme.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var targetType = ResolveTypeFromTypeExpression(
                compilation,
                document,
                controlTheme.TargetType,
                document.ClassNamespace);
            var themeVariant = string.IsNullOrWhiteSpace(controlTheme.ThemeVariant)
                ? null
                : controlTheme.ThemeVariant!.Trim();

            if (!string.IsNullOrWhiteSpace(controlTheme.TargetType) && targetType is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0302",
                    $"ControlTheme target type '{controlTheme.TargetType}' could not be resolved.",
                    document.FilePath,
                    controlTheme.Line,
                    controlTheme.Column,
                    options.StrictMode));
            }

            var themeDataType = ResolveTypeFromTypeExpression(compilation, document, controlTheme.DataType, document.ClassNamespace);
            var compileBindingsEnabled = controlTheme.CompileBindings ?? options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(controlTheme.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in controlTheme.Setters)
            {
                if (ShouldSkipConditionalBranch(
                        setter.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                var propertyAlias = ResolvePropertyAlias(targetType, setter.PropertyName);
                var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
                var resolvedPropertyName = normalizedPropertyName;
                IPropertySymbol? targetProperty = null;
                string? setterPropertyOwnerTypeName = null;
                string? setterPropertyFieldName = null;
                ITypeSymbol? setterValueType = null;

                if (propertyAlias.HasAvaloniaPropertyAlias &&
                    propertyAlias.AvaloniaPropertyOwnerType is not null &&
                    TryFindAvaloniaPropertyField(
                        propertyAlias.AvaloniaPropertyOwnerType,
                        propertyAlias.ResolvedPropertyName,
                        out var aliasedOwnerType,
                        out var aliasedPropertyField,
                        propertyAlias.AvaloniaPropertyFieldName))
                {
                    resolvedPropertyName = propertyAlias.ResolvedPropertyName;
                    setterPropertyOwnerTypeName = aliasedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = aliasedPropertyField.Name;
                    setterValueType = TryGetAvaloniaPropertyValueType(aliasedPropertyField.Type);
                }

                if (targetType is not null &&
                    TrySplitOwnerQualifiedPropertyToken(
                        setter.PropertyName,
                        out var ownerToken,
                        out var attachedPropertyName))
                {
                    var explicitOwnerType = ResolveTypeToken(
                        compilation,
                        document,
                        ownerToken,
                        document.ClassNamespace);
                    if (explicitOwnerType is not null &&
                        TryFindAvaloniaPropertyField(
                            explicitOwnerType,
                            attachedPropertyName,
                            out var attachedOwnerType,
                            out var attachedPropertyField))
                    {
                        resolvedPropertyName = attachedPropertyName;
                        setterPropertyOwnerTypeName =
                            attachedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        setterPropertyFieldName = attachedPropertyField.Name;
                        setterValueType = TryGetAvaloniaPropertyValueType(attachedPropertyField.Type);
                    }
                }

                if (targetType is not null)
                {
                    targetProperty = FindProperty(targetType, normalizedPropertyName);
                    if (targetProperty is not null)
                    {
                        resolvedPropertyName = targetProperty.Name;
                    }
                }

                setterValueType ??= targetProperty?.Type;
                if (targetType is not null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                    TryFindAvaloniaPropertyField(targetType, resolvedPropertyName, out var themePropertyOwnerType, out var themePropertyField))
                {
                    setterPropertyOwnerTypeName =
                        themePropertyOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = themePropertyField.Name;
                    setterValueType ??= TryGetAvaloniaPropertyValueType(themePropertyField.Type);
                }

                if (targetType is not null &&
                    targetProperty is null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0303",
                        $"ControlTheme setter property '{setter.PropertyName}' was not found on '{targetType.ToDisplayString()}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var duplicatePropertyKey = !string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                                           !string.IsNullOrWhiteSpace(setterPropertyFieldName)
                    ? setterPropertyOwnerTypeName + "." + setterPropertyFieldName
                    : resolvedPropertyName;
                if (!seenSetterProperties.Add(duplicatePropertyKey))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"ControlTheme setter property '{resolvedPropertyName}' is duplicated.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var valueExpression = string.Empty;
                var valueResolvedFromMarkup = false;
                var valueKind = ResolvedValueKind.Literal;
                var requiresStaticResourceResolver = false;
                var valueRequirements = ResolvedValueRequirements.None;

                var isCompiledBinding = false;
                string? compiledBindingPath = null;
                string? compiledBindingSourceType = null;
                if (TryConvertCSharpExpressionMarkupToBindingExpression(
                        setter.Value,
                        compilation,
                        document,
                        options,
                        themeDataType,
                        out var isExpressionMarkup,
                        out var expressionBindingValueExpression,
                        out var expressionAccessorExpression,
                        out var normalizedExpression,
                        out var expressionErrorCode,
                        out var expressionErrorMessage))
                {
                    var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                    var expressionPath = "{= " + normalizedExpression + " }";
                    var sourceTypeName = themeDataType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: resolvedPropertyName,
                        Path: expressionPath,
                        SourceTypeName: sourceTypeName,
                        AccessorExpression: expressionAccessorExpression,
                        IsSetterBinding: true,
                        Line: setter.Line,
                        Column: setter.Column));

                    setters.Add(new ResolvedSetterDefinition(
                        PropertyName: resolvedPropertyName,
                        ValueExpression: expressionBindingValueExpression,
                        IsCompiledBinding: true,
                        CompiledBindingPath: expressionPath,
                        CompiledBindingSourceTypeName: sourceTypeName,
                        AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                        AvaloniaPropertyFieldName: setterPropertyFieldName,
                        Line: setter.Line,
                        Column: setter.Column,
                        Condition: setter.Condition,
                        ValueKind: ResolvedValueKind.Binding,
                        ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true)));
                    continue;
                }

                if (isExpressionMarkup)
                {
                    var message = expressionErrorCode == "AXSG0110"
                        ? $"Expression binding for control theme setter '{setter.PropertyName}' requires x:DataType on the control theme."
                        : $"Expression binding '{setter.Value}' is invalid for source type '{themeDataType?.ToDisplayString() ?? "unknown"}': {expressionErrorMessage}";
                    diagnostics.Add(new DiagnosticInfo(
                        expressionErrorCode,
                        message,
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                    continue;
                }

                if (TryParseBindingMarkup(setter.Value, out var bindingMarkup))
                {
                    if (!TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            setter.Line,
                            setter.Column,
                            options.StrictMode) &&
                        CanUseCompiledBinding(bindingMarkup) &&
                        (bindingMarkup.IsCompiledBinding || compileBindingsEnabled))
                    {
                        if (themeDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for control theme setter '{setter.PropertyName}' requires x:DataType on the control theme.",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else if (!TryBuildCompiledBindingAccessorExpression(
                                     compilation,
                                     document,
                                     themeDataType,
                                     bindingMarkup.Path,
                                     out var accessorExpression,
                                     out var normalizedPath,
                                     out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{themeDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else
                        {
                            var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                            compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                                TargetTypeName: targetTypeName,
                                TargetPropertyName: resolvedPropertyName,
                                Path: normalizedPath,
                                SourceTypeName: themeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                AccessorExpression: accessorExpression,
                                IsSetterBinding: true,
                                Line: setter.Line,
                                Column: setter.Column));

                            isCompiledBinding = true;
                            compiledBindingPath = normalizedPath;
                            compiledBindingSourceType = themeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                    }

                    if (!isCompiledBinding &&
                        !bindingMarkup.HasSourceConflict &&
                        TryBuildRuntimeBindingExpression(
                            compilation,
                            document,
                            bindingMarkup,
                            targetType,
                            BindingPriorityScope.Style,
                            out var runtimeBindingExpression))
                    {
                        valueExpression = runtimeBindingExpression;
                        valueResolvedFromMarkup = true;
                        valueKind = ResolvedValueKind.Binding;
                        valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
                    }
                }

                var conversionTargetType = setterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                var hasKnownSetterValueType = setterValueType is not null;
                if (!valueResolvedFromMarkup &&
                    TryResolveSetterValueWithPolicy(
                        rawValue: setter.Value,
                        conversionTargetType: conversionTargetType,
                        compilation: compilation,
                        document: document,
                        setterTargetType: targetType,
                        bindingPriorityScope: BindingPriorityScope.Style,
                        strictMode: options.StrictMode,
                        preferTypedStaticResourceCoercion: string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName),
                        allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                        hasKnownSetterValueType &&
                                                                        conversionTargetType.SpecialType == SpecialType.System_Object,
                        allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                 conversionTargetType.SpecialType == SpecialType.System_Object,
                        propertyName: resolvedPropertyName,
                        ownerDisplayName: targetType?.ToDisplayString() ?? "control theme",
                        line: setter.Line,
                        column: setter.Column,
                        diagnostics: diagnostics,
                        resolution: out var controlThemeSetterResolution,
                        setterContext: true))
                {
                    valueExpression = controlThemeSetterResolution.Expression;
                    valueResolvedFromMarkup = true;
                    valueKind = controlThemeSetterResolution.ValueKind;
                    requiresStaticResourceResolver = controlThemeSetterResolution.RequiresStaticResourceResolver;
                    valueRequirements = controlThemeSetterResolution.ValueRequirements;
                }

                if (!valueResolvedFromMarkup)
                {
                    continue;
                }

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: resolvedPropertyName,
                    ValueExpression: valueExpression,
                    IsCompiledBinding: isCompiledBinding,
                    CompiledBindingPath: compiledBindingPath,
                    CompiledBindingSourceTypeName: compiledBindingSourceType,
                    AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                    AvaloniaPropertyFieldName: setterPropertyFieldName,
                    Line: setter.Line,
                    Column: setter.Column,
                    Condition: setter.Condition,
                    ValueKind: isCompiledBinding
                        ? ResolvedValueKind.Binding
                        : valueKind,
                    RequiresStaticResourceResolver: requiresStaticResourceResolver,
                    ValueRequirements: valueRequirements));
            }

            controlThemes.Add(new ResolvedControlThemeDefinition(
                Key: controlTheme.Key,
                TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                BasedOn: controlTheme.BasedOn,
                ThemeVariant: themeVariant,
                Setters: setters.ToImmutable(),
                RawXaml: controlTheme.RawXaml,
                Line: controlTheme.Line,
                Column: controlTheme.Column,
                Condition: controlTheme.Condition));
        }

        var resolvedControlThemes = controlThemes.ToImmutable();
        ValidateControlThemeBasedOnChains(
            resolvedControlThemes,
            diagnostics,
            document,
            options);

        return resolvedControlThemes;
    }

    private static void ValidateControlThemeBasedOnChains(
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options)
    {
        if (controlThemes.IsDefaultOrEmpty)
        {
            return;
        }

        var byKey = new Dictionary<string, ResolvedControlThemeDefinition>(StringComparer.Ordinal);
        foreach (var controlTheme in controlThemes)
        {
            if (string.IsNullOrWhiteSpace(controlTheme.Key))
            {
                continue;
            }

            byKey[controlTheme.Key.Trim()] = controlTheme;
        }

        foreach (var controlTheme in controlThemes)
        {
            var basedOnKey = TryExtractControlThemeBasedOnKey(controlTheme.BasedOn);
            if (string.IsNullOrWhiteSpace(basedOnKey))
            {
                continue;
            }

            if (!byKey.ContainsKey(basedOnKey))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0305",
                    $"ControlTheme '{controlTheme.Key ?? "<unnamed>"}' references missing BasedOn theme key '{basedOnKey}'.",
                    document.FilePath,
                    controlTheme.Line,
                    controlTheme.Column,
                    options.StrictMode));
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in byKey.Keys)
        {
            DetectCycle(key, key);
        }

        void DetectCycle(string key, string startKey)
        {
            if (!byKey.TryGetValue(key, out var currentTheme))
            {
                return;
            }

            if (state.TryGetValue(key, out var currentState))
            {
                if (currentState == 2)
                {
                    return;
                }

                if (currentState == 1)
                {
                    var cycleKey = startKey + "->" + key;
                    if (emitted.Add(cycleKey))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0306",
                            $"ControlTheme BasedOn chain contains a cycle at key '{key}'.",
                            document.FilePath,
                            currentTheme.Line,
                            currentTheme.Column,
                            true));
                    }

                    return;
                }
            }

            state[key] = 1;
            var basedOnKey = TryExtractControlThemeBasedOnKey(currentTheme.BasedOn);
            if (!string.IsNullOrWhiteSpace(basedOnKey) && byKey.ContainsKey(basedOnKey))
            {
                DetectCycle(basedOnKey, startKey);
            }

            state[key] = 2;
        }
    }

    private static string? TryExtractControlThemeBasedOnKey(string? basedOnExpression)
    {
        if (string.IsNullOrWhiteSpace(basedOnExpression))
        {
            return null;
        }

        var trimmed = basedOnExpression.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) &&
            trimmed.EndsWith("}", StringComparison.Ordinal) &&
            TryParseMarkupExtension(trimmed, out var markup))
        {
            var markupName = markup.Name.ToLowerInvariant();
            if (markupName != "staticresource" && markupName != "dynamicresource")
            {
                return null;
            }

            var resourceKey = TryGetNamedMarkupArgument(markup, "ResourceKey", "Key");
            if (string.IsNullOrWhiteSpace(resourceKey) && markup.PositionalArguments.Length > 0)
            {
                resourceKey = Unquote(markup.PositionalArguments[0]);
            }

            return string.IsNullOrWhiteSpace(resourceKey)
                ? null
                : resourceKey.Trim();
        }

        return Unquote(trimmed);
    }

    private static ImmutableArray<ResolvedIncludeDefinition> BindIncludes(
        XamlDocumentModel document,
        Compilation compilation,
        string currentDocumentUri,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var includes = ImmutableArray.CreateBuilder<ResolvedIncludeDefinition>(document.Includes.Length);

        foreach (var include in document.Includes)
        {
            if (ShouldSkipConditionalBranch(
                    include.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(include.Source))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0400",
                    $"Include '{include.Kind}' is missing Source.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            var normalizedIncludeSource = NormalizeIncludeSourceForResolution(include.Source);
            if (!Uri.TryCreate(normalizedIncludeSource, UriKind.RelativeOrAbsolute, out var sourceUri))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0401",
                    $"Include source '{include.Source}' is not a valid URI.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            if (include.MergeTarget == "Unknown")
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0402",
                    $"Include '{include.Kind}' is outside known merge groups (MergedDictionaries/Styles).",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    false));
            }

            var resolvedIncludeUri = ResolveIncludedBuildUri(
                normalizedIncludeSource,
                document.TargetPath,
                currentDocumentUri,
                out var isProjectLocalInclude);

            includes.Add(new ResolvedIncludeDefinition(
                Kind: include.Kind,
                Source: include.Source,
                MergeTarget: include.MergeTarget,
                IsAbsoluteUri: sourceUri.IsAbsoluteUri,
                ResolvedSourceUri: resolvedIncludeUri,
                IsProjectLocal: isProjectLocalInclude,
                RawXaml: include.RawXaml,
                Line: include.Line,
                Column: include.Column,
                Condition: include.Condition));
        }

        return includes.ToImmutable();
    }

    private static string? ResolveIncludedBuildUri(
        string includeSource,
        string currentTargetPath,
        string currentDocumentUri,
        out bool isProjectLocal)
    {
        isProjectLocal = false;
        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return null;
        }

        var trimmedSource = includeSource.Trim();
        if (!Uri.TryCreate(currentDocumentUri, UriKind.Absolute, out var currentUri) ||
            !currentUri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var currentAssembly = currentUri.Host;
        if (string.IsNullOrWhiteSpace(currentAssembly))
        {
            return null;
        }

        if (trimmedSource.StartsWith("/", StringComparison.Ordinal))
        {
            var normalizedRootedPath = NormalizeIncludePath(trimmedSource.TrimStart('/'));
            if (normalizedRootedPath.Length == 0)
            {
                return null;
            }

            isProjectLocal = true;
            return "avares://" + currentAssembly + "/" + normalizedRootedPath;
        }

        if (Uri.TryCreate(trimmedSource, UriKind.Absolute, out var absoluteSource))
        {
            if (!absoluteSource.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                return absoluteSource.ToString();
            }

            if (!string.Equals(absoluteSource.Host, currentAssembly, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteSource.ToString();
            }

            var normalizedAbsolutePath = NormalizeIncludePath(absoluteSource.AbsolutePath.TrimStart('/'));
            if (normalizedAbsolutePath.Length == 0)
            {
                return null;
            }

            isProjectLocal = true;
            return "avares://" + currentAssembly + "/" + normalizedAbsolutePath;
        }

        var normalizedCurrentPath = NormalizeIncludePath(currentTargetPath);
        var currentDirectory = GetIncludeDirectory(normalizedCurrentPath);
        var normalizedIncludePath = trimmedSource.StartsWith("/", StringComparison.Ordinal)
            ? NormalizeIncludePath(trimmedSource.TrimStart('/'))
            : NormalizeIncludePath(CombineIncludePath(currentDirectory, trimmedSource));

        if (normalizedIncludePath.Length == 0)
        {
            return null;
        }

        isProjectLocal = true;
        return "avares://" + currentAssembly + "/" + normalizedIncludePath;
    }

    private static string NormalizeIncludeSourceForResolution(string includeSource)
    {
        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return includeSource;
        }

        var trimmedSource = includeSource.Trim();
        if (!TryParseMarkupExtension(trimmedSource, out var markup))
        {
            return trimmedSource;
        }

        var markupName = markup.Name.ToLowerInvariant();
        if (markupName is not ("x:uri" or "uri"))
        {
            return trimmedSource;
        }

        var uriToken = markup.NamedArguments.TryGetValue("Uri", out var explicitUri)
            ? explicitUri
            : markup.NamedArguments.TryGetValue("Value", out var explicitValue)
                ? explicitValue
                : markup.PositionalArguments.Length > 0
                    ? markup.PositionalArguments[0]
                    : null;

        return string.IsNullOrWhiteSpace(uriToken)
            ? trimmedSource
            : Unquote(uriToken!).Trim();
    }

    private static string GetIncludeDirectory(string targetPath)
    {
        var lastSeparator = targetPath.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            return string.Empty;
        }

        return targetPath.Substring(0, lastSeparator);
    }

    private static string CombineIncludePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return relativePath;
        }

        return baseDirectory + "/" + relativePath;
    }

    private static string NormalizeIncludePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalizedSeparators = path.Replace('\\', '/');
        var parts = normalizedSeparators.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return string.Join("/", stack);
    }

    private static ImmutableArray<ResolvedResourceDefinition> BindResources(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedResourceDefinition>(document.Resources.Length);

        foreach (var resource in document.Resources)
        {
            if (ShouldSkipConditionalBranch(
                    resource.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var typeName = ResolveTypeName(compilation, resource.XmlNamespace, resource.XmlTypeName, out var symbol);
            if (symbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0100",
                    $"Could not resolve resource type '{resource.XmlTypeName}' for key '{resource.Key}'. Falling back to System.Object metadata.",
                    document.FilePath,
                    resource.Line,
                    resource.Column,
                    false));
                typeName = "global::System.Object";
            }

            result.Add(new ResolvedResourceDefinition(
                Key: resource.Key,
                TypeName: typeName!,
                RawXaml: resource.RawXaml,
                Line: resource.Line,
                Column: resource.Column,
                Condition: resource.Condition));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<ResolvedTemplateDefinition> BindTemplates(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedTemplateDefinition>(document.Templates.Length);

        foreach (var template in document.Templates)
        {
            if (ShouldSkipConditionalBranch(
                    template.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            string? targetTypeName = null;
            INamedTypeSymbol? controlTemplateTargetType = null;

            if (!IsKnownTemplateKind(template.Kind))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Template kind '{template.Kind}' is not recognized as a template node.",
                    document.FilePath,
                    template.Line,
                    template.Column,
                    false));
                continue;
            }

            if ((template.Kind.Equals("DataTemplate", StringComparison.Ordinal) ||
                 template.Kind.Equals("TreeDataTemplate", StringComparison.Ordinal)) &&
                string.IsNullOrWhiteSpace(template.DataType))
            {
                if (options.StrictMode)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0500",
                        $"Template '{template.Kind}' should declare x:DataType for compiled-binding safety.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        true));
                }
            }

            if (template.Kind.Equals("ControlTemplate", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(template.TargetType))
                {
                    if (options.StrictMode)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0501",
                            "ControlTemplate requires TargetType for source-generated validation.",
                            document.FilePath,
                            template.Line,
                            template.Column,
                            true));
                    }
                }
                else
                {
                    controlTemplateTargetType = ResolveTypeFromTypeExpression(
                        compilation,
                        document,
                        template.TargetType,
                        document.ClassNamespace);
                    if (controlTemplateTargetType is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0501",
                            $"ControlTemplate target type '{template.TargetType}' could not be resolved.",
                            document.FilePath,
                            template.Line,
                            template.Column,
                            options.StrictMode));
                    }
                    else
                    {
                        targetTypeName = controlTemplateTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        ValidateControlTemplateParts(
                            template,
                            controlTemplateTargetType,
                            compilation,
                            document,
                            diagnostics,
                            options);
                    }
                }
            }

            ValidateTemplateContentRootType(
                template,
                compilation,
                document,
                diagnostics,
                options);

            result.Add(new ResolvedTemplateDefinition(
                Kind: template.Kind,
                Key: template.Key,
                TargetTypeName: targetTypeName,
                DataType: template.DataType,
                RawXaml: template.RawXaml,
                Line: template.Line,
                Column: template.Column,
                Condition: template.Condition));
        }

        return result.ToImmutable();
    }

    private static void ValidateControlTemplateParts(
        XamlTemplateDefinition template,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var expectedParts = ResolveControlTemplatePartExpectations(targetType);
        if (expectedParts.Count == 0)
        {
            return;
        }

        if (!TryFindTemplateNode(document, template, out var templateNode))
        {
            return;
        }

        if (!TryCollectControlTemplateNamedParts(templateNode, compilation, out var actualParts))
        {
            return;
        }

        foreach (var pair in expectedParts)
        {
            var partName = pair.Key;
            var expectation = pair.Value;
            if (!actualParts.TryGetValue(partName, out var actual))
            {
                if (expectation.IsRequired)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0502",
                        $"Required template part with x:Name '{partName}' must be defined on '{targetType.Name}' ControlTemplate.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        true));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0504",
                        $"Optional template part with x:Name '{partName}' can be defined on '{targetType.Name}' ControlTemplate.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        options.StrictMode));
                }

                continue;
            }

            if (expectation.ExpectedType is not null &&
                actual.Type is not null &&
                !IsTypeAssignableTo(actual.Type, expectation.ExpectedType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0503",
                    $"Template part '{partName}' is expected to be assignable to '{expectation.ExpectedType.Name}', but actual type is {actual.Type.Name}.",
                    document.FilePath,
                    actual.Line,
                    actual.Column,
                    true));
            }
        }
    }

    private static void ValidateTemplateContentRootType(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var expectedTypeMetadataName = template.Kind switch
        {
            "ItemsPanelTemplate" => "Avalonia.Controls.Panel",
            "ControlTemplate" => "Avalonia.Controls.Control",
            "DataTemplate" => "Avalonia.Controls.Control",
            "TreeDataTemplate" => "Avalonia.Controls.Control",
            _ => null
        };

        if (expectedTypeMetadataName is null)
        {
            return;
        }

        var expectedType = compilation.GetTypeByMetadataName(expectedTypeMetadataName);
        if (expectedType is null)
        {
            return;
        }

        if (!TryFindTemplateNode(document, template, out var templateNode))
        {
            return;
        }

        if (!TryGetTemplateContentRootNode(templateNode, out var contentRoot))
        {
            return;
        }

        var actualType = ResolveTypeSymbol(compilation, contentRoot.XmlNamespace, contentRoot.XmlTypeName);
        if (actualType is null || IsTypeAssignableTo(actualType, expectedType))
        {
            return;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0506",
            $"Template '{template.Kind}' content root '{contentRoot.XmlTypeName}' is expected to be assignable to '{expectedType.Name}', but actual type is '{actualType.Name}'.",
            document.FilePath,
            contentRoot.Line,
            contentRoot.Column,
            options.StrictMode));
    }

    private static bool TryGetTemplateContentRootNode(XamlObjectNode templateNode, out XamlObjectNode contentRoot)
    {
        foreach (var propertyElement in templateNode.PropertyElements)
        {
            if (!NormalizePropertyName(propertyElement.PropertyName).Equals("Content", StringComparison.Ordinal))
            {
                continue;
            }

            if (propertyElement.ObjectValues.Length > 0)
            {
                contentRoot = propertyElement.ObjectValues[0];
                return true;
            }
        }

        if (templateNode.ChildObjects.Length > 0)
        {
            contentRoot = templateNode.ChildObjects[0];
            return true;
        }

        contentRoot = default!;
        return false;
    }

    private static void ValidateItemContainerInsideTemplateWarning(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyElement propertyElement,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        if (!property.Name.Equals("ItemTemplate", StringComparison.Ordinal) &&
            !property.Name.Equals("DataTemplates", StringComparison.Ordinal))
        {
            return;
        }

        var itemsControlType = compilation.GetTypeByMetadataName("Avalonia.Controls.ItemsControl");
        if (itemsControlType is null || !IsTypeAssignableTo(objectType, itemsControlType))
        {
            return;
        }

        var knownContainerType = ResolveKnownItemContainerType(objectType, compilation);
        if (knownContainerType is null)
        {
            return;
        }

        var contentControlType = compilation.GetTypeByMetadataName("Avalonia.Controls.ContentControl");
        if (contentControlType is null)
        {
            return;
        }

        foreach (var templateNode in propertyElement.ObjectValues)
        {
            if (!IsDataTemplateNode(templateNode))
            {
                continue;
            }

            var contentNode = TryGetTemplateContentNode(templateNode);
            if (contentNode is null)
            {
                continue;
            }

            var contentType = ResolveTypeSymbol(compilation, contentNode.XmlNamespace, contentNode.XmlTypeName);
            if (contentType is null ||
                !IsTypeAssignableTo(contentType, contentControlType) ||
                !IsTypeAssignableTo(contentType, knownContainerType))
            {
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0505",
                $"Unexpected '{knownContainerType.Name}' inside of '{objectType.Name}.{property.Name}'. '{objectType.Name}.{property.Name}' defines template of the container content, not the container itself.",
                document.FilePath,
                contentNode.Line,
                contentNode.Column,
                options.StrictMode));
        }
    }

    private static INamedTypeSymbol? ResolveKnownItemContainerType(
        INamedTypeSymbol itemsControlType,
        Compilation compilation)
    {
        foreach (var mapping in KnownItemContainerTypeMappings)
        {
            var mappedControlType = compilation.GetTypeByMetadataName(mapping.ItemsControlMetadataName);
            if (mappedControlType is null || !IsTypeAssignableTo(itemsControlType, mappedControlType))
            {
                continue;
            }

            return compilation.GetTypeByMetadataName(mapping.ItemContainerMetadataName);
        }

        return null;
    }

    private static bool IsDataTemplateNode(XamlObjectNode node)
    {
        return node.XmlTypeName is "DataTemplate" or "TreeDataTemplate";
    }

    private static XamlObjectNode? TryGetTemplateContentNode(XamlObjectNode templateNode)
    {
        foreach (var propertyElement in templateNode.PropertyElements)
        {
            if (NormalizePropertyName(propertyElement.PropertyName).Equals("Content", StringComparison.Ordinal) &&
                propertyElement.ObjectValues.Length > 0)
            {
                return propertyElement.ObjectValues[0];
            }
        }

        if (templateNode.ChildObjects.Length > 0)
        {
            return templateNode.ChildObjects[0];
        }

        return null;
    }

    private static Dictionary<string, TemplatePartExpectation> ResolveControlTemplatePartExpectations(INamedTypeSymbol targetType)
    {
        var result = new Dictionary<string, TemplatePartExpectation>(StringComparer.Ordinal);
        for (var current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!attribute.AttributeClass?.Name.Equals("TemplatePartAttribute", StringComparison.Ordinal) ?? true)
                {
                    continue;
                }

                var partName = TryGetTemplatePartName(attribute);
                if (string.IsNullOrWhiteSpace(partName) || result.ContainsKey(partName!))
                {
                    continue;
                }

                result.Add(partName!, new TemplatePartExpectation(
                    expectedType: TryGetTemplatePartType(attribute),
                    isRequired: TryGetTemplatePartIsRequired(attribute)));
            }
        }

        return result;
    }

    private static string? TryGetTemplatePartName(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("Name", StringComparison.Ordinal) &&
                namedArgument.Value.Value is string namedName &&
                !string.IsNullOrWhiteSpace(namedName))
            {
                return namedName;
            }
        }

        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is string ctorName &&
            !string.IsNullOrWhiteSpace(ctorName))
        {
            return ctorName;
        }

        return null;
    }

    private static ITypeSymbol? TryGetTemplatePartType(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("Type", StringComparison.Ordinal) &&
                namedArgument.Value.Kind == TypedConstantKind.Type &&
                namedArgument.Value.Value is ITypeSymbol namedType)
            {
                return namedType;
            }
        }

        if (attribute.ConstructorArguments.Length > 1 &&
            attribute.ConstructorArguments[1].Kind == TypedConstantKind.Type &&
            attribute.ConstructorArguments[1].Value is ITypeSymbol ctorType)
        {
            return ctorType;
        }

        return null;
    }

    private static bool TryGetTemplatePartIsRequired(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("IsRequired", StringComparison.Ordinal) &&
                namedArgument.Value.Value is bool isRequired)
            {
                return isRequired;
            }
        }

        return false;
    }

    private static bool TryCollectControlTemplateNamedParts(
        XamlObjectNode templateNode,
        Compilation compilation,
        out ImmutableDictionary<string, TemplatePartActual> parts)
    {
        var result = ImmutableDictionary.CreateBuilder<string, TemplatePartActual>(StringComparer.Ordinal);
        CollectControlTemplateNamedPartsRecursive(templateNode, compilation, result, isTemplateRoot: true);
        parts = result.ToImmutable();
        return true;
    }

    private static void CollectControlTemplateNamedPartsRecursive(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableDictionary<string, TemplatePartActual>.Builder result,
        bool isTemplateRoot)
    {
        if (!isTemplateRoot && IsTemplateLikeElement(node.XmlTypeName))
        {
            return;
        }

        if (TryGetNodeNameScopeRegistration(node, out var partName, out var line, out var column) &&
            !result.ContainsKey(partName))
        {
            var type = ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
            result[partName] = new TemplatePartActual(
                type: type,
                line: line,
                column: column);
        }

        foreach (var constructorArgument in node.ConstructorArguments)
        {
            CollectControlTemplateNamedPartsRecursive(constructorArgument, compilation, result, isTemplateRoot: false);
        }

        foreach (var child in node.ChildObjects)
        {
            CollectControlTemplateNamedPartsRecursive(child, compilation, result, isTemplateRoot: false);
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                CollectControlTemplateNamedPartsRecursive(objectValue, compilation, result, isTemplateRoot: false);
            }
        }
    }

    private static bool TryGetNodeNameScopeRegistration(
        XamlObjectNode node,
        out string name,
        out int line,
        out int column)
    {
        name = string.Empty;
        line = node.Line;
        column = node.Column;

        if (TryParseNameScopeRegistrationValue(node.Name ?? string.Empty, out var parsedNodeName))
        {
            name = parsedNodeName;
            return true;
        }

        foreach (var assignment in node.PropertyAssignments)
        {
            if (!NormalizePropertyName(assignment.PropertyName).Equals("Name", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseNameScopeRegistrationValue(assignment.Value, out var parsedName))
            {
                continue;
            }

            name = parsedName;
            line = assignment.Line;
            column = assignment.Column;
            return true;
        }

        return false;
    }

    private static bool TryParseNameScopeRegistrationValue(string rawValue, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (TryParseMarkupExtension(trimmed, out _))
        {
            return false;
        }

        if (!TryNormalizeReferenceName(trimmed, out var normalizedName))
        {
            return false;
        }

        name = normalizedName;
        return true;
    }

    private static string? NormalizeObjectNodeName(string? rawName)
    {
        return TryParseNameScopeRegistrationValue(rawName ?? string.Empty, out var name)
            ? name
            : null;
    }

    private static bool TryFindTemplateNode(
        XamlDocumentModel document,
        XamlTemplateDefinition template,
        out XamlObjectNode templateNode)
    {
        foreach (var node in EnumerateObjectNodeSubtree(document.RootObject))
        {
            if (!node.XmlTypeName.Equals(template.Kind, StringComparison.Ordinal))
            {
                continue;
            }

            if (node.Line == template.Line &&
                node.Column == template.Column)
            {
                templateNode = node;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(template.Key))
        {
            foreach (var node in EnumerateObjectNodeSubtree(document.RootObject))
            {
                if (!node.XmlTypeName.Equals(template.Kind, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(node.Key, template.Key, StringComparison.Ordinal))
                {
                    templateNode = node;
                    return true;
                }
            }
        }

        foreach (var node in EnumerateObjectNodeSubtree(document.RootObject))
        {
            if (node.XmlTypeName.Equals(template.Kind, StringComparison.Ordinal))
            {
                templateNode = node;
                return true;
            }
        }

        templateNode = default!;
        return false;
    }

    private static IEnumerable<XamlObjectNode> EnumerateObjectNodeSubtree(XamlObjectNode node)
    {
        yield return node;

        foreach (var constructorArgument in node.ConstructorArguments)
        {
            foreach (var descendant in EnumerateObjectNodeSubtree(constructorArgument))
            {
                yield return descendant;
            }
        }

        foreach (var child in node.ChildObjects)
        {
            foreach (var descendant in EnumerateObjectNodeSubtree(child))
            {
                yield return descendant;
            }
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                foreach (var descendant in EnumerateObjectNodeSubtree(objectValue))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static bool IsTemplateLikeElement(string localName)
    {
        return IsKnownTemplateKind(localName);
    }

    private static bool IsKnownTemplateKind(string kind)
    {
        return kind is "DataTemplate" or "ControlTemplate" or "ItemsPanelTemplate" or "TreeDataTemplate";
    }

    private static bool TryParseBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        bindingMarkup = default;
        if (!TryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        var extensionName = markup.Name;
        if (!extensionName.Equals("Binding", StringComparison.OrdinalIgnoreCase) &&
            !extensionName.Equals("CompiledBinding", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionName, out bindingMarkup);
    }

    private static bool TryParseReflectionBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        bindingMarkup = default;
        if (!TryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        var extensionName = markup.Name;
        if (!extensionName.Equals("ReflectionBinding", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionName, out bindingMarkup);
    }

    private static bool TryParseBindingMarkupCore(
        MarkupExtensionInfo markup,
        string extensionName,
        out BindingMarkup bindingMarkup)
    {
        var path = string.Empty;
        if (markup.NamedArguments.TryGetValue("Path", out var explicitPath))
        {
            path = Unquote(explicitPath);
        }
        else if (markup.PositionalArguments.Length > 0)
        {
            path = Unquote(markup.PositionalArguments[0]);
        }

        var parsedMarkup = new BindingMarkup(
            isCompiledBinding: extensionName.Equals("CompiledBinding", StringComparison.OrdinalIgnoreCase),
            path: path,
            mode: TryGetNamedMarkupArgument(markup, "Mode"),
            elementName: TryGetNamedMarkupArgument(markup, "ElementName"),
            relativeSource: markup.NamedArguments.TryGetValue("RelativeSource", out var relativeSourceValue) &&
                            TryParseRelativeSourceMarkup(relativeSourceValue, out var relativeSourceMarkup)
                ? relativeSourceMarkup
                : null,
            source: TryGetNamedMarkupArgument(markup, "Source"),
            converter: TryGetNamedMarkupArgument(markup, "Converter"),
            converterCulture: TryGetNamedMarkupArgument(markup, "ConverterCulture"),
            converterParameter: TryGetNamedMarkupArgument(markup, "ConverterParameter"),
            stringFormat: TryGetNamedMarkupArgument(markup, "StringFormat", "Format"),
            fallbackValue: TryGetNamedMarkupArgument(markup, "FallbackValue", "Fallback"),
            targetNullValue: TryGetNamedMarkupArgument(markup, "TargetNullValue", "NullValue"),
            delay: TryGetNamedMarkupArgument(markup, "Delay"),
            priority: TryGetNamedMarkupArgument(markup, "Priority", "BindingPriority"),
            updateSourceTrigger: TryGetNamedMarkupArgument(markup, "UpdateSourceTrigger", "Trigger"),
            hasSourceConflict: false,
            sourceConflictMessage: null);

        bindingMarkup = NormalizeBindingQuerySyntax(parsedMarkup);

        return true;
    }

    private static string? TryGetNamedMarkupArgument(MarkupExtensionInfo markup, params string[] argumentNames)
    {
        foreach (var argumentName in argumentNames)
        {
            if (markup.NamedArguments.TryGetValue(argumentName, out var value))
            {
                return Unquote(value);
            }
        }

        return null;
    }

    private static BindingMarkup NormalizeBindingQuerySyntax(BindingMarkup bindingMarkup)
    {
        if (CountExplicitBindingSources(bindingMarkup) > 1)
        {
            return CreateBindingSourceConflict(
                bindingMarkup,
                "Only one binding source may be specified. Use only one of ElementName, RelativeSource, or Source.");
        }

        var normalizedBindingMarkup = bindingMarkup;
        if (TryExtractReferenceElementName(normalizedBindingMarkup.Source, out var referenceElementName))
        {
            normalizedBindingMarkup = new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedBindingMarkup.Path,
                mode: normalizedBindingMarkup.Mode,
                elementName: referenceElementName,
                relativeSource: normalizedBindingMarkup.RelativeSource,
                source: null,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        var trimmedPath = normalizedBindingMarkup.Path.Trim();
        if (trimmedPath.Length == 0)
        {
            return normalizedBindingMarkup;
        }

        if (TryParseElementNameQuery(trimmedPath, out var elementName, out var normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '#name' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: elementName,
                relativeSource: normalizedBindingMarkup.RelativeSource,
                source: normalizedBindingMarkup.Source,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        if (TryParseSelfQuery(trimmedPath, out var selfRelativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$self' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: selfRelativeSource,
                source: normalizedBindingMarkup.Source,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        if (TryParseParentQuery(trimmedPath, out var relativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$parent' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: relativeSource,
                source: normalizedBindingMarkup.Source,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        return normalizedBindingMarkup;
    }

    private static bool HasExplicitBindingSource(BindingMarkup bindingMarkup)
    {
        return CountExplicitBindingSources(bindingMarkup) > 0;
    }

    private static int CountExplicitBindingSources(BindingMarkup bindingMarkup)
    {
        var sourceCount = 0;
        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            sourceCount++;
        }

        if (bindingMarkup.RelativeSource is not null)
        {
            sourceCount++;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            sourceCount++;
        }

        return sourceCount;
    }

    private static BindingMarkup CreateBindingSourceConflict(BindingMarkup bindingMarkup, string message)
    {
        if (bindingMarkup.HasSourceConflict)
        {
            return bindingMarkup;
        }

        return new BindingMarkup(
            isCompiledBinding: bindingMarkup.IsCompiledBinding,
            path: bindingMarkup.Path,
            mode: bindingMarkup.Mode,
            elementName: bindingMarkup.ElementName,
            relativeSource: bindingMarkup.RelativeSource,
            source: bindingMarkup.Source,
            converter: bindingMarkup.Converter,
            converterCulture: bindingMarkup.ConverterCulture,
            converterParameter: bindingMarkup.ConverterParameter,
            stringFormat: bindingMarkup.StringFormat,
            fallbackValue: bindingMarkup.FallbackValue,
            targetNullValue: bindingMarkup.TargetNullValue,
            delay: bindingMarkup.Delay,
            priority: bindingMarkup.Priority,
            updateSourceTrigger: bindingMarkup.UpdateSourceTrigger,
            hasSourceConflict: true,
            sourceConflictMessage: message);
    }

    private static bool TryExtractReferenceElementName(string? sourceValue, out string elementName)
    {
        elementName = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceValue))
        {
            return false;
        }

        if (!TryParseResolveByNameReferenceToken(sourceValue!, out var referenceToken) ||
            !referenceToken.FromMarkupExtension)
        {
            return false;
        }

        elementName = referenceToken.Name;
        return elementName.Length > 0;
    }

    private static bool HasResolveByNameSemantics(INamedTypeSymbol ownerType, string propertyName)
    {
        var property = FindProperty(ownerType, propertyName);
        if (property is not null)
        {
            if (HasResolveByNameAttribute(property) ||
                (property.GetMethod is not null && HasResolveByNameAttribute(property.GetMethod)) ||
                (property.SetMethod is not null && HasResolveByNameAttribute(property.SetMethod)))
            {
                return true;
            }
        }

        var setterName = "Set" + propertyName;
        var getterName = "Get" + propertyName;
        for (var current = ownerType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(setterName).OfType<IMethodSymbol>())
            {
                if (HasResolveByNameAttribute(method))
                {
                    return true;
                }
            }

            foreach (var method in current.GetMembers(getterName).OfType<IMethodSymbol>())
            {
                if (HasResolveByNameAttribute(method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasResolveByNameAttribute(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeType = attribute.AttributeClass;
            if (attributeType is null)
            {
                continue;
            }

            if (attributeType.Name.Equals("ResolveByNameAttribute", StringComparison.Ordinal))
            {
                return true;
            }

            if (attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Equals("global::Avalonia.Controls.ResolveByNameAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildResolveByNameLiteralExpression(
        string rawValue,
        ITypeSymbol? targetType,
        out string expression)
    {
        expression = string.Empty;
        if (!TryParseResolveByNameReferenceToken(rawValue, out var referenceToken))
        {
            return false;
        }

        var resolveExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReference(\"" +
            Escape(referenceToken.Name) +
            "\", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", " +
            MarkupContextBaseUriToken +
            ", " +
            MarkupContextParentStackToken +
            ")";

        expression = targetType is null
            ? resolveExpression
            : WrapWithTargetTypeCast(targetType, resolveExpression);
        return true;
    }

    private static bool TryParseResolveByNameReferenceToken(
        string rawValue,
        out ResolveByNameReferenceToken referenceToken)
    {
        referenceToken = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (TryParseMarkupExtension(trimmed, out var markup))
        {
            var markupName = markup.Name.Trim();
            if (!markupName.Equals("x:Reference", StringComparison.OrdinalIgnoreCase) &&
                !markupName.Equals("Reference", StringComparison.OrdinalIgnoreCase) &&
                !markupName.Equals("ResolveByName", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rawName = TryGetNamedMarkupArgument(markup, "Name", "ElementName") ??
                          (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
            if (!TryNormalizeReferenceName(rawName, out var normalizedName))
            {
                return false;
            }

            referenceToken = new ResolveByNameReferenceToken(normalizedName, FromMarkupExtension: true);
            return true;
        }

        if (!TryNormalizeReferenceName(trimmed, out var literalName))
        {
            return false;
        }

        referenceToken = new ResolveByNameReferenceToken(literalName, FromMarkupExtension: false);
        return true;
    }

    private static bool TryNormalizeReferenceName(string? rawName, out string normalizedName)
    {
        normalizedName = string.Empty;
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        var unquoted = Unquote(rawName!).Trim();
        if (unquoted.Length == 0 ||
            unquoted.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }

        normalizedName = unquoted;
        return true;
    }

    private static bool TryParseElementNameQuery(string path, out string elementName, out string normalizedPath)
    {
        elementName = string.Empty;
        normalizedPath = string.Empty;

        if (!path.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        var index = 1;
        while (index < path.Length && IsIdentifierPart(path[index]))
        {
            index++;
        }

        if (index == 1)
        {
            return false;
        }

        elementName = path.Substring(1, index - 1);
        if (index == path.Length)
        {
            normalizedPath = ".";
            return true;
        }

        if (path[index] != '.')
        {
            return false;
        }

        normalizedPath = path.Substring(index + 1).Trim();
        if (normalizedPath.Length == 0)
        {
            normalizedPath = ".";
        }

        return true;
    }

    private static bool TryParseSelfQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        relativeSource = default;
        normalizedPath = string.Empty;

        if (!path.StartsWith("$self", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$self".Length;
        if (index == path.Length)
        {
            normalizedPath = ".";
        }
        else
        {
            if (path[index] != '.')
            {
                return false;
            }

            normalizedPath = path.Substring(index + 1).Trim();
            if (normalizedPath.Length == 0)
            {
                normalizedPath = ".";
            }
        }

        relativeSource = new RelativeSourceMarkup(
            mode: "Self",
            ancestorTypeToken: null,
            ancestorLevel: null,
            tree: null);
        return true;
    }

    private static bool TryParseParentQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        relativeSource = default;
        normalizedPath = string.Empty;

        if (!path.StartsWith("$parent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$parent".Length;
        string? ancestorTypeToken = null;
        int? ancestorLevel = 1;

        if (index < path.Length && path[index] == '[')
        {
            var closingBracket = path.IndexOf(']', index + 1);
            if (closingBracket <= index + 1)
            {
                return false;
            }

            var inside = path.Substring(index + 1, closingBracket - index - 1).Trim();
            if (inside.Length > 0)
            {
                var separators = inside.Split(new[] { ',', ';' }, 2, StringSplitOptions.None);
                var firstPart = separators[0].Trim();
                if (separators.Length == 1)
                {
                    if (int.TryParse(firstPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSingleLevel) &&
                        parsedSingleLevel > 0)
                    {
                        ancestorLevel = parsedSingleLevel;
                        ancestorTypeToken = null;
                    }
                    else
                    {
                        ancestorTypeToken = firstPart.Length > 0 ? firstPart : null;
                    }
                }
                else
                {
                    ancestorTypeToken = firstPart.Length > 0 ? firstPart : null;
                    if (int.TryParse(separators[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel) &&
                        parsedLevel > 0)
                    {
                        ancestorLevel = parsedLevel;
                    }
                }
            }

            index = closingBracket + 1;
        }

        if (index == path.Length)
        {
            normalizedPath = ".";
        }
        else
        {
            if (path[index] != '.')
            {
                return false;
            }

            normalizedPath = path.Substring(index + 1).Trim();
            if (normalizedPath.Length == 0)
            {
                normalizedPath = ".";
            }
        }

        relativeSource = new RelativeSourceMarkup(
            mode: "FindAncestor",
            ancestorTypeToken: ancestorTypeToken,
            ancestorLevel: ancestorLevel,
            tree: null);
        return true;
    }

    private static bool CanUseCompiledBinding(BindingMarkup bindingMarkup)
    {
        return !bindingMarkup.HasSourceConflict &&
               string.IsNullOrWhiteSpace(bindingMarkup.ElementName) &&
               bindingMarkup.RelativeSource is null &&
               string.IsNullOrWhiteSpace(bindingMarkup.Source);
    }

    private static bool TryBuildCompiledBindingAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        out string accessorExpression,
        out string normalizedPath,
        out string errorMessage)
    {
        accessorExpression = "source";
        normalizedPath = string.IsNullOrWhiteSpace(rawPath) ? "." : rawPath.Trim();
        errorMessage = string.Empty;

        if (normalizedPath == ".")
        {
            return true;
        }

        if (!TryParseCompiledBindingPathSegments(normalizedPath, out var segments, out var leadingNotCount, out errorMessage))
        {
            return false;
        }

        if (segments.Count == 0)
        {
            normalizedPath = ".";
            return true;
        }

        var expressionBuilder = "source";
        ITypeSymbol? currentType = sourceType;
        var normalizedSegments = new List<string>(segments.Count);

        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (currentType is not INamedTypeSymbol currentNamedType)
            {
                errorMessage = "intermediate segment is not a named CLR type";
                return false;
            }

            if (segment.AcceptsNull &&
                !CanUseNullConditionalAccess(currentType))
            {
                errorMessage = $"null-conditional access '?.' is not valid on '{currentType.ToDisplayString()}'";
                return false;
            }

            if (segment.IsAttachedProperty)
            {
                if (string.IsNullOrWhiteSpace(segment.AttachedOwnerTypeToken))
                {
                    errorMessage = "attached property owner type token is missing";
                    return false;
                }

                var ownerTypeToken = segment.AttachedOwnerTypeToken!;
                var ownerType = ResolveTypeToken(compilation, document, ownerTypeToken, document.ClassNamespace);
                if (ownerType is null)
                {
                    errorMessage = $"attached property owner type '{ownerTypeToken}' could not be resolved";
                    return false;
                }

                var getterMethod = FindAttachedPropertyGetterMethod(ownerType, segment.MemberName, currentType);
                if (getterMethod is null || getterMethod.ReturnsVoid)
                {
                    errorMessage = $"attached property getter 'Get{segment.MemberName}' was not found on '{ownerType.ToDisplayString()}'";
                    return false;
                }

                var ownerTypeName = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (segment.AcceptsNull)
                {
                    expressionBuilder = "(" +
                                        expressionBuilder +
                                        " is null ? default : " +
                                        ownerTypeName +
                                        "." +
                                        getterMethod.Name +
                                        "(" +
                                        expressionBuilder +
                                        "))";
                }
                else
                {
                    expressionBuilder = ownerTypeName + "." + getterMethod.Name + "(" + expressionBuilder + ")";
                }

                currentType = getterMethod.ReturnType;
                var attachedNormalizedSegment = "(" + ownerTypeToken + "." + segment.MemberName + ")";
                if (normalizedSegments.Count == 0)
                {
                    normalizedSegments.Add(attachedNormalizedSegment);
                }
                else
                {
                    var separator = segment.AcceptsNull ? "?." : ".";
                    normalizedSegments.Add(separator + attachedNormalizedSegment);
                }

                foreach (var indexerToken in segment.Indexers)
                {
                    if (!TryBuildIndexerExpression(currentType, indexerToken, out var indexerExpression, out var normalizedIndexerToken, out var resultType, out errorMessage))
                    {
                        return false;
                    }

                    expressionBuilder += "[" + indexerExpression + "]";
                    normalizedSegments[normalizedSegments.Count - 1] += "[" + normalizedIndexerToken + "]";
                    currentType = resultType;
                }

                var attachedSegmentIndex = normalizedSegments.Count - 1;
                var updatedAttachedSegment = normalizedSegments[attachedSegmentIndex];
                if (!TryApplyStreamOperators(
                        compilation,
                        segment.StreamCount,
                        ref currentType,
                        ref expressionBuilder,
                        ref updatedAttachedSegment,
                        out errorMessage))
                {
                    return false;
                }

                normalizedSegments[attachedSegmentIndex] = updatedAttachedSegment;
                continue;
            }

            var accessor = segment.AcceptsNull ? "?." : ".";
            var property = segment.IsMethodCall ? null : FindProperty(currentNamedType, segment.MemberName);
            var method = segment.IsMethodCall
                ? null
                : FindParameterlessMethod(currentNamedType, segment.MemberName);
            if (property is null && method is null)
            {
                if (segment.IsMethodCall &&
                    TryResolveMethodInvocation(
                        currentNamedType,
                        segment.MemberName,
                        segment.MethodArguments,
                        out var methodInvocationExpression,
                        out var methodInvocationNormalizedSegment,
                        out var methodReturnType,
                        out errorMessage))
                {
                    expressionBuilder += accessor + methodInvocationExpression;
                    currentType = methodReturnType;
                    var segmentSeparator = normalizedSegments.Count == 0
                        ? string.Empty
                        : segment.AcceptsNull ? "?." : ".";
                    var normalizedInvocationSegment = segmentSeparator + methodInvocationNormalizedSegment;
                    if (!TryApplyStreamOperators(
                            compilation,
                            segment.StreamCount,
                            ref currentType,
                            ref expressionBuilder,
                            ref normalizedInvocationSegment,
                            out errorMessage))
                    {
                        return false;
                    }

                    normalizedSegments.Add(normalizedInvocationSegment);
                    continue;
                }

                if (segment.IsMethodCall && !string.IsNullOrWhiteSpace(errorMessage))
                {
                    return false;
                }

                errorMessage = $"segment '{segment.MemberName}' was not found as a property or parameterless method on '{currentNamedType.ToDisplayString()}'";
                return false;
            }

            string normalizedSegment;
            if (property is not null)
            {
                expressionBuilder += accessor + property.Name;
                currentType = property.Type;
                normalizedSegment = property.Name;
            }
            else
            {
                if (method is null || method.ReturnsVoid)
                {
                    errorMessage = $"method segment '{segment.MemberName}' is not a supported parameterless method with a return value";
                    return false;
                }

                expressionBuilder += accessor + method.Name + "()";
                currentType = method.ReturnType;
                normalizedSegment = method.Name + "()";
            }

            foreach (var indexerToken in segment.Indexers)
            {
                if (!TryBuildIndexerExpression(currentType, indexerToken, out var indexerExpression, out var normalizedIndexerToken, out var resultType, out errorMessage))
                {
                    return false;
                }

                expressionBuilder += "[" + indexerExpression + "]";
                normalizedSegment += "[" + normalizedIndexerToken + "]";
                currentType = resultType;
            }

            if (!string.IsNullOrWhiteSpace(segment.CastTypeToken))
            {
                var castTypeToken = segment.CastTypeToken!;
                var castType = ResolveTypeToken(compilation, document, castTypeToken, document.ClassNamespace);
                if (castType is null)
                {
                    errorMessage = $"cast type '{castTypeToken}' could not be resolved";
                    return false;
                }

                expressionBuilder = "((" +
                                    castType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                    ")" +
                                    expressionBuilder +
                                    ")";
                normalizedSegment = "((" + castTypeToken + ")" + normalizedSegment + ")";
                currentType = castType;
            }

            if (!TryApplyStreamOperators(
                    compilation,
                    segment.StreamCount,
                    ref currentType,
                    ref expressionBuilder,
                    ref normalizedSegment,
                    out errorMessage))
            {
                return false;
            }

            if (normalizedSegments.Count == 0)
            {
                normalizedSegments.Add(normalizedSegment);
            }
            else
            {
                var separator = segment.AcceptsNull ? "?." : ".";
                normalizedSegments.Add(separator + normalizedSegment);
            }
        }

        if (leadingNotCount > 0)
        {
            for (var i = 0; i < leadingNotCount; i++)
            {
                expressionBuilder = "(!global::System.Convert.ToBoolean(" + expressionBuilder + "))";
            }
        }

        accessorExpression = expressionBuilder;
        if (normalizedSegments.Count > 0)
        {
            normalizedPath = string.Join(string.Empty, normalizedSegments);
            if (leadingNotCount > 0)
            {
                normalizedPath = new string('!', leadingNotCount) + normalizedPath;
            }
        }
        return true;
    }

    private static bool TryParseCompiledBindingPathSegments(
        string path,
        out List<CompiledPathSegment> segments,
        out int leadingNotCount,
        out string errorMessage)
    {
        segments = new List<CompiledPathSegment>();
        leadingNotCount = 0;
        errorMessage = string.Empty;

        var index = 0;
        while (index < path.Length && path[index] == '!')
        {
            leadingNotCount++;
            index++;
        }

        if (leadingNotCount > 0 && index >= path.Length)
        {
            errorMessage = "compiled binding path cannot end after '!'";
            return false;
        }

        var nextSegmentAcceptsNull = false;
        while (index < path.Length)
        {
            while (index < path.Length && path[index] == '.')
            {
                index++;
            }

            if (index >= path.Length)
            {
                break;
            }

            if (!TryParseCompiledBindingMemberSegment(
                    path,
                    ref index,
                    out var memberName,
                    out var castTypeToken,
                    out var isMethodCall,
                    out var methodArguments,
                    out var isAttachedProperty,
                    out var attachedOwnerTypeToken,
                    out errorMessage))
            {
                return false;
            }

            var indexers = ImmutableArray.CreateBuilder<string>();
            while (index < path.Length && path[index] == '[')
            {
                index++;
                var tokenStart = index;
                var bracketDepth = 1;
                while (index < path.Length && bracketDepth > 0)
                {
                    var ch = path[index];
                    if (ch == '[')
                    {
                        bracketDepth++;
                        index++;
                        continue;
                    }

                    if (ch == ']')
                    {
                        bracketDepth--;
                        if (bracketDepth == 0)
                        {
                            break;
                        }
                    }

                    index++;
                }

                if (index >= path.Length || path[index] != ']')
                {
                    errorMessage = "unterminated indexer segment";
                    return false;
                }

                var tokenLength = index - tokenStart;
                if (tokenLength <= 0)
                {
                    errorMessage = "empty indexer segment is not supported";
                    return false;
                }

                var token = path.Substring(tokenStart, tokenLength).Trim();
                if (token.Length == 0)
                {
                    errorMessage = "empty indexer segment is not supported";
                    return false;
                }

                indexers.Add(token);
                index++;
            }

            var streamCount = 0;
            while (index < path.Length && path[index] == '^')
            {
                streamCount++;
                index++;
            }

            segments.Add(new CompiledPathSegment(
                memberName,
                indexers.ToImmutable(),
                castTypeToken,
                isMethodCall,
                nextSegmentAcceptsNull,
                methodArguments,
                isAttachedProperty,
                attachedOwnerTypeToken,
                streamCount));
            nextSegmentAcceptsNull = false;

            if (index >= path.Length)
            {
                break;
            }

            if (path[index] == '.')
            {
                index++;
                continue;
            }

            if (path[index] == '?' &&
                index + 1 < path.Length &&
                path[index + 1] == '.')
            {
                index += 2;
                nextSegmentAcceptsNull = true;
                continue;
            }

            errorMessage = $"unexpected token '{path[index]}' in binding path";
            return false;
        }

        return true;
    }

    private static bool TryParseCompiledBindingMemberSegment(
        string path,
        ref int index,
        out string memberName,
        out string? castTypeToken,
        out bool isMethodCall,
        out ImmutableArray<string> methodArguments,
        out bool isAttachedProperty,
        out string? attachedOwnerTypeToken,
        out string errorMessage)
    {
        memberName = string.Empty;
        castTypeToken = null;
        isMethodCall = false;
        methodArguments = ImmutableArray<string>.Empty;
        isAttachedProperty = false;
        attachedOwnerTypeToken = null;
        errorMessage = string.Empty;

        if (index >= path.Length)
        {
            errorMessage = "expected member segment";
            return false;
        }

        var castRequiresSegmentClosure = false;
        if (path[index] == '(')
        {
            if (index + 1 < path.Length && path[index + 1] != '(')
            {
                var attachedClosing = path.IndexOf(')', index + 1);
                if (attachedClosing > index + 1)
                {
                    var attachedInner = path.Substring(index + 1, attachedClosing - index - 1).Trim();
                    var attachedSeparator = attachedInner.LastIndexOf('.');
                    var isAttachedTail = attachedClosing + 1 >= path.Length ||
                                         path[attachedClosing + 1] == '.' ||
                                         path[attachedClosing + 1] == '?' ||
                                         path[attachedClosing + 1] == '[';
                    if (attachedSeparator > 0 &&
                        attachedSeparator < attachedInner.Length - 1 &&
                        isAttachedTail)
                    {
                        attachedOwnerTypeToken = attachedInner.Substring(0, attachedSeparator).Trim();
                        memberName = attachedInner.Substring(attachedSeparator + 1).Trim();
                        if (attachedOwnerTypeToken.Length == 0 || memberName.Length == 0)
                        {
                            errorMessage = "invalid attached property segment";
                            return false;
                        }

                        isAttachedProperty = true;
                        index = attachedClosing + 1;
                        return true;
                    }
                }
            }

            if (index + 1 < path.Length && path[index + 1] == '(')
            {
                castRequiresSegmentClosure = true;
                index += 2;
            }
            else
            {
                index++;
            }

            var castStart = index;
            while (index < path.Length && path[index] != ')')
            {
                index++;
            }

            if (index >= path.Length || path[index] != ')')
            {
                errorMessage = "unterminated cast segment";
                return false;
            }

            castTypeToken = path.Substring(castStart, index - castStart).Trim();
            if (castTypeToken.Length == 0)
            {
                errorMessage = "cast type token cannot be empty";
                return false;
            }

            index++;
            while (index < path.Length && char.IsWhiteSpace(path[index]))
            {
                index++;
            }
        }

        if (index >= path.Length || !IsIdentifierStart(path[index]))
        {
            errorMessage = "member name expected in binding path segment";
            return false;
        }

        var nameStart = index;
        index++;
        while (index < path.Length && IsIdentifierPart(path[index]))
        {
            index++;
        }

        memberName = path.Substring(nameStart, index - nameStart);

        if (index < path.Length && path[index] == '(')
        {
            if (!TryParseMethodArguments(path, ref index, out methodArguments, out errorMessage))
            {
                return false;
            }

            isMethodCall = true;
        }

        if (castRequiresSegmentClosure)
        {
            while (index < path.Length && char.IsWhiteSpace(path[index]))
            {
                index++;
            }

            if (index >= path.Length || path[index] != ')')
            {
                errorMessage = "expected ')' to close casted member segment";
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool TryParseMethodArguments(
        string path,
        ref int index,
        out ImmutableArray<string> methodArguments,
        out string errorMessage)
    {
        methodArguments = ImmutableArray<string>.Empty;
        errorMessage = string.Empty;

        if (index >= path.Length || path[index] != '(')
        {
            errorMessage = "method argument list must start with '('";
            return false;
        }

        index++;
        var argumentStart = index;
        var parenthesisDepth = 1;
        var inQuote = false;
        var quoteChar = '\0';

        while (index < path.Length && parenthesisDepth > 0)
        {
            var ch = path[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                index++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                index++;
                continue;
            }

            if (ch == '(')
            {
                parenthesisDepth++;
            }
            else if (ch == ')')
            {
                parenthesisDepth--;
                if (parenthesisDepth == 0)
                {
                    break;
                }
            }

            index++;
        }

        if (index >= path.Length || parenthesisDepth != 0)
        {
            errorMessage = "unterminated method argument list";
            return false;
        }

        var argumentsText = path.Substring(argumentStart, index - argumentStart).Trim();
        index++;

        if (argumentsText.Length == 0)
        {
            return true;
        }

        var arguments = ImmutableArray.CreateBuilder<string>();
        foreach (var token in SplitTopLevel(argumentsText, ','))
        {
            var argument = token.Trim();
            if (argument.Length == 0)
            {
                errorMessage = "method argument list contains an empty argument";
                return false;
            }

            arguments.Add(argument);
        }

        methodArguments = arguments.ToImmutable();
        return true;
    }

    private static bool CanUseNullConditionalAccess(ITypeSymbol type)
    {
        if (type.IsReferenceType)
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }

        return false;
    }

    private static bool TryApplyStreamOperators(
        Compilation compilation,
        int streamCount,
        ref ITypeSymbol? currentType,
        ref string expressionBuilder,
        ref string normalizedSegment,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (streamCount <= 0)
        {
            return true;
        }

        for (var index = 0; index < streamCount; index++)
        {
            if (currentType is null)
            {
                errorMessage = "stream operator '^' cannot be applied because the current segment type is unknown";
                return false;
            }

            if (TryResolveTaskStreamType(compilation, currentType, out var taskResultType, out var useGenericTaskUnwrap))
            {
                if (useGenericTaskUnwrap)
                {
                    expressionBuilder =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapTask<" +
                        taskResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                        ">(" +
                        expressionBuilder +
                        ")";
                }
                else
                {
                    expressionBuilder =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapTask(" +
                        expressionBuilder +
                        ")";
                }

                normalizedSegment += "^";
                currentType = taskResultType;
                continue;
            }

            if (TryResolveObservableStreamType(compilation, currentType, out var observableElementType))
            {
                expressionBuilder =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapObservable<" +
                    observableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    ">(" +
                    expressionBuilder +
                    ")";
                normalizedSegment += "^";
                currentType = observableElementType;
                continue;
            }

            errorMessage =
                $"stream operator '^' is not supported for type '{currentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'";
            return false;
        }

        return true;
    }

    private static bool TryResolveTaskStreamType(
        Compilation compilation,
        ITypeSymbol sourceType,
        out ITypeSymbol resultType,
        out bool useGenericTaskUnwrap)
    {
        resultType = compilation.GetSpecialType(SpecialType.System_Object);
        useGenericTaskUnwrap = false;

        if (sourceType is not INamedTypeSymbol namedSourceType)
        {
            return false;
        }

        var taskOfType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        if (taskOfType is not null &&
            TryFindConstructedGenericType(namedSourceType, taskOfType, out var taskOfConstructedType) &&
            taskOfConstructedType.TypeArguments.Length == 1)
        {
            resultType = taskOfConstructedType.TypeArguments[0];
            useGenericTaskUnwrap = true;
            return true;
        }

        var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        if (taskType is not null && IsTypeAssignableTo(sourceType, taskType))
        {
            resultType = compilation.GetSpecialType(SpecialType.System_Object);
            useGenericTaskUnwrap = false;
            return true;
        }

        return false;
    }

    private static bool TryResolveObservableStreamType(
        Compilation compilation,
        ITypeSymbol sourceType,
        out ITypeSymbol elementType)
    {
        elementType = compilation.GetSpecialType(SpecialType.System_Object);

        if (sourceType is not INamedTypeSymbol namedSourceType)
        {
            return false;
        }

        var observableType = compilation.GetTypeByMetadataName("System.IObservable`1");
        if (observableType is null)
        {
            return false;
        }

        if (!TryFindConstructedGenericType(namedSourceType, observableType, out var constructedObservableType) ||
            constructedObservableType.TypeArguments.Length != 1)
        {
            return false;
        }

        elementType = constructedObservableType.TypeArguments[0];
        return true;
    }

    private static bool TryFindConstructedGenericType(
        INamedTypeSymbol sourceType,
        INamedTypeSymbol genericTypeDefinition,
        out INamedTypeSymbol constructedType)
    {
        if (sourceType.IsGenericType &&
            SymbolEqualityComparer.Default.Equals(sourceType.OriginalDefinition, genericTypeDefinition))
        {
            constructedType = sourceType;
            return true;
        }

        foreach (var interfaceType in sourceType.AllInterfaces)
        {
            if (interfaceType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, genericTypeDefinition))
            {
                constructedType = interfaceType;
                return true;
            }
        }

        for (var currentType = sourceType.BaseType; currentType is not null; currentType = currentType.BaseType)
        {
            if (currentType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(currentType.OriginalDefinition, genericTypeDefinition))
            {
                constructedType = currentType;
                return true;
            }
        }

        constructedType = null!;
        return false;
    }

    private static bool TryBuildIndexerExpression(
        ITypeSymbol collectionType,
        string rawIndexerToken,
        out string indexerExpression,
        out string normalizedIndexerToken,
        out ITypeSymbol resultType,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        normalizedIndexerToken = rawIndexerToken;

        if (collectionType is IArrayTypeSymbol arrayType)
        {
            if (!int.TryParse(rawIndexerToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var arrayIndex))
            {
                indexerExpression = string.Empty;
                resultType = collectionType;
                errorMessage = $"array index '{rawIndexerToken}' is not a valid integer";
                return false;
            }

            indexerExpression = arrayIndex.ToString(CultureInfo.InvariantCulture);
            normalizedIndexerToken = indexerExpression;
            resultType = arrayType.ElementType;
            return true;
        }

        if (collectionType is not INamedTypeSymbol namedCollectionType)
        {
            indexerExpression = string.Empty;
            resultType = collectionType;
            errorMessage = $"type '{collectionType.ToDisplayString()}' does not support indexers";
            return false;
        }

        var indexer = namedCollectionType.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => property.IsIndexer &&
                                        property.Parameters.Length == 1 &&
                                        property.GetMethod is not null);
        if (indexer is null)
        {
            indexerExpression = string.Empty;
            resultType = collectionType;
            errorMessage = $"type '{collectionType.ToDisplayString()}' does not define a supported indexer";
            return false;
        }

        if (!TryConvertIndexerToken(rawIndexerToken, indexer.Parameters[0].Type, out indexerExpression, out normalizedIndexerToken))
        {
            resultType = collectionType;
            errorMessage = $"indexer token '{rawIndexerToken}' is incompatible with '{indexer.Parameters[0].Type.ToDisplayString()}'";
            return false;
        }

        resultType = indexer.Type;
        return true;
    }

    private static bool TryResolveMethodInvocation(
        INamedTypeSymbol targetType,
        string methodName,
        ImmutableArray<string> methodArguments,
        out string invocationExpression,
        out string normalizedSegment,
        out ITypeSymbol returnType,
        out string errorMessage)
    {
        invocationExpression = string.Empty;
        normalizedSegment = string.Empty;
        returnType = targetType;
        errorMessage = string.Empty;

        var candidates = new List<IMethodSymbol>();
        for (INamedTypeSymbol? current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    method.ReturnsVoid ||
                    method.IsGenericMethod ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
                    method.Parameters.Length != methodArguments.Length)
                {
                    continue;
                }

                candidates.Add(method);
            }
        }

        if (candidates.Count == 0)
        {
            errorMessage = $"method segment '{methodName}' has no compatible overloads on '{targetType.ToDisplayString()}'";
            return false;
        }

        var bestScore = int.MaxValue;
        var bestObjectParameterCount = int.MaxValue;
        var bestSortKey = string.Empty;
        string[]? bestArgumentExpressions = null;
        string[]? bestNormalizedArguments = null;
        IMethodSymbol? bestCandidate = null;

        foreach (var candidate in candidates.OrderBy(method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            var argumentExpressions = new string[methodArguments.Length];
            var normalizedArguments = new string[methodArguments.Length];
            var candidateScore = 0;
            var objectParameterCount = 0;
            var canUseCandidate = true;

            for (var i = 0; i < methodArguments.Length; i++)
            {
                var parameter = candidate.Parameters[i];
                if (parameter.Type.SpecialType == SpecialType.System_Object)
                {
                    objectParameterCount++;
                }

                if (!TryConvertMethodArgumentToken(
                        methodArguments[i],
                        parameter.Type,
                        out var argumentExpression,
                        out var normalizedArgument,
                        out var conversionCost))
                {
                    canUseCandidate = false;
                    break;
                }

                candidateScore += conversionCost;
                argumentExpressions[i] = BuildTypedInvocationArgument(parameter.Type, argumentExpression);
                normalizedArguments[i] = normalizedArgument;
            }

            if (!canUseCandidate)
            {
                continue;
            }

            var candidateSortKey = candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (bestCandidate is null ||
                candidateScore < bestScore ||
                (candidateScore == bestScore && objectParameterCount < bestObjectParameterCount) ||
                (candidateScore == bestScore &&
                 objectParameterCount == bestObjectParameterCount &&
                 string.Compare(candidateSortKey, bestSortKey, StringComparison.Ordinal) < 0))
            {
                bestCandidate = candidate;
                bestScore = candidateScore;
                bestObjectParameterCount = objectParameterCount;
                bestSortKey = candidateSortKey;
                bestArgumentExpressions = argumentExpressions;
                bestNormalizedArguments = normalizedArguments;
            }
        }

        if (bestCandidate is not null && bestArgumentExpressions is not null && bestNormalizedArguments is not null)
        {
            invocationExpression = bestCandidate.Name + "(" + string.Join(", ", bestArgumentExpressions) + ")";
            normalizedSegment = bestCandidate.Name + "(" + string.Join(", ", bestNormalizedArguments) + ")";
            returnType = bestCandidate.ReturnType;
            return true;
        }

        errorMessage = $"method segment '{methodName}' arguments do not match available overloads on '{targetType.ToDisplayString()}'";
        return false;
    }

    private static bool TryConvertMethodArgumentToken(
        string rawToken,
        ITypeSymbol parameterType,
        out string expression,
        out string normalizedToken,
        out int conversionCost)
    {
        expression = string.Empty;
        normalizedToken = rawToken.Trim();
        conversionCost = int.MaxValue;
        var tokenWasQuoted = IsQuotedLiteral(normalizedToken);

        if (parameterType is INamedTypeSymbol nullableType &&
            IsNullableValueType(nullableType))
        {
            if (normalizedToken.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                expression = "null";
                normalizedToken = "null";
                conversionCost = 2;
                return true;
            }

            if (TryConvertMethodArgumentToken(
                    rawToken,
                    nullableType.TypeArguments[0],
                    out expression,
                    out normalizedToken,
                    out var underlyingCost))
            {
                conversionCost = underlyingCost + 1;
                return true;
            }

            return false;
        }

        if (normalizedToken.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            if (parameterType.IsReferenceType)
            {
                expression = "null";
                normalizedToken = "null";
                conversionCost = 2;
                return true;
            }

            return false;
        }

        var unquotedToken = Unquote(normalizedToken);

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + Escape(unquotedToken) + "\"";
            normalizedToken = unquotedToken;
            conversionCost = tokenWasQuoted ? 0 : 4;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Char &&
            unquotedToken.Length == 1)
        {
            expression = "'" + EscapeChar(unquotedToken[0]) + "'";
            normalizedToken = unquotedToken;
            conversionCost = tokenWasQuoted ? 0 : 4;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Boolean &&
            bool.TryParse(unquotedToken, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Int32 &&
            int.TryParse(unquotedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Int64 &&
            long.TryParse(unquotedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            expression = longValue.ToString(CultureInfo.InvariantCulture) + "L";
            normalizedToken = longValue.ToString(CultureInfo.InvariantCulture);
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Double &&
            double.TryParse(unquotedToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            expression = doubleValue.ToString("R", CultureInfo.InvariantCulture);
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Single &&
            float.TryParse(unquotedToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            expression = floatValue.ToString("R", CultureInfo.InvariantCulture) + "f";
            normalizedToken = floatValue.ToString("R", CultureInfo.InvariantCulture);
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Object)
        {
            if (bool.TryParse(unquotedToken, out boolValue))
            {
                expression = boolValue ? "true" : "false";
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            if (int.TryParse(unquotedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                expression = intValue.ToString(CultureInfo.InvariantCulture);
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            if (double.TryParse(unquotedToken, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                expression = doubleValue.ToString("R", CultureInfo.InvariantCulture);
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            expression = "\"" + Escape(unquotedToken) + "\"";
            normalizedToken = unquotedToken;
            conversionCost = 60;
            return true;
        }

        if (parameterType.TypeKind == TypeKind.Enum &&
            parameterType is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
                member.HasConstantValue &&
                member.Name.Equals(unquotedToken, StringComparison.OrdinalIgnoreCase));
            if (enumMember is not null)
            {
                expression = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + enumMember.Name;
                normalizedToken = enumMember.Name;
                conversionCost = 0;
                return true;
            }
        }

        return false;
    }

    private static string BuildTypedInvocationArgument(ITypeSymbol parameterType, string expression)
    {
        if (parameterType.TypeKind == TypeKind.Dynamic)
        {
            return expression;
        }

        return "(" +
               parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               ")(" +
               expression +
               ")";
    }

    private static bool IsNullableValueType(INamedTypeSymbol type)
    {
        return type.IsGenericType &&
               type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
               type.TypeArguments.Length == 1;
    }

    private static bool TryConvertIndexerToken(
        string rawToken,
        ITypeSymbol parameterType,
        out string expression,
        out string normalizedToken)
    {
        expression = string.Empty;
        normalizedToken = rawToken.Trim();
        var token = Unquote(normalizedToken);

        if (parameterType.SpecialType == SpecialType.System_Int32 &&
            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            normalizedToken = expression;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + Escape(token) + "\"";
            normalizedToken = token;
            return true;
        }

        if (parameterType.TypeKind == TypeKind.Enum && parameterType is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
                member.HasConstantValue &&
                member.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (enumMember is not null)
            {
                expression = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + enumMember.Name;
                normalizedToken = enumMember.Name;
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierStart(char ch)
    {
        return char.IsLetter(ch) || ch == '_';
    }

    private static bool IsIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }

    private static bool IsSelectorTokenStart(char ch)
    {
        return char.IsLetter(ch) || ch == '_';
    }

    private static bool IsSelectorTokenPart(char ch)
    {
        if (IsSelectorTokenStart(ch) || ch == '-')
        {
            return true;
        }

        var category = char.GetUnicodeCategory(ch);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.Format
            or UnicodeCategory.DecimalDigitNumber;
    }

    private static ITypeSymbol? TryResolveSetterValueType(
        INamedTypeSymbol? objectType,
        ImmutableArray<XamlPropertyAssignment> propertyAssignments,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType)
    {
        if (!IsSetterType(objectType))
        {
            return null;
        }

        foreach (var assignment in propertyAssignments)
        {
            if (assignment.IsAttached)
            {
                continue;
            }

            if (!NormalizePropertyName(assignment.PropertyName).Equals("Property", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryResolveAvaloniaPropertyValueTypeFromToken(
                    assignment.Value,
                    compilation,
                    document,
                    defaultOwnerType,
                    out var valueType))
            {
                return valueType;
            }
        }

        return null;
    }

    private static bool IsSetterType(INamedTypeSymbol? objectType)
    {
        return objectType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               "global::Avalonia.Styling.Setter";
    }

    private static bool IsBindingObjectType(INamedTypeSymbol? typeSymbol, Compilation compilation)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (IsTypeByMetadataName(typeSymbol, "Avalonia.Data.Binding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Data.MultiBinding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Data.InstancedBinding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Binding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.MultiBinding"))
        {
            return true;
        }

        var bindingBaseType = compilation.GetTypeByMetadataName("Avalonia.Data.BindingBase");
        if (bindingBaseType is not null && IsTypeAssignableTo(typeSymbol, bindingBaseType))
        {
            return true;
        }

        var bindingInterfaceType = compilation.GetTypeByMetadataName("Avalonia.Data.IBinding");
        if (bindingInterfaceType is not null && IsTypeAssignableTo(typeSymbol, bindingInterfaceType))
        {
            return true;
        }

        var bindingInterface2Type = compilation.GetTypeByMetadataName("Avalonia.Data.Core.IBinding2");
        if (bindingInterface2Type is not null && IsTypeAssignableTo(typeSymbol, bindingInterface2Type))
        {
            return true;
        }

        return false;
    }

    private static bool IsTypeByMetadataName(INamedTypeSymbol symbol, string metadataName)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Equals("global::" + metadataName, StringComparison.Ordinal);
    }

    private static ResolvedValueConversionResult CreateLiteralConversion(string expression)
    {
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Literal);
    }

    private static ResolvedValueConversionResult CreateMarkupExtensionConversion(
        string expression,
        bool requiresRuntimeServiceProvider = false,
        bool requiresParentStack = false,
        bool requiresStaticResourceResolver = false,
        bool isRuntimeFallback = false,
        ResolvedResourceKeyExpression? resourceKey = null)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.MarkupExtension,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            RequiresStaticResourceResolver: requiresStaticResourceResolver,
            IsRuntimeFallback: isRuntimeFallback,
            ResourceKey: resourceKey,
            ValueRequirements: requirements);
    }

    private static ResolvedValueConversionResult CreateBindingConversion(
        string expression,
        bool requiresRuntimeServiceProvider = false,
        bool requiresParentStack = false)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Binding,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            ValueRequirements: requirements);
    }

    private static ResolvedValueConversionResult CreateTemplateBindingConversion(string expression)
    {
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.TemplateBinding);
    }

    private static ResolvedValueConversionResult CreateDynamicResourceBindingConversion(
        string expression,
        bool requiresRuntimeServiceProvider,
        bool requiresParentStack,
        ResolvedResourceKeyExpression? resourceKey = null)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.DynamicResourceBinding,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            ResourceKey: resourceKey,
            ValueRequirements: requirements);
    }

    private static bool TryResolveSetterValueWithPolicy(
        string rawValue,
        ITypeSymbol conversionTargetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        bool strictMode,
        bool preferTypedStaticResourceCoercion,
        bool allowObjectStringLiteralFallbackDuringConversion,
        bool allowCompatibilityStringLiteralFallback,
        string propertyName,
        string ownerDisplayName,
        int line,
        int column,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out SetterValueResolutionResult resolution,
        INamedTypeSymbol? selectorNestingTypeHint = null,
        bool setterContext = true)
    {
        resolution = default;

        if (TryBuildRuntimeXamlFragmentExpression(
                rawValue,
                conversionTargetType,
                document,
                out var runtimeXamlSetterValue))
        {
            resolution = new SetterValueResolutionResult(
                Expression: runtimeXamlSetterValue,
                ValueKind: ResolvedValueKind.RuntimeXamlFallback,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        if (TryConvertValueConversion(
                rawValue,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var convertedSetterValue,
                preferTypedStaticResourceCoercion: preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback: allowObjectStringLiteralFallbackDuringConversion,
                selectorNestingTypeHint: selectorNestingTypeHint))
        {
            resolution = new SetterValueResolutionResult(
                Expression: convertedSetterValue.Expression,
                ValueKind: convertedSetterValue.ValueKind,
                RequiresStaticResourceResolver: convertedSetterValue.RequiresStaticResourceResolver,
                ValueRequirements: convertedSetterValue.EffectiveRequirements);
            return true;
        }

        if (conversionTargetType.SpecialType == SpecialType.System_String)
        {
            resolution = new SetterValueResolutionResult(
                Expression: "\"" + Escape(rawValue) + "\"",
                ValueKind: ResolvedValueKind.Literal,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.None);
            return true;
        }

        if (strictMode)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Strategy=StrictError (no compatibility fallback).",
                document.FilePath,
                line,
                column,
                true));
            return false;
        }

        if (TryGetAvaloniaUnsetValueExpression(compilation, out var unsetValueExpression))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Strategy=AvaloniaProperty.UnsetValueFallback.",
                document.FilePath,
                line,
                column,
                strictMode));

            resolution = new SetterValueResolutionResult(
                Expression: unsetValueExpression,
                ValueKind: ResolvedValueKind.Literal,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.None);
            return true;
        }

        if (allowCompatibilityStringLiteralFallback &&
            conversionTargetType.SpecialType == SpecialType.System_Object)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Strategy=CompatibilityStringLiteralFallback.",
                document.FilePath,
                line,
                column,
                false));
            resolution = new SetterValueResolutionResult(
                Expression: "\"" + Escape(rawValue) + "\"",
                ValueKind: ResolvedValueKind.Literal,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.None);
            return true;
        }

        var skipMessage = setterContext
            ? $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Setter was skipped."
            : $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'.";
        diagnostics.Add(new DiagnosticInfo(
            "AXSG0102",
            skipMessage,
            document.FilePath,
            line,
            column,
            strictMode));
        return false;
    }

    private static bool TryResolveAvaloniaPropertyValueTypeFromToken(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out ITypeSymbol? valueType)
    {
        valueType = null;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var token = rawValue.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        INamedTypeSymbol? ownerType = defaultOwnerType;
        var propertyToken = token;
        var separatorIndex = token.LastIndexOf('.');
        if (separatorIndex > 0 && separatorIndex < token.Length - 1)
        {
            var ownerToken = token.Substring(0, separatorIndex);
            propertyToken = token.Substring(separatorIndex + 1);
            ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace) ?? ownerType;
        }

        if (propertyToken.EndsWith("Property", StringComparison.Ordinal))
        {
            propertyToken = propertyToken.Substring(0, propertyToken.Length - "Property".Length);
        }

        if (ownerType is null)
        {
            return false;
        }

        if (!TryFindAvaloniaPropertyField(ownerType, propertyToken, out _, out var propertyField))
        {
            return false;
        }

        valueType = TryGetAvaloniaPropertyValueType(propertyField.Type);
        return valueType is not null;
    }

    private static (ResolvedChildAttachmentMode Mode, string? ContentPropertyName) DetermineChildAttachment(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return (ResolvedChildAttachmentMode.None, null);
        }

        var contentPropertyName = FindContentPropertyName(symbol);
        var contentProperty = !string.IsNullOrWhiteSpace(contentPropertyName)
            ? FindProperty(symbol, contentPropertyName!)
            : null;
        if (contentProperty?.SetMethod is not null)
        {
            return (ResolvedChildAttachmentMode.Content, contentProperty.Name);
        }

        var implicitContentProperty = FindProperty(symbol, "Content");
        if (implicitContentProperty?.SetMethod is not null)
        {
            return (ResolvedChildAttachmentMode.Content, implicitContentProperty.Name);
        }

        if (IsStyleBaseType(symbol))
        {
            return (ResolvedChildAttachmentMode.DirectAdd, null);
        }

        if (CanAddToCollectionProperty(symbol, "Children"))
        {
            return (ResolvedChildAttachmentMode.ChildrenCollection, null);
        }

        if (CanAddToCollectionProperty(symbol, "Items"))
        {
            return (ResolvedChildAttachmentMode.ItemsCollection, null);
        }

        if (HasDictionaryAddMethod(symbol))
        {
            return (ResolvedChildAttachmentMode.DictionaryAdd, null);
        }

        if (HasDirectAddMethod(symbol))
        {
            return (ResolvedChildAttachmentMode.DirectAdd, null);
        }

        return (ResolvedChildAttachmentMode.None, null);
    }

    private static bool IsStyleBaseType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Styling.StyleBase")
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindContentPropertyName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.SetMethod is null)
                {
                    continue;
                }

                if (property.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString() == "Avalonia.Metadata.ContentAttribute" ||
                        attribute.AttributeClass?.Name == "ContentAttribute"))
                {
                    return property.Name;
                }
            }
        }

        return null;
    }

    private static BindingPriorityScope ResolveCurrentBindingPriorityScope(
        INamedTypeSymbol? nodeType,
        Compilation compilation,
        BindingPriorityScope inheritedScope)
    {
        if (nodeType is null)
        {
            return inheritedScope;
        }

        if (IsStyleType(nodeType, compilation) || IsControlThemeType(nodeType, compilation))
        {
            return BindingPriorityScope.Style;
        }

        if (IsTemplateScopeType(nodeType, compilation))
        {
            return BindingPriorityScope.Template;
        }

        return inheritedScope;
    }

    private static bool HasDirectAddMethod(INamedTypeSymbol type)
    {
        foreach (var current in EnumerateTypeHierarchyAndInterfaces(type))
        {
            var hasAdd = current.GetMembers("Add").OfType<IMethodSymbol>().Any(method =>
                !method.IsStatic &&
                method.Parameters.Length == 1 &&
                method.DeclaredAccessibility == Accessibility.Public);

            if (hasAdd)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDictionaryAddMethod(INamedTypeSymbol type)
    {
        foreach (var current in EnumerateTypeHierarchyAndInterfaces(type))
        {
            var hasAdd = current.GetMembers("Add").OfType<IMethodSymbol>().Any(method =>
                !method.IsStatic &&
                method.Parameters.Length == 2 &&
                method.DeclaredAccessibility == Accessibility.Public);

            if (hasAdd)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchyAndInterfaces(INamedTypeSymbol type)
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (visited.Add(current))
            {
                yield return current;
            }

            foreach (var interfaceType in current.AllInterfaces)
            {
                if (visited.Add(interfaceType))
                {
                    yield return interfaceType;
                }
            }
        }
    }

    private static bool TryBuildKeyedDictionaryMergeContainer(
        IPropertySymbol property,
        ImmutableArray<ResolvedObjectNode> values,
        int line,
        int column,
        out ResolvedObjectNode container)
    {
        container = null!;
        if (values.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.KeyExpression))
            {
                return false;
            }
        }

        container = new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: values,
            ChildAttachmentMode: ResolvedChildAttachmentMode.DictionaryAdd,
            ContentPropertyName: null,
            Line: line,
            Column: column,
            Condition: null);
        return true;
    }

    private static ImmutableArray<ResolvedObjectNode> MaterializePropertyElementValuesForTargetTypeIfNeeded(
        ITypeSymbol? targetType,
        ImmutableArray<ResolvedObjectNode> values,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column)
    {
        if (targetType is not INamedTypeSymbol namedTargetType || values.IsDefaultOrEmpty)
        {
            return values;
        }

        var (attachmentMode, contentPropertyName) = DetermineChildAttachment(namedTargetType);
        if (attachmentMode == ResolvedChildAttachmentMode.None)
        {
            return values;
        }

        if (values.Length == 1)
        {
            var resolvedValueType = ResolveTypeToken(
                compilation,
                document,
                values[0].TypeName,
                document.ClassNamespace);

            if (resolvedValueType is null || IsTypeAssignableTo(resolvedValueType, namedTargetType))
            {
                return values;
            }
        }

        var container = new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: namedTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: values,
            ChildAttachmentMode: attachmentMode,
            ContentPropertyName: contentPropertyName,
            Line: line,
            Column: column,
            Condition: null);

        return ImmutableArray.Create(container);
    }

    private static bool CanAddToCollectionProperty(INamedTypeSymbol type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        if (property is null)
        {
            return false;
        }

        var namedType = property.Type as INamedTypeSymbol;
        if (namedType is null)
        {
            return false;
        }

        if (HasDictionaryAddMethod(namedType))
        {
            return false;
        }

        return HasDirectAddMethod(namedType);
    }

    private static bool CanMergeDictionaryProperty(INamedTypeSymbol type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        if (property is null)
        {
            return false;
        }

        if (property.GetMethod is null)
        {
            return false;
        }

        var namedType = property.Type as INamedTypeSymbol;
        if (namedType is null)
        {
            return false;
        }

        return HasDictionaryAddMethod(namedType);
    }

    private static bool TryBindCollectionLiteralPropertyAssignment(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        out ResolvedPropertyElementAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (TryParseMarkupExtension(assignment.Value, out _))
        {
            return false;
        }

        if (!property.Name.Equals("Classes", StringComparison.Ordinal))
        {
            return false;
        }

        var styledElementType = compilation.GetTypeByMetadataName("Avalonia.StyledElement");
        if (styledElementType is null || !IsTypeAssignableTo(objectType, styledElementType))
        {
            return false;
        }

        if (property.Type is not INamedTypeSymbol propertyType || !HasDirectAddMethod(propertyType))
        {
            return false;
        }

        var classTokens = SplitClassTokens(assignment.Value);
        var values = ImmutableArray.CreateBuilder<ResolvedObjectNode>(classTokens.Length);
        foreach (var classToken in classTokens)
        {
            values.Add(new ResolvedObjectNode(
                KeyExpression: null,
                Name: null,
                TypeName: "global::System.String",
                IsBindingObjectNode: false,
                FactoryExpression: "\"" + Escape(classToken) + "\"",
                FactoryValueRequirements: ResolvedValueRequirements.None,
                UseServiceProviderConstructor: false,
                UseTopDownInitialization: false,
                PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
                PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
                EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
                Children: ImmutableArray<ResolvedObjectNode>.Empty,
                ChildAttachmentMode: ResolvedChildAttachmentMode.None,
                ContentPropertyName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition));
        }

        resolvedAssignment = new ResolvedPropertyElementAssignment(
            PropertyName: property.Name,
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            BindingPriorityExpression: null,
            IsCollectionAdd: true,
            IsDictionaryMerge: false,
            ObjectValues: values.ToImmutable(),
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition);
        return true;
    }

    private static ImmutableArray<string> SplitClassTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ImmutableArray<string>.Empty;
        }

        var rawTokens = value.Split(
            [' ', '\t', '\r', '\n', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawTokens.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(rawTokens.Length);
        foreach (var token in rawTokens)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                builder.Add(token);
            }
        }

        return builder.ToImmutable();
    }

    private static bool TryBindAttachedPropertyAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        string targetTypeName,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? explicitOwnerType,
        string? explicitPropertyName,
        string? explicitPropertyFieldName,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;

        var attachedPropertyName = explicitPropertyName;
        var ownerType = explicitOwnerType;
        if (ownerType is null || string.IsNullOrWhiteSpace(attachedPropertyName))
        {
            var separator = assignment.PropertyName.LastIndexOf('.');
            if (separator <= 0 || separator >= assignment.PropertyName.Length - 1)
            {
                return false;
            }

            var ownerToken = assignment.PropertyName.Substring(0, separator);
            attachedPropertyName = assignment.PropertyName.Substring(separator + 1);
            ownerType = ResolveTypeSymbol(compilation, assignment.XmlNamespace, ownerToken)
                        ?? ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        }

        if (ownerType is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(explicitPropertyFieldName) &&
            !TryFindAvaloniaPropertyField(
                ownerType,
                attachedPropertyName!,
                out _,
                out _,
                explicitPropertyFieldName))
        {
            return false;
        }

        return TryBindAvaloniaPropertyAssignment(
            targetType,
            targetTypeName,
            attachedPropertyName!,
            assignment,
            compilation,
            document,
            options,
            diagnostics,
            compiledBindings,
            compileBindingsEnabled,
            nodeDataType,
            fallbackValueType: null,
            bindingPriorityScope,
            setterTargetType,
            out resolvedAssignment,
            allowCompiledBindingRegistration: true,
            explicitOwnerType: ownerType,
            explicitAvaloniaPropertyFieldName: explicitPropertyFieldName);
    }

    private static bool TryBindEventSubscription(
        INamedTypeSymbol targetType,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        var eventName = NormalizePropertyName(assignment.PropertyName);

        if (FindEvent(targetType, eventName) is { } eventSymbol)
        {
            if (TryParseMarkupExtension(assignment.Value, out var clrEventMarkupExtension) &&
                IsEventBindingMarkupExtension(clrEventMarkupExtension))
            {
                if (!TryBindEventBinding(
                        assignment,
                        eventName,
                        compilation,
                        eventSymbol.Type,
                        nodeDataType,
                        rootTypeSymbol,
                        diagnostics,
                        document,
                        options,
                        out var eventBindingDefinition))
                {
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: eventBindingDefinition.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.ClrEvent,
                    RoutedEventOwnerTypeName: null,
                    RoutedEventFieldName: null,
                    RoutedEventHandlerTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: eventBindingDefinition);
                return true;
            }

            if (!TryParseHandlerName(assignment.Value, out var handlerMethodName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' expects a CLR handler method name.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (rootTypeSymbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' requires x:Class-backed root type for handler '{handlerMethodName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            var isCompatible = HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, eventSymbol.Type);
            if (!isCompatible)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Handler method '{handlerMethodName}' is not compatible with event '{eventName}' delegate type '{eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: handlerMethodName!,
                Kind: ResolvedEventSubscriptionKind.ClrEvent,
                RoutedEventOwnerTypeName: null,
                RoutedEventFieldName: null,
                RoutedEventHandlerTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition);
            return true;
        }

        if (TryFindStaticEventField(
                targetType,
                eventName,
                out var routedEventOwnerType,
                out var routedEventField))
        {
            if (!TryResolveRoutedEventHandlerType(routedEventField.Type, compilation, out var routedEventHandlerTypeSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event definition '{routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{routedEventField.Name}' is not compatible with Avalonia routed events.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (TryParseMarkupExtension(assignment.Value, out var routedEventMarkupExtension) &&
                IsEventBindingMarkupExtension(routedEventMarkupExtension))
            {
                if (!TryBindEventBinding(
                        assignment,
                        eventName,
                        compilation,
                        routedEventHandlerTypeSymbol,
                        nodeDataType,
                        rootTypeSymbol,
                        diagnostics,
                        document,
                        options,
                        out var eventBindingDefinition))
                {
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: eventBindingDefinition.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                    RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    RoutedEventFieldName: routedEventField.Name,
                    RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: eventBindingDefinition);
                return true;
            }

            if (!TryParseHandlerName(assignment.Value, out var handlerMethodName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' expects a CLR handler method name.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (rootTypeSymbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' requires x:Class-backed root type for handler '{handlerMethodName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (!HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, routedEventHandlerTypeSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Handler method '{handlerMethodName}' is not compatible with event '{eventName}' delegate type '{routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: handlerMethodName!,
                Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RoutedEventFieldName: routedEventField.Name,
                RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition);
            return true;
        }

        return false;
    }

    private static bool TryBindEventBinding(
        XamlPropertyAssignment assignment,
        string eventName,
        Compilation compilation,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventBindingDefinition eventBindingDefinition)
    {
        eventBindingDefinition = null!;

        if (rootTypeSymbol is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding on '{eventName}' requires x:Class-backed root type.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        if (!TryParseMarkupExtension(assignment.Value, out var markupExtension) ||
            !IsEventBindingMarkupExtension(markupExtension))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Event '{eventName}' uses unsupported EventBinding syntax.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        if (!TryParseEventBindingMarkup(
                markupExtension,
                assignment,
                compilation,
                document,
                out var parsedBinding,
                out var parseError))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                parseError ?? $"EventBinding on '{eventName}' is invalid.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        if (!TryBuildEventBindingDelegateSignature(
                eventHandlerType,
                out var delegateTypeName,
                out var delegateParameters))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding on '{eventName}' is not supported for delegate type '{eventHandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        // Soft semantic checks when data-type context is available.
        if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command &&
            !TryValidateEventBindingCommandPath(parsedBinding.SourceMode, parsedBinding.TargetPath, compilation, nodeDataType, rootTypeSymbol))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding command path '{parsedBinding.TargetPath}' could not be validated against available source types.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }
        else if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Method &&
                 !TryValidateEventBindingMethodPath(parsedBinding.SourceMode, parsedBinding.TargetPath, nodeDataType, rootTypeSymbol))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding method path '{parsedBinding.TargetPath}' could not be validated against available source types.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }

        var dataContextTypeName = nodeDataType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rootTypeName = rootTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var compiledDataContextTargetPath = (string?)null;
        var compiledRootTargetPath = (string?)null;
        var compiledDataContextMethodCall = (ResolvedEventBindingMethodCallPlan?)null;
        var compiledRootMethodCall = (ResolvedEventBindingMethodCallPlan?)null;
        var compiledDataContextParameterPath = (string?)null;
        var compiledRootParameterPath = (string?)null;
        var delegateParameterTypes = GetEventBindingDelegateParameterTypes(eventHandlerType);
        var objectType = compilation.GetSpecialType(SpecialType.System_Object);
        var hasParameterToken = parsedBinding.HasParameterValueExpression || !string.IsNullOrWhiteSpace(parsedBinding.ParameterPath);

        if (parsedBinding.ParameterPath is { } parameterPath &&
            IsSimpleEventBindingPath(parameterPath))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null &&
                TryResolveMemberPathType(nodeDataType, parameterPath, out _))
            {
                compiledDataContextParameterPath = parameterPath;
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext &&
                TryResolveMemberPathType(rootTypeSymbol, parameterPath, out _))
            {
                compiledRootParameterPath = parameterPath;
            }
        }
        else if (parsedBinding.ParameterPath is not null &&
                 parsedBinding.ParameterPath.Trim().Equals(".", StringComparison.Ordinal))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null)
            {
                compiledDataContextParameterPath = ".";
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext)
            {
                compiledRootParameterPath = ".";
            }
        }

        if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command &&
            IsSimpleEventBindingPath(parsedBinding.TargetPath))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null &&
                TryResolveMemberPathType(nodeDataType, parsedBinding.TargetPath, out _))
            {
                compiledDataContextTargetPath = parsedBinding.TargetPath;
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext &&
                TryResolveMemberPathType(rootTypeSymbol, parsedBinding.TargetPath, out _))
            {
                compiledRootTargetPath = parsedBinding.TargetPath;
            }
        }
        else if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Method &&
                 IsSimpleEventBindingPath(parsedBinding.TargetPath))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null)
            {
                if (TryResolveEventBindingParameterType(
                        nodeDataType,
                        hasParameterToken,
                        compiledDataContextParameterPath,
                        parsedBinding.HasParameterValueExpression,
                        objectType,
                        out var dataContextParameterType) &&
                    TryResolveEventBindingMethodCallPlan(
                        nodeDataType,
                        parsedBinding.TargetPath,
                        delegateParameterTypes,
                        hasParameterToken,
                        parsedBinding.PassEventArgs,
                        dataContextParameterType,
                        out compiledDataContextMethodCall))
                {
                    compiledDataContextTargetPath = parsedBinding.TargetPath;
                }
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext)
            {
                if (TryResolveEventBindingParameterType(
                        rootTypeSymbol,
                        hasParameterToken,
                        compiledRootParameterPath,
                        parsedBinding.HasParameterValueExpression,
                        objectType,
                        out var rootParameterType) &&
                    TryResolveEventBindingMethodCallPlan(
                        rootTypeSymbol,
                        parsedBinding.TargetPath,
                        delegateParameterTypes,
                        hasParameterToken,
                        parsedBinding.PassEventArgs,
                        rootParameterType,
                        out compiledRootMethodCall))
                {
                    compiledRootTargetPath = parsedBinding.TargetPath;
                }
            }
        }

        var hasCompiledDataContextTarget = parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command
            ? !string.IsNullOrWhiteSpace(compiledDataContextTargetPath)
            : compiledDataContextMethodCall is not null;
        var hasCompiledRootTarget = parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command
            ? !string.IsNullOrWhiteSpace(compiledRootTargetPath)
            : compiledRootMethodCall is not null;
        if (!HasCompiledEventBindingCoverage(parsedBinding.SourceMode, hasCompiledDataContextTarget, hasCompiledRootTarget))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding {(parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command ? "command" : "method")} path '{parsedBinding.TargetPath}' requires compile-time resolvable members for source mode '{parsedBinding.SourceMode}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }

        var methodName = BuildGeneratedEventBindingMethodName(eventName, assignment.Line, assignment.Column);
        eventBindingDefinition = new ResolvedEventBindingDefinition(
            GeneratedMethodName: methodName,
            DelegateTypeName: delegateTypeName,
            Parameters: delegateParameters,
            TargetKind: parsedBinding.TargetKind,
            SourceMode: parsedBinding.SourceMode,
            TargetPath: parsedBinding.TargetPath,
            ParameterPath: parsedBinding.ParameterPath,
            ParameterValueExpression: parsedBinding.ParameterValueExpression,
            HasParameterValueExpression: parsedBinding.HasParameterValueExpression,
            PassEventArgs: parsedBinding.PassEventArgs,
            DataContextTypeName: dataContextTypeName,
            RootTypeName: rootTypeName,
            CompiledDataContextTargetPath: compiledDataContextTargetPath,
            CompiledRootTargetPath: compiledRootTargetPath,
            CompiledDataContextMethodCall: compiledDataContextMethodCall,
            CompiledRootMethodCall: compiledRootMethodCall,
            CompiledDataContextParameterPath: compiledDataContextParameterPath,
            CompiledRootParameterPath: compiledRootParameterPath,
            Line: assignment.Line,
            Column: assignment.Column);
        return true;
    }

    private static bool TryBuildEventBindingDelegateSignature(
        ITypeSymbol eventHandlerType,
        out string delegateTypeName,
        out ImmutableArray<ResolvedEventBindingParameter> parameters)
    {
        delegateTypeName = eventHandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        parameters = ImmutableArray<ResolvedEventBindingParameter>.Empty;

        if (eventHandlerType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.TypeKind != TypeKind.Delegate ||
            namedDelegate.DelegateInvokeMethod is not { } invokeMethod ||
            !invokeMethod.ReturnsVoid)
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<ResolvedEventBindingParameter>(invokeMethod.Parameters.Length);
        for (var index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            var parameter = invokeMethod.Parameters[index];
            builder.Add(new ResolvedEventBindingParameter(
                Name: "__arg" + index.ToString(CultureInfo.InvariantCulture),
                TypeName: parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        parameters = builder.ToImmutable();
        return true;
    }

    private static ImmutableArray<ITypeSymbol> GetEventBindingDelegateParameterTypes(ITypeSymbol eventHandlerType)
    {
        if (eventHandlerType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.TypeKind != TypeKind.Delegate ||
            namedDelegate.DelegateInvokeMethod is not { } invokeMethod)
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        if (invokeMethod.Parameters.IsDefaultOrEmpty)
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>(invokeMethod.Parameters.Length);
        for (var index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            builder.Add(invokeMethod.Parameters[index].Type);
        }

        return builder.ToImmutable();
    }

    private static bool HasCompiledEventBindingCoverage(
        ResolvedEventBindingSourceMode sourceMode,
        bool hasDataContextTarget,
        bool hasRootTarget)
    {
        return sourceMode switch
        {
            ResolvedEventBindingSourceMode.DataContext => hasDataContextTarget,
            ResolvedEventBindingSourceMode.Root => hasRootTarget,
            _ => hasDataContextTarget || hasRootTarget
        };
    }

    private static bool TryResolveEventBindingParameterType(
        INamedTypeSymbol sourceType,
        bool hasParameterToken,
        string? compiledParameterPath,
        bool hasParameterValueExpression,
        ITypeSymbol objectType,
        out ITypeSymbol parameterType)
    {
        parameterType = objectType;
        if (!hasParameterToken)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(compiledParameterPath))
        {
            if (compiledParameterPath.Equals(".", StringComparison.Ordinal))
            {
                parameterType = sourceType;
                return true;
            }

            return TryResolveMemberPathType(sourceType, compiledParameterPath, out parameterType);
        }

        if (hasParameterValueExpression)
        {
            parameterType = objectType;
            return true;
        }

        return false;
    }

    private static bool TryResolveEventBindingMethodCallPlan(
        INamedTypeSymbol sourceType,
        string methodPath,
        ImmutableArray<ITypeSymbol> delegateParameterTypes,
        bool hasParameterToken,
        bool passEventArgs,
        ITypeSymbol parameterType,
        out ResolvedEventBindingMethodCallPlan? methodCallPlan)
    {
        methodCallPlan = null;
        if (!TrySplitEventBindingMethodPath(methodPath, out var targetPath, out var methodName))
        {
            return false;
        }

        INamedTypeSymbol? targetType = sourceType;
        if (!targetPath.Equals(".", StringComparison.Ordinal))
        {
            if (!TryResolveMemberPathType(sourceType, targetPath, out var resolvedTargetType) ||
                resolvedTargetType is not INamedTypeSymbol namedTargetType)
            {
                return false;
            }

            targetType = namedTargetType;
        }

        if (targetType is null)
        {
            return false;
        }

        var candidateMethods = EnumerateEventBindingMethods(targetType, methodName)
            .OrderBy(static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .ToImmutableArray();
        if (candidateMethods.IsDefaultOrEmpty)
        {
            return false;
        }

        var argumentSets = BuildEventBindingMethodArgumentSets(hasParameterToken, passEventArgs);
        for (var setIndex = 0; setIndex < argumentSets.Length; setIndex++)
        {
            var argumentSet = argumentSets[setIndex];
            for (var methodIndex = 0; methodIndex < candidateMethods.Length; methodIndex++)
            {
                var candidateMethod = candidateMethods[methodIndex];
                if (candidateMethod.Parameters.Length != argumentSet.Length)
                {
                    continue;
                }

                var arguments = ImmutableArray.CreateBuilder<ResolvedEventBindingMethodArgument>(argumentSet.Length);
                var compatible = true;
                for (var parameterIndex = 0; parameterIndex < candidateMethod.Parameters.Length; parameterIndex++)
                {
                    var argumentKind = argumentSet[parameterIndex];
                    var argumentType = GetEventBindingMethodArgumentType(argumentKind, delegateParameterTypes, parameterType);
                    if (argumentType is null ||
                        !IsEventBindingMethodArgumentCompatible(argumentType, candidateMethod.Parameters[parameterIndex].Type))
                    {
                        compatible = false;
                        break;
                    }

                    arguments.Add(new ResolvedEventBindingMethodArgument(
                        argumentKind,
                        candidateMethod.Parameters[parameterIndex].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }

                if (!compatible)
                {
                    continue;
                }

                methodCallPlan = new ResolvedEventBindingMethodCallPlan(
                    TargetPath: targetPath,
                    MethodName: candidateMethod.Name,
                    Arguments: arguments.ToImmutable());
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> EnumerateEventBindingMethods(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    !method.ReturnsVoid ||
                    method.IsGenericMethod ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
                {
                    continue;
                }

                if (string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                    string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return method;
                }
            }
        }
    }

    private static bool TrySplitEventBindingMethodPath(string methodPath, out string targetPath, out string methodName)
    {
        targetPath = ".";
        methodName = string.Empty;

        if (string.IsNullOrWhiteSpace(methodPath))
        {
            return false;
        }

        var normalized = methodPath.Trim();
        if (!IsSimpleEventBindingPath(normalized))
        {
            return false;
        }

        var lastDot = normalized.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= normalized.Length - 1)
        {
            methodName = normalized;
            return IsSimpleEventBindingIdentifier(methodName);
        }

        targetPath = normalized[..lastDot];
        methodName = normalized[(lastDot + 1)..];
        return targetPath.Length > 0 &&
               methodName.Length > 0 &&
               IsSimpleEventBindingPath(targetPath) &&
               IsSimpleEventBindingIdentifier(methodName);
    }

    private static ImmutableArray<ImmutableArray<ResolvedEventBindingMethodArgumentKind>> BuildEventBindingMethodArgumentSets(
        bool hasParameterToken,
        bool passEventArgs)
    {
        if (hasParameterToken)
        {
            return
            [
                [ResolvedEventBindingMethodArgumentKind.Parameter],
                [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.Parameter],
                [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.EventArgs, ResolvedEventBindingMethodArgumentKind.Parameter]
            ];
        }

        if (passEventArgs)
        {
            return
            [
                [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.EventArgs],
                [ResolvedEventBindingMethodArgumentKind.EventArgs],
                [ResolvedEventBindingMethodArgumentKind.Sender],
                ImmutableArray<ResolvedEventBindingMethodArgumentKind>.Empty
            ];
        }

        return
        [
            ImmutableArray<ResolvedEventBindingMethodArgumentKind>.Empty,
            [ResolvedEventBindingMethodArgumentKind.Sender],
            [ResolvedEventBindingMethodArgumentKind.EventArgs],
            [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.EventArgs]
        ];
    }

    private static ITypeSymbol? GetEventBindingMethodArgumentType(
        ResolvedEventBindingMethodArgumentKind argumentKind,
        ImmutableArray<ITypeSymbol> delegateParameterTypes,
        ITypeSymbol parameterType)
    {
        return argumentKind switch
        {
            ResolvedEventBindingMethodArgumentKind.Sender => delegateParameterTypes.Length > 0 ? delegateParameterTypes[0] : null,
            ResolvedEventBindingMethodArgumentKind.EventArgs => delegateParameterTypes.Length > 1 ? delegateParameterTypes[1] : null,
            ResolvedEventBindingMethodArgumentKind.Parameter => parameterType,
            _ => null
        };
    }

    private static bool IsEventBindingMethodArgumentCompatible(ITypeSymbol argumentType, ITypeSymbol parameterType)
    {
        if (IsTypeAssignableTo(argumentType, parameterType))
        {
            return true;
        }

        if (argumentType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        if (parameterType is ITypeParameterSymbol)
        {
            return true;
        }

        return false;
    }

    private static bool IsEventBindingMarkupExtension(MarkupExtensionInfo markupExtension)
    {
        var name = markupExtension.Name.Trim();
        return name.Equals("EventBinding", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("x:EventBinding", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseEventBindingMarkup(
        MarkupExtensionInfo markupExtension,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document,
        out EventBindingMarkup eventBindingMarkup,
        out string? errorMessage)
    {
        eventBindingMarkup = default;
        errorMessage = null;

        var commandToken = markupExtension.NamedArguments.TryGetValue("Command", out var explicitCommand)
            ? explicitCommand
            : markupExtension.NamedArguments.TryGetValue("Path", out var explicitPath)
                ? explicitPath
                : markupExtension.PositionalArguments.Length > 0
                    ? markupExtension.PositionalArguments[0]
                    : null;
        var methodToken = markupExtension.NamedArguments.TryGetValue("Method", out var explicitMethod)
            ? explicitMethod
            : null;

        if (!string.IsNullOrWhiteSpace(commandToken) && !string.IsNullOrWhiteSpace(methodToken))
        {
            errorMessage = "EventBinding cannot define both Command and Method.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(commandToken) && string.IsNullOrWhiteSpace(methodToken))
        {
            errorMessage = "EventBinding requires either Command/Path or Method.";
            return false;
        }

        if (!TryParseEventBindingSourceMode(markupExtension, out var sourceMode, out errorMessage))
        {
            return false;
        }

        var passEventArgs = false;
        if (markupExtension.NamedArguments.TryGetValue("PassEventArgs", out var passEventArgsToken))
        {
            if (!bool.TryParse(Unquote(passEventArgsToken), out passEventArgs))
            {
                errorMessage = "EventBinding PassEventArgs must be true or false.";
                return false;
            }
        }

        var parameterToken = markupExtension.NamedArguments.TryGetValue("Parameter", out var explicitParameter)
                             ? explicitParameter
                             : markupExtension.NamedArguments.TryGetValue("CommandParameter", out var explicitCommandParameter)
                                 ? explicitCommandParameter
                                 : null;

        string? parameterPath = null;
        string? parameterValueExpression = null;
        var hasParameterValueExpression = false;
        if (!TryParseEventBindingParameter(
                parameterToken,
                compilation,
                document,
                assignment,
                out parameterPath,
                out parameterValueExpression,
                out hasParameterValueExpression,
                out var parameterError))
        {
            errorMessage = parameterError;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(commandToken))
        {
            if (!TryParseEventBindingPath(commandToken!, out var commandPath, out var pathError))
            {
                errorMessage = pathError;
                return false;
            }

            eventBindingMarkup = new EventBindingMarkup(
                ResolvedEventBindingTargetKind.Command,
                sourceMode,
                commandPath,
                parameterPath,
                parameterValueExpression,
                hasParameterValueExpression,
                passEventArgs);
            return true;
        }

        var methodPath = Unquote(methodToken!).Trim();
        if (methodPath.Length == 0)
        {
            errorMessage = "EventBinding Method must not be empty.";
            return false;
        }

        eventBindingMarkup = new EventBindingMarkup(
            ResolvedEventBindingTargetKind.Method,
            sourceMode,
            methodPath,
            parameterPath,
            parameterValueExpression,
            hasParameterValueExpression,
            passEventArgs);
        return true;
    }

    private static bool TryParseEventBindingSourceMode(
        MarkupExtensionInfo markupExtension,
        out ResolvedEventBindingSourceMode sourceMode,
        out string? errorMessage)
    {
        errorMessage = null;
        sourceMode = ResolvedEventBindingSourceMode.DataContextThenRoot;
        if (!markupExtension.NamedArguments.TryGetValue("Source", out var sourceToken))
        {
            return true;
        }

        var normalized = Unquote(sourceToken).Trim();
        if (normalized.Equals("DataContextThenRoot", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.DataContextThenRoot;
            return true;
        }

        if (normalized.Equals("DataContext", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.DataContext;
            return true;
        }

        if (normalized.Equals("Root", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.Root;
            return true;
        }

        errorMessage = "EventBinding Source must be DataContext, Root, or DataContextThenRoot.";
        return false;
    }

    private static bool TryParseEventBindingPath(
        string token,
        out string path,
        out string? errorMessage)
    {
        path = string.Empty;
        errorMessage = null;

        if (TryParseBindingMarkup(token, out var bindingMarkup))
        {
            if (!TryValidateEventBindingBindingSource(
                    bindingMarkup,
                    "EventBinding command path",
                    out errorMessage))
            {
                return false;
            }

            path = string.IsNullOrWhiteSpace(bindingMarkup.Path)
                ? "."
                : bindingMarkup.Path.Trim();
            return true;
        }

        if (TryParseMarkupExtension(token, out _))
        {
            errorMessage = "EventBinding command path supports only plain paths or Binding/CompiledBinding markup.";
            return false;
        }

        path = Unquote(token).Trim();
        if (path.Length == 0)
        {
            errorMessage = "EventBinding command path must not be empty.";
            return false;
        }

        return true;
    }

    private static bool TryParseEventBindingParameter(
        string? parameterToken,
        Compilation compilation,
        XamlDocumentModel document,
        XamlPropertyAssignment assignment,
        out string? parameterPath,
        out string? parameterValueExpression,
        out bool hasParameterValueExpression,
        out string? errorMessage)
    {
        parameterPath = null;
        parameterValueExpression = null;
        hasParameterValueExpression = false;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(parameterToken))
        {
            return true;
        }

        if (TryParseBindingMarkup(parameterToken, out var bindingMarkup))
        {
            if (!TryValidateEventBindingBindingSource(
                    bindingMarkup,
                    "EventBinding parameter path",
                    out errorMessage))
            {
                return false;
            }

            parameterPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
                ? "."
                : bindingMarkup.Path.Trim();
            return true;
        }

        if (TryParseMarkupExtension(parameterToken, out var markupExtension))
        {
            var extensionName = markupExtension.Name.Trim();
            if (extensionName.Equals("x:Null", StringComparison.OrdinalIgnoreCase) ||
                extensionName.Equals("Null", StringComparison.OrdinalIgnoreCase))
            {
                parameterValueExpression = "null";
                hasParameterValueExpression = true;
                return true;
            }

            errorMessage = "EventBinding parameter supports literals, x:Null, or Binding/CompiledBinding paths.";
            return false;
        }

        if (!TryConvertUntypedValueExpression(Unquote(parameterToken), out var literalExpression))
        {
            errorMessage = "EventBinding parameter literal is invalid.";
            return false;
        }

        parameterValueExpression = literalExpression;
        hasParameterValueExpression = true;
        return true;
    }

    private static bool TryValidateEventBindingBindingSource(
        BindingMarkup bindingMarkup,
        string contextName,
        out string? errorMessage)
    {
        errorMessage = null;
        if (bindingMarkup.HasSourceConflict)
        {
            errorMessage = bindingMarkup.SourceConflictMessage ?? contextName + " binding source is invalid.";
            return false;
        }

        if (HasExplicitBindingSource(bindingMarkup))
        {
            errorMessage = contextName + " does not support explicit Binding Source/ElementName/RelativeSource.";
            return false;
        }

        return true;
    }

    private static bool TryValidateEventBindingCommandPath(
        ResolvedEventBindingSourceMode sourceMode,
        string path,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol)
    {
        var commandType = compilation.GetTypeByMetadataName("System.Windows.Input.ICommand");
        if (commandType is null || string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var validOnDataContext = sourceMode != ResolvedEventBindingSourceMode.Root &&
                                 nodeDataType is not null &&
                                 TryResolveMemberPathType(nodeDataType, path, out var dataContextPathType) &&
                                 IsTypeAssignableTo(dataContextPathType, commandType);
        var validOnRoot = sourceMode != ResolvedEventBindingSourceMode.DataContext &&
                          rootTypeSymbol is not null &&
                          TryResolveMemberPathType(rootTypeSymbol, path, out var rootPathType) &&
                          IsTypeAssignableTo(rootPathType, commandType);

        if (sourceMode == ResolvedEventBindingSourceMode.DataContext)
        {
            return validOnDataContext || nodeDataType is null;
        }

        if (sourceMode == ResolvedEventBindingSourceMode.Root)
        {
            return validOnRoot || rootTypeSymbol is null;
        }

        return validOnDataContext || validOnRoot || nodeDataType is null || rootTypeSymbol is null;
    }

    private static bool TryValidateEventBindingMethodPath(
        ResolvedEventBindingSourceMode sourceMode,
        string path,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var methodName = path;
        var dot = path.LastIndexOf('.');
        if (dot > 0 && dot < path.Length - 1)
        {
            methodName = path[(dot + 1)..];
        }

        var validOnDataContext = sourceMode != ResolvedEventBindingSourceMode.Root &&
                                 nodeDataType is not null &&
                                 HasInstanceMethod(nodeDataType, methodName);
        var validOnRoot = sourceMode != ResolvedEventBindingSourceMode.DataContext &&
                          rootTypeSymbol is not null &&
                          HasInstanceMethod(rootTypeSymbol, methodName);

        if (sourceMode == ResolvedEventBindingSourceMode.DataContext)
        {
            return validOnDataContext || nodeDataType is null;
        }

        if (sourceMode == ResolvedEventBindingSourceMode.Root)
        {
            return validOnRoot || rootTypeSymbol is null;
        }

        return validOnDataContext || validOnRoot || nodeDataType is null || rootTypeSymbol is null;
    }

    private static bool TryResolveMemberPathType(INamedTypeSymbol rootType, string path, out ITypeSymbol resolvedType)
    {
        resolvedType = rootType;
        var normalizedPath = path.Trim();
        if (normalizedPath.Length == 0 || normalizedPath == ".")
        {
            return true;
        }

        var currentType = (ITypeSymbol)rootType;
        var segments = normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var namedType = currentType as INamedTypeSymbol;
            if (namedType is null)
            {
                return false;
            }

            var segment = segments[index];
            var member = namedType.GetMembers(segment).FirstOrDefault() ??
                         namedType.GetMembers().FirstOrDefault(candidate => candidate.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            switch (member)
            {
                case IPropertySymbol property:
                    currentType = property.Type;
                    break;
                case IFieldSymbol field:
                    currentType = field.Type;
                    break;
                default:
                    return false;
            }
        }

        resolvedType = currentType;
        return true;
    }

    private static bool IsSimpleEventBindingPath(string path)
    {
        var normalizedPath = path.Trim();
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        if (normalizedPath == ".")
        {
            return true;
        }

        var segments = normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            if (!IsSimpleEventBindingIdentifier(segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSimpleEventBindingIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var start = value[0];
        if (!(start == '_' || char.IsLetter(start)))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var current = value[index];
            if (!(current == '_' || char.IsLetterOrDigit(current)))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildGeneratedEventBindingMethodName(string eventName, int line, int column)
    {
        var chars = eventName.ToCharArray();
        if (chars.Length == 0)
        {
            return "__AXSG_EventBinding_" + line.ToString(CultureInfo.InvariantCulture) + "_" + column.ToString(CultureInfo.InvariantCulture);
        }

        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_')
            {
                chars[index] = '_';
            }
        }

        if (!char.IsLetter(chars[0]) && chars[0] != '_')
        {
            return "__AXSG_EventBinding_E" + new string(chars) + "_" +
                   line.ToString(CultureInfo.InvariantCulture) + "_" +
                   column.ToString(CultureInfo.InvariantCulture);
        }

        return "__AXSG_EventBinding_" + new string(chars) + "_" +
               line.ToString(CultureInfo.InvariantCulture) + "_" +
               column.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryBindAvaloniaPropertyAssignment(
        INamedTypeSymbol targetType,
        string targetTypeName,
        string propertyName,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        ITypeSymbol? fallbackValueType,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? setterTargetType,
        out ResolvedPropertyAssignment? resolvedAssignment,
        bool allowCompiledBindingRegistration = true,
        INamedTypeSymbol? explicitOwnerType = null,
        string? explicitAvaloniaPropertyFieldName = null)
    {
        resolvedAssignment = null;

        if (!TryFindAvaloniaPropertyField(
                explicitOwnerType ?? targetType,
                propertyName,
                out var ownerType,
                out var propertyField,
                explicitAvaloniaPropertyFieldName))
        {
            return false;
        }

        var valueType = fallbackValueType ?? TryGetAvaloniaPropertyValueType(propertyField.Type);

        if (TryConvertCSharpExpressionMarkupToBindingExpression(
                assignment.Value,
                compilation,
                document,
                options,
                nodeDataType,
                out var isExpressionMarkup,
                out var expressionBindingValueExpression,
                out var expressionAccessorExpression,
                out var normalizedExpression,
                out var expressionErrorCode,
                out var expressionErrorMessage))
        {
            if (allowCompiledBindingRegistration)
            {
                compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                    TargetTypeName: targetTypeName,
                    TargetPropertyName: propertyName,
                    Path: "{= " + normalizedExpression + " }",
                    SourceTypeName: nodeDataType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AccessorExpression: expressionAccessorExpression,
                    IsSetterBinding: false,
                    Line: assignment.Line,
                    Column: assignment.Column));
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: expressionBindingValueExpression,
                AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AvaloniaPropertyFieldName: propertyField.Name,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                    targetType,
                    propertyField,
                    compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }
        if (isExpressionMarkup)
        {
            var message = expressionErrorCode == "AXSG0110"
                ? $"Expression binding for '{propertyName}' requires x:DataType in scope."
                : $"Expression binding '{assignment.Value}' is invalid for source type '{nodeDataType?.ToDisplayString() ?? "unknown"}': {expressionErrorMessage}";
            diagnostics.Add(new DiagnosticInfo(
                expressionErrorCode,
                message,
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

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
                return true;
            }

            var shouldCompileBinding = CanUseCompiledBinding(bindingMarkup) &&
                                       (bindingMarkup.IsCompiledBinding || compileBindingsEnabled);
            if (shouldCompileBinding)
            {
                if (nodeDataType is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0110",
                        $"Compiled binding for '{propertyName}' requires x:DataType in scope.",
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    return true;
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
                    return true;
                }

                if (allowCompiledBindingRegistration)
                {
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: propertyName,
                        Path: normalizedPath,
                        SourceTypeName: nodeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        AccessorExpression: accessorExpression,
                        IsSetterBinding: false,
                        Line: assignment.Line,
                        Column: assignment.Column));
                }
            }

            if (TryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    bindingMarkup,
                    setterTargetType ?? targetType,
                    bindingPriorityScope,
                    out var bindingExpression))
            {
                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: bindingExpression,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        targetType,
                        propertyField,
                        compilation,
                        bindingPriorityScope),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: ResolvedValueKind.Binding,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            }

            return shouldCompileBinding || resolvedAssignment is not null;
        }

        if (HasResolveByNameSemantics(ownerType, propertyName) &&
            TryBuildResolveByNameLiteralExpression(
                assignment.Value,
                valueType,
                out var resolveByNameValueExpression))
        {
            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: resolveByNameValueExpression,
                AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AvaloniaPropertyFieldName: propertyField.Name,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                    targetType,
                    propertyField,
                    compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.MarkupExtension,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        var valueExpression = string.Empty;
        var valueConversion = default(ResolvedValueConversionResult);
        var valueKind = ResolvedValueKind.Literal;
        var requiresStaticResourceResolver = false;
        var valueRequirements = ResolvedValueRequirements.None;
        if ((valueType is null || !TryConvertValueConversion(
                assignment.Value,
                valueType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out valueConversion,
                preferTypedStaticResourceCoercion: false,
                allowObjectStringLiteralFallback: !options.StrictMode)) &&
            !TryConvertUntypedValueExpression(assignment.Value, out valueExpression))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{assignment.Value}' for Avalonia property '{propertyName}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }
        else if (!string.IsNullOrEmpty(valueConversion.Expression))
        {
            valueExpression = valueConversion.Expression;
            valueKind = valueConversion.ValueKind;
            requiresStaticResourceResolver = valueConversion.RequiresStaticResourceResolver;
            valueRequirements = valueConversion.EffectiveRequirements;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: propertyName,
            ValueExpression: valueExpression,
            AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            AvaloniaPropertyFieldName: propertyField.Name,
            ClrPropertyOwnerTypeName: null,
            ClrPropertyTypeName: null,
            BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                targetType,
                propertyField,
                compilation,
                bindingPriorityScope),
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: valueKind,
            RequiresStaticResourceResolver: requiresStaticResourceResolver,
            ValueRequirements: valueRequirements);
        return true;
    }

    private static bool TryReportBindingSourceConflict(
        BindingMarkup bindingMarkup,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        int line,
        int column,
        bool strictMode)
    {
        if (!bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0111",
            bindingMarkup.SourceConflictMessage ?? "Binding source configuration is invalid.",
            document.FilePath,
            line,
            column,
            strictMode));
        return true;
    }

    private static bool TryBuildBindingValueExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        ITypeSymbol propertyType,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (!CanAssignBindingValue(propertyType, compilation))
        {
            return false;
        }

        if (TryBuildRuntimeBindingExpression(
                compilation,
                document,
                bindingMarkup,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        return false;
    }

    private static bool TryBuildRuntimeBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (compilation.GetTypeByMetadataName("Avalonia.Data.Binding") is not INamedTypeSymbol bindingType)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
            ? "."
            : bindingMarkup.Path.Trim();

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            TryMapBindingMode(bindingMarkup.Mode!, out var bindingModeExpression))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            initializerParts.Add("ElementName = \"" + Escape(bindingMarkup.ElementName!) + "\"");
        }

        if (bindingMarkup.RelativeSource is not null &&
            TryBuildRelativeSourceExpression(bindingMarkup.RelativeSource.Value, compilation, document, out var relativeSourceExpression))
        {
            initializerParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        AddBindingInitializerPart(
            bindingType,
            propertyName: "Source",
            rawValue: bindingMarkup.Source,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        AddBindingInitializerPart(
            bindingType,
            propertyName: "Converter",
            rawValue: bindingMarkup.Converter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "ConverterCulture",
            rawValue: bindingMarkup.ConverterCulture,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "ConverterParameter",
            rawValue: bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "StringFormat",
            rawValue: bindingMarkup.StringFormat,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "FallbackValue",
            rawValue: bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "TargetNullValue",
            rawValue: bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "Delay",
            rawValue: bindingMarkup.Delay,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "Priority",
            rawValue: !string.IsNullOrWhiteSpace(bindingMarkup.Priority)
                ? bindingMarkup.Priority
                : GetDefaultBindingPriorityToken(bindingPriorityScope),
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "UpdateSourceTrigger",
            rawValue: bindingMarkup.UpdateSourceTrigger,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        if (initializerParts.Count == 0)
        {
            expression = "new global::Avalonia.Data.Binding(\"" + Escape(normalizedPath) + "\")";
            return true;
        }

        expression = "new global::Avalonia.Data.Binding(\"" + Escape(normalizedPath) + "\") { " +
                     string.Join(", ", initializerParts) +
                     " }";
        return true;
    }

    private static bool TryBuildReflectionBindingExtensionExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension") is not INamedTypeSymbol reflectionBindingExtensionType)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
            ? "."
            : bindingMarkup.Path.Trim();

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            TryMapBindingMode(bindingMarkup.Mode!, out var bindingModeExpression))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            initializerParts.Add("ElementName = \"" + Escape(bindingMarkup.ElementName!) + "\"");
        }

        if (bindingMarkup.RelativeSource is not null &&
            TryBuildRelativeSourceExpression(bindingMarkup.RelativeSource.Value, compilation, document, out var relativeSourceExpression))
        {
            initializerParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Source",
            rawValue: bindingMarkup.Source,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Converter",
            rawValue: bindingMarkup.Converter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "ConverterCulture",
            rawValue: bindingMarkup.ConverterCulture,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "ConverterParameter",
            rawValue: bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "StringFormat",
            rawValue: bindingMarkup.StringFormat,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "FallbackValue",
            rawValue: bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "TargetNullValue",
            rawValue: bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Delay",
            rawValue: bindingMarkup.Delay,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Priority",
            rawValue: !string.IsNullOrWhiteSpace(bindingMarkup.Priority)
                ? bindingMarkup.Priority
                : GetDefaultBindingPriorityToken(bindingPriorityScope),
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "UpdateSourceTrigger",
            rawValue: bindingMarkup.UpdateSourceTrigger,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        if (initializerParts.Count == 0)
        {
            expression =
                "new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"" +
                Escape(normalizedPath) +
                "\")";
            return true;
        }

        expression =
            "new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"" +
            Escape(normalizedPath) +
            "\") { " +
            string.Join(", ", initializerParts) +
            " }";
        return true;
    }

    private static bool TryConvertOnPlatformExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        var defaultToken = TryGetNamedMarkupArgument(markup, "Default") ??
                           (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (!TryConvertMarkupOptionValueExpression(
                defaultToken,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var defaultExpression))
        {
            return false;
        }

        if (!TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Windows"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var windowsExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "macOS", "MacOS", "OSX"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var macOsExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Linux"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var linuxExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Android"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var androidExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "iOS", "IOS"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var iosExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Browser"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var browserExpression))
        {
            return false;
        }

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnPlatform(" +
            defaultExpression +
            ", " +
            windowsExpression +
            ", " +
            macOsExpression +
            ", " +
            linuxExpression +
            ", " +
            androidExpression +
            ", " +
            iosExpression +
            ", " +
            browserExpression +
            ")");
        return true;
    }

    private static bool TryConvertOnFormFactorExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        var defaultToken = TryGetNamedMarkupArgument(markup, "Default") ??
                           (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (!TryConvertMarkupOptionValueExpression(
                defaultToken,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var defaultExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Desktop"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var desktopExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Mobile"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var mobileExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "TV"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var tvExpression))
        {
            return false;
        }

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(" +
            defaultExpression +
            ", " +
            desktopExpression +
            ", " +
            mobileExpression +
            ", " +
            tvExpression +
            ", " +
            MarkupContextServiceProviderToken +
            ")");
        return true;
    }

    private static bool TryConvertMarkupOptionValueExpression(
        string? rawToken,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = "null";
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return true;
        }

        return TryConvertValueExpression(
            Unquote(rawToken!),
            targetType,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            out expression);
    }

    private static bool TryBuildResourceKeyExpression(
        string rawKeyToken,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedResourceKeyExpression expression)
    {
        expression = default;
        var token = rawKeyToken.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var unquotedToken = Unquote(token);
        if (TryParseMarkupExtension(unquotedToken, out var markup))
        {
            switch (markup.Name.ToLowerInvariant())
            {
                case "x:type":
                case "type":
                {
                    var typeToken = markup.NamedArguments.TryGetValue("Type", out var explicitType)
                        ? explicitType
                        : markup.NamedArguments.TryGetValue("TypeName", out var explicitTypeName)
                            ? explicitTypeName
                            : markup.PositionalArguments.Length > 0
                                ? markup.PositionalArguments[0]
                                : null;
                    if (string.IsNullOrWhiteSpace(typeToken))
                    {
                        return false;
                    }

                    var resolvedType = ResolveTypeToken(compilation, document, Unquote(typeToken!), document.ClassNamespace);
                    if (resolvedType is null)
                    {
                        return false;
                    }

                    expression = new ResolvedResourceKeyExpression(
                        Expression: "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
                        Kind: ResolvedResourceKeyKind.TypeReference);
                    return true;
                }
                case "x:static":
                case "static":
                {
                    var memberToken = markup.NamedArguments.TryGetValue("Member", out var explicitMember)
                        ? explicitMember
                        : markup.PositionalArguments.Length > 0
                            ? markup.PositionalArguments[0]
                            : null;
                    if (string.IsNullOrWhiteSpace(memberToken))
                    {
                        return false;
                    }

                    return TryResolveStaticMemberExpression(
                        compilation,
                        document,
                        Unquote(memberToken!),
                        out var staticMemberExpression) &&
                           TryCreateStaticMemberResourceKeyExpression(staticMemberExpression, out expression);
                }
            }
        }

        expression = new ResolvedResourceKeyExpression(
            Expression: "\"" + Escape(unquotedToken) + "\"",
            Kind: ResolvedResourceKeyKind.StringLiteral);
        return true;
    }

    private static bool TryCreateStaticMemberResourceKeyExpression(
        string staticMemberExpression,
        out ResolvedResourceKeyExpression expression)
    {
        if (string.IsNullOrWhiteSpace(staticMemberExpression))
        {
            expression = default;
            return false;
        }

        expression = new ResolvedResourceKeyExpression(
            Expression: staticMemberExpression,
            Kind: ResolvedResourceKeyKind.StaticMemberReference);
        return true;
    }

    private static void AddBindingInitializerPart(
        INamedTypeSymbol bindingType,
        string propertyName,
        string? rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        List<string> initializerParts)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        if (!TryGetWritableProperty(bindingType, propertyName, out var property))
        {
            return;
        }

        var normalizedToken = Unquote(rawValue!);
        if (!TryConvertValueExpression(
                normalizedToken,
                property.Type,
                compilation,
                document,
                setterTargetType,
                BindingPriorityScope.None,
                out var valueExpression))
        {
            return;
        }

        initializerParts.Add(propertyName + " = " + valueExpression);
    }

    private static bool TryGetWritableProperty(INamedTypeSymbol type, string propertyName, out IPropertySymbol property)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var candidate = current.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(item => !item.IsStatic &&
                                        !item.IsIndexer &&
                                        item.SetMethod is not null);
            if (candidate is not null)
            {
                property = candidate;
                return true;
            }
        }

        property = null!;
        return false;
    }

    private static bool TryBuildRelativeSourceExpression(
        RelativeSourceMarkup relativeSourceMarkup,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        if (compilation.GetTypeByMetadataName("Avalonia.Data.RelativeSource") is null)
        {
            return false;
        }

        var mode = relativeSourceMarkup.Mode;
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = !string.IsNullOrWhiteSpace(relativeSourceMarkup.AncestorTypeToken)
                ? "FindAncestor"
                : "Self";
        }

        if (!TryMapRelativeSourceMode(mode!, out var relativeSourceModeExpression))
        {
            return false;
        }

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(relativeSourceMarkup.AncestorTypeToken))
        {
            var ancestorType = ResolveTypeToken(compilation, document, relativeSourceMarkup.AncestorTypeToken!, document.ClassNamespace);
            if (ancestorType is null)
            {
                return false;
            }

            initializerParts.Add("AncestorType = typeof(" +
                                 ancestorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                 ")");
        }

        if (relativeSourceMarkup.AncestorLevel.HasValue)
        {
            initializerParts.Add("AncestorLevel = " + relativeSourceMarkup.AncestorLevel.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(relativeSourceMarkup.Tree) &&
            TryMapTreeType(relativeSourceMarkup.Tree!, out var treeTypeExpression))
        {
            initializerParts.Add("Tree = " + treeTypeExpression);
        }

        if (initializerParts.Count == 0)
        {
            expression = "new global::Avalonia.Data.RelativeSource(" + relativeSourceModeExpression + ")";
            return true;
        }

        expression = "new global::Avalonia.Data.RelativeSource(" + relativeSourceModeExpression + ") { " +
                     string.Join(", ", initializerParts) +
                     " }";
        return true;
    }

    private static bool TryMapBindingMode(string modeToken, out string expression)
    {
        expression = string.Empty;
        var normalized = modeToken.Trim();
        if (normalized.StartsWith("global::Avalonia.Data.BindingMode.", StringComparison.Ordinal))
        {
            expression = normalized;
            return true;
        }

        expression = normalized.ToLowerInvariant() switch
        {
            "default" => "global::Avalonia.Data.BindingMode.Default",
            "oneway" => "global::Avalonia.Data.BindingMode.OneWay",
            "twoway" => "global::Avalonia.Data.BindingMode.TwoWay",
            "onewaytosource" => "global::Avalonia.Data.BindingMode.OneWayToSource",
            "onetime" => "global::Avalonia.Data.BindingMode.OneTime",
            _ => string.Empty
        };

        return expression.Length > 0;
    }

    private static bool TryMapRelativeSourceMode(string modeToken, out string expression)
    {
        expression = modeToken.Trim().ToLowerInvariant() switch
        {
            "self" => "global::Avalonia.Data.RelativeSourceMode.Self",
            "templatedparent" => "global::Avalonia.Data.RelativeSourceMode.TemplatedParent",
            "datacontext" => "global::Avalonia.Data.RelativeSourceMode.DataContext",
            "findancestor" => "global::Avalonia.Data.RelativeSourceMode.FindAncestor",
            "ancestor" => "global::Avalonia.Data.RelativeSourceMode.FindAncestor",
            _ => string.Empty
        };

        return expression.Length > 0;
    }

    private static bool TryMapTreeType(string treeToken, out string expression)
    {
        expression = treeToken.Trim().ToLowerInvariant() switch
        {
            "visual" => "global::Avalonia.Data.TreeType.Visual",
            "logical" => "global::Avalonia.Data.TreeType.Logical",
            _ => string.Empty
        };

        return expression.Length > 0;
    }

    private static string? GetDefaultBindingPriorityToken(BindingPriorityScope scope)
    {
        return scope switch
        {
            BindingPriorityScope.Style => "Style",
            BindingPriorityScope.Template => "Template",
            _ => null
        };
    }

    private static string? GetSetValueBindingPriorityExpression(
        INamedTypeSymbol targetType,
        IFieldSymbol propertyField,
        Compilation compilation,
        BindingPriorityScope scope)
    {
        if (scope != BindingPriorityScope.Template)
        {
            return null;
        }

        if (!IsStyledOrAttachedAvaloniaProperty(propertyField))
        {
            return null;
        }

        if (!HasSetValueWithPriorityOverload(targetType, compilation))
        {
            return null;
        }

        return "global::Avalonia.Data.BindingPriority.Template";
    }

    private static bool IsStyledOrAttachedAvaloniaProperty(IFieldSymbol propertyField)
    {
        for (var current = propertyField.Type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name is "StyledProperty" or "AvaloniaAttachedProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSetValueWithPriorityOverload(INamedTypeSymbol targetType, Compilation compilation)
    {
        var bindingPriorityType = compilation.GetTypeByMetadataName("Avalonia.Data.BindingPriority");
        if (bindingPriorityType is null)
        {
            return false;
        }

        for (var current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers("SetValue").OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.Parameters.Length != 3)
                {
                    continue;
                }

                if (!IsAvaloniaPropertyType(method.Parameters[0].Type))
                {
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(method.Parameters[2].Type, bindingPriorityType))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool CanAssignBindingValue(ITypeSymbol propertyType, Compilation compilation)
    {
        if (propertyType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        var iBindingType = compilation.GetTypeByMetadataName("Avalonia.Data.IBinding");
        if (iBindingType is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(propertyType, iBindingType))
        {
            return true;
        }

        if (propertyType is INamedTypeSymbol namedPropertyType &&
            namedPropertyType.AllInterfaces.Any(interfaceType => SymbolEqualityComparer.Default.Equals(interfaceType, iBindingType)))
        {
            return true;
        }

        for (var current = propertyType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, iBindingType))
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? TryGetAvaloniaPropertyValueType(ITypeSymbol propertyFieldType)
    {
        if (propertyFieldType is not INamedTypeSymbol namedType)
        {
            return null;
        }

        if (namedType.IsGenericType &&
            namedType.Name == "AvaloniaProperty" &&
            namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0];
        }

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                current.Name == "AvaloniaProperty" &&
                current.TypeArguments.Length == 1)
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool TryGetAvaloniaUnsetValueExpression(Compilation compilation, out string expression)
    {
        expression = string.Empty;
        var avaloniaPropertyType = compilation.GetTypeByMetadataName("Avalonia.AvaloniaProperty");
        if (avaloniaPropertyType is null)
        {
            return false;
        }

        var hasUnsetMember =
            avaloniaPropertyType.GetMembers("UnsetValue").OfType<IFieldSymbol>().Any(member => member.IsStatic) ||
            avaloniaPropertyType.GetMembers("UnsetValue").OfType<IPropertySymbol>().Any(member => member.IsStatic);
        if (!hasUnsetMember)
        {
            return false;
        }

        expression = "global::Avalonia.AvaloniaProperty.UnsetValue";
        return true;
    }

    private static bool TryFindAvaloniaPropertyField(
        INamedTypeSymbol ownerType,
        string propertyName,
        out INamedTypeSymbol resolvedOwnerType,
        out IFieldSymbol propertyField,
        string? explicitFieldName = null)
    {
        var fieldName = string.IsNullOrWhiteSpace(explicitFieldName)
            ? propertyName + "Property"
            : explicitFieldName.Trim();
        for (INamedTypeSymbol? current = ownerType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(member => member.IsStatic);
            if (field is not null)
            {
                resolvedOwnerType = current;
                propertyField = field;
                return true;
            }
        }

        resolvedOwnerType = ownerType;
        propertyField = null!;
        return false;
    }

    private static bool TryConvertUntypedValueExpression(string value, out string expression)
    {
        var trimmed = value.Trim();

        if (bool.TryParse(trimmed, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            expression = doubleValue.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        expression = "\"" + Escape(trimmed) + "\"";
        return true;
    }

    private static bool TryParseHandlerName(string value, out string? handlerName)
    {
        handlerName = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.IndexOf('.') >= 0)
        {
            return false;
        }

        if (!IsIdentifier(trimmed))
        {
            return false;
        }

        handlerName = trimmed;
        return true;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var ch = value[index];
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCompatibleInstanceMethod(
        INamedTypeSymbol type,
        string methodName,
        ITypeSymbol delegateType)
    {
        if (delegateType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.DelegateInvokeMethod is not { } invokeMethod)
        {
            return HasInstanceMethod(type, methodName);
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (IsMethodCompatibleWithDelegate(method, invokeMethod))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasInstanceMethod(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(member =>
                !member.IsStatic &&
                member.MethodKind == MethodKind.Ordinary);
            if (method is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMethodCompatibleWithDelegate(
        IMethodSymbol candidate,
        IMethodSymbol delegateInvoke)
    {
        if (candidate.Parameters.Length != delegateInvoke.Parameters.Length)
        {
            return false;
        }

        if (delegateInvoke.ReturnsVoid != candidate.ReturnsVoid)
        {
            return false;
        }

        if (!delegateInvoke.ReturnsVoid &&
            !IsTypeAssignableTo(candidate.ReturnType, delegateInvoke.ReturnType))
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < delegateInvoke.Parameters.Length; parameterIndex++)
        {
            var delegateParameter = delegateInvoke.Parameters[parameterIndex];
            var candidateParameter = candidate.Parameters[parameterIndex];
            if (!IsTypeAssignableTo(delegateParameter.Type, candidateParameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindStaticEventField(
        INamedTypeSymbol targetType,
        string eventName,
        out INamedTypeSymbol ownerType,
        out IFieldSymbol eventField)
    {
        var fieldName = eventName + "Event";
        for (INamedTypeSymbol? current = targetType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(member => member.IsStatic);
            if (field is null)
            {
                continue;
            }

            ownerType = current;
            eventField = field;
            return true;
        }

        ownerType = targetType;
        eventField = null!;
        return false;
    }

    private static bool TryResolveRoutedEventHandlerType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol handlerType)
    {
        handlerType = compilation.GetTypeByMetadataName("System.Delegate") ?? compilation.ObjectType;
        if (!TryGetRoutedEventArgsType(routedEventType, compilation, out var routedEventArgsType))
        {
            return false;
        }

        var eventHandlerType = compilation.GetTypeByMetadataName("System.EventHandler`1");
        var eventArgsBaseType = compilation.GetTypeByMetadataName("System.EventArgs");
        if (eventHandlerType is INamedTypeSymbol eventHandlerNamed &&
            eventArgsBaseType is not null &&
            IsTypeAssignableTo(routedEventArgsType, eventArgsBaseType))
        {
            handlerType = eventHandlerNamed.Construct(routedEventArgsType);
            return true;
        }

        var routedEventHandlerType = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEventHandler");
        if (routedEventHandlerType is not null)
        {
            handlerType = routedEventHandlerType;
            return true;
        }

        return true;
    }

    private static bool TryGetRoutedEventArgsType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol routedEventArgsType)
    {
        routedEventArgsType = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEventArgs")
                              ?? compilation.GetTypeByMetadataName("System.EventArgs")
                              ?? compilation.ObjectType;

        if (routedEventType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var routedEventTypeSymbol = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent");
        var genericRoutedEventTypeSymbol = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent`1");
        for (INamedTypeSymbol? current = namedType; current is not null; current = current.BaseType)
        {
            var isGenericRoutedEvent = genericRoutedEventTypeSymbol is not null &&
                                       SymbolEqualityComparer.Default.Equals(
                                           current.OriginalDefinition,
                                           genericRoutedEventTypeSymbol);
            if (!isGenericRoutedEvent &&
                current.Name == "RoutedEvent" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Interactivity" &&
                current.IsGenericType &&
                current.TypeArguments.Length == 1)
            {
                isGenericRoutedEvent = true;
            }

            if (isGenericRoutedEvent && current.TypeArguments.Length == 1)
            {
                routedEventArgsType = current.TypeArguments[0];
                return true;
            }

            var isNonGenericRoutedEvent = routedEventTypeSymbol is not null &&
                                          SymbolEqualityComparer.Default.Equals(current, routedEventTypeSymbol);
            if (!isNonGenericRoutedEvent &&
                current.Name == "RoutedEvent" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Interactivity" &&
                !current.IsGenericType)
            {
                isNonGenericRoutedEvent = true;
            }

            if (isNonGenericRoutedEvent)
            {
                return true;
            }
        }

        return false;
    }

    private static IMethodSymbol? FindParameterlessMethod(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(member =>
                !member.IsStatic &&
                member.MethodKind == MethodKind.Ordinary &&
                member.Parameters.Length == 0);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static IMethodSymbol? FindAttachedPropertyGetterMethod(
        INamedTypeSymbol ownerType,
        string propertyName,
        ITypeSymbol targetType)
    {
        var getterName = "Get" + propertyName;
        foreach (var method in ownerType.GetMembers(getterName).OfType<IMethodSymbol>())
        {
            if (!method.IsStatic ||
                method.MethodKind != MethodKind.Ordinary ||
                method.Parameters.Length != 1)
            {
                continue;
            }

            if (IsTypeAssignableTo(targetType, method.Parameters[0].Type))
            {
                return method;
            }
        }

        return null;
    }

    private static bool IsTypeAssignableTo(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return true;
        }

        if (sourceType is INamedTypeSymbol sourceNamed)
        {
            for (INamedTypeSymbol? current = sourceNamed; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, targetType))
                {
                    return true;
                }

                foreach (var implementedInterface in current.Interfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(implementedInterface, targetType))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEventSymbol? FindEvent(INamedTypeSymbol type, string eventName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string propertyName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static string NormalizePropertyName(string propertyName)
    {
        var separator = propertyName.LastIndexOf('.');
        return separator < 0 ? propertyName : propertyName.Substring(separator + 1);
    }

    private static bool IsDesignTimePropertyToken(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var trimmed = propertyName.Trim();
        return trimmed.StartsWith("Design.", StringComparison.Ordinal);
    }

    private static bool TrySplitOwnerQualifiedPropertyToken(
        string propertyToken,
        out string ownerToken,
        out string propertyName)
    {
        ownerToken = string.Empty;
        propertyName = string.Empty;

        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        var trimmed = propertyToken.Trim();
        var separator = trimmed.LastIndexOf('.');
        if (separator <= 0 || separator >= trimmed.Length - 1)
        {
            return false;
        }

        ownerToken = trimmed.Substring(0, separator);
        propertyName = trimmed.Substring(separator + 1);
        return ownerToken.Length > 0 && propertyName.Length > 0;
    }

    private static bool HasResolvedPropertyAssignment(
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        string propertyName)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            if (assignments[index].PropertyName.Equals(propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertValueExpression(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null)
    {
        if (TryConvertValueConversion(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var conversion,
                preferTypedStaticResourceCoercion: preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback: allowObjectStringLiteralFallback,
                allowStaticParseMethodFallback: true,
                selectorNestingTypeHint: selectorNestingTypeHint))
        {
            expression = conversion.Expression;
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static bool TryConvertValueConversion(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        bool allowStaticParseMethodFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null)
    {
        conversion = default;

        if (TryConvertMarkupExtensionConversion(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion))
        {
            return true;
        }

        if (type is INamedTypeSymbol nullableType &&
            nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            nullableType.TypeArguments.Length == 1)
        {
            if (value.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                conversion = CreateLiteralConversion("null");
                return true;
            }

            return TryConvertValueConversion(
                value,
                nullableType.TypeArguments[0],
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback,
                allowStaticParseMethodFallback,
                selectorNestingTypeHint);
        }

        if (IsAvaloniaPropertyType(type) &&
            TryResolveAvaloniaPropertyReferenceExpression(value, compilation, document, setterTargetType, out var propertyReferenceExpression))
        {
            conversion = CreateLiteralConversion(propertyReferenceExpression);
            return true;
        }

        if (IsAvaloniaSelectorType(type) &&
            TryBuildSimpleSelectorExpression(
                value,
                compilation,
                document,
                setterTargetType,
                selectorNestingTypeHint,
                out var selectorExpression))
        {
            conversion = CreateLiteralConversion(selectorExpression);
            return true;
        }

        var escaped = Escape(value);

        var fullyQualifiedTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullyQualifiedTypeName is "global::System.Globalization.CultureInfo" or "global::System.Globalization.CultureInfo?")
        {
            conversion = CreateLiteralConversion(
                "global::System.Globalization.CultureInfo.GetCultureInfo(\"" + escaped + "\")");
            return true;
        }

        if (fullyQualifiedTypeName is "global::System.Type" or "global::System.Type?")
        {
            var resolvedType = ResolveTypeFromTypeExpression(
                compilation,
                document,
                Unquote(value),
                document.ClassNamespace);
            if (resolvedType is not null)
            {
                conversion = CreateLiteralConversion(
                    "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")");
                return true;
            }
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            conversion = CreateLiteralConversion("\"" + escaped + "\"");
            return true;
        }

        if (type.SpecialType == SpecialType.System_Boolean && bool.TryParse(value, out var boolValue))
        {
            conversion = CreateLiteralConversion(boolValue ? "true" : "false");
            return true;
        }

        if (type.SpecialType == SpecialType.System_Int32 && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            conversion = CreateLiteralConversion(intValue.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        if (type.SpecialType == SpecialType.System_Int64 && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            conversion = CreateLiteralConversion(longValue.ToString(CultureInfo.InvariantCulture) + "L");
            return true;
        }

        if (type.SpecialType == SpecialType.System_Double && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            conversion = CreateLiteralConversion(doubleValue.ToString("R", CultureInfo.InvariantCulture) + "d");
            return true;
        }

        if (type.SpecialType == SpecialType.System_Single && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            conversion = CreateLiteralConversion(floatValue.ToString("R", CultureInfo.InvariantCulture) + "f");
            return true;
        }

        if (TryConvertStaticPropertyValueExpression(type, value, out var staticPropertyExpression))
        {
            conversion = CreateLiteralConversion(staticPropertyExpression);
            return true;
        }

        if (TryConvertCollectionLiteralExpression(
                type,
                value,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var collectionExpression))
        {
            conversion = CreateLiteralConversion(collectionExpression);
            return true;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            if (TryConvertEnumValueExpression(enumType, value, out var enumExpression))
            {
                conversion = CreateLiteralConversion(enumExpression);
                return true;
            }
        }

        if (fullyQualifiedTypeName is "global::System.Uri" or "global::System.Uri?")
        {
            conversion = CreateLiteralConversion(
                "new global::System.Uri(\"" + escaped + "\", global::System.UriKind.RelativeOrAbsolute)");
            return true;
        }

        if (TryConvertAvaloniaBrushExpression(type, value, compilation, out var brushExpression))
        {
            conversion = CreateLiteralConversion(brushExpression);
            return true;
        }

        if (TryConvertAvaloniaTransformExpression(type, value, compilation, out var transformExpression))
        {
            conversion = CreateLiteralConversion(transformExpression);
            return true;
        }

        if (allowStaticParseMethodFallback &&
            TryConvertByStaticParseMethod(type, value, out var parsedExpression))
        {
            conversion = CreateLiteralConversion(parsedExpression);
            return true;
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            if (!allowObjectStringLiteralFallback)
            {
                return false;
            }

            conversion = CreateLiteralConversion("\"" + escaped + "\"");
            return true;
        }

        return false;
    }

    private static bool TryConvertEnumValueExpression(
        INamedTypeSymbol enumType,
        string value,
        out string expression)
    {
        expression = string.Empty;

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericValue))
        {
            expression = "(" + enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" +
                         numericValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        var separators = new[] { ',', '|' };
        var tokens = trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var enumMembers = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static member => member.HasConstantValue)
            .ToArray();
        if (enumMembers.Length == 0)
        {
            return false;
        }

        var fullyQualifiedEnumTypeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var shortEnumTypeName = enumType.Name;
        var memberExpressions = ImmutableArray.CreateBuilder<string>(tokens.Length);
        foreach (var token in tokens)
        {
            var normalizedToken = NormalizeEnumToken(token, shortEnumTypeName);
            var enumMember = enumMembers.FirstOrDefault(member =>
                member.Name.Equals(normalizedToken, StringComparison.OrdinalIgnoreCase));
            if (enumMember is null)
            {
                return false;
            }

            memberExpressions.Add(fullyQualifiedEnumTypeName + "." + enumMember.Name);
        }

        if (memberExpressions.Count == 0)
        {
            return false;
        }

        expression = memberExpressions.Count == 1
            ? memberExpressions[0]
            : string.Join(" | ", memberExpressions);
        return true;
    }

    private static bool TryConvertStaticPropertyValueExpression(
        ITypeSymbol type,
        string value,
        out string expression)
    {
        expression = string.Empty;
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var token = value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var memberToken = token;
        var separatorIndex = token.LastIndexOf('.');
        if (separatorIndex > 0 && separatorIndex < token.Length - 1)
        {
            var ownerToken = token.Substring(0, separatorIndex).Trim();
            var shortOwner = namedType.Name;
            var fullyQualifiedOwner = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty, StringComparison.Ordinal);
            if (ownerToken.Equals(shortOwner, StringComparison.OrdinalIgnoreCase) ||
                ownerToken.Equals(fullyQualifiedOwner, StringComparison.OrdinalIgnoreCase))
            {
                memberToken = token.Substring(separatorIndex + 1).Trim();
            }
        }

        if (memberToken.Length == 0)
        {
            return false;
        }

        var staticProperty = namedType.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property =>
                property.IsStatic &&
                !property.IsIndexer &&
                SymbolEqualityComparer.Default.Equals(property.Type, namedType) &&
                property.Name.Equals(memberToken, StringComparison.OrdinalIgnoreCase));
        if (staticProperty is not null)
        {
            expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "." +
                         staticProperty.Name;
            return true;
        }

        var staticField = namedType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field =>
                field.IsStatic &&
                SymbolEqualityComparer.Default.Equals(field.Type, namedType) &&
                field.Name.Equals(memberToken, StringComparison.OrdinalIgnoreCase));
        if (staticField is null)
        {
            return false;
        }

        expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "." +
                     staticField.Name;
        return true;
    }

    private static string NormalizeEnumToken(string token, string enumTypeName)
    {
        var trimmedToken = token.Trim();
        var separatorIndex = trimmedToken.LastIndexOf('.');
        if (separatorIndex > 0 && separatorIndex < trimmedToken.Length - 1)
        {
            var ownerToken = trimmedToken.Substring(0, separatorIndex).Trim();
            var memberToken = trimmedToken.Substring(separatorIndex + 1).Trim();
            if (ownerToken.Equals(enumTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return memberToken;
            }

            return memberToken;
        }

        return trimmedToken;
    }

    private static bool TryConvertCollectionLiteralExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryGetCollectionElementType(
                targetType,
                out var elementType,
                out var isArrayTarget,
                out var collectionTypeForSplitConfig))
        {
            return false;
        }

        var trimEntriesFlag = (StringSplitOptions)2;
        var splitOptions = StringSplitOptions.RemoveEmptyEntries | trimEntriesFlag;
        var separators = new[] { "," };
        if (collectionTypeForSplitConfig is not null)
        {
            TryGetCollectionSplitConfiguration(
                collectionTypeForSplitConfig,
                ref separators,
                ref splitOptions,
                trimEntriesFlag);
        }

        string[] items;
        var trimmedValue = value.Trim();
        var useTopLevelCommaSplit = separators.Length == 1 &&
                                    separators[0] == ",";
        if (trimmedValue.Length == 0)
        {
            items = Array.Empty<string>();
        }
        else if (useTopLevelCommaSplit)
        {
            items = SplitTopLevel(trimmedValue, ',').ToArray();
            if ((splitOptions & trimEntriesFlag) != 0)
            {
                for (var index = 0; index < items.Length; index++)
                {
                    items[index] = items[index].Trim();
                }
            }

            if ((splitOptions & StringSplitOptions.RemoveEmptyEntries) != 0)
            {
                items = items.Where(item => item.Length > 0).ToArray();
            }
        }
        else
        {
            var effectiveSplitOptions = splitOptions & ~trimEntriesFlag;
            items = trimmedValue.Split(separators, effectiveSplitOptions);
            if ((splitOptions & trimEntriesFlag) != 0)
            {
                for (var index = 0; index < items.Length; index++)
                {
                    items[index] = items[index].Trim();
                }
            }

            if ((splitOptions & StringSplitOptions.RemoveEmptyEntries) != 0)
            {
                items = items.Where(item => item.Length > 0).ToArray();
            }
        }

        var itemExpressions = new List<string>(items.Length);
        foreach (var item in items)
        {
            if (!TryConvertValueExpression(
                    item,
                    elementType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var itemExpression))
            {
                return false;
            }

            itemExpressions.Add(itemExpression);
        }

        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var listTypeName = "global::System.Collections.Generic.List<" + elementTypeName + ">";
        var listExpression = itemExpressions.Count == 0
            ? "new " + listTypeName + "()"
            : "new " + listTypeName + " { " + string.Join(", ", itemExpressions) + " }";

        if (isArrayTarget)
        {
            expression = "new " + elementTypeName + "[] { " + string.Join(", ", itemExpressions) + " }";
            return true;
        }

        var listTypeDefinition = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        var listTypeSymbol = listTypeDefinition?.Construct(elementType);
        if (listTypeSymbol is not null &&
            IsTypeAssignableTo(listTypeSymbol, targetType))
        {
            expression = listExpression;
            return true;
        }

        if (targetType is INamedTypeSymbol namedTargetType &&
            !namedTargetType.IsAbstract)
        {
            var constructor = namedTargetType.Constructors
                .FirstOrDefault(ctor =>
                    !ctor.IsStatic &&
                    ctor.Parameters.Length == 1 &&
                    listTypeSymbol is not null &&
                    IsTypeAssignableTo(listTypeSymbol, ctor.Parameters[0].Type));
            if (constructor is not null)
            {
                expression = "new " + namedTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                             "(" +
                             listExpression +
                             ")";
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCollectionElementType(
        ITypeSymbol targetType,
        out ITypeSymbol elementType,
        out bool isArrayTarget,
        out INamedTypeSymbol? collectionTypeForSplitConfig)
    {
        elementType = null!;
        isArrayTarget = false;
        collectionTypeForSplitConfig = null;

        if (targetType.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (targetType is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            isArrayTarget = true;
            return true;
        }

        if (targetType is not INamedTypeSymbol namedTargetType)
        {
            return false;
        }

        collectionTypeForSplitConfig = namedTargetType;

        if (TryGetGenericCollectionElementType(namedTargetType, out elementType))
        {
            return true;
        }

        foreach (var interfaceType in namedTargetType.AllInterfaces)
        {
            if (interfaceType is not INamedTypeSymbol namedInterface ||
                !TryGetGenericCollectionElementType(namedInterface, out elementType))
            {
                continue;
            }

            if (collectionTypeForSplitConfig is null)
            {
                collectionTypeForSplitConfig = namedInterface;
            }

            return true;
        }

        return false;
    }

    private static bool TryGetGenericCollectionElementType(
        INamedTypeSymbol type,
        out ITypeSymbol elementType)
    {
        elementType = null!;
        if (!type.IsGenericType || type.TypeArguments.Length != 1)
        {
            return false;
        }

        var definitionName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (definitionName is
            "global::System.Collections.Generic.IEnumerable<T>" or
            "global::System.Collections.Generic.ICollection<T>" or
            "global::System.Collections.Generic.IReadOnlyCollection<T>" or
            "global::System.Collections.Generic.IList<T>" or
            "global::System.Collections.Generic.IReadOnlyList<T>")
        {
            elementType = type.TypeArguments[0];
            return true;
        }

        return false;
    }

    private static void TryGetCollectionSplitConfiguration(
        INamedTypeSymbol collectionType,
        ref string[] separators,
        ref StringSplitOptions splitOptions,
        StringSplitOptions trimEntriesFlag)
    {
        var listAttribute = collectionType.GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Metadata.AvaloniaListAttribute");
        if (listAttribute is null)
        {
            return;
        }

        foreach (var (key, value) in listAttribute.NamedArguments)
        {
            if (key.Equals("Separators", StringComparison.Ordinal) &&
                value.Kind == TypedConstantKind.Array &&
                !value.IsNull)
            {
                var configuredSeparators = value.Values
                    .Where(item => item.Kind == TypedConstantKind.Primitive && item.Value is string)
                    .Select(item => (string)item.Value!)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                if (configuredSeparators.Length > 0)
                {
                    separators = configuredSeparators;
                }

                continue;
            }

            if (key.Equals("SplitOptions", StringComparison.Ordinal) &&
                value.Kind == TypedConstantKind.Enum &&
                value.Value is int configuredSplitOptions)
            {
                splitOptions = (StringSplitOptions)configuredSplitOptions;
            }
        }

        splitOptions |= trimEntriesFlag;
    }

    private static bool TryBuildRuntimeXamlFragmentExpression(
        string value,
        ITypeSymbol targetType,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        var trimmed = value.Trim();
        if (!IsLikelyXamlFragment(trimmed))
        {
            return false;
        }

        var baseUri = string.IsNullOrWhiteSpace(document.TargetPath)
            ? document.FilePath
            : document.TargetPath;
        var escapedBaseUri = Escape(baseUri ?? string.Empty);
        var escapedXaml = Escape(trimmed);

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideRuntimeXamlValue(\"" +
            escapedXaml +
            "\", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", \"" +
            escapedBaseUri +
            "\", " +
            MarkupContextParentStackToken +
            ")");
        return true;
    }

    private static bool IsLikelyXamlFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= 3 &&
               trimmed[0] == '<' &&
               trimmed[^1] == '>';
    }

    private static bool TryConvertAvaloniaBrushExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        if (targetType.SpecialType == SpecialType.System_Object ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var iBrushType = compilation.GetTypeByMetadataName("Avalonia.Media.IBrush");
        var brushType = compilation.GetTypeByMetadataName("Avalonia.Media.Brush");
        if (iBrushType is null || brushType is null)
        {
            return false;
        }

        if (!IsTypeAssignableTo(iBrushType, targetType))
        {
            return false;
        }

        expression = "global::Avalonia.Media.Brush.Parse(\"" + Escape(value.Trim()) + "\")";
        return true;
    }

    private static bool TryConvertAvaloniaTransformExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || targetType.SpecialType == SpecialType.System_Object)
        {
            return false;
        }

        var transformOperationsType = compilation.GetTypeByMetadataName("Avalonia.Media.Transformation.TransformOperations");
        if (transformOperationsType is null)
        {
            return false;
        }

        if (!IsTypeAssignableTo(transformOperationsType, targetType))
        {
            return false;
        }

        expression = "global::Avalonia.Media.Transformation.TransformOperations.Parse(\"" +
                     Escape(trimmed) +
                     "\")";
        return true;
    }

    private static bool IsAvaloniaSelectorType(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return display == "global::Avalonia.Styling.Selector" ||
               display == "global::Avalonia.Styling.Selector?";
    }

    private static bool TryBuildSimpleSelectorExpression(
        string selector,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        out string expression)
    {
        expression = string.Empty;
        var text = selector.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var branchExpressions = ImmutableArray.CreateBuilder<string>();
        foreach (var branch in SplitTopLevel(text, ','))
        {
            var trimmedBranch = branch.Trim();
            if (trimmedBranch.Length == 0)
            {
                return false;
            }

            if (!TryBuildSimpleSelectorBranchExpression(
                    trimmedBranch,
                    compilation,
                    document,
                    selectorTypeFallback,
                    selectorNestingTypeHint,
                    out var branchExpression))
            {
                return false;
            }

            branchExpressions.Add(branchExpression);
        }

        if (branchExpressions.Count == 0)
        {
            return false;
        }

        expression = branchExpressions.Count == 1
            ? branchExpressions[0]
            : "global::Avalonia.Styling.Selectors.Or(" + string.Join(", ", branchExpressions) + ")";
        return true;
    }

    private static bool TryBuildSimpleSelectorBranchExpression(
        string selectorBranch,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        out string expression)
    {
        expression = string.Empty;
        if (!TryTokenizeSelectorBranch(selectorBranch, out var segments))
        {
            return false;
        }

        var currentExpression = "null";
        var hasExpression = false;
        foreach (var segment in segments)
        {
            if (segment.Combinator != SelectorCombinatorKind.None)
            {
                if (!hasExpression)
                {
                    return false;
                }

                currentExpression = segment.Combinator switch
                {
                    SelectorCombinatorKind.Descendant => "global::Avalonia.Styling.Selectors.Descendant(" + currentExpression + ")",
                    SelectorCombinatorKind.Child => "global::Avalonia.Styling.Selectors.Child(" + currentExpression + ")",
                    SelectorCombinatorKind.Template => "global::Avalonia.Styling.Selectors.Template(" + currentExpression + ")",
                    _ => currentExpression
                };
            }

            var segmentText = segment.Text.Trim();
            if (segmentText.Length == 0)
            {
                return false;
            }

            var segmentApplied = false;
            var index = 0;
            INamedTypeSymbol? selectorTypeHint = selectorTypeFallback;
            while (index < segmentText.Length && segmentText[index] == '^')
            {
                currentExpression = "global::Avalonia.Styling.Selectors.Nesting(" + (hasExpression ? currentExpression : "null") + ")";
                if (selectorNestingTypeHint is not null)
                {
                    selectorTypeHint = selectorNestingTypeHint;
                }
                hasExpression = true;
                segmentApplied = true;
                index++;
            }

            while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
            {
                index++;
            }

            var isWildcard = false;
            if (index < segmentText.Length && segmentText[index] == '*')
            {
                isWildcard = true;
                index++;
            }

            if (TryReadSelectorTypeToken(segmentText, ref index, out var typeToken))
            {
                if (!string.IsNullOrWhiteSpace(typeToken))
                {
                    var resolvedType = ResolveTypeToken(compilation, document, typeToken!, document.ClassNamespace);
                    if (resolvedType is null)
                    {
                        return false;
                    }

                    currentExpression = "global::Avalonia.Styling.Selectors.OfType(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", typeof(" +
                                        resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                        "))";
                    selectorTypeHint = resolvedType as INamedTypeSymbol;
                    hasExpression = true;
                    segmentApplied = true;
                }
            }
            else
            {
                return false;
            }

            while (index < segmentText.Length)
            {
                while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
                {
                    index++;
                }

                if (index >= segmentText.Length)
                {
                    break;
                }

                var tokenType = segmentText[index];
                if (tokenType == '[')
                {
                    if (!TryReadBalancedSelectorContent(segmentText, ref index, '[', ']', out var predicateText) ||
                        !TryApplySelectorPropertyPredicate(
                            predicateText,
                            compilation,
                            document,
                            selectorTypeHint,
                            ref currentExpression,
                            ref hasExpression))
                    {
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                if (tokenType != '.' && tokenType != '#' && tokenType != ':')
                {
                    return false;
                }

                index++;
                if (index >= segmentText.Length || !IsSelectorTokenStart(segmentText[index]))
                {
                    return false;
                }

                var tokenStart = index;
                while (index < segmentText.Length &&
                       IsSelectorTokenPart(segmentText[index]))
                {
                    index++;
                }

                if (tokenStart == index)
                {
                    return false;
                }

                var tokenValue = segmentText.Substring(tokenStart, index - tokenStart);
                if (tokenType == ':' && index < segmentText.Length && segmentText[index] == '(')
                {
                    if (!TryReadBalancedSelectorContent(segmentText, ref index, '(', ')', out var pseudoArgument) ||
                        !TryApplySelectorPseudoFunction(
                            tokenValue,
                            pseudoArgument,
                            compilation,
                            document,
                            selectorTypeFallback,
                            selectorNestingTypeHint,
                            ref currentExpression,
                            ref hasExpression,
                            ref selectorTypeHint))
                    {
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                if (tokenType == '.')
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Class(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", \"" +
                                        Escape(tokenValue) +
                                        "\")";
                }
                else if (tokenType == '#')
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Name(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", \"" +
                                        Escape(tokenValue) +
                                        "\")";
                }
                else
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Class(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", \":" +
                                        Escape(tokenValue) +
                                        "\")";
                }

                hasExpression = true;
                segmentApplied = true;
            }

            if (!segmentApplied && isWildcard)
            {
                var styledElementType = compilation.GetTypeByMetadataName("Avalonia.StyledElement");
                if (styledElementType is not null)
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Is(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", typeof(" +
                                        styledElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                        "))";
                    selectorTypeHint = styledElementType;
                    hasExpression = true;
                    segmentApplied = true;
                }
            }

            if (!segmentApplied)
            {
                return false;
            }
        }

        if (!hasExpression)
        {
            return false;
        }

        expression = currentExpression;
        return true;
    }

    private static bool TryReadBalancedSelectorContent(
        string text,
        ref int index,
        char openChar,
        char closeChar,
        out string content)
    {
        content = string.Empty;
        if (index >= text.Length || text[index] != openChar)
        {
            return false;
        }

        index++;
        var contentStart = index;
        var depth = 1;
        var inQuote = false;
        var quoteChar = '\0';

        while (index < text.Length)
        {
            var ch = text[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                index++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                index++;
                continue;
            }

            if (ch == openChar)
            {
                depth++;
                index++;
                continue;
            }

            if (ch == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    content = text.Substring(contentStart, index - contentStart);
                    index++;
                    return true;
                }

                index++;
                continue;
            }

            index++;
        }

        return false;
    }

    private static bool TryApplySelectorPseudoFunction(
        string pseudoName,
        string pseudoArgument,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        ref string currentExpression,
        ref bool hasExpression,
        ref INamedTypeSymbol? selectorTypeHint)
    {
        var normalizedPseudoName = pseudoName.Trim().ToLowerInvariant();
        if (normalizedPseudoName.Length == 0)
        {
            return false;
        }

        var previousExpression = hasExpression ? currentExpression : "null";
        if (normalizedPseudoName == "is")
        {
            var typeToken = pseudoArgument.Trim().Replace('|', ':');
            if (typeToken.Length == 0)
            {
                return false;
            }

            var resolvedType = ResolveTypeToken(compilation, document, typeToken, document.ClassNamespace);
            if (resolvedType is null)
            {
                return false;
            }

            currentExpression = "global::Avalonia.Styling.Selectors.Is(" +
                                previousExpression +
                                ", typeof(" +
                                resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                "))";
            selectorTypeHint = resolvedType as INamedTypeSymbol;
            hasExpression = true;
            return true;
        }

        if (normalizedPseudoName == "not")
        {
            if (!TryBuildSimpleSelectorExpression(
                    pseudoArgument,
                    compilation,
                    document,
                    selectorTypeHint ?? selectorTypeFallback,
                    selectorNestingTypeHint,
                    out var argumentExpression))
            {
                return false;
            }

            currentExpression = "global::Avalonia.Styling.Selectors.Not(" +
                                previousExpression +
                                ", " +
                                argumentExpression +
                                ")";
            hasExpression = true;
            return true;
        }

        if (normalizedPseudoName == "nth-child" &&
            TryParseNthChildPseudoArguments(pseudoArgument, out var nthChildStep, out var nthChildOffset))
        {
            currentExpression = "global::Avalonia.Styling.Selectors.NthChild(" +
                                previousExpression +
                                ", " +
                                nthChildStep.ToString(CultureInfo.InvariantCulture) +
                                ", " +
                                nthChildOffset.ToString(CultureInfo.InvariantCulture) +
                                ")";
            hasExpression = true;
            return true;
        }

        if (normalizedPseudoName == "nth-last-child" &&
            TryParseNthChildPseudoArguments(pseudoArgument, out var nthLastChildStep, out var nthLastChildOffset))
        {
            currentExpression = "global::Avalonia.Styling.Selectors.NthLastChild(" +
                                previousExpression +
                                ", " +
                                nthLastChildStep.ToString(CultureInfo.InvariantCulture) +
                                ", " +
                                nthLastChildOffset.ToString(CultureInfo.InvariantCulture) +
                                ")";
            hasExpression = true;
            return true;
        }

        return false;
    }

    private static bool TryParseNthChildPseudoArguments(string pseudoArgument, out int step, out int offset)
    {
        step = 0;
        offset = 0;

        var text = pseudoArgument.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return false;
        }

        if (text == "odd")
        {
            step = 2;
            offset = 1;
            return true;
        }

        if (text == "even")
        {
            step = 2;
            offset = 0;
            return true;
        }

        var compact = text.Replace(" ", string.Empty);
        var nIndex = compact.IndexOf('n');
        if (nIndex < 0)
        {
            if (!int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
            {
                return false;
            }

            step = 0;
            return true;
        }

        var stepToken = compact.Substring(0, nIndex);
        if (stepToken.Length == 0 || stepToken == "+")
        {
            step = 1;
        }
        else if (stepToken == "-")
        {
            step = -1;
        }
        else if (!int.TryParse(stepToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out step))
        {
            return false;
        }

        var offsetToken = compact.Substring(nIndex + 1);
        if (offsetToken.Length == 0)
        {
            offset = 0;
            return true;
        }

        if (!int.TryParse(offsetToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
        {
            return false;
        }

        return true;
    }

    private static bool TryApplySelectorPropertyPredicate(
        string predicateText,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        ref string currentExpression,
        ref bool hasExpression)
    {
        if (!TryParseSelectorPropertyPredicate(predicateText, out var propertyToken, out var rawValue))
        {
            return false;
        }

        if (!TryResolveAvaloniaPropertyReferenceExpression(
                propertyToken,
                compilation,
                document,
                defaultOwnerType,
                out var propertyExpression,
                out var propertyValueType))
        {
            return false;
        }

        var selectorValue = Unquote(rawValue);
        var valueExpression = string.Empty;
        if (propertyValueType is null)
        {
            if (!TryConvertUntypedValueExpression(selectorValue, out valueExpression))
            {
                return false;
            }
        }
        else if (!TryConvertValueConversion(
                     selectorValue,
                     propertyValueType,
                     compilation,
                     document,
                     defaultOwnerType,
                     BindingPriorityScope.Style,
                     out var typedValueConversion,
                     allowObjectStringLiteralFallback: propertyValueType.SpecialType == SpecialType.System_Object,
                     allowStaticParseMethodFallback: false))
        {
            return false;
        }
        else
        {
            valueExpression = typedValueConversion.Expression;
        }
        currentExpression = "global::Avalonia.Styling.Selectors.PropertyEquals(" +
                            (hasExpression ? currentExpression : "null") +
                            ", " +
                            propertyExpression +
                            ", " +
                            valueExpression +
                            ")";
        hasExpression = true;
        return true;
    }

    private static bool TryParseSelectorPropertyPredicate(
        string predicateText,
        out string propertyToken,
        out string rawValue)
    {
        propertyToken = string.Empty;
        rawValue = string.Empty;
        if (string.IsNullOrWhiteSpace(predicateText))
        {
            return false;
        }

        var equalsIndex = IndexOfTopLevel(predicateText, '=');
        if (equalsIndex <= 0 || equalsIndex >= predicateText.Length - 1)
        {
            return false;
        }

        var propertyText = predicateText.Substring(0, equalsIndex).Trim();
        rawValue = predicateText.Substring(equalsIndex + 1).Trim();
        if (propertyText.Length == 0 || rawValue.Length == 0)
        {
            return false;
        }

        if (propertyText.StartsWith("(", StringComparison.Ordinal) &&
            propertyText.EndsWith(")", StringComparison.Ordinal) &&
            propertyText.Length > 2)
        {
            propertyText = propertyText.Substring(1, propertyText.Length - 2).Trim();
        }

        propertyToken = propertyText.Replace('|', ':');
        return propertyToken.Length > 0;
    }

    private static bool TryTokenizeSelectorBranch(string selectorBranch, out ImmutableArray<SelectorBranchSegment> segments)
    {
        segments = ImmutableArray<SelectorBranchSegment>.Empty;
        var builder = ImmutableArray.CreateBuilder<SelectorBranchSegment>();
        var index = 0;
        var hasSegment = false;
        var pendingCombinator = SelectorCombinatorKind.None;

        while (index < selectorBranch.Length)
        {
            var consumedWhitespace = false;
            while (index < selectorBranch.Length && char.IsWhiteSpace(selectorBranch[index]))
            {
                consumedWhitespace = true;
                index++;
            }

            if (index >= selectorBranch.Length)
            {
                break;
            }

            if (consumedWhitespace &&
                hasSegment &&
                pendingCombinator == SelectorCombinatorKind.None)
            {
                pendingCombinator = SelectorCombinatorKind.Descendant;
            }

            if (selectorBranch[index] == '>')
            {
                if (!hasSegment)
                {
                    return false;
                }

                pendingCombinator = SelectorCombinatorKind.Child;
                index++;
                continue;
            }

            if (IsSelectorTemplateAxisAt(selectorBranch, index))
            {
                if (!hasSegment)
                {
                    return false;
                }

                pendingCombinator = SelectorCombinatorKind.Template;
                index += SelectorTemplateAxisToken.Length;
                continue;
            }

            var segmentStart = index;
            var bracketDepth = 0;
            var parenthesisDepth = 0;
            var inQuote = false;
            var quoteChar = '\0';

            while (index < selectorBranch.Length)
            {
                var ch = selectorBranch[index];
                if (inQuote)
                {
                    if (ch == quoteChar)
                    {
                        inQuote = false;
                    }

                    index++;
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inQuote = true;
                    quoteChar = ch;
                    index++;
                    continue;
                }

                if (ch == '[')
                {
                    bracketDepth++;
                    index++;
                    continue;
                }

                if (ch == ']')
                {
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    index++;
                    continue;
                }

                if (ch == '(')
                {
                    parenthesisDepth++;
                    index++;
                    continue;
                }

                if (ch == ')')
                {
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    index++;
                    continue;
                }

                if (bracketDepth == 0 && parenthesisDepth == 0)
                {
                    if (char.IsWhiteSpace(ch) || ch == '>' || IsSelectorTemplateAxisAt(selectorBranch, index))
                    {
                        break;
                    }
                }

                index++;
            }

            var segmentText = selectorBranch.Substring(segmentStart, index - segmentStart).Trim();
            if (segmentText.Length == 0)
            {
                return false;
            }

            builder.Add(new SelectorBranchSegment(segmentText, pendingCombinator));
            pendingCombinator = SelectorCombinatorKind.None;
            hasSegment = true;
        }

        if (builder.Count == 0 || pendingCombinator != SelectorCombinatorKind.None)
        {
            return false;
        }

        segments = builder.ToImmutable();
        return true;
    }

    private static bool TryReadSelectorTypeToken(string segmentText, ref int index, out string? typeToken)
    {
        typeToken = null;
        while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
        {
            index++;
        }

        if (index >= segmentText.Length || !IsIdentifierStart(segmentText[index]))
        {
            return true;
        }

        var aliasStart = index;
        index++;
        while (index < segmentText.Length && IsIdentifierPart(segmentText[index]))
        {
            index++;
        }

        var aliasOrType = segmentText.Substring(aliasStart, index - aliasStart);
        if (index < segmentText.Length && segmentText[index] == '|')
        {
            index++;
            if (index >= segmentText.Length || !IsIdentifierStart(segmentText[index]))
            {
                return false;
            }

            var typeStart = index;
            index++;
            while (index < segmentText.Length && IsIdentifierPart(segmentText[index]))
            {
                index++;
            }

            var typeName = segmentText.Substring(typeStart, index - typeStart);
            typeToken = aliasOrType + ":" + typeName;
            return true;
        }

        typeToken = aliasOrType;
        return true;
    }

    private static bool IsSelectorTemplateAxisAt(string text, int index)
    {
        if (index < 0 || index + SelectorTemplateAxisToken.Length > text.Length)
        {
            return false;
        }

        return text.Substring(index, SelectorTemplateAxisToken.Length)
            .Equals(SelectorTemplateAxisToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertMarkupExtensionExpression(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true)
    {
        if (TryConvertMarkupExtensionConversion(
                value,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var conversion,
                preferTypedStaticResourceCoercion))
        {
            expression = conversion.Expression;
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static bool TryConvertMarkupExtensionConversion(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true)
    {
        conversion = default;
        if (!TryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        if (TryConvertXamlPrimitiveMarkupExtension(markup, targetType, out var primitiveExpression))
        {
            conversion = CreateLiteralConversion(primitiveExpression);
            return true;
        }

        switch (markup.Name.ToLowerInvariant())
        {
            case "binding":
            case "compiledbinding":
            {
                if (!TryParseBindingMarkup(value, out var bindingMarkup))
                {
                    return false;
                }

                if (bindingMarkup.HasSourceConflict)
                {
                    return false;
                }

                if (!TryBuildBindingValueExpression(
                        compilation,
                        document,
                        bindingMarkup,
                        targetType,
                        setterTargetType,
                        bindingPriorityScope,
                        out var bindingExpression))
                {
                    return false;
                }

                conversion = CreateBindingConversion(
                    bindingExpression,
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }
            case "x:null":
            case "null":
            {
                conversion = CreateLiteralConversion("null");
                return true;
            }
            case "x:type":
            case "type":
            {
                var typeToken = markup.NamedArguments.TryGetValue("Type", out var explicitType)
                    ? explicitType
                    : markup.NamedArguments.TryGetValue("TypeName", out var explicitTypeName)
                        ? explicitTypeName
                        : markup.PositionalArguments.Length > 0
                            ? markup.PositionalArguments[0]
                            : null;
                if (string.IsNullOrWhiteSpace(typeToken))
                {
                    return false;
                }

                var resolvedType = ResolveTypeToken(compilation, document, Unquote(typeToken!), document.ClassNamespace);
                if (resolvedType is null)
                {
                    return false;
                }

                conversion = CreateLiteralConversion(
                    "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")");
                return true;
            }
            case "x:static":
            case "static":
            {
                var memberToken = markup.NamedArguments.TryGetValue("Member", out var explicitMember)
                    ? explicitMember
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(memberToken))
                {
                    return false;
                }

                if (!TryResolveStaticMemberExpression(compilation, document, Unquote(memberToken!), out var staticMemberExpression))
                {
                    return false;
                }

                conversion = CreateLiteralConversion(staticMemberExpression);
                return true;
            }
            case "staticresource":
            {
                var keyToken = markup.NamedArguments.TryGetValue("ResourceKey", out var explicitKey)
                    ? explicitKey
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(keyToken))
                {
                    return false;
                }

                if (!TryBuildResourceKeyExpression(keyToken!, compilation, document, out var keyExpression))
                {
                    return false;
                }
                var staticResourceExpression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideStaticResource(" +
                    keyExpression.Expression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                if (!preferTypedStaticResourceCoercion ||
                    targetType.SpecialType == SpecialType.System_Object)
                {
                    // Keep StaticResource values untyped for object/AP assignment paths so
                    // AvaloniaProperty.UnsetValue can flow without invalid cast exceptions.
                    conversion = CreateMarkupExtensionConversion(
                        staticResourceExpression,
                        requiresRuntimeServiceProvider: true,
                        requiresParentStack: true,
                        requiresStaticResourceResolver: true,
                        resourceKey: keyExpression);
                    return true;
                }

                var typedTargetExpression = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                conversion = CreateMarkupExtensionConversion(
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceStaticResourceValue<" +
                    typedTargetExpression +
                    ">(" +
                    staticResourceExpression +
                    ")",
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true,
                    requiresStaticResourceResolver: true,
                    resourceKey: keyExpression);
                return true;
            }
            case "dynamicresource":
            {
                var keyToken = markup.NamedArguments.TryGetValue("ResourceKey", out var explicitKey)
                    ? explicitKey
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(keyToken))
                {
                    return false;
                }

                if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension") is null)
                {
                    return false;
                }

                if (!TryBuildResourceKeyExpression(keyToken!, compilation, document, out var dynamicResourceKeyExpression))
                {
                    return false;
                }

                conversion = CreateDynamicResourceBindingConversion(
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideDynamicResource(" +
                    dynamicResourceKeyExpression.Expression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")",
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true,
                    resourceKey: dynamicResourceKeyExpression);
                return true;
            }
            case "reflectionbinding":
            {
                if (!TryParseReflectionBindingMarkup(value, out var reflectionBindingMarkup))
                {
                    return false;
                }

                if (reflectionBindingMarkup.HasSourceConflict ||
                    !TryBuildReflectionBindingExtensionExpression(
                        compilation,
                        document,
                        reflectionBindingMarkup,
                        setterTargetType,
                        bindingPriorityScope,
                        out var reflectionBindingExtensionExpression))
                {
                    return false;
                }

                conversion = CreateBindingConversion(
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(" +
                    reflectionBindingExtensionExpression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")",
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }
            case "relativesource":
            {
                if (!TryParseRelativeSourceMarkup(value, out var relativeSourceMarkup) ||
                    !TryBuildRelativeSourceExpression(relativeSourceMarkup, compilation, document, out var relativeSourceExpression))
                {
                    return false;
                }

                conversion = CreateLiteralConversion(WrapWithTargetTypeCast(targetType, relativeSourceExpression));
                return true;
            }
            case "onplatform":
            {
                if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.OnPlatformExtension") is null)
                {
                    return false;
                }

                if (!TryConvertOnPlatformExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out var onPlatformExpression))
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    onPlatformExpression,
                    requiresRuntimeServiceProvider: true);
                return true;
            }
            case "onformfactor":
            {
                if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.OnFormFactorExtension") is null)
                {
                    return false;
                }

                if (!TryConvertOnFormFactorExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out var onFormFactorExpression))
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    onFormFactorExpression,
                    requiresRuntimeServiceProvider: true);
                return true;
            }
            case "x:reference":
            case "reference":
            case "resolvebyname":
            {
                var referenceName = TryGetNamedMarkupArgument(markup, "Name", "ElementName") ??
                                    (markup.PositionalArguments.Length > 0 ? Unquote(markup.PositionalArguments[0]) : null);
                if (string.IsNullOrWhiteSpace(referenceName))
                {
                    return false;
                }

                var resolveExpression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReference(\"" +
                    Escape(referenceName!.Trim()) +
                    "\", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                if (!targetType.IsReferenceType &&
                    targetType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T &&
                    targetType.SpecialType != SpecialType.System_Object)
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    WrapWithTargetTypeCast(targetType, resolveExpression),
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }
            case "templatebinding":
            {
                var propertyToken = markup.NamedArguments.TryGetValue("Property", out var explicitProperty)
                    ? explicitProperty
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(propertyToken))
                {
                    if (compilation.GetTypeByMetadataName("Avalonia.Data.Binding") is null)
                    {
                        return false;
                    }

                    conversion = CreateBindingConversion(
                        "new global::Avalonia.Data.Binding(\".\") { RelativeSource = new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.TemplatedParent), Priority = global::Avalonia.Data.BindingPriority.Template }");
                    return true;
                }

                if (setterTargetType is null)
                {
                    return false;
                }

                if (!TryResolveAvaloniaPropertyReferenceExpression(
                        Unquote(propertyToken!),
                        compilation,
                        document,
                        setterTargetType,
                        out var propertyExpression))
                {
                    return false;
                }

                if (compilation.GetTypeByMetadataName("Avalonia.Data.TemplateBinding") is null)
                {
                    return false;
                }

                conversion = CreateTemplateBindingConversion(
                    "new global::Avalonia.Data.TemplateBinding(" + propertyExpression + ")");
                return true;
            }
            default:
                if (!TryConvertGenericMarkupExtensionExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out var genericExpression))
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    genericExpression,
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
        }
    }

    private static bool TryConvertXamlPrimitiveMarkupExtension(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        out string expression)
    {
        expression = string.Empty;
        var name = markup.Name.Trim().ToLowerInvariant();
        var rawValue = TryGetNamedMarkupArgument(markup, "Value") ??
                       (markup.PositionalArguments.Length > 0 ? Unquote(markup.PositionalArguments[0]) : null);
        switch (name)
        {
            case "x:true":
            case "true":
                expression = "true";
                return true;
            case "x:false":
            case "false":
                expression = "false";
                return true;
            case "x:string":
            case "string":
            {
                var value = rawValue ?? string.Empty;
                expression = "\"" + Escape(value ?? string.Empty) + "\"";
                return true;
            }
            case "x:char":
            case "char":
            {
                if (string.IsNullOrEmpty(rawValue))
                {
                    return false;
                }

                var trimmedValue = rawValue!.Trim();
                if (trimmedValue.StartsWith("\\u", StringComparison.OrdinalIgnoreCase) &&
                    trimmedValue.Length == 6 &&
                    int.TryParse(trimmedValue.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicodeCode))
                {
                    expression = "'\\u" + unicodeCode.ToString("x4", CultureInfo.InvariantCulture) + "'";
                    return true;
                }

                if (trimmedValue.Length != 1)
                {
                    return false;
                }

                expression = "'" + EscapeChar(trimmedValue[0]) + "'";
                return true;
            }
            case "x:byte":
            case "byte":
            {
                if (!byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((byte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:sbyte":
            case "sbyte":
            {
                if (!sbyte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((sbyte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:int16":
            case "int16":
            {
                if (!short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((short)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:uint16":
            case "uint16":
            {
                if (!ushort.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((ushort)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:int32":
            case "int32":
            {
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            case "x:uint32":
            case "uint32":
            {
                if (!uint.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "u";
                return true;
            }
            case "x:int64":
            case "int64":
            {
                if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            }
            case "x:uint64":
            case "uint64":
            {
                if (!ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            }
            case "x:single":
            case "single":
            case "x:double":
            case "double":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = targetType.SpecialType == SpecialType.System_Single
                    ? parsed.ToString("R", CultureInfo.InvariantCulture) + "f"
                    : parsed.ToString("R", CultureInfo.InvariantCulture) + "d";
                return true;
            }
            case "x:decimal":
            case "decimal":
            {
                if (!decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "m";
                return true;
            }
            case "x:datetime":
            case "datetime":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                expression = "global::System.DateTime.Parse(\"" + Escape(rawValue.Trim()) + "\", global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind)";
                return true;
            }
            case "x:timespan":
            case "timespan":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                expression = "global::System.TimeSpan.Parse(\"" + Escape(rawValue.Trim()) + "\", global::System.Globalization.CultureInfo.InvariantCulture)";
                return true;
            }
            case "x:uri":
            case "uri":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                expression = "new global::System.Uri(\"" + Escape(rawValue.Trim()) + "\", global::System.UriKind.RelativeOrAbsolute)";
                return true;
            }
            case "x:null":
            case "null":
                expression = "null";
                return true;
            default:
                return false;
        }
    }

    private static bool TryConvertGenericMarkupExtensionExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryResolveMarkupExtensionType(compilation, document, markup.Name, out var extensionType))
        {
            return false;
        }

        if (extensionType is null)
        {
            return false;
        }

        var constructor = extensionType.InstanceConstructors
            .Where(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                !candidate.IsStatic)
            .Where(candidate => candidate.Parameters.Length == markup.PositionalArguments.Length)
            .OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();

        if (constructor is null && markup.PositionalArguments.Length > 0)
        {
            return false;
        }

        var positionalExpressions = new List<string>(markup.PositionalArguments.Length);
        for (var index = 0; index < markup.PositionalArguments.Length; index++)
        {
            var positionalArgument = markup.PositionalArguments[index];
            var positionalTargetType = constructor?.Parameters[index].Type;
            if (!TryConvertMarkupArgumentExpression(
                    positionalArgument,
                    positionalTargetType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var positionalExpression))
            {
                return false;
            }

            positionalExpressions.Add(positionalExpression);
        }

        var constructorExpression = "new " + extensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "(" +
                                    string.Join(", ", positionalExpressions) +
                                    ")";

        var initializerExpressions = new List<string>();
        foreach (var namedArgument in markup.NamedArguments)
        {
            var property = extensionType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate =>
                    candidate.SetMethod is not null &&
                    candidate.Name.Equals(namedArgument.Key, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                return false;
            }

            if (!TryConvertMarkupArgumentExpression(
                    namedArgument.Value,
                    property.Type,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var propertyExpression))
            {
                return false;
            }

            initializerExpressions.Add(property.Name + " = " + propertyExpression);
        }

        if (initializerExpressions.Count > 0)
        {
            constructorExpression += " { " + string.Join(", ", initializerExpressions) + " }";
        }

        var runtimeProvideValueExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(" +
            constructorExpression +
            ", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", " +
            MarkupContextBaseUriToken +
            ", " +
            MarkupContextParentStackToken +
            ")";

        expression = WrapWithTargetTypeCast(targetType, runtimeProvideValueExpression);
        return true;
    }

    private static bool TryConvertMarkupArgumentExpression(
        string rawValue,
        ITypeSymbol? targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            expression = "null";
            return true;
        }

        var conversionTargetType = targetType ?? compilation.GetTypeByMetadataName("System.Object");
        if (conversionTargetType is null)
        {
            return false;
        }

        if (TryConvertValueExpression(
                Unquote(value),
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (TryConvertMarkupExtensionExpression(
                value,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (conversionTargetType.SpecialType == SpecialType.System_Object)
        {
            expression = "\"" + Escape(Unquote(value)) + "\"";
            return true;
        }

        return false;
    }

    private static bool TryResolveMarkupExtensionType(
        Compilation compilation,
        XamlDocumentModel document,
        string markupName,
        out INamedTypeSymbol? extensionType)
    {
        extensionType = null;
        var token = markupName.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        token = token switch
        {
            "x:Null" or "x:null" => string.Empty,
            _ => token
        };
        if (token.Length == 0)
        {
            return false;
        }

        if (!token.EndsWith("Extension", StringComparison.Ordinal))
        {
            token += "Extension";
        }

        extensionType = ResolveTypeToken(compilation, document, token, document.ClassNamespace);
        if (extensionType is null)
        {
            return false;
        }

        var markupExtensionBase = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtension");
        if (markupExtensionBase is null)
        {
            return false;
        }

        return IsTypeAssignableTo(extensionType, markupExtensionBase);
    }

    private static string WrapWithTargetTypeCast(ITypeSymbol targetType, string expression)
    {
        if (targetType.SpecialType == SpecialType.System_Object)
        {
            return expression;
        }

        var typedTargetExpression = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<" +
               typedTargetExpression +
               ">(" +
               expression +
               ")";
    }

    private static bool TryConvertByStaticParseMethod(ITypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var parseMethod = namedType.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, namedType));
        if (parseMethod is not null)
        {
            expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         ".Parse(\"" + Escape(value) + "\")";
            return true;
        }

        var parseWithCultureMethod = namedType.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic &&
                method.Parameters.Length == 2 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                IsCultureAwareParseParameter(method.Parameters[1].Type) &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, namedType));
        if (parseWithCultureMethod is null)
        {
            return false;
        }

        expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     ".Parse(\"" + Escape(value) + "\", global::System.Globalization.CultureInfo.InvariantCulture)";
        return true;
    }

    private static bool IsCultureAwareParseParameter(ITypeSymbol type)
    {
        var fullyQualifiedTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullyQualifiedTypeName is
            "global::System.IFormatProvider" or
            "global::System.IFormatProvider?" or
            "global::System.Globalization.CultureInfo" or
            "global::System.Globalization.CultureInfo?";
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "AvaloniaProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveAvaloniaPropertyReferenceExpression(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out string expression)
    {
        return TryResolveAvaloniaPropertyReferenceExpression(
            rawValue,
            compilation,
            document,
            defaultOwnerType,
            out expression,
            out _);
    }

    private static bool TryResolveAvaloniaPropertyReferenceExpression(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out string expression,
        out ITypeSymbol? propertyValueType)
    {
        expression = string.Empty;
        propertyValueType = null;
        var token = rawValue.Trim();
        if (token.Length > 2 &&
            token[0] == '(' &&
            token[^1] == ')')
        {
            token = token.Substring(1, token.Length - 2).Trim();
        }

        if (token.Length == 0)
        {
            return false;
        }

        var separatorIndex = token.LastIndexOf('.');
        var ownerType = defaultOwnerType;
        var propertyName = token;

        if (separatorIndex > 0 && separatorIndex < token.Length - 1)
        {
            var ownerToken = token.Substring(0, separatorIndex);
            ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace) ?? ownerType;
            propertyName = token.Substring(separatorIndex + 1);
        }

        if (ownerType is null ||
            !TryFindAvaloniaPropertyField(ownerType, propertyName, out var resolvedOwnerType, out var propertyField))
        {
            return false;
        }

        expression = resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + propertyField.Name;
        propertyValueType = TryGetAvaloniaPropertyValueType(propertyField.Type);
        return true;
    }

    private static bool TryResolveStaticMemberExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string memberToken,
        out string expression)
    {
        expression = string.Empty;
        var separatorIndex = memberToken.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= memberToken.Length - 1)
        {
            return false;
        }

        var ownerToken = memberToken.Substring(0, separatorIndex);
        var memberName = memberToken.Substring(separatorIndex + 1);
        var ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        if (ownerType is null)
        {
            return false;
        }

        var staticField = ownerType.GetMembers(memberName)
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.IsStatic);
        if (staticField is not null)
        {
            expression = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + staticField.Name;
            return true;
        }

        var staticProperty = ownerType.GetMembers(memberName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => property.IsStatic);
        if (staticProperty is not null)
        {
            expression = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + staticProperty.Name;
            return true;
        }

        return false;
    }

    private static bool RequiresStaticResourceResolver(
        ResolvedObjectNode root,
        ImmutableArray<ResolvedStyleDefinition> styles,
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes)
    {
        if (HasStaticResourceResolverRequirement(root))
        {
            return true;
        }

        foreach (var style in styles)
        {
            if (style.Setters.Any(static setter => setter.RequiresStaticResourceResolver))
            {
                return true;
            }
        }

        foreach (var controlTheme in controlThemes)
        {
            if (controlTheme.Setters.Any(static setter => setter.RequiresStaticResourceResolver))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStaticResourceResolverRequirement(ResolvedObjectNode node)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (assignment.RequiresStaticResourceResolver)
            {
                return true;
            }
        }

        foreach (var propertyElement in node.PropertyElementAssignments)
        {
            foreach (var value in propertyElement.ObjectValues)
            {
                if (HasStaticResourceResolverRequirement(value))
                {
                    return true;
                }
            }
        }

        foreach (var child in node.Children)
        {
            if (HasStaticResourceResolverRequirement(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertCSharpExpressionMarkupToBindingExpression(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? sourceType,
        out bool isExpressionMarkup,
        out string expressionBindingValueExpression,
        out string accessorExpression,
        out string normalizedExpression,
        out string diagnosticId,
        out string diagnosticMessage)
    {
        isExpressionMarkup = false;
        expressionBindingValueExpression = string.Empty;
        accessorExpression = string.Empty;
        normalizedExpression = string.Empty;
        diagnosticId = string.Empty;
        diagnosticMessage = string.Empty;

        if (!TryParseCSharpExpressionMarkup(
                value,
                compilation,
                document,
                options.CSharpExpressionsEnabled,
                options.ImplicitCSharpExpressionsEnabled,
                out var csharpExpressionCode,
                out _))
        {
            return false;
        }

        isExpressionMarkup = true;
        if (sourceType is null)
        {
            diagnosticId = "AXSG0110";
            diagnosticMessage = "Expression binding requires x:DataType in scope.";
            return false;
        }

        if (!TryBuildCompiledExpressionAccessorExpression(
                compilation,
                document,
                sourceType,
                csharpExpressionCode,
                out accessorExpression,
                out normalizedExpression,
                out var expressionDependencyNames,
                out var errorMessage))
        {
            diagnosticId = "AXSG0111";
            diagnosticMessage = errorMessage;
            return false;
        }

        if (!TryBuildExpressionBindingRuntimeExpression(
                sourceType,
                accessorExpression,
                expressionDependencyNames,
                out expressionBindingValueExpression))
        {
            diagnosticId = "AXSG0111";
            diagnosticMessage = "expression could not be materialized.";
            return false;
        }

        return true;
    }

    private static bool TryParseCSharpExpressionMarkup(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        bool expressionsEnabled,
        bool implicitExpressionsEnabled,
        out string csharpExpression,
        out bool isExplicitExpression)
    {
        csharpExpression = string.Empty;
        isExplicitExpression = false;

        if (!expressionsEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith("{}", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Length > 3 &&
            trimmed[1] == '=')
        {
            var explicitExpression = trimmed.Substring(2, trimmed.Length - 3).Trim();
            if (explicitExpression.Length == 0)
            {
                return false;
            }

            csharpExpression = NormalizeCSharpExpressionMarkupCode(explicitExpression);
            isExplicitExpression = true;
            return csharpExpression.Length > 0;
        }

        var implicitExpression = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (!implicitExpressionsEnabled)
        {
            return false;
        }

        if (!IsImplicitCSharpExpressionMarkup(implicitExpression, compilation, document))
        {
            return false;
        }

        csharpExpression = NormalizeCSharpExpressionMarkupCode(implicitExpression);
        return csharpExpression.Length > 0;
    }

    private static bool IsImplicitCSharpExpressionMarkup(
        string expressionBody,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (string.IsNullOrWhiteSpace(expressionBody))
        {
            return false;
        }

        if (LooksLikeMarkupExtensionStart(expressionBody, compilation, document))
        {
            return false;
        }

        var trimmed = expressionBody.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("(", StringComparison.Ordinal) ||
            trimmed.StartsWith("!", StringComparison.Ordinal) ||
            trimmed.StartsWith("new ", StringComparison.Ordinal) ||
            trimmed.StartsWith("$\"", StringComparison.Ordinal) ||
            trimmed.StartsWith("$'", StringComparison.Ordinal) ||
            trimmed.StartsWith("typeof(", StringComparison.Ordinal) ||
            trimmed.StartsWith("nameof(", StringComparison.Ordinal) ||
            trimmed.StartsWith("default(", StringComparison.Ordinal) ||
            trimmed.StartsWith("sizeof(", StringComparison.Ordinal) ||
            trimmed.StartsWith(".", StringComparison.Ordinal) ||
            trimmed.StartsWith("this.", StringComparison.Ordinal) ||
            trimmed.StartsWith("BindingContext.", StringComparison.Ordinal))
        {
            return true;
        }

        if (ContainsImplicitExpressionOperator(trimmed))
        {
            return true;
        }

        if (IsMethodCallLikeExpression(trimmed) || IsMemberAccessLikeExpression(trimmed))
        {
            return true;
        }

        return IsBareIdentifierExpression(trimmed);
    }

    private static bool LooksLikeMarkupExtensionStart(
        string expressionBody,
        Compilation compilation,
        XamlDocumentModel document)
    {
        var wrappedExpression = "{" + expressionBody + "}";
        if (!TryParseMarkupExtension(wrappedExpression, out var markup))
        {
            return false;
        }

        var token = markup.Name;
        if (token.Length == 0)
        {
            return false;
        }

        if (token.Contains(':', StringComparison.Ordinal))
        {
            return true;
        }

        if (KnownMarkupExtensionNames.Contains(token) ||
            KnownMarkupExtensionNames.Contains(token + "Extension"))
        {
            return true;
        }

        return TryResolveMarkupExtensionType(compilation, document, token, out _);
    }

    private static bool ContainsImplicitExpressionOperator(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        for (var index = 0; index < expression.Length; index++)
        {
            var ch = expression[index];
            if (!inDoubleQuotedString &&
                ch == '\'' &&
                !IsEscapedChar(expression, index))
            {
                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (!inSingleQuotedString &&
                ch == '"' &&
                !IsEscapedChar(expression, index))
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                continue;
            }

            if (inSingleQuotedString || inDoubleQuotedString)
            {
                continue;
            }

            if (index + 1 < expression.Length)
            {
                var twoChars = expression.Substring(index, 2);
                if (twoChars is "=>" or "??" or "?." or "&&" or "||" or "==" or "!=" or "<=" or ">=" or "<<" or ">>" or "++" or "--")
                {
                    return true;
                }
            }

            if (ch is '+' or '-' or '*' or '/' or '%' or '<' or '>' or '?' or ':')
            {
                return true;
            }
        }

        foreach (var alias in ExpressionOperatorAliases)
        {
            if (ContainsAliasToken(expression, alias))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAliasToken(string expression, string alias)
    {
        var searchStart = 0;
        while (searchStart < expression.Length)
        {
            var index = expression.IndexOf(alias, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeBoundary = index == 0 || !IsIdentifierPart(expression[index - 1]);
            var afterIndex = index + alias.Length;
            var afterBoundary = afterIndex >= expression.Length || !IsIdentifierPart(expression[afterIndex]);
            if (beforeBoundary && afterBoundary)
            {
                return true;
            }

            searchStart = index + alias.Length;
        }

        return false;
    }

    private static bool IsMethodCallLikeExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmed = expression.Trim();
        if (!IsIdentifierStart(trimmed[0]))
        {
            return false;
        }

        var index = 1;
        while (index < trimmed.Length && IsIdentifierPart(trimmed[index]))
        {
            index++;
        }

        while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
        {
            index++;
        }

        return index < trimmed.Length && trimmed[index] == '(';
    }

    private static bool IsMemberAccessLikeExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmed = expression.Trim();
        var separator = trimmed.IndexOf('.');
        if (separator <= 0 || separator >= trimmed.Length - 1)
        {
            return false;
        }

        var first = trimmed.Substring(0, separator).Trim();
        var second = trimmed.Substring(separator + 1).Trim();
        return IsBareIdentifierExpression(first) && IsBareIdentifierExpression(second);
    }

    private static bool IsBareIdentifierExpression(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        if (!IsIdentifierStart(trimmed[0]))
        {
            return false;
        }

        for (var index = 1; index < trimmed.Length; index++)
        {
            if (!IsIdentifierPart(trimmed[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeCSharpExpressionMarkupCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var normalized = ReplaceExpressionOperatorAliases(code.Trim());
        normalized = NormalizeSingleQuotedExpressionStrings(normalized);
        return normalized.Trim();
    }

    private static string ReplaceExpressionOperatorAliases(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var result = new System.Text.StringBuilder(code.Length);
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var index = 0;
        while (index < code.Length)
        {
            var ch = code[index];
            if (!inDoubleQuotedString &&
                ch == '\'' &&
                !IsEscapedChar(code, index))
            {
                inSingleQuotedString = !inSingleQuotedString;
                result.Append(ch);
                index++;
                continue;
            }

            if (!inSingleQuotedString &&
                ch == '"' &&
                !IsEscapedChar(code, index))
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                result.Append(ch);
                index++;
                continue;
            }

            if (inSingleQuotedString || inDoubleQuotedString || !IsIdentifierStart(ch))
            {
                result.Append(ch);
                index++;
                continue;
            }

            var start = index;
            index++;
            while (index < code.Length && IsIdentifierPart(code[index]))
            {
                index++;
            }

            var token = code.Substring(start, index - start);
            if (TryMapExpressionAliasToken(token, out var replacement) &&
                (start == 0 || !IsIdentifierPart(code[start - 1])) &&
                (index >= code.Length || !IsIdentifierPart(code[index])))
            {
                result.Append(replacement);
            }
            else
            {
                result.Append(token);
            }
        }

        return result.ToString();
    }

    private static bool TryMapExpressionAliasToken(string token, out string replacement)
    {
        replacement = token;
        if (token.Equals("AND", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "&&";
            return true;
        }

        if (token.Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "||";
            return true;
        }

        if (token.Equals("LT", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "<";
            return true;
        }

        if (token.Equals("GT", StringComparison.OrdinalIgnoreCase))
        {
            replacement = ">";
            return true;
        }

        if (token.Equals("LTE", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "<=";
            return true;
        }

        if (token.Equals("GTE", StringComparison.OrdinalIgnoreCase))
        {
            replacement = ">=";
            return true;
        }

        return false;
    }

    private static string NormalizeSingleQuotedExpressionStrings(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var result = new System.Text.StringBuilder(code.Length);
        var inDoubleQuotedString = false;
        var index = 0;
        while (index < code.Length)
        {
            var ch = code[index];
            if (ch == '"' && !IsEscapedChar(code, index))
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                result.Append(ch);
                index++;
                continue;
            }

            if (inDoubleQuotedString || ch != '\'' || IsEscapedChar(code, index))
            {
                result.Append(ch);
                index++;
                continue;
            }

            var literalStart = index + 1;
            var cursor = literalStart;
            while (cursor < code.Length)
            {
                if (code[cursor] == '\'' && !IsEscapedChar(code, cursor))
                {
                    break;
                }

                cursor++;
            }

            if (cursor >= code.Length)
            {
                result.Append(ch);
                index++;
                continue;
            }

            var literalContent = code.Substring(literalStart, cursor - literalStart);
            if (LooksLikeCharLiteralContent(literalContent))
            {
                result.Append(code, index, cursor - index + 1);
                index = cursor + 1;
                continue;
            }

            result.Append('"');
            for (var contentIndex = 0; contentIndex < literalContent.Length; contentIndex++)
            {
                var contentChar = literalContent[contentIndex];
                if (contentChar == '\\' &&
                    contentIndex + 1 < literalContent.Length &&
                    literalContent[contentIndex + 1] == '\'')
                {
                    result.Append('\'');
                    contentIndex++;
                    continue;
                }

                if (contentChar == '"')
                {
                    result.Append("\\\"");
                    continue;
                }

                result.Append(contentChar);
            }

            result.Append('"');
            index = cursor + 1;
        }

        return result.ToString();
    }

    private static bool LooksLikeCharLiteralContent(string content)
    {
        if (content.Length == 1)
        {
            return true;
        }

        if (content.StartsWith("\\", StringComparison.Ordinal))
        {
            if (content.Length == 2)
            {
                return true;
            }

            if (content.StartsWith("\\u", StringComparison.OrdinalIgnoreCase) && content.Length == 6)
            {
                return true;
            }

            if (content.StartsWith("\\x", StringComparison.OrdinalIgnoreCase) &&
                content.Length >= 3 &&
                content.Length <= 5)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEscapedChar(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return false;
        }

        var escapeCount = 0;
        for (var current = index - 1; current >= 0 && text[current] == '\\'; current--)
        {
            escapeCount++;
        }

        return escapeCount % 2 == 1;
    }

    private static bool TryBuildCompiledExpressionAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawExpression,
        out string accessorExpression,
        out string normalizedExpression,
        out ImmutableArray<string> dependencyNames,
        out string errorMessage)
    {
        accessorExpression = "source";
        normalizedExpression = rawExpression.Trim();
        dependencyNames = ImmutableArray<string>.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            errorMessage = "expression text is empty";
            return false;
        }

        var parsedExpression = SyntaxFactory.ParseExpression(normalizedExpression);
        var parseDiagnostic = parsedExpression
            .GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var sourceMemberNames = GetExpressionSourceMemberNames(sourceType);
        var expressionLocalNames = GetExpressionLocalNames(parsedExpression);
        var rewriter = new SourceContextExpressionRewriter(
            sourceMemberNames,
            expressionLocalNames,
            ExpressionSourceParameterName);
        if (rewriter.Visit(parsedExpression) is not ExpressionSyntax rewrittenExpressionSyntax)
        {
            errorMessage = "expression rewrite failed";
            return false;
        }

        var rewrittenExpression = rewrittenExpressionSyntax.ToFullString().Trim();
        if (rewrittenExpression.Length == 0)
        {
            errorMessage = "expression rewrite produced an empty expression";
            return false;
        }

        if (!TryValidateGeneratedExpression(compilation, sourceType, rewrittenExpression, out errorMessage))
        {
            return false;
        }

        accessorExpression = rewrittenExpression;
        normalizedExpression = rewrittenExpression;
        dependencyNames = rewriter.Dependencies
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToImmutableArray();
        return true;
    }

    private static ImmutableHashSet<string> GetExpressionLocalNames(ExpressionSyntax expression)
    {
        var collector = new ExpressionLocalNameCollector();
        collector.Visit(expression);
        return collector.Names.ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static ImmutableHashSet<string> GetExpressionSourceMemberNames(INamedTypeSymbol sourceType)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        for (INamedTypeSymbol? current = sourceType; current is not null; current = current.BaseType)
        {
            AddExpressionSourceMemberNames(current, builder);
        }

        foreach (var interfaceType in sourceType.AllInterfaces)
        {
            AddExpressionSourceMemberNames(interfaceType, builder);
        }

        return builder.ToImmutable();
    }

    private static void AddExpressionSourceMemberNames(
        INamedTypeSymbol type,
        ImmutableHashSet<string>.Builder builder)
    {
        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol property when !property.IsStatic && property.GetMethod is not null:
                    builder.Add(property.Name);
                    break;
                case IFieldSymbol field when !field.IsStatic:
                    builder.Add(field.Name);
                    break;
                case IMethodSymbol method when
                    !method.IsStatic &&
                    method.MethodKind == MethodKind.Ordinary &&
                    !method.IsImplicitlyDeclared:
                    builder.Add(method.Name);
                    break;
            }
        }
    }

    private static bool TryValidateGeneratedExpression(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string expression,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_ExpressionValidation",
            "{",
            "    internal static class __ExpressionValidator",
            "    {",
            "        internal static object? __Evaluate(" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " " +
            ExpressionSourceParameterName +
            ") => (object?)(" +
            expression +
            ");",
            "    }",
            "}");

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationTree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var validationCompilation = compilation.AddSyntaxTrees(validationTree);
        var validationDiagnostic = validationCompilation.GetDiagnostics()
            .FirstOrDefault(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.SourceTree == validationTree);
        if (validationDiagnostic is null)
        {
            return true;
        }

        errorMessage = validationDiagnostic.GetMessage(CultureInfo.InvariantCulture);
        return false;
    }

    private static bool TryBuildExpressionBindingRuntimeExpression(
        INamedTypeSymbol sourceType,
        string accessorExpression,
        ImmutableArray<string> dependencyNames,
        out string expression)
    {
        expression = string.Empty;
        if (string.IsNullOrWhiteSpace(accessorExpression))
        {
            return false;
        }

        var dependencyArrayExpression = BuildStringArrayLiteral(dependencyNames);
        expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            ">(static " +
            ExpressionSourceParameterName +
            " => (object?)(" +
            accessorExpression +
            "), " +
            dependencyArrayExpression +
            ", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", " +
            MarkupContextBaseUriToken +
            ", " +
            MarkupContextParentStackToken +
            ")";
        return true;
    }

    private static string BuildStringArrayLiteral(ImmutableArray<string> values)
    {
        if (values.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<string>()";
        }

        return "new string[] { " +
               string.Join(", ", values.Select(static value => "\"" + Escape(value) + "\"")) +
               " }";
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return CanonicalMarkupExpressionParser.TryParseMarkupExtension(value, out markupExtension);
    }

    private static bool TryParseRelativeSourceMarkup(string value, out RelativeSourceMarkup relativeSourceMarkup)
    {
        relativeSourceMarkup = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (TryParseMarkupExtension(value, out var markupExtension))
        {
            if (!markupExtension.Name.Equals("RelativeSource", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var mode = markupExtension.NamedArguments.TryGetValue("Mode", out var explicitMode)
                ? Unquote(explicitMode)
                : markupExtension.PositionalArguments.Length > 0
                    ? Unquote(markupExtension.PositionalArguments[0])
                    : null;

            int? ancestorLevel = null;
            if ((markupExtension.NamedArguments.TryGetValue("AncestorLevel", out var rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("FindAncestor", out rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("Level", out rawAncestorLevel)) &&
                int.TryParse(Unquote(rawAncestorLevel), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAncestorLevel))
            {
                ancestorLevel = parsedAncestorLevel;
            }

            var ancestorType = markupExtension.NamedArguments.TryGetValue("AncestorType", out var rawAncestorType)
                ? Unquote(rawAncestorType)
                : null;
            var tree = markupExtension.NamedArguments.TryGetValue("Tree", out var rawTree)
                ? Unquote(rawTree)
                : markupExtension.NamedArguments.TryGetValue("TreeType", out var rawTreeType)
                    ? Unquote(rawTreeType)
                : null;

            relativeSourceMarkup = new RelativeSourceMarkup(mode, ancestorType, ancestorLevel, tree);
            return true;
        }

        relativeSourceMarkup = new RelativeSourceMarkup(
            mode: Unquote(value.Trim()),
            ancestorTypeToken: null,
            ancestorLevel: null,
            tree: null);
        return true;
    }

    private static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        var result = new List<string>();
        var start = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }
                    break;
                default:
                    if (ch == separator &&
                        braceDepth == 0 &&
                        bracketDepth == 0 &&
                        parenthesisDepth == 0)
                    {
                        result.Add(value.Substring(start, index - start));
                        start = index + 1;
                    }
                    break;
            }
        }

        if (start <= value.Length)
        {
            result.Add(value.Substring(start));
        }

        return result;
    }

    private static int IndexOfTopLevel(string value, char token)
    {
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    continue;
                case '(':
                    parenthesisDepth++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    continue;
            }

            if (ch == token &&
                braceDepth == 0 &&
                bracketDepth == 0 &&
                parenthesisDepth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
             (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static bool IsQuotedLiteral(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 &&
               ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
                (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\''));
    }

    private static string EscapeChar(char value)
    {
        return value switch
        {
            '\'' => "\\'",
            '\\' => "\\\\",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            '\0' => "\\0",
            _ => value.ToString()
        };
    }

    private static INamedTypeSymbol? ResolveTypeFromTypeExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace)
    {
        if (string.IsNullOrWhiteSpace(typeExpression))
        {
            return null;
        }

        var token = ExtractTypeToken(typeExpression!);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return ResolveTypeToken(compilation, document, token, fallbackClrNamespace);
    }

    private static string ExtractTypeToken(string typeExpression)
    {
        var trimmed = typeExpression.Trim();

        if (trimmed.StartsWith("{x:Type", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            var firstSpace = inner.IndexOf(' ');
            if (firstSpace >= 0)
            {
                var payload = inner.Substring(firstSpace + 1).Trim();

                if (payload.StartsWith("TypeName=", StringComparison.OrdinalIgnoreCase))
                {
                    payload = payload.Substring("TypeName=".Length).Trim();
                }
                else if (payload.StartsWith("Type=", StringComparison.OrdinalIgnoreCase))
                {
                    payload = payload.Substring("Type=".Length).Trim();
                }

                return payload;
            }
        }

        return trimmed;
    }

    private static string? TryExtractSelectorTypeToken(string selector)
    {
        var validation = SelectorSyntaxValidator.Validate(selector);
        if (!validation.IsValid || validation.Branches.Length != 1)
        {
            return null;
        }

        return validation.Branches[0].LastTypeToken;
    }

    private static INamedTypeSymbol? TryResolveSelectorTargetType(
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<SelectorSyntaxValidator.BranchInfo> branches,
        out string? unresolvedTypeToken,
        out int unresolvedTypeOffset)
    {
        unresolvedTypeToken = null;
        unresolvedTypeOffset = 0;
        if (branches.Length == 0)
        {
            return null;
        }

        var resolvedTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>(branches.Length);
        foreach (var branch in branches)
        {
            if (string.IsNullOrWhiteSpace(branch.LastTypeToken))
            {
                return null;
            }

            var resolved = ResolveTypeToken(compilation, document, branch.LastTypeToken!, document.ClassNamespace);
            if (resolved is null)
            {
                unresolvedTypeToken = branch.LastTypeToken;
                unresolvedTypeOffset = branch.LastTypeOffset;
                return null;
            }

            resolvedTypes.Add(resolved);
        }

        if (resolvedTypes.Count == 0)
        {
            return null;
        }

        var targetType = resolvedTypes[0];
        for (var index = 1; index < resolvedTypes.Count && targetType is not null; index++)
        {
            var candidate = resolvedTypes[index];
            while (targetType is not null && !IsTypeAssignableTo(candidate, targetType))
            {
                targetType = targetType.BaseType;
            }
        }

        return targetType;
    }

    private static (int Line, int Column) AdvanceLineAndColumn(
        int startLine,
        int startColumn,
        string source,
        int offset)
    {
        var line = Math.Max(1, startLine);
        var column = Math.Max(1, startColumn);
        if (string.IsNullOrEmpty(source) || offset <= 0)
        {
            return (line, column);
        }

        var cappedOffset = Math.Min(offset, source.Length);
        for (var index = 0; index < cappedOffset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private const string SelectorTemplateAxisToken = "/template/";

    private static IEnumerable<string> SplitSelectorSegments(string selectorBranch)
    {
        var segments = new List<string>();
        var start = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;

        for (var index = 0; index < selectorBranch.Length; index++)
        {
            var ch = selectorBranch[index];
            switch (ch)
            {
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    continue;
                case '(':
                    parenthesisDepth++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }
                    continue;
            }

            if (bracketDepth > 0 || parenthesisDepth > 0)
            {
                continue;
            }

            if (ch == '>')
            {
                AddSelectorSegment(selectorBranch, start, index, segments);
                start = index + 1;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                AddSelectorSegment(selectorBranch, start, index, segments);
                while (index + 1 < selectorBranch.Length && char.IsWhiteSpace(selectorBranch[index + 1]))
                {
                    index++;
                }

                start = index + 1;
                continue;
            }

            if (IsSelectorTemplateAxisAt(selectorBranch, index))
            {
                AddSelectorSegment(selectorBranch, start, index, segments);
                index += SelectorTemplateAxisToken.Length - 1;
                start = index + 1;
            }
        }

        AddSelectorSegment(selectorBranch, start, selectorBranch.Length, segments);
        return segments;
    }

    private static string? TryExtractLeadingSelectorTypeToken(string selectorSegment)
    {
        if (string.IsNullOrWhiteSpace(selectorSegment))
        {
            return null;
        }

        var text = selectorSegment.Trim();
        var index = 0;
        while (index < text.Length && (text[index] == '^' || char.IsWhiteSpace(text[index])))
        {
            index++;
        }

        if (index >= text.Length || text[index] == '*' || !IsIdentifierStart(text[index]))
        {
            return null;
        }

        var aliasStart = index;
        index++;
        while (index < text.Length && IsIdentifierPart(text[index]))
        {
            index++;
        }

        var aliasOrType = text.Substring(aliasStart, index - aliasStart);
        if (index < text.Length && text[index] == '|')
        {
            index++;
            if (index >= text.Length || !IsIdentifierStart(text[index]))
            {
                return null;
            }

            var typeStart = index;
            index++;
            while (index < text.Length && IsIdentifierPart(text[index]))
            {
                index++;
            }

            var typeName = text.Substring(typeStart, index - typeStart);
            return aliasOrType + ":" + typeName;
        }

        return aliasOrType;
    }

    private static void AddSelectorSegment(string value, int start, int end, List<string> segments)
    {
        var length = end - start;
        if (length <= 0)
        {
            return;
        }

        var segment = value.Substring(start, length).Trim();
        if (segment.Length > 0)
        {
            segments.Add(segment);
        }
    }

    private sealed class AvaloniaNamespaceCandidateCacheEntry
    {
        public AvaloniaNamespaceCandidateCacheEntry(ImmutableArray<string> namespaces)
        {
            Namespaces = namespaces;
        }

        public ImmutableArray<string> Namespaces { get; }
    }

    private sealed class XmlnsDefinitionCacheEntry
    {
        public XmlnsDefinitionCacheEntry(ImmutableDictionary<string, ImmutableArray<string>> map)
        {
            Map = map;
        }

        public ImmutableDictionary<string, ImmutableArray<string>> Map { get; }
    }

    private sealed class SourceAssemblyNamespaceCacheEntry
    {
        public SourceAssemblyNamespaceCacheEntry(ImmutableArray<string> namespaces)
        {
            Namespaces = namespaces;
        }

        public ImmutableArray<string> Namespaces { get; }
    }

    private sealed class TypeResolutionDiagnosticContext
    {
        public TypeResolutionDiagnosticContext(
            ImmutableArray<DiagnosticInfo>.Builder diagnostics,
            string filePath,
            bool strictMode)
        {
            Diagnostics = diagnostics;
            FilePath = filePath;
            StrictMode = strictMode;
            ReportedKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

        public string FilePath { get; }

        public bool StrictMode { get; }

        public HashSet<string> ReportedKeys { get; }
    }

    private static ImmutableArray<string> GetAvaloniaDefaultNamespaceCandidates(Compilation compilation)
    {
        return AvaloniaNamespaceCandidateCache
            .GetValue(
                compilation,
                static x => new AvaloniaNamespaceCandidateCacheEntry(BuildAvaloniaDefaultNamespaceCandidates(x)))
            .Namespaces;
    }

    private static ImmutableArray<string> GetClrNamespacesForXmlNamespace(Compilation compilation, string xmlNamespace)
    {
        if (string.IsNullOrWhiteSpace(xmlNamespace))
        {
            return ImmutableArray<string>.Empty;
        }

        var normalizedXmlNamespace = NormalizeXmlNamespaceKey(xmlNamespace);
        var cacheEntry = XmlnsDefinitionCache.GetValue(
            compilation,
            static x => new XmlnsDefinitionCacheEntry(BuildXmlnsDefinitionMap(x)));
        return cacheEntry.Map.TryGetValue(normalizedXmlNamespace, out var namespaces)
            ? namespaces
            : ImmutableArray<string>.Empty;
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> BuildXmlnsDefinitionMap(Compilation compilation)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsXmlnsDefinitionAttribute(attribute.AttributeClass))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                    attribute.ConstructorArguments[1].Value is not string clrNamespace)
                {
                    continue;
                }

                var xmlKey = NormalizeXmlNamespaceKey(xmlNamespace);
                if (xmlKey.Length == 0 || string.IsNullOrWhiteSpace(clrNamespace))
                {
                    continue;
                }

                if (!map.TryGetValue(xmlKey, out var namespaces))
                {
                    namespaces = new HashSet<string>(StringComparer.Ordinal);
                    map[xmlKey] = namespaces;
                }

                namespaces.Add(clrNamespace.Trim());
            }
        }

        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        foreach (var entry in map)
        {
            builder[entry.Key] = entry.Value
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        return builder.ToImmutable();
    }

    private static string NormalizeXmlNamespaceKey(string xmlNamespace)
    {
        var normalized = xmlNamespace.Trim();
        return IsAvaloniaDefaultXmlNamespace(normalized)
            ? AvaloniaDefaultXmlNamespace
            : normalized;
    }

    private static ImmutableArray<string> BuildAvaloniaDefaultNamespaceCandidates(Compilation compilation)
    {
        var orderedNamespaces = new List<string>(AvaloniaDefaultNamespaceCandidateSeed.Length + 32);
        var knownNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var namespacePrefix in AvaloniaDefaultNamespaceCandidateSeed)
        {
            AddNamespaceCandidate(namespacePrefix);
        }

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsAvaloniaXmlnsDefinitionAttribute(attribute.AttributeClass))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                    !IsAvaloniaDefaultXmlNamespace(xmlNamespace) ||
                    attribute.ConstructorArguments[1].Value is not string clrNamespace)
                {
                    continue;
                }

                AddNamespaceCandidate(clrNamespace);
            }
        }

        return orderedNamespaces.ToImmutableArray();

        void AddNamespaceCandidate(string namespacePrefix)
        {
            var normalized = namespacePrefix.Trim();
            if (normalized.Length == 0)
            {
                return;
            }

            if (!normalized.EndsWith(".", StringComparison.Ordinal))
            {
                normalized += ".";
            }

            if (knownNamespaces.Add(normalized))
            {
                orderedNamespaces.Add(normalized);
            }
        }
    }

    private static bool TryGetImplicitProjectNamespaceRoot(
        Compilation compilation,
        out string rootNamespace)
    {
        rootNamespace = string.Empty;
        var options = ActiveGeneratorOptions.Value;
        if (options is null || !options.ImplicitProjectNamespacesEnabled)
        {
            return false;
        }

        rootNamespace = options.RootNamespace
                        ?? options.AssemblyName
                        ?? compilation.AssemblyName
                        ?? string.Empty;
        rootNamespace = rootNamespace.Trim();
        return true;
    }

    private static ImmutableArray<string> GetProjectNamespaceCandidates(
        Compilation compilation,
        string rootNamespace)
    {
        var allSourceNamespaces = SourceAssemblyNamespaceCache
            .GetValue(
                compilation,
                static x => new SourceAssemblyNamespaceCacheEntry(BuildSourceAssemblyNamespaces(x)))
            .Namespaces;

        if (allSourceNamespaces.IsDefaultOrEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        var normalizedRoot = rootNamespace.Trim();
        var hasRoot = normalizedRoot.Length > 0;
        var orderedNamespaces = new List<string>(allSourceNamespaces.Length + 1);
        var knownNamespaces = new HashSet<string>(StringComparer.Ordinal);

        if (hasRoot)
        {
            AddNamespaceCandidate(normalizedRoot);
        }

        foreach (var candidate in allSourceNamespaces)
        {
            if (hasRoot &&
                !candidate.Equals(normalizedRoot, StringComparison.Ordinal) &&
                !candidate.StartsWith(normalizedRoot + ".", StringComparison.Ordinal))
            {
                continue;
            }

            AddNamespaceCandidate(candidate);
        }

        return orderedNamespaces.ToImmutableArray();

        void AddNamespaceCandidate(string namespacePrefix)
        {
            var normalized = namespacePrefix.Trim();
            if (normalized.Length == 0)
            {
                return;
            }

            if (!normalized.EndsWith(".", StringComparison.Ordinal))
            {
                normalized += ".";
            }

            if (knownNamespaces.Add(normalized))
            {
                orderedNamespaces.Add(normalized);
            }
        }
    }

    private static ImmutableArray<string> BuildSourceAssemblyNamespaces(Compilation compilation)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        CollectSourceAssemblyNamespaces(compilation.Assembly.GlobalNamespace, namespaces);
        return namespaces
            .OrderBy(static value => value.Count(static ch => ch == '.'))
            .ThenBy(static value => value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void CollectSourceAssemblyNamespaces(
        INamespaceSymbol namespaceSymbol,
        HashSet<string> namespaces)
    {
        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            var displayName = childNamespace.ToDisplayString();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                namespaces.Add(displayName);
            }

            CollectSourceAssemblyNamespaces(childNamespace, namespaces);
        }
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var visitedAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        if (visitedAssemblies.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }

        foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referencedAssembly is not null && visitedAssemblies.Add(referencedAssembly))
            {
                yield return referencedAssembly;
            }
        }
    }

    private static bool IsAvaloniaXmlnsDefinitionAttribute(INamedTypeSymbol? attributeType)
    {
        return string.Equals(
            attributeType?.ToDisplayString(),
            AvaloniaXmlnsDefinitionAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static bool IsXmlnsDefinitionAttribute(INamedTypeSymbol? attributeType)
    {
        var metadataName = attributeType?.ToDisplayString();
        return string.Equals(metadataName, AvaloniaXmlnsDefinitionAttributeMetadataName, StringComparison.Ordinal) ||
               string.Equals(metadataName, SourceGenXmlnsDefinitionAttributeMetadataName, StringComparison.Ordinal);
    }

    private static bool IsAvaloniaDefaultXmlNamespace(string xmlNamespace)
    {
        return string.Equals(xmlNamespace, AvaloniaDefaultXmlNamespace, StringComparison.Ordinal) ||
               string.Equals(xmlNamespace, AvaloniaDefaultXmlNamespaceWithSlash, StringComparison.Ordinal);
    }

    private static bool IsTypeResolutionCompatibilityFallbackEnabled()
    {
        var options = ActiveGeneratorOptions.Value;
        return options?.TypeResolutionCompatibilityFallbackEnabled ?? true;
    }

    private static bool IsStrictTypeResolutionMode()
    {
        var options = ActiveGeneratorOptions.Value;
        return options?.StrictMode ?? false;
    }

    private static ImmutableArray<INamedTypeSymbol> CollectTypeCandidatesFromNamespacePrefixes(
        Compilation compilation,
        IEnumerable<string> namespacePrefixes,
        string typeName,
        int? genericArity = null,
        bool extensionSuffix = false)
    {
        var candidates = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var effectiveTypeName = extensionSuffix
            ? typeName + "Extension"
            : AppendGenericArity(typeName, genericArity);

        foreach (var namespacePrefix in namespacePrefixes)
        {
            var candidate = compilation.GetTypeByMetadataName(namespacePrefix + effectiveTypeName);
            if (candidate is not null && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates.ToImmutable();
    }

    private static INamedTypeSymbol? SelectDeterministicTypeCandidate(
        ImmutableArray<INamedTypeSymbol> candidates,
        string token,
        string strategy)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return null;
        }

        if (candidates.Length > 1)
        {
            ReportTypeResolutionAmbiguity(token, strategy, candidates);
        }

        return candidates[0];
    }

    private static void ReportTypeResolutionAmbiguity(
        string token,
        string strategy,
        ImmutableArray<INamedTypeSymbol> candidates)
    {
        var context = ActiveTypeResolutionDiagnosticContext.Value;
        if (context is null || candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var candidateNames = candidates
            .Select(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToImmutableArray();
        var dedupeKey = token + "|" + strategy + "|" + string.Join("|", candidateNames);
        if (!context.ReportedKeys.Add(dedupeKey))
        {
            return;
        }

        context.Diagnostics.Add(new DiagnosticInfo(
            "AXSG0112",
            $"Type resolution for '{token}' is ambiguous via {strategy}. Candidates: {string.Join(", ", candidateNames)}. Using '{candidateNames[0]}' deterministically.",
            context.FilePath,
            1,
            1,
            context.StrictMode));
    }

    private static void ReportTypeResolutionFallbackUsage(
        string token,
        string strategy,
        INamedTypeSymbol selectedCandidate)
    {
        var context = ActiveTypeResolutionDiagnosticContext.Value;
        if (context is null)
        {
            return;
        }

        var selectedName = selectedCandidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var dedupeKey = "fallback|" + token + "|" + strategy + "|" + selectedName;
        if (!context.ReportedKeys.Add(dedupeKey))
        {
            return;
        }

        context.Diagnostics.Add(new DiagnosticInfo(
            "AXSG0113",
            $"Type resolution for '{token}' used compatibility fallback '{strategy}' and selected '{selectedName}'.",
            context.FilePath,
            1,
            1,
            false));
    }

    private static INamedTypeSymbol? ResolveTypeToken(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        string? fallbackClrNamespace)
    {
        var normalized = token.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized.Substring("global::".Length);
        }

        if (TryResolveIntrinsicTypeByToken(compilation, normalized, out var intrinsicType))
        {
            return intrinsicType;
        }

        if (TryParseGenericTypeToken(normalized, out var genericTypeToken, out var genericArgumentTokens))
        {
            var genericType = ResolveTypeToken(compilation, document, genericTypeToken, fallbackClrNamespace);
            if (genericType is not null &&
                genericArgumentTokens.Length > 0)
            {
                var resolvedArguments = new List<ITypeSymbol>(genericArgumentTokens.Length);
                foreach (var genericArgumentToken in genericArgumentTokens)
                {
                    var resolvedArgument = ResolveTypeToken(compilation, document, genericArgumentToken, fallbackClrNamespace);
                    if (resolvedArgument is null)
                    {
                        resolvedArguments.Clear();
                        break;
                    }

                    resolvedArguments.Add(resolvedArgument);
                }

                if (resolvedArguments.Count == genericArgumentTokens.Length)
                {
                    if (genericType.TypeParameters.Length == resolvedArguments.Count)
                    {
                        return genericType.Construct(resolvedArguments.ToArray());
                    }

                    if (genericType.OriginalDefinition.TypeParameters.Length == resolvedArguments.Count)
                    {
                        return genericType.OriginalDefinition.Construct(resolvedArguments.ToArray());
                    }
                }
            }
        }

        var colonIndex = normalized.IndexOf(':');
        if (colonIndex > 0)
        {
            var prefix = normalized.Substring(0, colonIndex);
            var typeName = normalized.Substring(colonIndex + 1);

            if (document.XmlNamespaces.TryGetValue(prefix, out var xmlNamespace))
            {
                return ResolveTypeSymbol(compilation, xmlNamespace, typeName);
            }
        }

        if (normalized.IndexOf('.') >= 0)
        {
            var direct = compilation.GetTypeByMetadataName(normalized);
            if (direct is not null)
            {
                return direct;
            }
        }

        if (document.XmlNamespaces.TryGetValue(string.Empty, out var defaultXmlNamespaceForAlias) &&
            TryResolveConfiguredTypeAlias(compilation, defaultXmlNamespaceForAlias, normalized, genericArity: null, out var aliasedDefaultType))
        {
            return aliasedDefaultType;
        }

        if (!string.IsNullOrWhiteSpace(fallbackClrNamespace))
        {
            var inFallbackNamespace = compilation.GetTypeByMetadataName(fallbackClrNamespace + "." + normalized);
            if (inFallbackNamespace is not null)
            {
                return inFallbackNamespace;
            }
        }

        if (document.XmlNamespaces.TryGetValue(string.Empty, out var defaultXmlNamespace))
        {
            var inDefaultXmlNamespace = ResolveTypeSymbol(compilation, defaultXmlNamespace, normalized);
            if (inDefaultXmlNamespace is not null)
            {
                return inDefaultXmlNamespace;
            }
        }

        if (IsTypeResolutionCompatibilityFallbackEnabled())
        {
            var defaultNamespaceCandidates = GetAvaloniaDefaultNamespaceCandidates(compilation);

            var compatibilityMatch = SelectDeterministicTypeCandidate(
                CollectTypeCandidatesFromNamespacePrefixes(
                    compilation,
                    defaultNamespaceCandidates,
                    normalized),
                normalized,
                "Avalonia default namespace compatibility fallback");
            if (compatibilityMatch is not null)
            {
                ReportTypeResolutionFallbackUsage(
                    normalized,
                    "Avalonia default namespace compatibility fallback",
                    compatibilityMatch);
                return compatibilityMatch;
            }

            if (!IsStrictTypeResolutionMode())
            {
                var compatibilityExtensionMatch = SelectDeterministicTypeCandidate(
                    CollectTypeCandidatesFromNamespacePrefixes(
                        compilation,
                        defaultNamespaceCandidates,
                        normalized,
                        extensionSuffix: true),
                    normalized,
                    "Avalonia default namespace extension compatibility fallback");
                if (compatibilityExtensionMatch is not null)
                {
                    ReportTypeResolutionFallbackUsage(
                        normalized,
                        "Avalonia default namespace extension compatibility fallback",
                        compatibilityExtensionMatch);
                    return compatibilityExtensionMatch;
                }
            }
        }

        if (TryGetImplicitProjectNamespaceRoot(compilation, out var rootNamespace))
        {
            var projectNamespaceCandidates = GetProjectNamespaceCandidates(compilation, rootNamespace);

            var projectMatch = SelectDeterministicTypeCandidate(
                CollectTypeCandidatesFromNamespacePrefixes(
                    compilation,
                    projectNamespaceCandidates,
                    normalized),
                normalized,
                "implicit project namespace fallback");
            if (projectMatch is not null)
            {
                ReportTypeResolutionFallbackUsage(
                    normalized,
                    "implicit project namespace fallback",
                    projectMatch);
                return projectMatch;
            }

            if (IsTypeResolutionCompatibilityFallbackEnabled() &&
                !IsStrictTypeResolutionMode())
            {
                var projectExtensionMatch = SelectDeterministicTypeCandidate(
                    CollectTypeCandidatesFromNamespacePrefixes(
                        compilation,
                        projectNamespaceCandidates,
                        normalized,
                        extensionSuffix: true),
                    normalized,
                    "implicit project namespace extension compatibility fallback");
                if (projectExtensionMatch is not null)
                {
                    ReportTypeResolutionFallbackUsage(
                        normalized,
                        "implicit project namespace extension compatibility fallback",
                        projectExtensionMatch);
                    return projectExtensionMatch;
                }
            }
        }

        return null;
    }

    private static bool TryParseGenericTypeToken(
        string token,
        out string typeToken,
        out ImmutableArray<string> argumentTokens)
    {
        typeToken = string.Empty;
        argumentTokens = ImmutableArray<string>.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = token.Trim();
        var openingIndex = IndexOfTopLevel(normalized, '(');
        if (openingIndex <= 0 || !normalized.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var typePart = normalized.Substring(0, openingIndex).Trim();
        var argumentPart = normalized.Substring(openingIndex + 1, normalized.Length - openingIndex - 2).Trim();
        if (typePart.Length == 0 || argumentPart.Length == 0)
        {
            return false;
        }

        var parsedArguments = SplitTopLevel(argumentPart, ',')
            .Select(static argument => argument.Trim())
            .Where(static argument => argument.Length > 0)
            .ToImmutableArray();
        if (parsedArguments.Length == 0)
        {
            return false;
        }

        typeToken = typePart;
        argumentTokens = parsedArguments;
        return true;
    }

    private static string? ResolveTypeName(Compilation compilation, string xmlNamespace, string xmlTypeName, out INamedTypeSymbol? symbol)
    {
        symbol = ResolveTypeSymbol(compilation, xmlNamespace, xmlTypeName);
        return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static INamedTypeSymbol? ResolveTypeSymbol(Compilation compilation, string xmlNamespace, string xmlTypeName)
    {
        return ResolveTypeSymbol(compilation, xmlNamespace, xmlTypeName, genericArity: null);
    }

    private static INamedTypeSymbol? ResolveTypeSymbol(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity)
    {
        if (TryResolveXamlDirectiveType(compilation, xmlNamespace, xmlTypeName, out var intrinsicType))
        {
            return intrinsicType;
        }

        if (TryResolveIntrinsicTypeByToken(compilation, xmlTypeName, out var intrinsicByName))
        {
            return intrinsicByName;
        }

        if (TryResolveConfiguredTypeAlias(compilation, xmlNamespace, xmlTypeName, genericArity, out var configuredAlias))
        {
            return configuredAlias;
        }

        var explicitClrMetadataName = TryBuildClrNamespaceMetadataName(xmlNamespace, xmlTypeName, genericArity);
        if (explicitClrMetadataName is not null)
        {
            var resolved = compilation.GetTypeByMetadataName(explicitClrMetadataName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        var xmlnsDefinitionResolved = ResolveTypeFromXmlnsDefinitionMap(
            compilation,
            xmlNamespace,
            xmlTypeName,
            genericArity);
        if (xmlnsDefinitionResolved is not null)
        {
            return xmlnsDefinitionResolved;
        }

        if (IsAvaloniaDefaultXmlNamespace(xmlNamespace) &&
            IsTypeResolutionCompatibilityFallbackEnabled())
        {
            var defaultNamespaceCandidates = GetAvaloniaDefaultNamespaceCandidates(compilation);

            var compatibilityMatch = SelectDeterministicTypeCandidate(
                CollectTypeCandidatesFromNamespacePrefixes(
                    compilation,
                    defaultNamespaceCandidates,
                    xmlTypeName,
                    genericArity),
                xmlTypeName,
                "Avalonia default xml namespace compatibility fallback");
            if (compatibilityMatch is not null)
            {
                ReportTypeResolutionFallbackUsage(
                    xmlTypeName,
                    "Avalonia default xml namespace compatibility fallback",
                    compatibilityMatch);
                return compatibilityMatch;
            }

            if ((!genericArity.HasValue || genericArity.Value <= 0) &&
                !IsStrictTypeResolutionMode())
            {
                var compatibilityExtensionMatch = SelectDeterministicTypeCandidate(
                    CollectTypeCandidatesFromNamespacePrefixes(
                        compilation,
                        defaultNamespaceCandidates,
                        xmlTypeName,
                        extensionSuffix: true),
                    xmlTypeName,
                    "Avalonia default xml namespace extension compatibility fallback");
                if (compatibilityExtensionMatch is not null)
                {
                    ReportTypeResolutionFallbackUsage(
                        xmlTypeName,
                        "Avalonia default xml namespace extension compatibility fallback",
                        compatibilityExtensionMatch);
                    return compatibilityExtensionMatch;
                }
            }

            if (TryGetImplicitProjectNamespaceRoot(compilation, out var projectRootNamespace))
            {
                var projectNamespaceCandidates = GetProjectNamespaceCandidates(compilation, projectRootNamespace);

                var projectMatch = SelectDeterministicTypeCandidate(
                    CollectTypeCandidatesFromNamespacePrefixes(
                        compilation,
                        projectNamespaceCandidates,
                        xmlTypeName,
                        genericArity),
                    xmlTypeName,
                    "implicit project namespace fallback");
                if (projectMatch is not null)
                {
                    ReportTypeResolutionFallbackUsage(
                        xmlTypeName,
                        "implicit project namespace fallback",
                        projectMatch);
                    return projectMatch;
                }

                if ((!genericArity.HasValue || genericArity.Value <= 0) &&
                    !IsStrictTypeResolutionMode())
                {
                    var projectExtensionMatch = SelectDeterministicTypeCandidate(
                        CollectTypeCandidatesFromNamespacePrefixes(
                            compilation,
                            projectNamespaceCandidates,
                            xmlTypeName,
                            extensionSuffix: true),
                        xmlTypeName,
                        "implicit project namespace extension compatibility fallback");
                    if (projectExtensionMatch is not null)
                    {
                        ReportTypeResolutionFallbackUsage(
                            xmlTypeName,
                            "implicit project namespace extension compatibility fallback",
                            projectExtensionMatch);
                        return projectExtensionMatch;
                    }
                }
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveTypeFromXmlnsDefinitionMap(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity)
    {
        var clrNamespaces = GetClrNamespacesForXmlNamespace(compilation, xmlNamespace);
        if (clrNamespaces.IsDefaultOrEmpty)
        {
            return null;
        }

        var typeName = AppendGenericArity(xmlTypeName, genericArity);
        var candidates = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var clrNamespace in clrNamespaces)
        {
            if (string.IsNullOrWhiteSpace(clrNamespace))
            {
                continue;
            }

            var candidate = compilation.GetTypeByMetadataName(clrNamespace + "." + typeName);
            if (candidate is not null && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }

        var resolved = SelectDeterministicTypeCandidate(
            candidates.ToImmutable(),
            xmlTypeName,
            "XmlnsDefinitionAttribute map");
        if (resolved is not null)
        {
            return resolved;
        }

        if ((!genericArity.HasValue || genericArity.Value <= 0) &&
            IsTypeResolutionCompatibilityFallbackEnabled() &&
            !IsStrictTypeResolutionMode())
        {
            candidates.Clear();
            seen.Clear();
            foreach (var clrNamespace in clrNamespaces)
            {
                if (string.IsNullOrWhiteSpace(clrNamespace))
                {
                    continue;
                }

                var extensionCandidate = compilation.GetTypeByMetadataName(clrNamespace + "." + xmlTypeName + "Extension");
                if (extensionCandidate is not null && seen.Add(extensionCandidate))
                {
                    candidates.Add(extensionCandidate);
                }
            }

            var extensionResolved = SelectDeterministicTypeCandidate(
                candidates.ToImmutable(),
                xmlTypeName,
                "XmlnsDefinitionAttribute extension compatibility fallback");
            if (extensionResolved is not null)
            {
                ReportTypeResolutionFallbackUsage(
                    xmlTypeName,
                    "XmlnsDefinitionAttribute extension compatibility fallback",
                    extensionResolved);
                return extensionResolved;
            }
        }

        return null;
    }

    private static bool TryResolveConfiguredTypeAlias(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity,
        out INamedTypeSymbol? typeSymbol)
    {
        typeSymbol = null;
        var extensions = ActiveTransformExtensions.Value;
        if (extensions is null || extensions.TypeAliases.Count == 0)
        {
            return false;
        }

        var key = new TypeAliasKey(xmlNamespace.Trim(), xmlTypeName.Trim());
        if (!extensions.TypeAliases.TryGetValue(key, out var configuredType))
        {
            return false;
        }

        if (genericArity is > 0)
        {
            var typeParameters = configuredType.TypeParameters.Length;
            var originalTypeParameters = configuredType.OriginalDefinition.TypeParameters.Length;
            if (typeParameters != genericArity.Value && originalTypeParameters != genericArity.Value)
            {
                return false;
            }
        }

        typeSymbol = configuredType;
        return true;
    }

    private static bool TryResolveIntrinsicTypeByToken(Compilation compilation, string token, out INamedTypeSymbol? symbol)
    {
        var normalized = token.Trim();
        if (normalized.StartsWith("x:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("x:".Length);
        }

        return TryResolveXamlDirectiveType(compilation, Xaml2006.NamespaceName, normalized, out symbol);
    }

    private static bool TryResolveXamlDirectiveType(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        out INamedTypeSymbol? symbol)
    {
        symbol = null;
        if (xmlNamespace != Xaml2006.NamespaceName)
        {
            return false;
        }

        var normalizedTypeName = xmlTypeName.Trim();
        var metadataName = normalizedTypeName switch
        {
            "String" => "System.String",
            "Boolean" or "Bool" => "System.Boolean",
            "Char" => "System.Char",
            "Byte" => "System.Byte",
            "SByte" => "System.SByte",
            "Int16" => "System.Int16",
            "UInt16" => "System.UInt16",
            "Int32" => "System.Int32",
            "UInt32" => "System.UInt32",
            "Int64" => "System.Int64",
            "UInt64" => "System.UInt64",
            "Single" or "Float" => "System.Single",
            "Double" => "System.Double",
            "Decimal" => "System.Decimal",
            "DateTime" => "System.DateTime",
            "TimeSpan" => "System.TimeSpan",
            "Object" => "System.Object",
            "Array" => "System.Array",
            "Type" => "System.Type",
            "Uri" => "System.Uri",
            "Null" => "System.Object",
            _ => null
        };

        if (metadataName is null)
        {
            return false;
        }

        symbol = compilation.GetTypeByMetadataName(metadataName);
        return symbol is not null;
    }

    private static string? TryBuildClrNamespaceMetadataName(string xmlNamespace, string xmlTypeName, int? genericArity)
    {
        if (xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal) ||
            xmlNamespace.StartsWith("using:", StringComparison.Ordinal))
        {
            var segment = xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)
                ? xmlNamespace.Substring("clr-namespace:".Length)
                : xmlNamespace.Substring("using:".Length);
            var separatorIndex = segment.IndexOf(';');
            var clrNamespace = separatorIndex < 0 ? segment : segment.Substring(0, separatorIndex);
            if (!string.IsNullOrWhiteSpace(clrNamespace))
            {
                return clrNamespace + "." + AppendGenericArity(xmlTypeName, genericArity);
            }
        }

        return null;
    }

    private static string AppendGenericArity(string xmlTypeName, int? genericArity)
    {
        if (!genericArity.HasValue || genericArity.Value <= 0 || xmlTypeName.IndexOf('`') >= 0)
        {
            return xmlTypeName;
        }

        return xmlTypeName + "`" + genericArity.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string? NormalizeClassModifier(string? classModifier)
    {
        if (string.IsNullOrWhiteSpace(classModifier))
        {
            return null;
        }

        var normalized = classModifier!.Trim().ToLowerInvariant();
        return normalized switch
        {
            "public" => "public",
            "internal" => "internal",
            "private" => "private",
            "protected" => "protected",
            "protected internal" => "protected internal",
            "private protected" => "private protected",
            "notpublic" => "internal",
            _ => null,
        };
    }

    private static string ToCSharpClassModifier(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };
    }

    private static bool ShouldUseServiceProviderConstructor(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        var publicInstanceConstructors = symbol.InstanceConstructors
            .Where(static ctor => ctor.DeclaredAccessibility == Accessibility.Public)
            .ToImmutableArray();
        if (publicInstanceConstructors.Length == 0)
        {
            return false;
        }

        if (publicInstanceConstructors.Any(static ctor => ctor.Parameters.Length == 0))
        {
            return false;
        }

        return publicInstanceConstructors.Any(IsSingleServiceProviderConstructor);
    }

    private static bool IsSingleServiceProviderConstructor(IMethodSymbol constructor)
    {
        if (constructor.Parameters.Length != 1)
        {
            return false;
        }

        var parameterType = constructor.Parameters[0].Type;
        return parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                   .Equals("global::System.IServiceProvider", StringComparison.Ordinal);
    }

    private static bool IsUsableDuringInitialization(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        for (var current = symbol; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                var attributeType = attribute.AttributeClass;
                if (attributeType is null)
                {
                    continue;
                }

                var metadataName = attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!metadataName.Equals("global::Avalonia.Metadata.UsableDuringInitializationAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length == 0)
                {
                    return true;
                }

                var first = attribute.ConstructorArguments[0];
                if (first.Kind == TypedConstantKind.Primitive && first.Value is bool flag)
                {
                    return flag;
                }

                return true;
            }
        }

        return false;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", string.Empty)
            .Replace("\n", "\\n");
    }

    private enum SelectorCombinatorKind
    {
        None,
        Descendant,
        Child,
        Template
    }

    private enum BindingPriorityScope
    {
        None,
        Style,
        Template
    }

    private readonly struct SelectorBranchSegment
    {
        public SelectorBranchSegment(string text, SelectorCombinatorKind combinator)
        {
            Text = text;
            Combinator = combinator;
        }

        public string Text { get; }

        public SelectorCombinatorKind Combinator { get; }
    }

    private readonly struct ItemContainerTypeMapping
    {
        public ItemContainerTypeMapping(string itemsControlMetadataName, string itemContainerMetadataName)
        {
            ItemsControlMetadataName = itemsControlMetadataName;
            ItemContainerMetadataName = itemContainerMetadataName;
        }

        public string ItemsControlMetadataName { get; }

        public string ItemContainerMetadataName { get; }
    }

    private sealed class ExpressionLocalNameCollector : CSharpSyntaxWalker
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            AddName(node.Parameter.Identifier.ValueText);
            base.VisitSimpleLambdaExpression(node);
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            foreach (var parameter in node.ParameterList.Parameters)
            {
                AddName(parameter.Identifier.ValueText);
            }

            base.VisitParenthesizedLambdaExpression(node);
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            if (node.ParameterList is not null)
            {
                foreach (var parameter in node.ParameterList.Parameters)
                {
                    AddName(parameter.Identifier.ValueText);
                }
            }

            base.VisitAnonymousMethodExpression(node);
        }

        public override void VisitFromClause(FromClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitFromClause(node);
        }

        public override void VisitLetClause(LetClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitLetClause(node);
        }

        public override void VisitJoinClause(JoinClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitJoinClause(node);
        }

        public override void VisitJoinIntoClause(JoinIntoClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitJoinIntoClause(node);
        }

        public override void VisitQueryContinuation(QueryContinuationSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitQueryContinuation(node);
        }

        public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            AddVariableDesignation(node.Designation);
            base.VisitDeclarationExpression(node);
        }

        public override void VisitDeclarationPattern(DeclarationPatternSyntax node)
        {
            AddVariableDesignation(node.Designation);
            base.VisitDeclarationPattern(node);
        }

        private void AddVariableDesignation(VariableDesignationSyntax designation)
        {
            switch (designation)
            {
                case SingleVariableDesignationSyntax single:
                    AddName(single.Identifier.ValueText);
                    break;
                case ParenthesizedVariableDesignationSyntax parenthesized:
                {
                    foreach (var variable in parenthesized.Variables)
                    {
                        AddVariableDesignation(variable);
                    }

                    break;
                }
            }
        }

        private void AddName(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Names.Add(name);
            }
        }
    }

    private sealed class SourceContextExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly ImmutableHashSet<string> _sourceMemberNames;
        private readonly ImmutableHashSet<string> _localNames;
        private readonly string _sourceParameterName;
        private readonly Stack<HashSet<string>> _scopes = new();

        public SourceContextExpressionRewriter(
            ImmutableHashSet<string> sourceMemberNames,
            ImmutableHashSet<string> localNames,
            string sourceParameterName)
        {
            _sourceMemberNames = sourceMemberNames;
            _localNames = localNames;
            _sourceParameterName = sourceParameterName;
        }

        public HashSet<string> Dependencies { get; } = new(StringComparer.Ordinal);

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            PushScope(node.Parameter.Identifier.ValueText);
            var rewritten = base.VisitSimpleLambdaExpression(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            PushScope(node.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText));
            var rewritten = base.VisitParenthesizedLambdaExpression(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            var parameters = node.ParameterList is null
                ? Enumerable.Empty<string>()
                : node.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText);
            PushScope(parameters);
            var rewritten = base.VisitAnonymousMethodExpression(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.ValueText;
            if (name.Length == 0 ||
                name.Equals(_sourceParameterName, StringComparison.Ordinal) ||
                _localNames.Contains(name) ||
                IsScopedName(name) ||
                !_sourceMemberNames.Contains(name))
            {
                return base.VisitIdentifierName(node);
            }

            if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name == node)
            {
                return base.VisitIdentifierName(node);
            }

            if (node.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NameColonSyntax)
            {
                return base.VisitIdentifierName(node);
            }

            Dependencies.Add(name);
            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(_sourceParameterName),
                    SyntaxFactory.IdentifierName(name))
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText.Equals(_sourceParameterName, StringComparison.Ordinal))
            {
                Dependencies.Add(node.Name.Identifier.ValueText);
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText.Equals(_sourceParameterName, StringComparison.Ordinal) &&
                node.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                memberBinding.Name is SimpleNameSyntax memberName)
            {
                Dependencies.Add(memberName.Identifier.ValueText);
            }

            return base.VisitConditionalAccessExpression(node);
        }

        private void PushScope(IEnumerable<string> names)
        {
            var scope = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    scope.Add(name);
                }
            }

            _scopes.Push(scope);
        }

        private void PushScope(string? singleName)
        {
            if (string.IsNullOrWhiteSpace(singleName))
            {
                PushScope(Enumerable.Empty<string>());
                return;
            }

            PushScope(new[] { singleName });
        }

        private void PopScope()
        {
            if (_scopes.Count > 0)
            {
                _scopes.Pop();
            }
        }

        private bool IsScopedName(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly struct TemplatePartExpectation
    {
        public TemplatePartExpectation(ITypeSymbol? expectedType, bool isRequired)
        {
            ExpectedType = expectedType;
            IsRequired = isRequired;
        }

        public ITypeSymbol? ExpectedType { get; }

        public bool IsRequired { get; }
    }

    private readonly struct TemplatePartActual
    {
        public TemplatePartActual(INamedTypeSymbol? type, int line, int column)
        {
            Type = type;
            Line = line;
            Column = column;
        }

        public INamedTypeSymbol? Type { get; }

        public int Line { get; }

        public int Column { get; }
    }

    private readonly struct CompiledPathSegment
    {
        public CompiledPathSegment(
            string memberName,
            ImmutableArray<string> indexers,
            string? castTypeToken,
            bool isMethodCall,
            bool acceptsNull,
            ImmutableArray<string> methodArguments,
            bool isAttachedProperty,
            string? attachedOwnerTypeToken,
            int streamCount)
        {
            MemberName = memberName;
            Indexers = indexers;
            CastTypeToken = castTypeToken;
            IsMethodCall = isMethodCall;
            AcceptsNull = acceptsNull;
            MethodArguments = methodArguments;
            IsAttachedProperty = isAttachedProperty;
            AttachedOwnerTypeToken = attachedOwnerTypeToken;
            StreamCount = streamCount;
        }

        public string MemberName { get; }

        public ImmutableArray<string> Indexers { get; }

        public string? CastTypeToken { get; }

        public bool IsMethodCall { get; }

        public bool AcceptsNull { get; }

        public ImmutableArray<string> MethodArguments { get; }

        public bool IsAttachedProperty { get; }

        public string? AttachedOwnerTypeToken { get; }

        public int StreamCount { get; }
    }

    private readonly struct RelativeSourceMarkup
    {
        public RelativeSourceMarkup(string? mode, string? ancestorTypeToken, int? ancestorLevel, string? tree)
        {
            Mode = mode;
            AncestorTypeToken = ancestorTypeToken;
            AncestorLevel = ancestorLevel;
            Tree = tree;
        }

        public string? Mode { get; }

        public string? AncestorTypeToken { get; }

        public int? AncestorLevel { get; }

        public string? Tree { get; }
    }

    private readonly struct ResolveByNameReferenceToken
    {
        public ResolveByNameReferenceToken(string Name, bool FromMarkupExtension)
        {
            this.Name = Name;
            this.FromMarkupExtension = FromMarkupExtension;
        }

        public string Name { get; }

        public bool FromMarkupExtension { get; }
    }

    private readonly struct SetterValueResolutionResult
    {
        public SetterValueResolutionResult(
            string Expression,
            ResolvedValueKind ValueKind,
            bool RequiresStaticResourceResolver,
            ResolvedValueRequirements ValueRequirements)
        {
            this.Expression = Expression;
            this.ValueKind = ValueKind;
            this.RequiresStaticResourceResolver = RequiresStaticResourceResolver;
            this.ValueRequirements = ValueRequirements;
        }

        public string Expression { get; }

        public ResolvedValueKind ValueKind { get; }

        public bool RequiresStaticResourceResolver { get; }

        public ResolvedValueRequirements ValueRequirements { get; }
    }

    private readonly struct EventBindingMarkup
    {
        public EventBindingMarkup(
            ResolvedEventBindingTargetKind targetKind,
            ResolvedEventBindingSourceMode sourceMode,
            string targetPath,
            string? parameterPath,
            string? parameterValueExpression,
            bool hasParameterValueExpression,
            bool passEventArgs)
        {
            TargetKind = targetKind;
            SourceMode = sourceMode;
            TargetPath = targetPath;
            ParameterPath = parameterPath;
            ParameterValueExpression = parameterValueExpression;
            HasParameterValueExpression = hasParameterValueExpression;
            PassEventArgs = passEventArgs;
        }

        public ResolvedEventBindingTargetKind TargetKind { get; }

        public ResolvedEventBindingSourceMode SourceMode { get; }

        public string TargetPath { get; }

        public string? ParameterPath { get; }

        public string? ParameterValueExpression { get; }

        public bool HasParameterValueExpression { get; }

        public bool PassEventArgs { get; }
    }

    private readonly struct BindingMarkup
    {
        public BindingMarkup(
            bool isCompiledBinding,
            string path,
            string? mode,
            string? elementName,
            RelativeSourceMarkup? relativeSource,
            string? source,
            string? converter,
            string? converterCulture,
            string? converterParameter,
            string? stringFormat,
            string? fallbackValue,
            string? targetNullValue,
            string? delay,
            string? priority,
            string? updateSourceTrigger,
            bool hasSourceConflict,
            string? sourceConflictMessage)
        {
            IsCompiledBinding = isCompiledBinding;
            Path = path;
            Mode = mode;
            ElementName = elementName;
            RelativeSource = relativeSource;
            Source = source;
            Converter = converter;
            ConverterCulture = converterCulture;
            ConverterParameter = converterParameter;
            StringFormat = stringFormat;
            FallbackValue = fallbackValue;
            TargetNullValue = targetNullValue;
            Delay = delay;
            Priority = priority;
            UpdateSourceTrigger = updateSourceTrigger;
            HasSourceConflict = hasSourceConflict;
            SourceConflictMessage = sourceConflictMessage;
        }

        public bool IsCompiledBinding { get; }

        public string Path { get; }

        public string? Mode { get; }

        public string? ElementName { get; }

        public RelativeSourceMarkup? RelativeSource { get; }

        public string? Source { get; }

        public string? Converter { get; }

        public string? ConverterCulture { get; }

        public string? ConverterParameter { get; }

        public string? StringFormat { get; }

        public string? FallbackValue { get; }

        public string? TargetNullValue { get; }

        public string? Delay { get; }

        public string? Priority { get; }

        public string? UpdateSourceTrigger { get; }

        public bool HasSourceConflict { get; }

        public string? SourceConflictMessage { get; }
    }
}
