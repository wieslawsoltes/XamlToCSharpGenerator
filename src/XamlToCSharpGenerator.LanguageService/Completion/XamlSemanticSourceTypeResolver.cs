using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Completion;

internal static class XamlSemanticSourceTypeResolver
{
    public static bool TryResolveBindingSourceType(
        XamlAnalysisResult analysis,
        XElement element,
        BindingMarkup bindingMarkup,
        out INamedTypeSymbol sourceTypeSymbol,
        out ImmutableDictionary<string, string> prefixMap)
    {
        sourceTypeSymbol = null!;
        prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);

        if (bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName) &&
            TryResolveNamedElementType(analysis, element, bindingMarkup.ElementName!, out sourceTypeSymbol))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            return false;
        }

        if (bindingMarkup.RelativeSource is { } relativeSource)
        {
            if (string.Equals(relativeSource.Mode, "Self", StringComparison.OrdinalIgnoreCase) &&
                TryResolveElementTypeSymbol(analysis, element, out sourceTypeSymbol))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken))
            {
                var ancestorPrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
                var ancestorType = ResolveTypeSymbol(analysis, ancestorPrefixMap, relativeSource.AncestorTypeToken!);
                if (ancestorType is not null)
                {
                    sourceTypeSymbol = ancestorType;
                    prefixMap = ancestorPrefixMap;
                    return true;
                }
            }

            if (string.Equals(relativeSource.Mode, "DataContext", StringComparison.OrdinalIgnoreCase))
            {
                if (TryResolveBindingLocalDataType(
                        analysis,
                        element,
                        bindingMarkup,
                        out sourceTypeSymbol,
                        out prefixMap,
                        out var hasExplicitLocalDataType))
                {
                    return true;
                }

                if (hasExplicitLocalDataType)
                {
                    return false;
                }

                return TryResolveAmbientDataType(analysis, element, out sourceTypeSymbol, out prefixMap);
            }

            return false;
        }

        if (TryResolveBindingLocalDataType(
                analysis,
                element,
                bindingMarkup,
                out sourceTypeSymbol,
                out prefixMap,
                out var hasExplicitBindingLocalDataType))
        {
            return true;
        }

        if (hasExplicitBindingLocalDataType)
        {
            return false;
        }

        return TryResolveAmbientDataType(analysis, element, out sourceTypeSymbol, out prefixMap);
    }

    public static bool TryResolveXBindSourceType(
        XamlAnalysisResult analysis,
        XElement element,
        XBindMarkup xBindMarkup,
        out INamedTypeSymbol sourceTypeSymbol,
        out ImmutableDictionary<string, string> prefixMap)
    {
        sourceTypeSymbol = null!;
        prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);

        if (xBindMarkup.HasSourceConflict)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(xBindMarkup.DataType))
        {
            var localPrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
            var localType = ResolveTypeSymbol(analysis, localPrefixMap, xBindMarkup.DataType!);
            if (localType is null)
            {
                return false;
            }

            sourceTypeSymbol = localType;
            prefixMap = localPrefixMap;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(xBindMarkup.ElementName) &&
            TryResolveNamedElementType(analysis, element, xBindMarkup.ElementName!, out sourceTypeSymbol))
        {
            return true;
        }

        if (xBindMarkup.RelativeSource is { } relativeSource)
        {
            if (string.Equals(relativeSource.Mode, "Self", StringComparison.OrdinalIgnoreCase) &&
                TryResolveElementTypeSymbol(analysis, element, out sourceTypeSymbol))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken))
            {
                var ancestorPrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
                var ancestorType = ResolveTypeSymbol(analysis, ancestorPrefixMap, relativeSource.AncestorTypeToken!);
                if (ancestorType is not null)
                {
                    sourceTypeSymbol = ancestorType;
                    prefixMap = ancestorPrefixMap;
                    return true;
                }
            }

            if (string.Equals(relativeSource.Mode, "DataContext", StringComparison.OrdinalIgnoreCase))
            {
                return TryResolveAmbientDataType(analysis, element, out sourceTypeSymbol, out prefixMap);
            }
        }

        if (!string.IsNullOrWhiteSpace(xBindMarkup.Source))
        {
            if (BindingEventMarkupParser.TryExtractReferenceElementName(
                    xBindMarkup.Source,
                    DefaultTryParseMarkupExtension,
                    out var referenceElementName) &&
                TryResolveNamedElementType(analysis, element, referenceElementName, out sourceTypeSymbol))
            {
                return true;
            }

            return false;
        }

        if (IsInsideDataTemplate(element))
        {
            return TryResolveAmbientDataType(analysis, element, out sourceTypeSymbol, out prefixMap);
        }

        return TryResolveRootType(analysis, out sourceTypeSymbol);
    }

    public static bool TryResolveAmbientDataType(
        XamlAnalysisResult analysis,
        XElement element,
        out INamedTypeSymbol sourceTypeSymbol,
        out ImmutableDictionary<string, string> prefixMap)
    {
        sourceTypeSymbol = null!;
        prefixMap = ImmutableDictionary<string, string>.Empty;

        for (var current = element; current is not null; current = current.Parent)
        {
            var dataTypeAttribute = current.Attributes()
                .FirstOrDefault(static attribute => string.Equals(attribute.Name.LocalName, "DataType", StringComparison.Ordinal));
            if (dataTypeAttribute is null)
            {
                continue;
            }

            var dataTypePrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(current);
            var dataTypeType = ResolveTypeSymbol(analysis, dataTypePrefixMap, dataTypeAttribute.Value);
            if (dataTypeType is null)
            {
                continue;
            }

            sourceTypeSymbol = dataTypeType;
            prefixMap = dataTypePrefixMap;
            return true;
        }

        return false;
    }

    private static bool TryResolveBindingLocalDataType(
        XamlAnalysisResult analysis,
        XElement element,
        BindingMarkup bindingMarkup,
        out INamedTypeSymbol sourceTypeSymbol,
        out ImmutableDictionary<string, string> prefixMap,
        out bool hasExplicitLocalDataType)
    {
        sourceTypeSymbol = null!;
        prefixMap = ImmutableDictionary<string, string>.Empty;
        hasExplicitLocalDataType = !string.IsNullOrWhiteSpace(bindingMarkup.DataType);

        if (!hasExplicitLocalDataType)
        {
            return false;
        }

        var localPrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        var localType = ResolveTypeSymbol(analysis, localPrefixMap, bindingMarkup.DataType!);
        if (localType is null)
        {
            return false;
        }

        sourceTypeSymbol = localType;
        prefixMap = localPrefixMap;
        return true;
    }

    public static bool TryResolveElementTypeSymbol(
        XamlAnalysisResult analysis,
        XElement element,
        out INamedTypeSymbol typeSymbol)
    {
        typeSymbol = null!;
        if (!TryResolveElementTypeInfo(analysis, element, out var typeInfo) ||
            typeInfo is null)
        {
            return false;
        }

        var resolvedType = ResolveTypeSymbolByFullTypeName(analysis.Compilation, typeInfo.FullTypeName);
        if (resolvedType is null)
        {
            return false;
        }

        typeSymbol = resolvedType;
        return true;
    }

    public static INamedTypeSymbol? ResolveTypeSymbol(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string typeToken)
    {
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            return null;
        }

        var normalizedTypeToken = NormalizeTypeReferenceToken(typeToken);
        if (normalizedTypeToken.Length == 0)
        {
            return null;
        }

        if (analysis.TypeIndex is not null &&
            XamlClrSymbolResolver.TryResolveTypeInfo(
                analysis.TypeIndex,
                prefixMap,
                normalizedTypeToken,
                out var typeInfo) &&
            typeInfo is not null)
        {
            return ResolveTypeSymbolByFullTypeName(analysis.Compilation, typeInfo.FullTypeName);
        }

        if (XamlTypeReferenceNavigationResolver.TryResolve(
                analysis,
                prefixMap,
                "DataType",
                normalizedTypeToken,
                out var resolvedTypeReference))
        {
            return ResolveTypeSymbolByFullTypeName(analysis.Compilation, resolvedTypeReference.FullTypeName);
        }

        return null;
    }

    public static bool TryResolveRootType(
        XamlAnalysisResult analysis,
        out INamedTypeSymbol sourceTypeSymbol)
    {
        sourceTypeSymbol = null!;
        var classFullName = analysis.ParsedDocument?.ClassFullName;
        if (string.IsNullOrWhiteSpace(classFullName))
        {
            return false;
        }

        var resolvedType = ResolveTypeSymbolByFullTypeName(analysis.Compilation, classFullName);
        if (resolvedType is null)
        {
            return false;
        }

        sourceTypeSymbol = resolvedType;
        return true;
    }

    public static bool IsInsideDataTemplate(XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (string.Equals(current.Name.LocalName, "DataTemplate", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static INamedTypeSymbol? ResolveTypeSymbolByFullTypeName(Compilation? compilation, string fullTypeName)
    {
        if (compilation is null || string.IsNullOrWhiteSpace(fullTypeName))
        {
            return null;
        }

        var direct = compilation.GetTypeByMetadataName(fullTypeName);
        if (direct is not null)
        {
            return direct;
        }

        var segments = fullTypeName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        ISymbol current = compilation.GlobalNamespace;
        var index = 0;
        while (index < segments.Length)
        {
            if (current is INamespaceSymbol namespaceSymbol)
            {
                var nextNamespace = namespaceSymbol.GetNamespaceMembers()
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, segments[index], StringComparison.Ordinal));
                if (nextNamespace is not null)
                {
                    current = nextNamespace;
                    index++;
                    continue;
                }

                var nextType = namespaceSymbol.GetTypeMembers(segments[index]).FirstOrDefault();
                if (nextType is null)
                {
                    return null;
                }

                current = nextType;
                index++;
                continue;
            }

            if (current is INamedTypeSymbol typeSymbol)
            {
                var nextType = typeSymbol.GetTypeMembers(segments[index]).FirstOrDefault();
                if (nextType is null)
                {
                    return null;
                }

                current = nextType;
                index++;
                continue;
            }

            return null;
        }

        return current as INamedTypeSymbol;
    }

    public static bool TryResolveTypeInfo(
        XamlAnalysisResult analysis,
        INamedTypeSymbol typeSymbol,
        out AvaloniaTypeInfo? typeInfo)
    {
        typeInfo = null;
        if (analysis.TypeIndex is null)
        {
            return false;
        }

        var fullTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return analysis.TypeIndex.TryGetTypeByFullTypeName(fullTypeName, out typeInfo) && typeInfo is not null;
    }

    public static ITypeSymbol? ResolveDisplayTypeSymbol(
        Compilation? compilation,
        string ownerNamespace,
        string displayTypeName)
    {
        if (compilation is null || string.IsNullOrWhiteSpace(displayTypeName))
        {
            return null;
        }

        var trimmedTypeName = displayTypeName.Trim();
        if (trimmedTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            trimmedTypeName = trimmedTypeName.Substring(0, trimmedTypeName.Length - 1);
        }

        switch (trimmedTypeName)
        {
            case "bool":
                return compilation.GetSpecialType(SpecialType.System_Boolean);
            case "byte":
                return compilation.GetSpecialType(SpecialType.System_Byte);
            case "char":
                return compilation.GetSpecialType(SpecialType.System_Char);
            case "decimal":
                return compilation.GetSpecialType(SpecialType.System_Decimal);
            case "double":
                return compilation.GetSpecialType(SpecialType.System_Double);
            case "short":
                return compilation.GetSpecialType(SpecialType.System_Int16);
            case "int":
                return compilation.GetSpecialType(SpecialType.System_Int32);
            case "long":
                return compilation.GetSpecialType(SpecialType.System_Int64);
            case "object":
                return compilation.GetSpecialType(SpecialType.System_Object);
            case "sbyte":
                return compilation.GetSpecialType(SpecialType.System_SByte);
            case "float":
                return compilation.GetSpecialType(SpecialType.System_Single);
            case "string":
                return compilation.GetSpecialType(SpecialType.System_String);
            case "ushort":
                return compilation.GetSpecialType(SpecialType.System_UInt16);
            case "uint":
                return compilation.GetSpecialType(SpecialType.System_UInt32);
            case "ulong":
                return compilation.GetSpecialType(SpecialType.System_UInt64);
        }

        var direct = compilation.GetTypeByMetadataName(trimmedTypeName);
        if (direct is not null)
        {
            return direct;
        }

        if (!trimmedTypeName.Contains('.', StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(ownerNamespace))
        {
            return compilation.GetTypeByMetadataName(ownerNamespace + "." + trimmedTypeName);
        }

        return null;
    }

    internal static bool TryResolveNamedElementType(
        XamlAnalysisResult analysis,
        XElement element,
        string elementName,
        out INamedTypeSymbol typeSymbol)
    {
        typeSymbol = null!;
        var documentRoot = element.Document?.Root;
        if (documentRoot is null)
        {
            return false;
        }

        var currentScopeRoot = FindVisibleNameScopeRoot(element);
        if (TryResolveNamedElementTypeInScope(analysis, currentScopeRoot, elementName, out typeSymbol))
        {
            return true;
        }

        return !ReferenceEquals(currentScopeRoot, documentRoot) &&
               TryResolveNamedElementTypeInScope(analysis, documentRoot, elementName, out typeSymbol);
    }

    private static bool TryResolveNamedElementTypeInScope(
        XamlAnalysisResult analysis,
        XElement scopeRoot,
        string elementName,
        out INamedTypeSymbol typeSymbol)
    {
        foreach (var candidateElement in EnumerateVisibleNameScopeElements(scopeRoot, scopeRoot))
        {
            var nameAttribute = candidateElement.Attributes()
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(attribute.Value));
            if (nameAttribute is null ||
                !string.Equals(nameAttribute.Value, elementName, StringComparison.Ordinal))
            {
                continue;
            }

            return TryResolveElementTypeSymbol(analysis, candidateElement, out typeSymbol);
        }

        typeSymbol = null!;
        return false;
    }

    private static IEnumerable<XElement> EnumerateVisibleNameScopeElements(XElement scopeRoot, XElement candidate)
    {
        yield return candidate;

        foreach (var child in candidate.Elements())
        {
            if (!ReferenceEquals(child, scopeRoot) && IsNameScopeBoundaryElement(child))
            {
                continue;
            }

            foreach (var descendant in EnumerateVisibleNameScopeElements(scopeRoot, child))
            {
                yield return descendant;
            }
        }
    }

    private static XElement FindVisibleNameScopeRoot(XElement element)
    {
        var documentRoot = element.Document?.Root ?? element;
        for (var current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, documentRoot) || IsNameScopeBoundaryElement(current))
            {
                return current;
            }
        }

        return documentRoot;
    }

    private static bool IsNameScopeBoundaryElement(XElement element)
    {
        return element.Name.LocalName switch
        {
            "DataTemplate" => true,
            "ControlTemplate" => true,
            "ItemsPanelTemplate" => true,
            "TreeDataTemplate" => true,
            _ => false
        };
    }

    private static bool DefaultTryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return DefaultMarkupParser.TryParseMarkupExtension(value, out markupExtension);
    }

    private static readonly MarkupExpressionParser DefaultMarkupParser = new(
        new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));

    private static bool TryResolveElementTypeInfo(
        XamlAnalysisResult analysis,
        XElement element,
        out AvaloniaTypeInfo? typeInfo)
    {
        typeInfo = null;
        if (analysis.TypeIndex is null)
        {
            return false;
        }

        if (analysis.TypeIndex.TryGetType(element.Name.NamespaceName, element.Name.LocalName, out typeInfo) &&
            typeInfo is not null)
        {
            return true;
        }

        if (XamlClrSymbolResolver.TryResolveClrNamespace(element.Name.NamespaceName, out var clrNamespace))
        {
            return analysis.TypeIndex.TryGetTypeByClrNamespace(clrNamespace, element.Name.LocalName, out typeInfo) &&
                   typeInfo is not null;
        }

        return false;
    }

    private static string NormalizeTypeReferenceToken(string rawTypeValue)
    {
        var candidate = rawTypeValue.Trim();
        if (candidate.Length == 0)
        {
            return string.Empty;
        }

        if (!candidate.StartsWith("{", StringComparison.Ordinal))
        {
            return candidate;
        }

        var closingBrace = candidate.LastIndexOf('}');
        if (closingBrace > 0)
        {
            candidate = candidate.Substring(1, closingBrace - 1).Trim();
        }
        else
        {
            candidate = candidate.TrimStart('{').Trim();
        }

        if (!candidate.StartsWith("x:Type", StringComparison.Ordinal))
        {
            return candidate;
        }

        var typePayload = candidate.Substring("x:Type".Length).Trim();
        if (typePayload.Length == 0)
        {
            return string.Empty;
        }

        var commaIndex = typePayload.IndexOf(',');
        if (commaIndex >= 0)
        {
            typePayload = typePayload.Substring(0, commaIndex).Trim();
        }

        return typePayload;
    }
}
