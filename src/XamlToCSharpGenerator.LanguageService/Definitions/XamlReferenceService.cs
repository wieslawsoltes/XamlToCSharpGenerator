using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

public sealed class XamlReferenceService
{
    private static readonly TimeSpan ProjectDiscoveryCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SourceValidationCacheTtl = TimeSpan.FromSeconds(1);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, CachedProjectFileList> ProjectFileListCache =
        new(PathComparer);
    private static readonly ConcurrentDictionary<string, CachedXamlSourceFile> SourceFileCache =
        new(PathComparer);
    private static readonly SourceRange MetadataDeclarationRange = new(
        new SourcePosition(0, 0),
        new SourcePosition(0, 1));

    public ImmutableArray<XamlReferenceLocation> GetReferences(XamlAnalysisResult analysis, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var identifier = XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        if (analysis.ParsedDocument is not null)
        {
            var hasNamedDeclaration = HasNamedElementDeclaration(analysis, identifier);
            var hasResourceDeclaration = HasResourceDeclaration(analysis, identifier);
            if (hasNamedDeclaration || hasResourceDeclaration)
            {
                var symbolKind = XamlNavigationTextSemantics.DetectSymbolKindAtOffset(
                    analysis.Document.Text,
                    offset,
                    identifier,
                    hasNamedDeclaration,
                    hasResourceDeclaration);
                if (symbolKind != XamlNavigationTextSemantics.NavigationSymbolKind.Unknown)
                {
                    return CollectNamedOrResourceReferences(analysis, identifier, symbolKind);
                }
            }
        }

        return CollectClrReferences(analysis, position, identifier);
    }

    private static ImmutableArray<XamlReferenceLocation> CollectNamedOrResourceReferences(
        XamlAnalysisResult analysis,
        string identifier,
        XamlNavigationTextSemantics.NavigationSymbolKind symbolKind)
    {
        var resultBuilder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var declarationBuilder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.NamedElement)
        {
            AddNamedElementDeclarations(analysis, identifier, declarationBuilder);
        }
        else if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.ResourceKey)
        {
            AddResourceDeclarations(analysis, identifier, declarationBuilder);
        }

        foreach (var declaration in declarationBuilder)
        {
            if (seen.Add(CreateReferenceIdentity(declaration.Uri, declaration.Range)))
            {
                resultBuilder.Add(declaration);
            }
        }

        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.NamedElement)
        {
            foreach (var range in XamlNavigationTextSemantics.FindElementReferenceRanges(analysis.Document.Text, identifier))
            {
                if (seen.Add(CreateReferenceIdentity(UriPathHelper.ToDocumentUri(analysis.Document.FilePath), range)))
                {
                    resultBuilder.Add(new XamlReferenceLocation(
                        UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                        range,
                        IsDeclaration: false));
                }
            }
        }
        else if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.ResourceKey)
        {
            foreach (var range in XamlNavigationTextSemantics.FindResourceReferenceRanges(analysis.Document.Text, identifier))
            {
                if (seen.Add(CreateReferenceIdentity(UriPathHelper.ToDocumentUri(analysis.Document.FilePath), range)))
                {
                    resultBuilder.Add(new XamlReferenceLocation(
                        UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                        range,
                        IsDeclaration: false));
                }
            }
        }

        return resultBuilder.ToImmutable();
    }

    private static ImmutableArray<XamlReferenceLocation> CollectClrReferences(
        XamlAnalysisResult analysis,
        SourcePosition position,
        string identifier)
    {
        if (analysis.TypeIndex is null)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        if (XamlBindingNavigationService.TryResolveNavigationTarget(analysis, position, out var bindingTarget))
        {
            if (bindingTarget.Kind == XamlBindingNavigationTargetKind.Property &&
                bindingTarget.OwnerTypeInfo is not null &&
                bindingTarget.PropertyInfo is not null)
            {
                return CollectPropertyReferences(
                    analysis,
                    bindingTarget.OwnerTypeInfo,
                    bindingTarget.PropertyInfo);
            }

            if (bindingTarget.Kind == XamlBindingNavigationTargetKind.Type &&
                bindingTarget.TypeReference is { } bindingTypeReference)
            {
                if (analysis.TypeIndex.TryGetTypeByFullTypeName(bindingTypeReference.FullTypeName, out var bindingTypeInfo) &&
                    bindingTypeInfo is not null)
                {
                    return CollectTypeReferences(analysis, bindingTypeInfo);
                }

                return CollectTypeAttributeValueReferences(analysis, bindingTypeReference);
            }
        }

        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        var prefixMap = analysis.PrefixMap;
        var token = string.IsNullOrWhiteSpace(context.Token) ? identifier : context.Token;
        var localAttributeName = GetLocalName(context.CurrentAttributeName);

        if (context.Kind == XamlCompletionContextKind.AttributeValue &&
            XamlStyleNavigationSemantics.IsSetterPropertyAttribute(context.CurrentElementName, context.CurrentAttributeName))
        {
            var setterPropertyToken = string.IsNullOrWhiteSpace(token) ? identifier : token;
            if (!string.IsNullOrWhiteSpace(setterPropertyToken))
            {
                var ownerTypeToken = context.CurrentElementName;
                if (!setterPropertyToken.Contains('.', StringComparison.Ordinal) &&
                    XamlStyleNavigationSemantics.TryResolveStyleSetterOwnerTypeToken(
                        analysis,
                        position,
                        setterPropertyToken,
                        out var resolvedOwnerTypeToken))
                {
                    ownerTypeToken = resolvedOwnerTypeToken;
                }

                if (XamlClrSymbolResolver.TryResolvePropertyInfo(
                        analysis.TypeIndex,
                        prefixMap,
                        ownerTypeToken,
                        setterPropertyToken,
                        out var setterPropertyInfo,
                        out var setterOwnerTypeInfo) &&
                    setterPropertyInfo is not null &&
                    setterOwnerTypeInfo is not null)
                {
                    return CollectPropertyReferences(analysis, setterOwnerTypeInfo, setterPropertyInfo);
                }
            }
        }

        if (context.Kind == XamlCompletionContextKind.AttributeValue &&
            XamlStyleNavigationSemantics.IsSelectorAttribute(context.CurrentAttributeName) &&
            XamlStyleNavigationSemantics.TryResolveSelectorTypeToken(
                string.IsNullOrWhiteSpace(token) ? context.CurrentAttributeValue : token,
                identifier,
                out var selectorTypeToken) &&
            XamlClrSymbolResolver.TryResolveTypeInfo(
                analysis.TypeIndex,
                prefixMap,
                selectorTypeToken,
                out var selectorTypeInfo) &&
            selectorTypeInfo is not null)
        {
            return CollectTypeReferences(analysis, selectorTypeInfo);
        }

        if (context.Kind == XamlCompletionContextKind.MarkupExtension &&
            XamlMarkupExtensionNavigationSemantics.TryResolveClassTokenAtOffset(
                analysis.Document.Text,
                TextCoordinateHelper.GetOffset(analysis.Document.Text, position),
                out var markupClassToken) &&
            string.Equals(markupClassToken.Name, identifier, StringComparison.Ordinal) &&
            XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
                analysis,
                prefixMap,
                markupClassToken.Name,
                out var markupTypeReference))
        {
            if (analysis.TypeIndex.TryGetTypeByFullTypeName(markupTypeReference.FullTypeName, out var markupTypeInfo) &&
                markupTypeInfo is not null)
            {
                return CollectTypeReferences(analysis, markupTypeInfo);
            }

            return CollectTypeAttributeValueReferences(analysis, markupTypeReference);
        }

        if (context.Kind is XamlCompletionContextKind.AttributeValue or XamlCompletionContextKind.MarkupExtension &&
            XamlTypeReferenceNavigationResolver.TryResolve(
                analysis,
                prefixMap,
                localAttributeName,
                string.IsNullOrWhiteSpace(token) ? context.CurrentAttributeValue : token,
                out var resolvedTypeReference))
        {
            return CollectTypeAttributeValueReferences(analysis, resolvedTypeReference);
        }

        if (context.Kind == XamlCompletionContextKind.ElementName &&
            XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex, prefixMap, token, out var typeInfo) &&
            typeInfo is not null)
        {
            return CollectTypeReferences(analysis, typeInfo);
        }

        if (context.Kind != XamlCompletionContextKind.AttributeName)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var propertyToken = string.IsNullOrWhiteSpace(token)
            ? context.CurrentAttributeName
            : token;
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex,
                prefixMap,
                context.CurrentElementName,
                propertyToken,
                out var propertyInfo,
                out var ownerTypeInfo) ||
            propertyInfo is null ||
            ownerTypeInfo is null)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        return CollectPropertyReferences(analysis, ownerTypeInfo, propertyInfo);
    }

    private static ImmutableArray<XamlReferenceLocation> CollectTypeReferences(
        XamlAnalysisResult analysis,
        AvaloniaTypeInfo typeInfo)
    {
        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var typeTokenCandidates = BuildTypeReferenceTokenCandidates(typeInfo);

        if (typeInfo.SourceLocation is { } declaration)
        {
            AddReference(builder, seen, declaration.Uri, declaration.Range, isDeclaration: true);
        }
        else if (XamlSourceLinkResolver.TryResolveTypeLocation(
                     analysis,
                     typeInfo.FullTypeName,
                     typeInfo.AssemblyName,
                     out var sourceLinkDeclaration))
        {
            AddReference(builder, seen, sourceLinkDeclaration.Uri, sourceLinkDeclaration.Range, isDeclaration: true);
        }
        else
        {
            var metadataUri = XamlMetadataSymbolUri.CreateTypeUri(typeInfo.FullTypeName);
            AddReference(builder, seen, metadataUri, MetadataDeclarationRange, isDeclaration: true);
        }

        foreach (var source in EnumerateProjectXamlSources(analysis))
        {
            if (!ContainsAnyToken(source.Text, typeTokenCandidates))
            {
                continue;
            }

            if (!TryEnsureXmlDocumentLoaded(source, out var xmlDocument) || xmlDocument is null)
            {
                continue;
            }

            foreach (var element in xmlDocument.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>())
            {
                AvaloniaTypeInfo? elementTypeInfo = null;
                if (string.Equals(element.Name.LocalName, typeInfo.XmlTypeName, StringComparison.Ordinal) &&
                    TryResolveTypeInfoByXmlNamespace(
                        analysis.TypeIndex!,
                        element.Name.NamespaceName,
                        element.Name.LocalName,
                        out elementTypeInfo) &&
                    elementTypeInfo is not null)
                {
                    if (string.Equals(elementTypeInfo.FullTypeName, typeInfo.FullTypeName, StringComparison.Ordinal) &&
                        TryCreateElementNameRange(source.Text, element, out var elementNameRange))
                    {
                        AddReference(
                            builder,
                            seen,
                            UriPathHelper.ToDocumentUri(source.FilePath),
                            elementNameRange,
                            isDeclaration: false);
                    }

                    if (element.Attributes().All(static attribute => attribute.IsNamespaceDeclaration))
                    {
                        continue;
                    }
                }

                var sourcePrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    if (string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal) &&
                        string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal) &&
                        TryAddSelectorTypeReference(
                            analysis,
                            typeInfo,
                            source.Text,
                            sourcePrefixMap,
                            element,
                            attribute,
                            out var selectorRange))
                    {
                        AddReference(
                            builder,
                            seen,
                            UriPathHelper.ToDocumentUri(source.FilePath),
                            selectorRange,
                            isDeclaration: false);
                    }

                    if (attribute.Value.IndexOf('{') >= 0 &&
                        TryAddMarkupExtensionTypeReference(
                            analysis,
                            typeInfo,
                            source.Text,
                            sourcePrefixMap,
                            attribute,
                            out var markupRange))
                    {
                        AddReference(
                            builder,
                            seen,
                            UriPathHelper.ToDocumentUri(source.FilePath),
                            markupRange,
                            isDeclaration: false);
                    }

                    if (attribute.Value.IndexOf('{') >= 0)
                    {
                        foreach (var bindingRange in XamlBindingNavigationService.FindTypeReferenceRanges(
                                     analysis,
                                     source.Text,
                                     element,
                                     attribute,
                                     typeInfo.FullTypeName))
                        {
                            AddReference(
                                builder,
                                seen,
                                UriPathHelper.ToDocumentUri(source.FilePath),
                                bindingRange,
                                isDeclaration: false);
                        }
                    }
                }
            }
        }

        return builder
            .OrderBy(static item => item.Uri, StringComparer.Ordinal)
            .ThenBy(static item => item.Range.Start.Line)
            .ThenBy(static item => item.Range.Start.Character)
            .ToImmutableArray();
    }

    private static ImmutableArray<XamlReferenceLocation> CollectTypeAttributeValueReferences(
        XamlAnalysisResult analysis,
        XamlResolvedTypeReference targetTypeReference)
    {
        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var typeTokenCandidates = BuildTypeReferenceTokenCandidates(targetTypeReference);

        if (targetTypeReference.SourceLocation is { } declaration)
        {
            AddReference(builder, seen, declaration.Uri, declaration.Range, isDeclaration: true);
        }
        else if (XamlSourceLinkResolver.TryResolveTypeLocation(
                     analysis,
                     targetTypeReference.FullTypeName,
                     targetTypeReference.AssemblyName,
                     out var sourceLinkDeclaration))
        {
            AddReference(builder, seen, sourceLinkDeclaration.Uri, sourceLinkDeclaration.Range, isDeclaration: true);
        }
        else
        {
            AddReference(
                builder,
                seen,
                XamlMetadataSymbolUri.CreateTypeUri(targetTypeReference.FullTypeName),
                MetadataDeclarationRange,
                isDeclaration: true);
        }

        foreach (var source in EnumerateProjectXamlSources(analysis))
        {
            if (!ContainsAnyToken(source.Text, typeTokenCandidates))
            {
                continue;
            }

            if (!TryEnsureXmlDocumentLoaded(source, out var xmlDocument) || xmlDocument is null)
            {
                continue;
            }

            foreach (var element in xmlDocument.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>())
            {
                var sourcePrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration || !XamlTypeReferenceNavigationResolver.IsTypeReferenceAttribute(attribute))
                    {
                        continue;
                    }

                    if (!XamlTypeReferenceNavigationResolver.TryResolve(
                            analysis,
                            sourcePrefixMap,
                            attribute.Name.LocalName,
                            attribute.Value,
                            out var candidateTypeReference))
                    {
                        continue;
                    }

                    if (!string.Equals(
                            candidateTypeReference.FullTypeName,
                            targetTypeReference.FullTypeName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!TryCreateAttributeValueRange(source.Text, attribute, out var range))
                    {
                        continue;
                    }

                    AddReference(
                        builder,
                        seen,
                        UriPathHelper.ToDocumentUri(source.FilePath),
                        range,
                        isDeclaration: false);
                }
            }
        }

        return builder
            .OrderBy(static item => item.Uri, StringComparer.Ordinal)
            .ThenBy(static item => item.Range.Start.Line)
            .ThenBy(static item => item.Range.Start.Character)
            .ToImmutableArray();
    }

    private static ImmutableArray<XamlReferenceLocation> CollectPropertyReferences(
        XamlAnalysisResult analysis,
        AvaloniaTypeInfo ownerTypeInfo,
        AvaloniaPropertyInfo targetProperty)
    {
        var typeIndex = analysis.TypeIndex;
        if (typeIndex is null)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var propertyTokenCandidates = BuildPropertyReferenceTokenCandidates(targetProperty);

        if (targetProperty.SourceLocation is { } declaration)
        {
            AddReference(builder, seen, declaration.Uri, declaration.Range, isDeclaration: true);
        }
        else if (XamlSourceLinkResolver.TryResolvePropertyLocation(
                     analysis,
                     ownerTypeInfo.FullTypeName,
                     targetProperty.Name,
                     ownerTypeInfo.AssemblyName,
                     out var sourceLinkDeclaration))
        {
            AddReference(builder, seen, sourceLinkDeclaration.Uri, sourceLinkDeclaration.Range, isDeclaration: true);
        }
        else
        {
            var metadataUri = XamlMetadataSymbolUri.CreatePropertyUri(
                ownerTypeInfo.FullTypeName,
                targetProperty.Name,
                targetProperty.TypeName,
                targetProperty.IsAttached,
                targetProperty.IsSettable);
            AddReference(builder, seen, metadataUri, MetadataDeclarationRange, isDeclaration: true);
        }

        foreach (var source in EnumerateProjectXamlSources(analysis))
        {
            if (!ContainsAnyToken(source.Text, propertyTokenCandidates))
            {
                continue;
            }

            if (!TryEnsureXmlDocumentLoaded(source, out var xmlDocument) || xmlDocument is null)
            {
                continue;
            }

            foreach (var element in xmlDocument.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>())
            {
                AvaloniaTypeInfo? elementTypeInfo = null;
                if (string.Equals(element.Name.LocalName, targetProperty.Name, StringComparison.Ordinal) ||
                    ElementMayReferencePropertyByAttribute(element, targetProperty.Name))
                {
                    TryResolveTypeInfoByXmlNamespace(
                        typeIndex,
                        element.Name.NamespaceName,
                        element.Name.LocalName,
                        out elementTypeInfo);
                }

                var sourcePrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);

                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    if (TryAddSetterPropertyValueReference(
                            typeIndex,
                            sourcePrefixMap,
                            source.Text,
                            element,
                            attribute,
                            ownerTypeInfo,
                            targetProperty,
                            out var setterValueRange))
                    {
                        AddReference(
                            builder,
                            seen,
                            UriPathHelper.ToDocumentUri(source.FilePath),
                            setterValueRange,
                            isDeclaration: false);
                    }

                    if (attribute.Value.IndexOf('{') >= 0)
                    {
                        foreach (var bindingRange in XamlBindingNavigationService.FindPropertyReferenceRanges(
                                     analysis,
                                     source.Text,
                                     element,
                                     attribute,
                                     ownerTypeInfo,
                                     targetProperty))
                        {
                            AddReference(
                                builder,
                                seen,
                                UriPathHelper.ToDocumentUri(source.FilePath),
                                bindingRange,
                                isDeclaration: false);
                        }
                    }

                    if (!IsPropertyReferenceMatch(
                            typeIndex,
                            element,
                            elementTypeInfo,
                            attribute,
                            ownerTypeInfo,
                            targetProperty))
                    {
                        continue;
                    }

                    if (!TryCreateAttributeNameRange(source.Text, attribute, out var range))
                    {
                        continue;
                    }

                    AddReference(
                        builder,
                        seen,
                        UriPathHelper.ToDocumentUri(source.FilePath),
                        range,
                        isDeclaration: false);
                }
            }
        }

        return builder
            .OrderBy(static item => item.Uri, StringComparer.Ordinal)
            .ThenBy(static item => item.Range.Start.Line)
            .ThenBy(static item => item.Range.Start.Character)
            .ToImmutableArray();
    }

    private static bool IsPropertyReferenceMatch(
        AvaloniaTypeIndex typeIndex,
        XElement element,
        AvaloniaTypeInfo? elementTypeInfo,
        XAttribute attribute,
        AvaloniaTypeInfo targetOwnerType,
        AvaloniaPropertyInfo targetProperty)
    {
        var attributeName = attribute.Name.LocalName;
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return false;
        }

        var separator = attributeName.IndexOf('.');
        if (separator >= 0)
        {
            var ownerToken = separator > 0 ? attributeName.Substring(0, separator) : string.Empty;
            var propertyToken = separator + 1 < attributeName.Length
                ? attributeName.Substring(separator + 1)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(ownerToken) ||
                !string.Equals(propertyToken, targetProperty.Name, StringComparison.Ordinal))
            {
                return false;
            }

            var ownerNamespace = string.IsNullOrWhiteSpace(attribute.Name.NamespaceName)
                ? element.GetDefaultNamespace().NamespaceName
                : attribute.Name.NamespaceName;
            if (!TryResolveTypeInfoByXmlNamespace(typeIndex, ownerNamespace, ownerToken, out var attributeOwnerType) ||
                attributeOwnerType is null)
            {
                return false;
            }

            var candidateProperty = attributeOwnerType.Properties.FirstOrDefault(property =>
                string.Equals(property.Name, propertyToken, StringComparison.Ordinal));
            if (candidateProperty is null)
            {
                return false;
            }

            return IsSamePropertySymbol(candidateProperty, attributeOwnerType, targetProperty, targetOwnerType);
        }

        if (!string.Equals(attributeName, targetProperty.Name, StringComparison.Ordinal) ||
            elementTypeInfo is null)
        {
            return false;
        }

        var matchedProperty = elementTypeInfo.Properties.FirstOrDefault(property =>
            string.Equals(property.Name, attributeName, StringComparison.Ordinal));
        if (matchedProperty is null)
        {
            return false;
        }

        return IsSamePropertySymbol(matchedProperty, elementTypeInfo, targetProperty, targetOwnerType);
    }

    private static bool TryAddSetterPropertyValueReference(
        AvaloniaTypeIndex typeIndex,
        ImmutableDictionary<string, string> prefixMap,
        string sourceText,
        XElement element,
        XAttribute attribute,
        AvaloniaTypeInfo targetOwnerType,
        AvaloniaPropertyInfo targetProperty,
        out SourceRange range)
    {
        range = default;
        if (!string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal) ||
            !string.Equals(attribute.Name.LocalName, "Property", StringComparison.Ordinal))
        {
            return false;
        }

        var propertyToken = attribute.Value?.Trim();
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        var ownerTypeToken = ResolveSetterOwnerTypeToken(element);
        var ownerTokenForResolution = propertyToken.Contains('.', StringComparison.Ordinal)
            ? element.Name.LocalName
            : ownerTypeToken;
        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                typeIndex,
                prefixMap,
                ownerTokenForResolution,
                propertyToken,
                out var candidateProperty,
                out var candidateOwnerType) ||
            candidateProperty is null ||
            candidateOwnerType is null ||
            !IsSamePropertySymbol(candidateProperty, candidateOwnerType, targetProperty, targetOwnerType))
        {
            return false;
        }

        return TryCreateAttributeValueRange(sourceText, attribute, out range);
    }

    private static string? ResolveSetterOwnerTypeToken(XElement setterElement)
    {
        for (var current = setterElement.Parent; current is not null; current = current.Parent)
        {
            if (string.Equals(current.Name.LocalName, "Style", StringComparison.Ordinal))
            {
                var selector = current.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.NamespaceName.Length == 0 &&
                        string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal))
                    ?.Value;
                return XamlStyleNavigationSemantics.TryExtractTargetTypeTokenFromSelector(selector, out var styleTypeToken)
                    ? styleTypeToken
                    : null;
            }

            if (string.Equals(current.Name.LocalName, "ControlTheme", StringComparison.Ordinal))
            {
                var targetType = current.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.NamespaceName.Length == 0 &&
                        string.Equals(attribute.Name.LocalName, "TargetType", StringComparison.Ordinal))
                    ?.Value;
                return XamlStyleNavigationSemantics.TryNormalizeControlThemeTargetType(targetType, out var controlThemeTypeToken)
                    ? controlThemeTypeToken
                    : null;
            }
        }

        return null;
    }

    private static bool IsSamePropertySymbol(
        AvaloniaPropertyInfo candidateProperty,
        AvaloniaTypeInfo candidateOwnerType,
        AvaloniaPropertyInfo targetProperty,
        AvaloniaTypeInfo targetOwnerType)
    {
        if (candidateProperty.SourceLocation is { } candidateSource &&
            targetProperty.SourceLocation is { } targetSource)
        {
            return candidateSource.Equals(targetSource);
        }

        if (!string.Equals(candidateProperty.Name, targetProperty.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (targetProperty.IsAttached)
        {
            return string.Equals(
                candidateOwnerType.FullTypeName,
                targetOwnerType.FullTypeName,
                StringComparison.Ordinal);
        }

        if (string.Equals(
                candidateOwnerType.FullTypeName,
                targetOwnerType.FullTypeName,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(candidateProperty.TypeName, targetProperty.TypeName, StringComparison.Ordinal);
    }

    private static bool TryAddSelectorTypeReference(
        XamlAnalysisResult analysis,
        AvaloniaTypeInfo targetTypeInfo,
        string sourceText,
        ImmutableDictionary<string, string> prefixMap,
        XElement element,
        XAttribute attribute,
        out SourceRange range)
    {
        range = default;
        if (!string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal) ||
            !string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal))
        {
            return false;
        }

        var validation = XamlToCSharpGenerator.MiniLanguageParsing.Selectors.SelectorSyntaxValidator.Validate(attribute.Value);
        if (!validation.IsValid || validation.Branches.IsEmpty)
        {
            return false;
        }

        foreach (var branch in validation.Branches)
        {
            if (string.IsNullOrWhiteSpace(branch.LastTypeToken) ||
                !XamlClrSymbolResolver.TryResolveTypeInfo(
                    analysis.TypeIndex!,
                    prefixMap,
                    branch.LastTypeToken,
                    out var selectorTypeInfo) ||
                selectorTypeInfo is null ||
                !string.Equals(selectorTypeInfo.FullTypeName, targetTypeInfo.FullTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryCreateAttributeValueTokenRange(
                    sourceText,
                    attribute,
                    branch.LastTypeOffset,
                    branch.LastTypeToken.Length,
                    out range))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAddMarkupExtensionTypeReference(
        XamlAnalysisResult analysis,
        AvaloniaTypeInfo targetTypeInfo,
        string sourceText,
        ImmutableDictionary<string, string> prefixMap,
        XAttribute attribute,
        out SourceRange range)
    {
        range = default;
        var classTokens = XamlMarkupExtensionNavigationSemantics.EnumerateClassTokens(attribute.Value);
        if (classTokens.IsEmpty)
        {
            return false;
        }

        foreach (var classToken in classTokens)
        {
            if (!XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
                    analysis,
                    prefixMap,
                    classToken.Name,
                    out var resolvedTypeReference) ||
                !string.Equals(
                    resolvedTypeReference.FullTypeName,
                    targetTypeInfo.FullTypeName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (TryCreateAttributeValueTokenRange(
                    sourceText,
                    attribute,
                    classToken.Start,
                    classToken.Length,
                    out range))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnyToken(string text, ImmutableArray<string> tokens)
    {
        if (tokens.IsEmpty || string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (!string.IsNullOrWhiteSpace(token) &&
                text.IndexOf(token, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<string> BuildTypeReferenceTokenCandidates(AvaloniaTypeInfo typeInfo)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddTokenCandidate(builder, seen, typeInfo.XmlTypeName);
        AddTokenCandidate(builder, seen, ExtractSimpleTypeName(typeInfo.FullTypeName));

        var simpleTypeName = ExtractSimpleTypeName(typeInfo.FullTypeName);
        if (simpleTypeName.EndsWith("Extension", StringComparison.Ordinal))
        {
            AddTokenCandidate(
                builder,
                seen,
                simpleTypeName.Substring(0, simpleTypeName.Length - "Extension".Length));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> BuildTypeReferenceTokenCandidates(XamlResolvedTypeReference typeReference)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddTokenCandidate(builder, seen, typeReference.FullTypeName);
        AddTokenCandidate(builder, seen, ExtractSimpleTypeName(typeReference.FullTypeName));
        return builder.ToImmutable();
    }

    private static ImmutableArray<string> BuildPropertyReferenceTokenCandidates(AvaloniaPropertyInfo propertyInfo)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddTokenCandidate(builder, seen, propertyInfo.Name);
        AddTokenCandidate(builder, seen, "." + propertyInfo.Name);
        return builder.ToImmutable();
    }

    private static void AddTokenCandidate(
        ImmutableArray<string>.Builder builder,
        HashSet<string> seen,
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (seen.Add(token))
        {
            builder.Add(token);
        }
    }

    private static string ExtractSimpleTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        var text = typeName.Trim();
        var genericSeparator = text.IndexOf('<');
        if (genericSeparator > 0)
        {
            text = text.Substring(0, genericSeparator);
        }

        var namespaceSeparator = text.LastIndexOf('.');
        if (namespaceSeparator >= 0 && namespaceSeparator + 1 < text.Length)
        {
            text = text.Substring(namespaceSeparator + 1);
        }

        var nestedSeparator = text.LastIndexOf('+');
        if (nestedSeparator >= 0 && nestedSeparator + 1 < text.Length)
        {
            text = text.Substring(nestedSeparator + 1);
        }

        return text;
    }

    private static bool ElementMayReferencePropertyByAttribute(XElement element, string propertyName)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var attributeName = attribute.Name.LocalName;
            if (string.Equals(attributeName, propertyName, StringComparison.Ordinal))
            {
                return true;
            }

            var separator = attributeName.LastIndexOf('.');
            if (separator >= 0 &&
                separator + 1 < attributeName.Length &&
                string.Equals(attributeName.Substring(separator + 1), propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<XamlProjectSourceFile> EnumerateProjectXamlSources(XamlAnalysisResult analysis)
    {
        var currentFilePath = NormalizePath(analysis.Document.FilePath);
        var seen = new HashSet<string>(PathComparer) { currentFilePath };
        yield return new XamlProjectSourceFile(
            currentFilePath,
            analysis.Document.Text,
            analysis.XmlDocument,
            XmlParsed: true);

        foreach (var candidatePath in DiscoverProjectXamlFilePaths(analysis.ProjectPath, currentFilePath))
        {
            if (!seen.Add(candidatePath))
            {
                continue;
            }

            if (!TryLoadCachedSourceFile(candidatePath, out var sourceFile))
            {
                continue;
            }

            yield return sourceFile;
        }
    }

    private static ImmutableArray<string> DiscoverProjectXamlFilePaths(string? projectPath, string currentFilePath)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(PathComparer);

        AddCandidatePath(builder, seen, currentFilePath);
        var resolvedProjectPath = ResolveProjectPath(projectPath, currentFilePath);
        if (resolvedProjectPath is null)
        {
            return builder.ToImmutable();
        }

        foreach (var includePath in GetCachedProjectXamlFileList(resolvedProjectPath))
        {
            AddCandidatePath(builder, seen, includePath);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> GetCachedProjectXamlFileList(string projectFilePath)
    {
        var normalizedProjectPath = NormalizePath(projectFilePath);
        var now = DateTimeOffset.UtcNow;
        if (ProjectFileListCache.TryGetValue(normalizedProjectPath, out var cached) &&
            now - cached.CachedAtUtc <= ProjectDiscoveryCacheTtl)
        {
            return cached.Paths;
        }

        var paths = BuildProjectXamlFileList(normalizedProjectPath);
        ProjectFileListCache[normalizedProjectPath] = new CachedProjectFileList(now, paths);
        return paths;
    }

    private static ImmutableArray<string> BuildProjectXamlFileList(string projectFilePath)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(PathComparer);
        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return builder.ToImmutable();
        }

        foreach (var filePath in EnumerateXamlFilesUnder(projectDirectory))
        {
            AddCandidatePath(builder, seen, filePath);
        }

        foreach (var includePath in EnumerateExplicitXamlIncludes(projectFilePath, projectDirectory))
        {
            AddCandidatePath(builder, seen, includePath);
        }

        return builder.ToImmutable();
    }

    private static bool TryLoadCachedSourceFile(string filePath, out XamlProjectSourceFile sourceFile)
    {
        sourceFile = default;
        var normalizedPath = NormalizePath(filePath);
        var now = DateTimeOffset.UtcNow;

        if (SourceFileCache.TryGetValue(normalizedPath, out var cachedEntry) &&
            now - cachedEntry.ValidatedAtUtc <= SourceValidationCacheTtl)
        {
            sourceFile = new XamlProjectSourceFile(
                normalizedPath,
                cachedEntry.Text,
                cachedEntry.XmlDocument,
                cachedEntry.XmlParsed);
            return true;
        }

        long lastWriteTicks;
        long fileLength;
        try
        {
            var info = new FileInfo(normalizedPath);
            if (!info.Exists)
            {
                return false;
            }

            lastWriteTicks = info.LastWriteTimeUtc.Ticks;
            fileLength = info.Length;
        }
        catch
        {
            return false;
        }

        if (SourceFileCache.TryGetValue(normalizedPath, out var cached) &&
            cached.LastWriteUtcTicks == lastWriteTicks &&
            cached.Length == fileLength)
        {
            SourceFileCache[normalizedPath] = cached with
            {
                ValidatedAtUtc = now
            };
            sourceFile = new XamlProjectSourceFile(
                normalizedPath,
                cached.Text,
                cached.XmlDocument,
                cached.XmlParsed);
            return true;
        }

        string text;
        try
        {
            text = File.ReadAllText(normalizedPath);
        }
        catch
        {
            return false;
        }

        SourceFileCache[normalizedPath] = new CachedXamlSourceFile(
            lastWriteTicks,
            fileLength,
            now,
            text,
            XmlDocument: null,
            XmlParsed: false);
        sourceFile = new XamlProjectSourceFile(
            normalizedPath,
            text,
            XmlDocument: null,
            XmlParsed: false);
        return true;
    }

    private static bool TryEnsureXmlDocumentLoaded(
        XamlProjectSourceFile sourceFile,
        out XDocument? xmlDocument)
    {
        if (sourceFile.XmlDocument is not null)
        {
            xmlDocument = sourceFile.XmlDocument;
            return true;
        }

        if (sourceFile.XmlParsed)
        {
            xmlDocument = null;
            return false;
        }

        xmlDocument = TryParseXml(sourceFile.Text, out var parsedDocument)
            ? parsedDocument
            : null;

        if (SourceFileCache.TryGetValue(sourceFile.FilePath, out var cached) &&
            string.Equals(cached.Text, sourceFile.Text, StringComparison.Ordinal))
        {
            SourceFileCache[sourceFile.FilePath] = cached with
            {
                ValidatedAtUtc = DateTimeOffset.UtcNow,
                XmlDocument = xmlDocument,
                XmlParsed = true
            };
        }

        return xmlDocument is not null;
    }

    private static IEnumerable<string> EnumerateXamlFilesUnder(string rootDirectory)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        using var enumerator = files.GetEnumerator();
        while (true)
        {
            string filePath;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                filePath = enumerator.Current;
            }
            catch
            {
                // Ignore inaccessible directories/files while traversing.
                yield break;
            }

            if (!IsXamlFile(filePath) || IsUnderBuildOutputDirectory(filePath))
            {
                continue;
            }

            yield return filePath;
        }
    }

    private static IEnumerable<string> EnumerateExplicitXamlIncludes(string projectFilePath, string projectDirectory)
    {
        XDocument projectDocument;
        try
        {
            projectDocument = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        foreach (var itemElement in projectDocument.Descendants())
        {
            if (!IsXamlItemElement(itemElement.Name.LocalName))
            {
                continue;
            }

            var includeValue = itemElement.Attribute("Include")?.Value
                ?? itemElement.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(includeValue))
            {
                continue;
            }

            foreach (var includePath in ExpandProjectIncludePattern(projectDirectory, includeValue))
            {
                yield return includePath;
            }
        }
    }

    private static IEnumerable<string> ExpandProjectIncludePattern(string projectDirectory, string includeValue)
    {
        var normalizedPattern = includeValue
            .Trim()
            .Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            yield break;
        }

        var hasWildcard = normalizedPattern.IndexOfAny(['*', '?']) >= 0;
        if (!hasWildcard)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(projectDirectory, normalizedPattern));
            if (IsXamlFile(candidatePath) && File.Exists(candidatePath))
            {
                yield return candidatePath;
            }

            yield break;
        }

        var searchRoot = ResolveSearchRoot(projectDirectory, normalizedPattern);
        if (searchRoot is null || !Directory.Exists(searchRoot))
        {
            yield break;
        }

        var patternRegex = BuildGlobRegex(normalizedPattern);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(searchRoot, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        using var enumerator = files.GetEnumerator();
        while (true)
        {
            string filePath;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                filePath = enumerator.Current;
            }
            catch
            {
                // Ignore inaccessible directories/files while traversing.
                yield break;
            }

            if (!IsXamlFile(filePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(projectDirectory, filePath).Replace('\\', '/');
            if (patternRegex.IsMatch(relativePath))
            {
                yield return Path.GetFullPath(filePath);
            }
        }
    }

    private static string? ResolveSearchRoot(string projectDirectory, string includePattern)
    {
        var wildcardIndex = includePattern.IndexOfAny(['*', '?']);
        var basePrefix = wildcardIndex <= 0 ? string.Empty : includePattern.Substring(0, wildcardIndex);
        if (string.IsNullOrWhiteSpace(basePrefix))
        {
            return projectDirectory;
        }

        var normalizedBase = basePrefix.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedBase))
        {
            return Path.GetDirectoryName(normalizedBase);
        }

        var combined = Path.GetFullPath(Path.Combine(projectDirectory, normalizedBase));
        if (Directory.Exists(combined))
        {
            return combined;
        }

        return Path.GetDirectoryName(combined);
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern.Replace('\\', '/'))
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", @"[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);

        return new Regex("^" + escaped + "$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string? ResolveProjectPath(string? projectPath, string currentFilePath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var normalizedProjectPath = NormalizePath(projectPath);
            if (File.Exists(normalizedProjectPath))
            {
                return normalizedProjectPath;
            }

            if (Directory.Exists(normalizedProjectPath))
            {
                try
                {
                    var directoryProject = Directory
                        .EnumerateFiles(normalizedProjectPath, "*.csproj", SearchOption.TopDirectoryOnly)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (directoryProject is not null)
                    {
                        return NormalizePath(directoryProject);
                    }
                }
                catch
                {
                    // Ignore inaccessible workspace roots.
                }
            }
        }

        var currentDirectory = Path.GetDirectoryName(currentFilePath);
        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            try
            {
                var projectFile = Directory
                    .EnumerateFiles(currentDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (projectFile is not null)
                {
                    return NormalizePath(projectFile);
                }
            }
            catch
            {
                // Ignore inaccessible parent directories and continue searching upward.
            }

            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }

        return null;
    }

    private static bool TryParseXml(string text, out XDocument document)
    {
        try
        {
            document = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            return true;
        }
        catch
        {
            document = null!;
            return false;
        }
    }

    private static bool TryCreateAttributeValueTokenRange(
        string text,
        XAttribute attribute,
        int tokenOffsetInValue,
        int tokenLength,
        out SourceRange range)
    {
        range = default;
        if (!TryCreateAttributeValueRange(text, attribute, out var valueRange) ||
            tokenOffsetInValue < 0 ||
            tokenLength <= 0)
        {
            return false;
        }

        var valueStartOffset = TextCoordinateHelper.GetOffset(text, valueRange.Start);
        if (valueStartOffset < 0)
        {
            return false;
        }

        var tokenStartOffset = valueStartOffset + tokenOffsetInValue;
        var tokenEndOffset = tokenStartOffset + tokenLength;
        if (tokenStartOffset < 0 || tokenEndOffset > text.Length)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, tokenStartOffset),
            TextCoordinateHelper.GetPosition(text, tokenEndOffset));
        return true;
    }

    private static void AddReference(
        ImmutableArray<XamlReferenceLocation>.Builder builder,
        HashSet<string> seen,
        string uri,
        SourceRange range,
        bool isDeclaration)
    {
        var identity = CreateReferenceIdentity(uri, range);
        if (!seen.Add(identity))
        {
            return;
        }

        builder.Add(new XamlReferenceLocation(uri, range, isDeclaration));
    }

    private static string CreateReferenceIdentity(string uri, SourceRange range)
    {
        return uri + "|" +
               range.Start.Line + ":" + range.Start.Character + "-" +
               range.End.Line + ":" + range.End.Character;
    }

    private static bool TryCreateElementNameRange(string text, XElement element, out SourceRange range)
    {
        range = default;
        if (element is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var startPosition = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var offset = TextCoordinateHelper.GetOffset(text, startPosition);
        if (offset < 0 || offset >= text.Length)
        {
            return false;
        }

        var openTagIndex = text.LastIndexOf('<', Math.Min(offset, text.Length - 1));
        if (openTagIndex < 0)
        {
            openTagIndex = text.IndexOf('<', offset);
        }
        if (openTagIndex < 0)
        {
            return false;
        }

        var nameStart = openTagIndex + 1;
        if (nameStart < text.Length && text[nameStart] == '/')
        {
            nameStart++;
        }

        while (nameStart < text.Length && char.IsWhiteSpace(text[nameStart]))
        {
            nameStart++;
        }

        var length = ReadXmlNameLength(text, nameStart);
        if (length <= 0)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, nameStart),
            TextCoordinateHelper.GetPosition(text, nameStart + length));
        return true;
    }

    private static bool TryCreateAttributeNameRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var startPosition = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var offset = TextCoordinateHelper.GetOffset(text, startPosition);
        if (offset < 0 || offset >= text.Length)
        {
            return false;
        }

        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }

        var length = ReadXmlNameLength(text, offset);
        if (length <= 0)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, offset),
            TextCoordinateHelper.GetPosition(text, offset + length));
        return true;
    }

    private static bool TryCreateAttributeValueRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var startPosition = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var offset = TextCoordinateHelper.GetOffset(text, startPosition);
        if (offset < 0 || offset >= text.Length)
        {
            return false;
        }

        var equalsIndex = text.IndexOf('=', offset);
        if (equalsIndex < 0)
        {
            return false;
        }

        var quoteIndex = equalsIndex + 1;
        while (quoteIndex < text.Length && char.IsWhiteSpace(text[quoteIndex]))
        {
            quoteIndex++;
        }

        if (quoteIndex >= text.Length || (text[quoteIndex] != '"' && text[quoteIndex] != '\''))
        {
            return false;
        }

        var quote = text[quoteIndex];
        var valueStart = quoteIndex + 1;
        var valueEnd = text.IndexOf(quote, valueStart);
        if (valueEnd < valueStart)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, valueStart),
            TextCoordinateHelper.GetPosition(text, valueEnd));
        return true;
    }

    private static int ReadXmlNameLength(string text, int start)
    {
        if (start < 0 || start >= text.Length)
        {
            return 0;
        }

        var index = start;
        while (index < text.Length && IsXmlNameCharacter(text[index]))
        {
            index++;
        }

        return index - start;
    }

    private static bool IsXmlNameCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or ':' or '.' or '-';
    }

    private static void AddCandidatePath(
        ImmutableArray<string>.Builder builder,
        HashSet<string> seen,
        string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return;
        }

        var normalizedPath = NormalizePath(candidatePath);
        if (!File.Exists(normalizedPath) || !seen.Add(normalizedPath))
        {
            return;
        }

        builder.Add(normalizedPath);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static bool IsXamlFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderBuildOutputDirectory(string path)
    {
        var normalized = NormalizePath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        var separator = Path.DirectorySeparatorChar;
        return normalized.Contains(separator + "obj" + separator, StringComparison.Ordinal) ||
               normalized.Contains(separator + "bin" + separator, StringComparison.Ordinal);
    }

    private static bool IsXamlItemElement(string localName)
    {
        return string.Equals(localName, "AvaloniaXaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "Page", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "None", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "Content", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "EmbeddedResource", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "AdditionalFiles", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveTypeInfoByXmlNamespace(
        AvaloniaTypeIndex typeIndex,
        string xmlNamespace,
        string xmlTypeName,
        out AvaloniaTypeInfo? typeInfo)
    {
        if (typeIndex.TryGetType(xmlNamespace, xmlTypeName, out typeInfo) && typeInfo is not null)
        {
            return true;
        }

        if (XamlClrSymbolResolver.TryResolveClrNamespace(xmlNamespace, out var clrNamespace))
        {
            return typeIndex.TryGetTypeByClrNamespace(clrNamespace, xmlTypeName, out typeInfo) && typeInfo is not null;
        }

        typeInfo = null;
        return false;
    }

    private static bool HasNamedElementDeclaration(XamlAnalysisResult analysis, string identifier)
    {
        foreach (var namedElement in analysis.ParsedDocument!.NamedElements)
        {
            if (string.Equals(namedElement.Name, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasResourceDeclaration(XamlAnalysisResult analysis, string identifier)
    {
        foreach (var resource in analysis.ParsedDocument!.Resources)
        {
            if (string.Equals(resource.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var template in analysis.ParsedDocument.Templates)
        {
            if (string.Equals(template.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var style in analysis.ParsedDocument.Styles)
        {
            if (string.Equals(style.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            if (string.Equals(controlTheme.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int AddNamedElementDeclarations(
        XamlAnalysisResult analysis,
        string identifier,
        ImmutableArray<XamlReferenceLocation>.Builder builder)
    {
        var added = 0;
        foreach (var namedElement in analysis.ParsedDocument!.NamedElements)
        {
            if (!string.Equals(namedElement.Name, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var range = CreateRange(namedElement.Line, namedElement.Column, identifier.Length);
            builder.Add(new XamlReferenceLocation(
                UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                range,
                IsDeclaration: true));
            added++;
        }

        return added;
    }

    private static int AddResourceDeclarations(
        XamlAnalysisResult analysis,
        string identifier,
        ImmutableArray<XamlReferenceLocation>.Builder builder)
    {
        var added = 0;

        foreach (var resource in analysis.ParsedDocument!.Resources)
        {
            if (TryAddDeclaration(resource.Key, resource.Line, resource.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        foreach (var template in analysis.ParsedDocument.Templates)
        {
            if (TryAddDeclaration(template.Key, template.Line, template.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        foreach (var style in analysis.ParsedDocument.Styles)
        {
            if (TryAddDeclaration(style.Key, style.Line, style.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            if (TryAddDeclaration(controlTheme.Key, controlTheme.Line, controlTheme.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        return added;
    }

    private static bool TryAddDeclaration(
        string? key,
        int line,
        int column,
        string identifier,
        string filePath,
        ImmutableArray<XamlReferenceLocation>.Builder builder)
    {
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, identifier, StringComparison.Ordinal))
        {
            return false;
        }

        var range = CreateRange(line, column, key.Length);
        builder.Add(new XamlReferenceLocation(
            UriPathHelper.ToDocumentUri(filePath),
            range,
            IsDeclaration: true));
        return true;
    }

    private static SourceRange CreateRange(int line, int column, int length)
    {
        var start = new SourcePosition(
            Math.Max(0, line - 1),
            Math.Max(0, column - 1));
        var end = new SourcePosition(start.Line, start.Character + Math.Max(1, length));
        return new SourceRange(start, end);
    }

    private static string GetLocalName(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return string.Empty;
        }

        var separator = qualifiedName.IndexOf(':');
        return separator >= 0 && separator + 1 < qualifiedName.Length
            ? qualifiedName.Substring(separator + 1)
            : qualifiedName;
    }

    private readonly record struct XamlProjectSourceFile(
        string FilePath,
        string Text,
        XDocument? XmlDocument,
        bool XmlParsed);

    private readonly record struct CachedProjectFileList(
        DateTimeOffset CachedAtUtc,
        ImmutableArray<string> Paths);

    private readonly record struct CachedXamlSourceFile(
        long LastWriteUtcTicks,
        long Length,
        DateTimeOffset ValidatedAtUtc,
        string Text,
        XDocument? XmlDocument,
        bool XmlParsed);
}
