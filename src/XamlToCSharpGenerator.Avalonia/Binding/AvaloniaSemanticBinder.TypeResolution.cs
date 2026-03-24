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
        return DeterministicTypeResolutionSemantics.CollectCandidatesFromNamespacePrefixes(
            compilation,
            namespacePrefixes,
            typeName,
            genericArity,
            extensionSuffix);
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

    private enum BindingPriorityScope
    {
        None,
        Style,
        Template
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
}
