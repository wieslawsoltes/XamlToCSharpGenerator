using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

public sealed class XamlDefinitionService
{
    private static readonly SourceRange MetadataDefinitionRange = new(
        new SourcePosition(0, 0),
        new SourcePosition(0, 1));

    public ImmutableArray<XamlDefinitionLocation> GetDefinitions(XamlAnalysisResult analysis, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var identifier = XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
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

        foreach (var resource in analysis.ParsedDocument!.Resources)
        {
            TryAddDefinition(resource.Key, resource.Line, resource.Column, identifier, analysis.Document.FilePath, builder);
        }

        foreach (var template in analysis.ParsedDocument.Templates)
        {
            TryAddDefinition(template.Key, template.Line, template.Column, identifier, analysis.Document.FilePath, builder);
        }

        foreach (var style in analysis.ParsedDocument.Styles)
        {
            TryAddDefinition(style.Key, style.Line, style.Column, identifier, analysis.Document.FilePath, builder);
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            TryAddDefinition(controlTheme.Key, controlTheme.Line, controlTheme.Column, identifier, analysis.Document.FilePath, builder);
        }

        return builder.ToImmutable();
    }

    private static bool TryAddDefinition(
        string? key,
        int line,
        int column,
        string identifier,
        string filePath,
        ImmutableArray<XamlDefinitionLocation>.Builder builder)
    {
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, identifier, StringComparison.Ordinal))
        {
            return false;
        }

        var start = new SourcePosition(
            Math.Max(0, line - 1),
            Math.Max(0, column - 1));
        var end = new SourcePosition(start.Line, start.Character + Math.Max(1, key.Length));
        builder.Add(new XamlDefinitionLocation(
            UriPathHelper.ToDocumentUri(filePath),
            new SourceRange(start, end)));
        return true;
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

        if (propertyInfo.SourceLocation is { } propertyLocation)
        {
            definitionLocation = new XamlDefinitionLocation(propertyLocation.Uri, propertyLocation.Range);
            return true;
        }

        var resolvedOwnerTypeToken = propertyToken.Contains('.', StringComparison.Ordinal)
            ? propertyToken[..propertyToken.IndexOf('.', StringComparison.Ordinal)]
            : ownerTypeToken ?? string.Empty;
        if (!XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex!, prefixMap, resolvedOwnerTypeToken, out var ownerType) ||
            ownerType is null)
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

        var metadataUri = XamlMetadataSymbolUri.CreatePropertyUri(
            ownerType.FullTypeName,
            propertyInfo.Name,
            propertyInfo.TypeName,
            propertyInfo.IsAttached,
            propertyInfo.IsSettable);
        definitionLocation = new XamlDefinitionLocation(metadataUri, MetadataDefinitionRange);
        return true;
    }

    private static XamlDefinitionLocation CreateTypeDefinitionLocation(
        XamlAnalysisResult analysis,
        XamlResolvedTypeReference typeReference)
    {
        if (typeReference.SourceLocation is { } sourceLocation)
        {
            return new XamlDefinitionLocation(sourceLocation.Uri, sourceLocation.Range);
        }

        if (XamlSourceLinkResolver.TryResolveTypeLocation(
                analysis,
                typeReference.FullTypeName,
                typeReference.AssemblyName,
                out var sourceLinkLocation))
        {
            return new XamlDefinitionLocation(sourceLinkLocation.Uri, sourceLinkLocation.Range);
        }

        var metadataUri = XamlMetadataSymbolUri.CreateTypeUri(typeReference.FullTypeName);
        return new XamlDefinitionLocation(metadataUri, MetadataDefinitionRange);
    }

    private static XamlDefinitionLocation CreateTypeDefinitionLocation(
        XamlAnalysisResult analysis,
        XamlToCSharpGenerator.LanguageService.Symbols.AvaloniaTypeInfo typeInfo)
    {
        if (typeInfo.SourceLocation is { } sourceLocation)
        {
            return new XamlDefinitionLocation(sourceLocation.Uri, sourceLocation.Range);
        }

        if (XamlSourceLinkResolver.TryResolveTypeLocation(
                analysis,
                typeInfo.FullTypeName,
                typeInfo.AssemblyName,
                out var sourceLinkLocation))
        {
            return new XamlDefinitionLocation(sourceLinkLocation.Uri, sourceLinkLocation.Range);
        }

        var metadataUri = XamlMetadataSymbolUri.CreateTypeUri(typeInfo.FullTypeName);
        return new XamlDefinitionLocation(metadataUri, MetadataDefinitionRange);
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
