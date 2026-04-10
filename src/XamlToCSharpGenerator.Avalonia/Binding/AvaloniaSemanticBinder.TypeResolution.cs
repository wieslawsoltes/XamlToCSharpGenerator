using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static INamedTypeSymbol? ResolveTypeFromTypeExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace)
    {
        return TypeExpressionResolutionService.ResolveTypeFromExpression(
            compilation,
            document,
            typeExpression,
            fallbackClrNamespace);
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

    private static readonly ImmutableArray<string> SilentCompatibilityDiagnosticNamespacePrefixes =
    [
        "Avalonia.Controls",
        "Avalonia.Controls.Primitives",
        "Avalonia.Controls.Presenters",
        "Avalonia.Controls.Shapes",
        "Avalonia.Controls.Documents",
        "Avalonia.Controls.Chrome",
        "Avalonia.Controls.Embedding",
        "Avalonia.Controls.Notifications",
        "Avalonia.Controls.Converters",
        "Avalonia.Styling",
        "Avalonia.Controls.Templates",
        "Avalonia.Input",
        "Avalonia.Automation",
        "Avalonia.Dialogs",
        "Avalonia.Dialogs.Internal",
        "Avalonia.Layout",
        "Avalonia.Media",
        "Avalonia.Media.Transformation",
        "Avalonia.Media.Imaging",
        "Avalonia.Animation",
        "Avalonia.Animation.Easings"
    ];

    private static ImmutableArray<string> GetAvaloniaDefaultNamespaceCandidates(Compilation compilation)
    {
        return NamespaceDiscoveryService.GetFrameworkDefaultNamespaceCandidates(compilation);
    }

    private static ImmutableArray<XmlnsDefinitionTarget> GetXmlnsDefinitionTargetsForXmlNamespace(Compilation compilation, string xmlNamespace)
    {
        return NamespaceDiscoveryService.GetXmlnsDefinitionTargetsForXmlNamespace(compilation, xmlNamespace);
    }

    private static string NormalizeXmlNamespaceKey(string xmlNamespace)
    {
        var normalized = xmlNamespace.Trim();
        return IsAvaloniaDefaultXmlNamespace(normalized)
            ? AvaloniaDefaultXmlNamespace
            : normalized;
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
        return NamespaceDiscoveryService.GetProjectNamespaceCandidates(compilation, rootNamespace);
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        return TypeResolutionNamespaceDiscoveryService.EnumerateAssemblies(compilation);
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
        return options?.TypeResolutionCompatibilityFallbackEnabled ?? false;
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
        return DeterministicTypeResolutionSemantics.CollectCandidatesFromNamespacePrefixes(
            compilation,
            namespacePrefixes,
            typeName,
            genericArity,
            extensionSuffix);
    }

    private static ImmutableArray<INamedTypeSymbol> CollectTypeCandidatesFromXmlnsDefinitionTargets(
        Compilation compilation,
        ImmutableArray<XmlnsDefinitionTarget> targets,
        string typeName,
        int? genericArity = null,
        bool extensionSuffix = false)
    {
        return NamespaceDiscoveryService.CollectTypeCandidatesFromXmlnsDefinitionTargets(
            compilation,
            targets,
            typeName,
            genericArity,
            extensionSuffix);
    }

    private static bool IsAccessibleTypeCandidate(Compilation compilation, INamedTypeSymbol candidate)
    {
        return compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly);
    }

    private static INamedTypeSymbol? TryResolveTypeFromNamespacePrefixes(
        Compilation compilation,
        ImmutableArray<string> namespacePrefixes,
        string typeName,
        int? genericArity,
        bool extensionSuffix,
        string strategy,
        bool reportFallbackUsage)
    {
        var selectedCandidate = SelectDeterministicTypeCandidate(
            CollectTypeCandidatesFromNamespacePrefixes(
                compilation,
                namespacePrefixes,
                typeName,
                genericArity,
                extensionSuffix),
            typeName,
            strategy);
        if (selectedCandidate is not null &&
            reportFallbackUsage)
        {
            ReportTypeResolutionFallbackUsage(typeName, strategy, selectedCandidate);
        }

        return selectedCandidate;
    }

    private static INamedTypeSymbol? SelectDeterministicTypeCandidate(
        ImmutableArray<INamedTypeSymbol> candidates,
        string token,
        string strategy)
    {
        var selection = DeterministicTypeResolutionSemantics.SelectDeterministicCandidate(
            candidates,
            token,
            strategy);
        ReportTypeResolutionAmbiguity(selection.Ambiguity);
        return selection.SelectedCandidate;
    }

    private static void ReportTypeResolutionAmbiguity(TypeResolutionAmbiguityInfo? ambiguity)
    {
        var context = ActiveTypeResolutionDiagnosticContext.Value;
        if (context is null || ambiguity is null)
        {
            return;
        }

        if (ShouldSuppressTypeResolutionCompatibilityDiagnostic(ambiguity.Token))
        {
            return;
        }

        if (!context.ReportedKeys.Add(ambiguity.DedupeKey))
        {
            return;
        }

        context.Diagnostics.Add(new DiagnosticInfo(
            "AXSG0112",
            ambiguity.Message,
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

        if (ShouldSuppressTypeResolutionCompatibilityDiagnostic(token, selectedCandidate))
        {
            return;
        }

        var selectedName = selectedCandidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var normalizedStrategy = NormalizeTypeResolutionCompatibilityStrategy(strategy);
        var dedupeKey = "fallback|" + token + "|" + normalizedStrategy + "|" + selectedName;
        if (!context.ReportedKeys.Add(dedupeKey))
        {
            return;
        }

        context.Diagnostics.Add(new DiagnosticInfo(
            "AXSG0113",
            $"Type resolution for '{token}' used compatibility fallback '{normalizedStrategy}' and selected '{selectedName}'.",
            context.FilePath,
            1,
            1,
            false));
    }

    private static bool ShouldSuppressTypeResolutionCompatibilityDiagnostic(string token)
    {
        return string.Equals(token, "CSharp", StringComparison.Ordinal);
    }

    private static bool ShouldSuppressTypeResolutionCompatibilityDiagnostic(
        string token,
        INamedTypeSymbol selectedCandidate)
    {
        if (ShouldSuppressTypeResolutionCompatibilityDiagnostic(token))
        {
            return true;
        }

        var containingNamespace = selectedCandidate.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (string.Equals(containingNamespace, "Avalonia", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var prefix in SilentCompatibilityDiagnosticNamespacePrefixes)
        {
            if (containingNamespace.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTypeResolutionCompatibilityStrategy(string strategy)
    {
        return strategy switch
        {
            "framework default namespace compatibility fallback" => "Avalonia default namespace compatibility fallback",
            "framework default namespace extension compatibility fallback" => "Avalonia default namespace extension compatibility fallback",
            "framework default xml namespace compatibility fallback" => "Avalonia default xml namespace compatibility fallback",
            "framework default xml namespace extension compatibility fallback" => "Avalonia default xml namespace extension compatibility fallback",
            _ => strategy
        };
    }

    private static INamedTypeSymbol? ResolveTypeToken(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        string? fallbackClrNamespace)
    {
        var normalized = XamlTypeTokenSemantics.TrimGlobalQualifier(token);

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

        if (XamlTokenSplitSemantics.TrySplitAtFirstSeparator(
                normalized,
                ':',
                out var prefix,
                out var typeName))
        {
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

        return TypeResolutionPolicyService.TryResolveTokenFallback(
            compilation,
            document,
            normalized,
            fallbackClrNamespace);
    }

    private static bool TryParseGenericTypeToken(
        string token,
        out string typeToken,
        out ImmutableArray<string> argumentTokens)
    {
        return DeterministicTypeResolutionSemantics.TryParseGenericTypeToken(
            token,
            out typeToken,
            out argumentTokens);
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
            var resolved = ResolveExplicitClrNamespaceType(compilation, xmlNamespace, explicitClrMetadataName);
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

        var markupObjectElementResolved = MarkupObjectElementTypeResolutionService.TryResolve(
            GetActiveTypeSymbolCatalog(compilation),
            xmlNamespace,
            xmlTypeName);
        if (markupObjectElementResolved is not null)
        {
            return markupObjectElementResolved;
        }

        return TypeResolutionPolicyService.TryResolveXmlNamespaceFallback(
            compilation,
            xmlNamespace,
            xmlTypeName,
            genericArity);
    }

    private static INamedTypeSymbol? ResolveExplicitClrNamespaceType(
        Compilation compilation,
        string xmlNamespace,
        string metadataName)
    {
        if (XamlXmlNamespaceSemantics.TryExtractClrNamespaceReference(
                xmlNamespace,
                out _,
                out var assemblySimpleName) &&
            !string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            if (string.Equals(compilation.AssemblyName, assemblySimpleName, StringComparison.OrdinalIgnoreCase))
            {
                return compilation.Assembly.GetTypeByMetadataName(metadataName) ??
                       compilation.GetTypeByMetadataName(metadataName);
            }

            foreach (var assembly in EnumerateAssemblies(compilation))
            {
                if (!string.Equals(assembly.Name, assemblySimpleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return assembly.GetTypeByMetadataName(metadataName) ??
                       compilation.GetTypeByMetadataName(metadataName);
            }
        }

        return compilation.GetTypeByMetadataName(metadataName);
    }

    private static INamedTypeSymbol? ResolveTypeFromXmlnsDefinitionMap(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity)
    {
        var targets = GetXmlnsDefinitionTargetsForXmlNamespace(compilation, xmlNamespace);
        if (targets.IsDefaultOrEmpty)
        {
            return null;
        }

        var resolved = SelectDeterministicTypeCandidate(
            CollectTypeCandidatesFromXmlnsDefinitionTargets(
                compilation,
                targets,
                xmlTypeName,
                genericArity,
                extensionSuffix: false),
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
            var extensionResolved = SelectDeterministicTypeCandidate(
                CollectTypeCandidatesFromXmlnsDefinitionTargets(
                    compilation,
                    targets,
                    xmlTypeName,
                    genericArity: null,
                    extensionSuffix: true),
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
        var normalized = XamlTypeTokenSemantics.TrimXamlDirectivePrefix(token);

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
        return DeterministicTypeResolutionSemantics.TryBuildClrNamespaceMetadataName(
            xmlNamespace,
            xmlTypeName,
            genericArity);
    }

    private static string AppendGenericArity(string xmlTypeName, int? genericArity)
    {
        return DeterministicTypeResolutionSemantics.AppendGenericArity(xmlTypeName, genericArity);
    }

    private static string? NormalizeClassModifier(string? classModifier)
    {
        return XamlAccessibilityModifierSemantics.NormalizeClassModifier(classModifier);
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
                if (!SemanticConventions.IsUsableDuringInitializationAttribute(metadataName))
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

    private enum BindingPriorityScope
    {
        None,
        Style,
        Template
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

}
