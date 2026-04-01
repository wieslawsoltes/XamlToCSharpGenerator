using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

public sealed class XamlDefinitionService
{
    private static readonly XamlReferenceService ReferenceService = new();

    public ImmutableArray<XamlDefinitionLocation> GetDefinitions(XamlAnalysisResult analysis, SourcePosition position)
    {
        if (XamlUriValueNavigationService.TryResolveDefinitionAtOffset(analysis, position, out var uriValueDefinition))
        {
            return [uriValueDefinition];
        }

        if (XamlXmlNamespaceNavigationService.TryResolvePrefixDefinitionAtPosition(analysis, position, out var xmlnsDefinition))
        {
            return [xmlnsDefinition];
        }

        if (XamlInlineCSharpNavigationService.TryResolveNavigationTarget(analysis, position, out var inlineCodeTarget))
        {
            if (inlineCodeTarget.DeclarationRange is { } inlineDeclarationRange)
            {
                return
                [
                    new XamlDefinitionLocation(
                        UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                        inlineDeclarationRange)
                ];
            }

            if (inlineCodeTarget.DefinitionLocation is { } inlineDefinitionLocation)
            {
                return
                [
                    new XamlDefinitionLocation(
                        inlineDefinitionLocation.Uri,
                        inlineDefinitionLocation.Range)
                ];
            }

            return [CreateSymbolDefinitionLocation(analysis, inlineCodeTarget.Symbol)];
        }

        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var identifier = XamlResourceReferenceNavigationSemantics.TryResolveResourceIdentifierAtOffset(
            analysis.Document.Text,
            offset,
            out var resourceIdentifier)
            ? resourceIdentifier
            : XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
        if (!string.IsNullOrWhiteSpace(identifier) && analysis.ParsedDocument is not null)
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

                if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.NamedElement)
                {
                    return CollectNamedElementDefinitions(analysis, identifier);
                }

                if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.ResourceKey)
                {
                    return CollectResourceDefinitions(analysis, identifier);
                }
            }
        }

        return CollectTypeOrPropertyDefinitions(analysis, position, identifier);
    }

    private static ImmutableArray<XamlDefinitionLocation> CollectNamedElementDefinitions(
        XamlAnalysisResult analysis,
        string identifier)
    {
        var builder = ImmutableArray.CreateBuilder<XamlDefinitionLocation>();
        foreach (var namedElement in analysis.ParsedDocument!.NamedElements)
        {
            if (!string.Equals(namedElement.Name, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var start = new SourcePosition(
                Math.Max(0, namedElement.Line - 1),
                Math.Max(0, namedElement.Column - 1));
            var end = new SourcePosition(start.Line, start.Character + Math.Max(1, namedElement.Name.Length));

            builder.Add(new XamlDefinitionLocation(
                UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                new SourceRange(start, end)));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlDefinitionLocation> CollectResourceDefinitions(
        XamlAnalysisResult analysis,
        string identifier)
    {
        var builder = ImmutableArray.CreateBuilder<XamlDefinitionLocation>();
        foreach (var range in XamlResourceDeclarationRangeService.FindDeclarationRanges(analysis, identifier))
        {
            builder.Add(new XamlDefinitionLocation(
                UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                range));
        }

        return builder.ToImmutable();
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

    private static ImmutableArray<XamlDefinitionLocation> CollectTypeOrPropertyDefinitions(
        XamlAnalysisResult analysis,
        SourcePosition position,
        string identifier)
    {
        if (analysis.TypeIndex is null)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        if (XamlExpressionBindingNavigationService.TryResolveNavigationTarget(analysis, position, out var expressionTarget))
        {
            return [CreateSymbolDefinitionLocation(analysis, expressionTarget.Symbol)];
        }

        if (XamlSelectorNavigationService.TryResolveTargetAtOffset(analysis, position, out var selectorTarget))
        {
            switch (selectorTarget.Kind)
            {
                case XamlSelectorNavigationTargetKind.Type:
                    if (XamlClrSymbolResolver.TryResolveTypeInfo(
                            analysis.TypeIndex,
                            analysis.PrefixMap,
                            selectorTarget.Name,
                            out var selectorNavigationTypeInfo) &&
                        selectorNavigationTypeInfo is not null)
                    {
                        return [CreateTypeDefinitionLocation(analysis, selectorNavigationTypeInfo)];
                    }

                    break;
                case XamlSelectorNavigationTargetKind.StyleClass:
                case XamlSelectorNavigationTargetKind.PseudoClass:
                    return CollectDefinitionsFromReferences(ReferenceService.GetReferences(analysis, position));
                case XamlSelectorNavigationTargetKind.NamedElement:
                    return CollectNamedElementDefinitions(analysis, selectorTarget.Name);
            }
        }

        if (XamlBindingNavigationService.TryResolveNavigationTarget(analysis, position, out var bindingTarget))
        {
            if (bindingTarget.Kind == XamlBindingNavigationTargetKind.Property &&
                bindingTarget.OwnerTypeInfo is not null &&
                bindingTarget.PropertyInfo is not null &&
                TryResolvePropertyDefinition(
                    analysis,
                    bindingTarget.OwnerTypeInfo,
                    bindingTarget.PropertyInfo,
                    out var bindingPropertyDefinition))
            {
                return [bindingPropertyDefinition];
            }

            if (bindingTarget.Kind == XamlBindingNavigationTargetKind.Type &&
                bindingTarget.TypeReference is { } bindingTypeReference)
            {
                return [CreateTypeDefinitionLocation(analysis, bindingTypeReference)];
            }

            if ((bindingTarget.Kind == XamlBindingNavigationTargetKind.Property ||
                 bindingTarget.Kind == XamlBindingNavigationTargetKind.Symbol) &&
                bindingTarget.Symbol is not null)
            {
                return [CreateSymbolDefinitionLocation(analysis, bindingTarget.Symbol)];
            }
        }

        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        var prefixMap = XamlNavigationPrefixMapResolver.Resolve(analysis, position);
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

                if (TryResolvePropertyDefinition(
                        analysis,
                        prefixMap,
                        ownerTypeToken,
                        setterPropertyToken,
                        out var setterPropertyDefinition))
                {
                    return [setterPropertyDefinition];
                }
            }
        }

        if (context.Kind == XamlCompletionContextKind.AttributeValue &&
            XamlStyleNavigationSemantics.IsSelectorAttribute(context.CurrentAttributeName) &&
            XamlStyleNavigationSemantics.TryResolveSelectorTypeToken(
                string.IsNullOrWhiteSpace(token) ? context.CurrentAttributeValue : token,
                identifier,
                out var selectorTypeToken) &&
            XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex, prefixMap, selectorTypeToken, out var selectorTypeInfo) &&
            selectorTypeInfo is not null)
        {
            return [CreateTypeDefinitionLocation(analysis, selectorTypeInfo)];
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
            return [CreateTypeDefinitionLocation(analysis, markupTypeReference)];
        }

        if (context.Kind is XamlCompletionContextKind.AttributeValue or XamlCompletionContextKind.MarkupExtension &&
            XamlTypeReferenceNavigationResolver.TryResolve(
                analysis,
                prefixMap,
                localAttributeName,
                string.IsNullOrWhiteSpace(token) ? context.CurrentAttributeValue : token,
                out var resolvedTypeReference))
        {
            return [CreateTypeDefinitionLocation(analysis, resolvedTypeReference)];
        }

        if (context.Kind == XamlCompletionContextKind.ElementName &&
            XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex, prefixMap, token, out var typeInfo) &&
            typeInfo is not null)
        {
            return [CreateTypeDefinitionLocation(analysis, typeInfo)];
        }

        if (context.Kind == XamlCompletionContextKind.QualifiedPropertyElement)
        {
            if (TryResolveQualifiedPropertyElementOwnerDefinition(
                    analysis,
                    position,
                    context,
                    prefixMap,
                    out var ownerDefinition))
            {
                return [ownerDefinition];
            }

            if (TryResolvePropertyDefinition(
                    analysis,
                    prefixMap,
                    ownerTypeToken: null,
                    token,
                    out var propertyElementDefinition))
            {
                return [propertyElementDefinition];
            }

            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        if (context.Kind != XamlCompletionContextKind.AttributeName)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var propertyToken = string.IsNullOrWhiteSpace(token)
            ? context.CurrentAttributeName
            : token;
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        if (TryResolvePropertyDefinition(
                analysis,
                prefixMap,
                context.CurrentElementName,
                propertyToken,
                out var propertyDefinition))
        {
            return [propertyDefinition];
        }

        return ImmutableArray<XamlDefinitionLocation>.Empty;
    }

    private static ImmutableArray<XamlDefinitionLocation> CollectDefinitionsFromReferences(
        ImmutableArray<XamlReferenceLocation> references)
    {
        if (references.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlDefinitionLocation>();
        foreach (var reference in references)
        {
            if (!reference.IsDeclaration)
            {
                continue;
            }

            builder.Add(new XamlDefinitionLocation(reference.Uri, reference.Range));
        }

        return builder.ToImmutable();
    }

    private static bool TryResolvePropertyDefinition(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string? ownerTypeToken,
        string propertyToken,
        out XamlDefinitionLocation definitionLocation)
    {
        definitionLocation = default!;
        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex!,
                prefixMap,
                ownerTypeToken,
                propertyToken,
                out var propertyInfo,
                out _) ||
            propertyInfo is null)
        {
            return false;
        }

        return TryResolvePropertyDefinition(analysis, ownerTypeInfo: null, propertyInfo, out definitionLocation, resolvedOwnerTypeToken: propertyToken.Contains('.', StringComparison.Ordinal)
            ? propertyToken[..propertyToken.IndexOf('.', StringComparison.Ordinal)]
            : ownerTypeToken ?? string.Empty, prefixMap);
    }

    private static bool TryResolveQualifiedPropertyElementOwnerDefinition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        XamlCompletionContext context,
        ImmutableDictionary<string, string> prefixMap,
        out XamlDefinitionLocation definitionLocation)
    {
        definitionLocation = default!;
        if (string.IsNullOrWhiteSpace(context.Token))
        {
            return false;
        }

        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position) - context.TokenStartOffset;
        if (!XamlPropertyElementSemantics.IsOwnerSegmentOffset(context.Token, offset) ||
            !XamlPropertyElementSemantics.TrySplitOwnerQualifiedPropertyFragment(context.Token, out var ownerToken, out _) ||
            !XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex!, prefixMap, ownerToken, out var ownerTypeInfo) ||
            ownerTypeInfo is null)
        {
            return false;
        }

        definitionLocation = CreateTypeDefinitionLocation(analysis, ownerTypeInfo);
        return true;
    }

    private static bool TryResolvePropertyDefinition(
        XamlAnalysisResult analysis,
        XamlToCSharpGenerator.LanguageService.Symbols.AvaloniaTypeInfo ownerTypeInfo,
        XamlToCSharpGenerator.LanguageService.Symbols.AvaloniaPropertyInfo propertyInfo,
        out XamlDefinitionLocation definitionLocation)
    {
        return TryResolvePropertyDefinition(
            analysis,
            ownerTypeInfo,
            propertyInfo,
            out definitionLocation,
            resolvedOwnerTypeToken: ownerTypeInfo.FullTypeName,
            prefixMap: analysis.PrefixMap);
    }

    private static bool TryResolvePropertyDefinition(
        XamlAnalysisResult analysis,
        XamlToCSharpGenerator.LanguageService.Symbols.AvaloniaTypeInfo? ownerTypeInfo,
        XamlToCSharpGenerator.LanguageService.Symbols.AvaloniaPropertyInfo propertyInfo,
        out XamlDefinitionLocation definitionLocation,
        string resolvedOwnerTypeToken,
        ImmutableDictionary<string, string> prefixMap)
    {
        definitionLocation = default!;

        if (propertyInfo.SourceLocation is { } propertyLocation)
        {
            definitionLocation = new XamlDefinitionLocation(propertyLocation.Uri, propertyLocation.Range);
            return true;
        }

        var ownerType = ownerTypeInfo;
        if (ownerType is null &&
            (!XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex!, prefixMap, resolvedOwnerTypeToken, out ownerType) ||
             ownerType is null))
        {
            return false;
        }

        if (XamlSourceLinkResolver.TryResolvePropertyLocation(
                analysis,
                ownerType.FullTypeName,
                propertyInfo.Name,
                ownerType.AssemblyName,
                out var propertySourceLink))
        {
            definitionLocation = new XamlDefinitionLocation(propertySourceLink.Uri, propertySourceLink.Range);
            return true;
        }

        if (XamlMetadataAsSourceService.TryCreatePropertyLocation(
                analysis,
                ownerType.FullTypeName,
                propertyInfo.Name,
                ownerType.AssemblyName,
                out var metadataDocumentLocation))
        {
            definitionLocation = new XamlDefinitionLocation(metadataDocumentLocation.Uri, metadataDocumentLocation.Range);
            return true;
        }

        var metadataUri = XamlMetadataSymbolUri.CreatePropertyUri(
            ownerType.FullTypeName,
            propertyInfo.Name,
            propertyInfo.TypeName,
            propertyInfo.IsAttached,
            propertyInfo.IsSettable);
        definitionLocation = new XamlDefinitionLocation(metadataUri, XamlClrNavigationLocationResolver.MetadataNavigationRange);
        return true;
    }

    private static XamlDefinitionLocation CreateTypeDefinitionLocation(
        XamlAnalysisResult analysis,
        XamlResolvedTypeReference typeReference)
    {
        var location = XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, typeReference);
        return new XamlDefinitionLocation(location.Uri, location.Range);
    }

    private static XamlDefinitionLocation CreateTypeDefinitionLocation(
        XamlAnalysisResult analysis,
        XamlToCSharpGenerator.LanguageService.Symbols.AvaloniaTypeInfo typeInfo)
    {
        var location = XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, typeInfo);
        return new XamlDefinitionLocation(location.Uri, location.Range);
    }

    private static XamlDefinitionLocation CreateSymbolDefinitionLocation(
        XamlAnalysisResult analysis,
        Microsoft.CodeAnalysis.ISymbol symbol)
    {
        var location = XamlClrNavigationLocationResolver.ResolveSymbolLocation(analysis, symbol);
        return new XamlDefinitionLocation(location.Uri, location.Range);
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
}
