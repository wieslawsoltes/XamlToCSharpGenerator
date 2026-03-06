using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

namespace XamlToCSharpGenerator.LanguageService.Hover;

public sealed class XamlHoverService
{
    public XamlHoverInfo? GetHover(XamlAnalysisResult analysis, SourcePosition position)
    {
        if (TryGetExpressionHover(analysis, position, out var expressionHover))
        {
            return expressionHover;
        }

        if (TryGetSelectorHover(analysis, position, out var selectorHover))
        {
            return selectorHover;
        }

        if (TryGetBindingHover(analysis, position, out var bindingHover))
        {
            return bindingHover;
        }

        if (TryGetTypeReferenceHover(analysis, position, out var typeReferenceHover))
        {
            return typeReferenceHover;
        }

        if (TryGetMarkupExtensionHover(analysis, position, out var markupExtensionHover))
        {
            return markupExtensionHover;
        }

        if (TryGetNamedElementOrResourceHover(analysis, position, out var namedOrResourceHover))
        {
            return namedOrResourceHover;
        }

        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        if (context.Kind == XamlCompletionContextKind.Unknown)
        {
            return null;
        }

        if (TryGetSetterPropertyHover(analysis, position, context, out var setterHover))
        {
            return setterHover;
        }

        if (TryGetAttributeHover(analysis, context, out var attributeHover))
        {
            return attributeHover;
        }

        if (TryGetElementHover(analysis, context, out var elementHover))
        {
            return elementHover;
        }

        return null;
    }

    private static bool TryGetExpressionHover(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (!XamlExpressionBindingNavigationService.TryResolveNavigationTarget(analysis, position, out var target))
        {
            return false;
        }

        hover = new XamlHoverInfo(
            XamlHoverMarkdownFormatter.FormatSymbol(GetSymbolHeading(target.Symbol), target.Symbol),
            target.UsageRange);
        return true;
    }

    private static bool TryGetSelectorHover(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (!XamlSelectorNavigationService.TryResolveReferenceAtOffset(analysis, position, out var reference))
        {
            return false;
        }

        switch (reference.Target.Kind)
        {
            case XamlSelectorNavigationTargetKind.Type:
                if (analysis.TypeIndex is not null &&
                    XamlClrSymbolResolver.TryResolveTypeInfo(
                        analysis.TypeIndex,
                        analysis.PrefixMap,
                        reference.Target.Name,
                        out var typeInfo) &&
                    typeInfo is not null)
                {
                    hover = new XamlHoverInfo(
                        XamlHoverMarkdownFormatter.FormatType("Selector Type", typeInfo),
                        reference.Range);
                    return true;
                }

                break;
            case XamlSelectorNavigationTargetKind.StyleClass:
                hover = new XamlHoverInfo(
                    XamlHoverMarkdownFormatter.FormatStyleClass(reference.Target.Name, reference.Target.TypeContextToken),
                    reference.Range);
                return true;
            case XamlSelectorNavigationTargetKind.PseudoClass:
                var declaringTypeName = TryResolvePseudoClassDeclaringTypeName(analysis, position, reference.Target);
                hover = new XamlHoverInfo(
                    XamlHoverMarkdownFormatter.FormatPseudoClass(reference.Target.Name, declaringTypeName),
                    reference.Range);
                return true;
        }

        return false;
    }

    private static bool TryGetBindingHover(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (!XamlBindingNavigationService.TryResolveHoverTarget(analysis, position, out var target))
        {
            return false;
        }

        switch (target.Kind)
        {
            case XamlBindingHoverTargetKind.Extension:
                var prefixMap = TryResolveLocalPrefixMap(analysis, position, out var localPrefixMap)
                    ? localPrefixMap
                    : analysis.PrefixMap;
                var extensionKind = XamlMarkupExtensionNameSemantics.Classify(target.ExtensionName);
                XamlResolvedTypeReference? resolvedTypeReference = null;
                if (!string.IsNullOrWhiteSpace(target.ExtensionName) &&
                    XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
                        analysis,
                        prefixMap,
                        target.ExtensionName!,
                        out var typeReference))
                {
                    resolvedTypeReference = typeReference;
                }

                hover = new XamlHoverInfo(
                    XamlHoverMarkdownFormatter.FormatMarkupExtension(target.ExtensionName ?? string.Empty, extensionKind, resolvedTypeReference),
                    target.UsageRange);
                return true;
            case XamlBindingHoverTargetKind.Argument:
                hover = new XamlHoverInfo(
                    XamlHoverMarkdownFormatter.FormatBindingArgument(target.ArgumentName ?? string.Empty, target.IsCompiledBinding),
                    target.UsageRange);
                return true;
            case XamlBindingHoverTargetKind.Property:
                hover = new XamlHoverInfo(
                    target.OwnerTypeInfo is not null && target.PropertyInfo is not null
                        ? XamlHoverMarkdownFormatter.FormatProperty(target.OwnerTypeInfo, target.PropertyInfo)
                        : target.Symbol is not null
                            ? XamlHoverMarkdownFormatter.FormatSymbol("Property", target.Symbol)
                            : string.Create(CultureInfo.InvariantCulture, $"**Property**\n\n`{target.ArgumentName}`"),
                    target.UsageRange);
                return true;
            case XamlBindingHoverTargetKind.Method:
                if (target.Symbol is not null)
                {
                    hover = new XamlHoverInfo(
                        XamlHoverMarkdownFormatter.FormatSymbol("Method", target.Symbol),
                        target.UsageRange);
                    return true;
                }

                break;
            case XamlBindingHoverTargetKind.Type:
                if (target.TypeReference is { } typeReferenceValue)
                {
                    hover = new XamlHoverInfo(
                        TryFormatResolvedTypeHover(analysis, "Binding Type", typeReferenceValue),
                        target.UsageRange);
                    return true;
                }

                break;
        }

        return false;
    }

    private static bool TryGetTypeReferenceHover(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                analysis.Document.Text,
                analysis.XmlDocument,
                position,
                out var element,
                out var attribute,
                out _,
                out var valueRange))
        {
            return false;
        }

        if (!ContainsPosition(analysis.Document.Text, valueRange, position))
        {
            return false;
        }

        var localName = attribute.Name.LocalName;
        XamlResolvedTypeReference resolvedTypeReference;
        var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);

        if (XamlTypeReferenceNavigationResolver.IsTypeReferenceAttribute(attribute))
        {
            if (!XamlTypeReferenceNavigationResolver.TryResolve(
                    analysis,
                    prefixMap,
                    localName,
                    attribute.Value,
                    out resolvedTypeReference))
            {
                return false;
            }

            var heading = string.Equals(localName, "Class", StringComparison.Ordinal)
                ? "XAML Class"
                : "Data Type";
            hover = new XamlHoverInfo(
                TryFormatResolvedTypeHover(analysis, heading, resolvedTypeReference),
                valueRange);
            return true;
        }

        if (string.Equals(localName, "TargetType", StringComparison.Ordinal) &&
            XamlTypeReferenceNavigationResolver.TryResolve(
                analysis,
                prefixMap,
                "DataType",
                attribute.Value,
                out resolvedTypeReference))
        {
            hover = new XamlHoverInfo(
                TryFormatResolvedTypeHover(analysis, "Target Type", resolvedTypeReference),
                valueRange);
            return true;
        }

        return false;
    }

    private static bool TryGetMarkupExtensionHover(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlHoverInfo? hover)
    {
        hover = null;
        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (!XamlMarkupExtensionNavigationSemantics.TryResolveClassTokenAtOffset(
                analysis.Document.Text,
                offset,
                out var classToken))
        {
            return false;
        }

        var prefixMap = TryResolveLocalPrefixMap(analysis, position, out var localPrefixMap)
            ? localPrefixMap
            : analysis.PrefixMap;
        var kind = XamlMarkupExtensionNameSemantics.Classify(classToken.Name);
        XamlResolvedTypeReference? resolvedTypeReference = null;
        if (XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
                analysis,
                prefixMap,
                classToken.Name,
                out var typeReference))
        {
            resolvedTypeReference = typeReference;
        }

        hover = new XamlHoverInfo(
            XamlHoverMarkdownFormatter.FormatMarkupExtension(classToken.Name, kind, resolvedTypeReference),
            BuildRange(analysis.Document.Text, classToken.Start, classToken.Start + classToken.Length));
        return true;
    }

    private static bool TryGetNamedElementOrResourceHover(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (analysis.ParsedDocument is null)
        {
            return false;
        }

        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var identifier = XamlResourceReferenceNavigationSemantics.TryResolveResourceIdentifierAtOffset(
            analysis.Document.Text,
            offset,
            out var resourceIdentifier)
            ? resourceIdentifier
            : XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        var hasNamedDeclaration = analysis.ParsedDocument.NamedElements.Any(named =>
            string.Equals(named.Name, identifier, StringComparison.Ordinal));
        var hasResourceDeclaration = HasResourceDeclaration(analysis.ParsedDocument, identifier);
        var symbolKind = XamlNavigationTextSemantics.DetectSymbolKindAtOffset(
            analysis.Document.Text,
            offset,
            identifier,
            hasNamedDeclaration,
            hasResourceDeclaration);

        var range = BuildIdentifierRange(analysis.Document.Text, offset, identifier);
        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.NamedElement)
        {
            var namedElement = analysis.ParsedDocument.NamedElements.FirstOrDefault(named =>
                string.Equals(named.Name, identifier, StringComparison.Ordinal));
            if (namedElement is not null)
            {
                hover = new XamlHoverInfo(
                    XamlHoverMarkdownFormatter.FormatNamedElement(namedElement),
                    range);
                return true;
            }
        }

        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.ResourceKey &&
            TryFindResourceDefinition(analysis.ParsedDocument, identifier, out var resourceKind, out var xmlTypeName))
        {
            hover = new XamlHoverInfo(
                XamlHoverMarkdownFormatter.FormatResourceKey(identifier, resourceKind, xmlTypeName),
                range);
            return true;
        }

        return false;
    }

    private static bool TryGetSetterPropertyHover(
        XamlAnalysisResult analysis,
        SourcePosition position,
        XamlCompletionContext context,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (analysis.TypeIndex is null ||
            context.Kind != XamlCompletionContextKind.AttributeValue ||
            !XamlStyleNavigationSemantics.IsSetterPropertyAttribute(context.CurrentElementName, context.CurrentAttributeName))
        {
            return false;
        }

        var propertyToken = string.IsNullOrWhiteSpace(context.Token)
            ? XamlNavigationTextSemantics.ExtractIdentifierAtOffset(
                analysis.Document.Text,
                TextCoordinateHelper.GetOffset(analysis.Document.Text, position))
            : context.Token;
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        var ownerTypeToken = context.CurrentElementName;
        if (!propertyToken.Contains('.', StringComparison.Ordinal) &&
            XamlStyleNavigationSemantics.TryResolveStyleSetterOwnerTypeToken(
                analysis,
                position,
                propertyToken,
                out var resolvedOwnerTypeToken))
        {
            ownerTypeToken = resolvedOwnerTypeToken;
        }

        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex,
                analysis.PrefixMap,
                ownerTypeToken,
                propertyToken,
                out var propertyInfo,
                out var ownerTypeInfo) ||
            propertyInfo is null ||
            ownerTypeInfo is null)
        {
            return false;
        }

        hover = new XamlHoverInfo(
            XamlHoverMarkdownFormatter.FormatProperty(ownerTypeInfo, propertyInfo),
            BuildRange(analysis.Document.Text, context.TokenStartOffset, context.TokenEndOffset));
        return true;
    }

    private static bool TryGetAttributeHover(
        XamlAnalysisResult analysis,
        XamlCompletionContext context,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (context.Kind != XamlCompletionContextKind.AttributeName ||
            string.IsNullOrWhiteSpace(context.Token))
        {
            return false;
        }

        if (context.Token.StartsWith("x:", StringComparison.Ordinal))
        {
            hover = new XamlHoverInfo(
                XamlHoverMarkdownFormatter.FormatDirective(context.Token),
                BuildRange(analysis.Document.Text, context.TokenStartOffset, context.TokenEndOffset));
            return true;
        }

        if (analysis.TypeIndex is null || string.IsNullOrWhiteSpace(context.CurrentElementName))
        {
            return false;
        }

        if (XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex,
                analysis.PrefixMap,
                context.CurrentElementName,
                context.Token,
                out var propertyInfo,
                out var ownerTypeInfo) &&
            propertyInfo is not null &&
            ownerTypeInfo is not null)
        {
            hover = new XamlHoverInfo(
                XamlHoverMarkdownFormatter.FormatProperty(ownerTypeInfo, propertyInfo),
                BuildRange(analysis.Document.Text, context.TokenStartOffset, context.TokenEndOffset));
            return true;
        }

        return false;
    }

    private static bool TryGetElementHover(
        XamlAnalysisResult analysis,
        XamlCompletionContext context,
        out XamlHoverInfo? hover)
    {
        hover = null;
        if (context.Kind != XamlCompletionContextKind.ElementName || analysis.TypeIndex is null)
        {
            return false;
        }

        if (!XamlXmlNamespaceResolver.TryResolveXmlNamespace(
                analysis.PrefixMap,
                context.Token,
                out var xmlNamespace,
                out var xmlTypeName) ||
            !analysis.TypeIndex.TryGetType(xmlNamespace, xmlTypeName, out var typeInfo) ||
            typeInfo is null)
        {
            return false;
        }

        hover = new XamlHoverInfo(
            XamlHoverMarkdownFormatter.FormatElement(typeInfo),
            BuildRange(analysis.Document.Text, context.TokenStartOffset, context.TokenEndOffset));
        return true;
    }

    private static string? TryResolvePseudoClassDeclaringTypeName(
        XamlAnalysisResult analysis,
        SourcePosition position,
        XamlSelectorNavigationTarget target)
    {
        if (analysis.TypeIndex is null)
        {
            return null;
        }

        var typeContextToken = target.TypeContextToken;
        if (string.IsNullOrWhiteSpace(typeContextToken) &&
            XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                analysis.Document.Text,
                analysis.XmlDocument,
                position,
                out _,
                out var attribute,
                out _,
                out _) &&
            string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal))
        {
            typeContextToken = SelectorReferenceSemantics.EnumerateReferences(attribute.Value)
                .FirstOrDefault(static reference => reference.Kind == SelectorReferenceKind.Type).Name;
        }

        if (string.IsNullOrWhiteSpace(typeContextToken))
        {
            return null;
        }

        if (!XamlClrSymbolResolver.TryResolveTypeInfo(
                analysis.TypeIndex,
                analysis.PrefixMap,
                typeContextToken!,
                out var typeInfo) ||
            typeInfo is null)
        {
            return null;
        }

        var pseudoClassInfo = typeInfo.PseudoClasses.FirstOrDefault(pseudo =>
            string.Equals(
                NormalizePseudoClassName(pseudo.Name),
                NormalizePseudoClassName(target.Name),
                StringComparison.Ordinal));
        return pseudoClassInfo?.DeclaringTypeFullName ?? typeInfo.FullTypeName;
    }

    private static string TryFormatResolvedTypeHover(
        XamlAnalysisResult analysis,
        string heading,
        XamlResolvedTypeReference typeReference)
    {
        if (analysis.TypeIndex is not null &&
            analysis.TypeIndex.TryGetTypeByFullTypeName(typeReference.FullTypeName, out var typeInfo) &&
            typeInfo is not null)
        {
            return XamlHoverMarkdownFormatter.FormatType(heading, typeInfo);
        }

        return XamlHoverMarkdownFormatter.FormatResolvedType(heading, typeReference);
    }

    private static bool TryResolveLocalPrefixMap(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out ImmutableDictionary<string, string> prefixMap)
    {
        prefixMap = analysis.PrefixMap;
        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                analysis.Document.Text,
                analysis.XmlDocument,
                position,
                out var element,
                out _,
                out _,
                out _))
        {
            return false;
        }

        prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        return true;
    }

    private static bool HasResourceDeclaration(XamlDocumentModel parsedDocument, string identifier)
    {
        return parsedDocument.Resources.Any(resource => string.Equals(resource.Key, identifier, StringComparison.Ordinal)) ||
               parsedDocument.Templates.Any(template => string.Equals(template.Key, identifier, StringComparison.Ordinal)) ||
               parsedDocument.Styles.Any(style => string.Equals(style.Key, identifier, StringComparison.Ordinal)) ||
               parsedDocument.ControlThemes.Any(theme => string.Equals(theme.Key, identifier, StringComparison.Ordinal));
    }

    private static bool TryFindResourceDefinition(
        XamlDocumentModel parsedDocument,
        string identifier,
        out string resourceKind,
        out string xmlTypeName)
    {
        foreach (var resource in parsedDocument.Resources)
        {
            if (!string.Equals(resource.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            resourceKind = "Resource";
            xmlTypeName = resource.XmlTypeName;
            return true;
        }

        foreach (var template in parsedDocument.Templates)
        {
            if (!string.Equals(template.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            resourceKind = template.Kind;
            xmlTypeName = template.Kind;
            return true;
        }

        foreach (var style in parsedDocument.Styles)
        {
            if (!string.Equals(style.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            resourceKind = "Style";
            xmlTypeName = "Style";
            return true;
        }

        foreach (var controlTheme in parsedDocument.ControlThemes)
        {
            if (!string.Equals(controlTheme.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            resourceKind = "ControlTheme";
            xmlTypeName = "ControlTheme";
            return true;
        }

        resourceKind = string.Empty;
        xmlTypeName = string.Empty;
        return false;
    }

    private static string GetSymbolHeading(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol => "Method",
            IPropertySymbol => "Property",
            IFieldSymbol => "Field",
            ITypeSymbol => "Type",
            _ => "Symbol"
        };
    }

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }

    private static SourceRange BuildIdentifierRange(string text, int offset, string identifier)
    {
        var start = Math.Max(0, offset);
        while (start > 0 && IsIdentifierCharacter(text[start - 1]))
        {
            start--;
        }

        var end = Math.Min(text.Length, offset);
        while (end < text.Length && IsIdentifierCharacter(text[end]))
        {
            end++;
        }

        if (end <= start)
        {
            return BuildRange(text, offset, Math.Min(text.Length, offset + Math.Max(1, identifier.Length)));
        }

        return BuildRange(text, start, end);
    }

    private static bool IsIdentifierCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or ':' or '.';
    }

    private static string NormalizePseudoClassName(string value)
    {
        return value.StartsWith(":", StringComparison.Ordinal)
            ? value.Substring(1)
            : value;
    }

    private static SourceRange BuildRange(string text, int startOffset, int endOffset)
    {
        var boundedStart = Math.Max(0, Math.Min(startOffset, text.Length));
        var boundedEnd = Math.Max(boundedStart, Math.Min(endOffset, text.Length));

        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, boundedStart),
            TextCoordinateHelper.GetPosition(text, boundedEnd));
    }
}
