using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Parsing;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal readonly record struct XamlInlineCSharpNavigationTarget(
    SourceRange UsageRange,
    ISymbol Symbol,
    SourceRange? DeclarationRange,
    AvaloniaSymbolSourceLocation? DefinitionLocation);

internal readonly record struct XamlInlineCSharpInlayHintTarget(
    SourceRange HintAnchorRange,
    string Code,
    string ContextTypeName,
    string ResultTypeName,
    AvaloniaSymbolSourceLocation? ResultTypeLocation);

internal readonly record struct XamlInlineCSharpContext(
    string SourceText,
    XElement ScopeElement,
    XElement? InlineCodeElement,
    XAttribute? Attribute,
    SourceRange CodeRange,
    INamedTypeSymbol? SourceType,
    INamedTypeSymbol? RootType,
    INamedTypeSymbol? TargetType,
    INamedTypeSymbol? EventHandlerType,
    INamedTypeSymbol? SenderType,
    INamedTypeSymbol? EventArgsType,
    string RawCode,
    string NormalizedCode,
    bool IsLambda,
    bool IsEventCode,
    ImmutableArray<MappedExpressionSymbolReference> SymbolReferences,
    ImmutableArray<SourceContextSymbolOccurrence> SymbolOccurrences,
    ITypeSymbol? ResultTypeSymbol);

internal static class XamlInlineCSharpNavigationService
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private const string RuntimeClrNamespace = "XamlToCSharpGenerator.Runtime";
    private const string RuntimeMarkupClrNamespace = "XamlToCSharpGenerator.Runtime.Markup";
    private const string RuntimeUsingNamespace = "using:XamlToCSharpGenerator.Runtime";
    private const string RuntimeMarkupUsingNamespace = "using:XamlToCSharpGenerator.Runtime.Markup";
    private const string RuntimeClrNamespaceUri = "clr-namespace:XamlToCSharpGenerator.Runtime";
    private const string RuntimeMarkupClrNamespaceUri = "clr-namespace:XamlToCSharpGenerator.Runtime.Markup";

    public static bool TryResolveNavigationTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlInlineCSharpNavigationTarget target)
    {
        target = default;
        if (!TryFindContextAtPosition(analysis, position, out var context, out _))
        {
            return false;
        }

        var documentOffset = TextCoordinateHelper.GetOffset(context.SourceText, position);
        foreach (var occurrence in context.SymbolOccurrences)
        {
            var start = TextCoordinateHelper.GetOffset(context.SourceText, context.CodeRange.Start) + occurrence.Start;
            var end = start + occurrence.Length;
            if (documentOffset < start || documentOffset > end)
            {
                continue;
            }

            var declarationRange = TryFindDeclarationRange(context, occurrence.Symbol, out var localDeclarationRange)
                ? (SourceRange?)localDeclarationRange
                : null;
            var definitionLocation = declarationRange is null
                ? TryResolveSyntheticDefinitionLocation(analysis, context, occurrence.Symbol)
                : null;
            target = new XamlInlineCSharpNavigationTarget(
                CreateRange(context.SourceText, start, occurrence.Length),
                occurrence.Symbol,
                declarationRange,
                definitionLocation);
            return true;
        }

        return false;
    }

    public static bool TryResolveInlayHintTarget(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        out XamlInlineCSharpInlayHintTarget target)
    {
        target = default;
        if (!TryResolveCompactAttributeContext(analysis, sourceText, element, attribute, out var context) ||
            context.IsEventCode ||
            context.IsLambda ||
            context.ResultTypeSymbol is null)
        {
            return false;
        }

        target = CreateInlayHintTarget(analysis, context);
        return true;
    }

    public static bool TryResolveInlayHintTarget(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement inlineCodeElement,
        out XamlInlineCSharpInlayHintTarget target)
    {
        target = default;
        if (!TryResolveElementContentContext(analysis, sourceText, inlineCodeElement, out var context) ||
            context.IsEventCode ||
            context.IsLambda ||
            context.ResultTypeSymbol is null)
        {
            return false;
        }

        target = CreateInlayHintTarget(analysis, context);
        return true;
    }

    public static ImmutableArray<SourceRange> FindReferenceRanges(
        XamlInlineCSharpContext context,
        ISymbol targetSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<SourceRange>();
        var normalizedTarget = NormalizeSymbol(targetSymbol);
        var codeStartOffset = TextCoordinateHelper.GetOffset(context.SourceText, context.CodeRange.Start);
        foreach (var occurrence in context.SymbolOccurrences)
        {
            if (!AreEquivalentSymbols(occurrence.Symbol, normalizedTarget))
            {
                continue;
            }

            builder.Add(CreateRange(
                context.SourceText,
                codeStartOffset + occurrence.Start,
                occurrence.Length));
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<XamlInlineCSharpContext> EnumerateContexts(XamlAnalysisResult analysis)
    {
        return EnumerateContexts(analysis, allowIncompleteExpressions: false);
    }

    public static ImmutableArray<XamlInlineCSharpContext> EnumerateContexts(
        XamlAnalysisResult analysis,
        bool allowIncompleteExpressions)
    {
        var builder = ImmutableArray.CreateBuilder<XamlInlineCSharpContext>();
        if (analysis.XmlDocument?.Root is null ||
            string.IsNullOrWhiteSpace(analysis.Document.Text))
        {
            return builder.ToImmutable();
        }

        var sourceText = analysis.Document.Text;
        foreach (var element in analysis.XmlDocument.Root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                if (TryResolveCompactAttributeContext(
                        analysis,
                        sourceText,
                        element,
                        attribute,
                        allowIncompleteExpressions,
                        out var compactContext))
                {
                    builder.Add(compactContext);
                }
            }

            if (TryResolveElementContentContext(
                    analysis,
                    sourceText,
                    element,
                    allowIncompleteExpressions,
                    out var elementContext))
            {
                builder.Add(elementContext);
            }
        }

        return builder.ToImmutable();
    }

    public static bool TryFindContextAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlInlineCSharpContext context,
        out int caretOffsetInCode)
    {
        return TryFindContextAtPosition(
            analysis,
            position,
            allowIncompleteExpressions: false,
            out context,
            out caretOffsetInCode);
    }

    public static bool TryFindContextAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        bool allowIncompleteExpressions,
        out XamlInlineCSharpContext context,
        out int caretOffsetInCode)
    {
        context = default;
        caretOffsetInCode = -1;
        var absoluteOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (absoluteOffset < 0)
        {
            return false;
        }

        foreach (var candidate in EnumerateContexts(analysis, allowIncompleteExpressions))
        {
            var codeStart = TextCoordinateHelper.GetOffset(candidate.SourceText, candidate.CodeRange.Start);
            var codeEnd = TextCoordinateHelper.GetOffset(candidate.SourceText, candidate.CodeRange.End);
            if (codeStart < 0 || codeEnd < codeStart || absoluteOffset < codeStart || absoluteOffset > codeEnd)
            {
                continue;
            }

            context = candidate;
            caretOffsetInCode = Math.Clamp(absoluteOffset - codeStart, 0, candidate.RawCode.Length);
            return true;
        }

        return false;
    }

    private static XamlInlineCSharpInlayHintTarget CreateInlayHintTarget(
        XamlAnalysisResult analysis,
        XamlInlineCSharpContext context)
    {
        var contextTypeName = context.SourceType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                              ?? context.RootType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                              ?? context.TargetType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                              ?? "<unknown>";

        return new XamlInlineCSharpInlayHintTarget(
            context.CodeRange,
            context.RawCode,
            contextTypeName,
            context.ResultTypeSymbol!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.ResultTypeSymbol));
    }

    internal static bool TryResolveCompactAttributeContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        bool allowIncompleteExpressions,
        out XamlInlineCSharpContext context)
    {
        context = default;
        if (analysis.Compilation is null ||
            !XamlXmlSourceRangeService.TryCreateAttributeValueRange(sourceText, attribute, out var attributeValueRange))
        {
            return false;
        }

        var valueStartOffset = TextCoordinateHelper.GetOffset(sourceText, attributeValueRange.Start);
        if (valueStartOffset < 0 ||
            !XamlMarkupExtensionSpanParser.TryParse(attribute.Value, valueStartOffset, out var markupSpan) ||
            !IsInlineCSharpMarkupExtension(analysis, element, markupSpan.ExtensionName))
        {
            return false;
        }

        var codeArgument = markupSpan.Arguments.FirstOrDefault(static argument =>
            string.Equals(argument.Name, "Code", StringComparison.Ordinal));
        if (codeArgument.ValueLength <= 0)
        {
            codeArgument = markupSpan.Arguments.FirstOrDefault(static argument => argument.Name is null);
        }
        if (codeArgument.ValueLength <= 0 || string.IsNullOrWhiteSpace(codeArgument.ValueText))
        {
            return false;
        }

        var codeRange = new SourceRange(
            TextCoordinateHelper.GetPosition(sourceText, codeArgument.ValueStart),
            TextCoordinateHelper.GetPosition(sourceText, codeArgument.ValueStart + codeArgument.ValueLength));

        return TryAnalyzeContext(
            analysis,
            sourceText,
            scopeElement: element,
            inlineCodeElement: null,
            attribute: attribute,
            codeRange,
            codeArgument.ValueText,
            allowIncompleteExpressions,
            out context);
    }

    internal static bool TryResolveCompactAttributeContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        out XamlInlineCSharpContext context)
    {
        return TryResolveCompactAttributeContext(
            analysis,
            sourceText,
            element,
            attribute,
            allowIncompleteExpressions: false,
            out context);
    }

    internal static bool TryResolveElementContentContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        bool allowIncompleteExpressions,
        out XamlInlineCSharpContext context)
    {
        context = default;
        if (analysis.Compilation is null ||
            !IsInlineCSharpElement(element))
        {
            return false;
        }

        var codeAttribute = element.Attributes().FirstOrDefault(static attribute =>
            !attribute.IsNamespaceDeclaration &&
            string.Equals(attribute.Name.LocalName, "Code", StringComparison.Ordinal));
        if (codeAttribute is not null &&
            XamlXmlSourceRangeService.TryCreateAttributeValueRange(sourceText, codeAttribute, out var codeAttributeRange) &&
            !string.IsNullOrWhiteSpace(codeAttribute.Value))
        {
            return TryAnalyzeContext(
                analysis,
                sourceText,
                scopeElement: element,
                inlineCodeElement: element,
                attribute: codeAttribute,
                codeAttributeRange,
                codeAttribute.Value.Trim(),
                allowIncompleteExpressions,
                out context);
        }

        var rawContent = GetInlineCodeRawContent(element);
        if (string.IsNullOrWhiteSpace(rawContent) ||
            !XamlXmlSourceRangeService.TryCreateElementContentRange(sourceText, element, rawContent, out var rawRange))
        {
            return false;
        }

        var trimStart = 0;
        while (trimStart < rawContent.Length && char.IsWhiteSpace(rawContent[trimStart]))
        {
            trimStart++;
        }

        var trimEnd = rawContent.Length;
        while (trimEnd > trimStart && char.IsWhiteSpace(rawContent[trimEnd - 1]))
        {
            trimEnd--;
        }

        if (trimEnd <= trimStart)
        {
            return false;
        }

        var rawStartOffset = TextCoordinateHelper.GetOffset(sourceText, rawRange.Start);
        if (rawStartOffset < 0)
        {
            return false;
        }

        var codeRange = new SourceRange(
            TextCoordinateHelper.GetPosition(sourceText, rawStartOffset + trimStart),
            TextCoordinateHelper.GetPosition(sourceText, rawStartOffset + trimEnd));

        return TryAnalyzeContext(
            analysis,
            sourceText,
            scopeElement: element,
            inlineCodeElement: element,
            attribute: null,
            codeRange,
            rawContent.Substring(trimStart, trimEnd - trimStart),
            allowIncompleteExpressions,
            out context);
    }

    internal static bool TryResolveElementContentContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        out XamlInlineCSharpContext context)
    {
        return TryResolveElementContentContext(
            analysis,
            sourceText,
            element,
            allowIncompleteExpressions: false,
            out context);
    }

    private static bool TryAnalyzeContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement scopeElement,
        XElement? inlineCodeElement,
        XAttribute? attribute,
        SourceRange codeRange,
        string rawCode,
        bool allowIncompleteExpressions,
        out XamlInlineCSharpContext context)
    {
        context = default;
        if (analysis.Compilation is null || string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        var contextElement = inlineCodeElement ?? scopeElement;
        XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(analysis, contextElement, out var sourceType, out _);
        var rootType = TryResolveRootType(analysis, contextElement);
        var targetType = TryResolveTargetType(analysis, scopeElement, attribute);
        var eventHandlerType = TryResolveEventHandlerType(analysis, scopeElement, attribute);
        var isEventCode = eventHandlerType is not null;
        var isLambda = isEventCode && CSharpMarkupExpressionSemantics.IsLambdaExpression(rawCode);

        ImmutableArray<SourceContextExpressionSymbolReference> references;
        ImmutableArray<SourceContextSymbolOccurrence> symbolOccurrences = ImmutableArray<SourceContextSymbolOccurrence>.Empty;
        ITypeSymbol? resultTypeSymbol = null;
        string normalizedCode;

        if (isEventCode)
        {
            if (!TryAnalyzeEventCode(
                    analysis.Compilation,
                    sourceType,
                    rootType,
                    targetType,
                    eventHandlerType!,
                    rawCode,
                    isLambda,
                    out normalizedCode,
                    out references,
                    out symbolOccurrences))
            {
                if (!allowIncompleteExpressions)
                {
                    return false;
                }

                normalizedCode = rawCode;
                references = ImmutableArray<SourceContextExpressionSymbolReference>.Empty;
                symbolOccurrences = ImmutableArray<SourceContextSymbolOccurrence>.Empty;
            }
        }
        else
        {
            if (!CSharpInlineCodeAnalysisService.TryAnalyzeExpression(
                    analysis.Compilation,
                    sourceType,
                    rootType,
                    targetType,
                    rawCode,
                    out var expressionAnalysis,
                    out _))
            {
                if (!allowIncompleteExpressions)
                {
                    return false;
                }

                normalizedCode = rawCode;
                references = ImmutableArray<SourceContextExpressionSymbolReference>.Empty;
                symbolOccurrences = ImmutableArray<SourceContextSymbolOccurrence>.Empty;
            }
            else
            {
                normalizedCode = expressionAnalysis.NormalizedExpression;
                references = expressionAnalysis.SymbolReferences;
                symbolOccurrences = expressionAnalysis.SymbolOccurrences;
                resultTypeSymbol = expressionAnalysis.ResultTypeSymbol;
            }
        }

        var invokeMethod = eventHandlerType?.DelegateInvokeMethod;
        context = new XamlInlineCSharpContext(
            sourceText,
            scopeElement,
            inlineCodeElement,
            attribute,
            codeRange,
            sourceType,
            rootType,
            targetType,
            eventHandlerType,
            invokeMethod?.Parameters.Length > 0 ? invokeMethod.Parameters[0].Type as INamedTypeSymbol : null,
            invokeMethod?.Parameters.Length > 1 ? invokeMethod.Parameters[1].Type as INamedTypeSymbol : null,
            rawCode,
            normalizedCode,
            isLambda,
            isEventCode,
            references.Select(static reference => new MappedExpressionSymbolReference(reference.Symbol, reference.Start, reference.Length)).ToImmutableArray(),
            symbolOccurrences,
            resultTypeSymbol);
        return true;
    }

    private static bool TryAnalyzeEventCode(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        INamedTypeSymbol eventHandlerType,
        string rawCode,
        bool isLambda,
        out string normalizedCode,
        out ImmutableArray<SourceContextExpressionSymbolReference> references,
        out ImmutableArray<SourceContextSymbolOccurrence> symbolOccurrences)
    {
        normalizedCode = string.Empty;
        references = ImmutableArray<SourceContextExpressionSymbolReference>.Empty;
        symbolOccurrences = ImmutableArray<SourceContextSymbolOccurrence>.Empty;

        SourceContextLambdaAnalysisResult analysisResult;
        var success = isLambda
            ? CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
                compilation,
                sourceType,
                rootType,
                targetType,
                eventHandlerType,
                rawCode,
                out analysisResult,
                out _)
            : CSharpInlineCodeAnalysisService.TryAnalyzeEventStatements(
                compilation,
                sourceType,
                rootType,
                targetType,
                eventHandlerType,
                rawCode,
                out analysisResult,
                out _);
        if (!success)
        {
            return false;
        }

        normalizedCode = analysisResult.RewrittenLambdaExpression;
        references = analysisResult.SymbolReferences;
        symbolOccurrences = analysisResult.SymbolOccurrences;
        return true;
    }

    private static bool TryFindDeclarationRange(
        XamlInlineCSharpContext context,
        ISymbol symbol,
        out SourceRange declarationRange)
    {
        declarationRange = default;
        foreach (var occurrence in context.SymbolOccurrences)
        {
            if (!occurrence.IsDeclaration || !AreEquivalentSymbols(occurrence.Symbol, symbol))
            {
                continue;
            }

            var startOffset = TextCoordinateHelper.GetOffset(context.SourceText, context.CodeRange.Start) + occurrence.Start;
            declarationRange = CreateRange(context.SourceText, startOffset, occurrence.Length);
            return true;
        }

        return false;
    }

    private static AvaloniaSymbolSourceLocation? TryResolveSyntheticDefinitionLocation(
        XamlAnalysisResult analysis,
        XamlInlineCSharpContext context,
        ISymbol symbol)
    {
        if (symbol is not IParameterSymbol parameterSymbol)
        {
            return null;
        }

        return parameterSymbol.Name switch
        {
            "source" when context.SourceType is not null
                => XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.SourceType),
            "root" when context.RootType is not null
                => XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.RootType),
            "target" when context.TargetType is not null
                => XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.TargetType),
            "sender" when context.SenderType is not null
                => XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.SenderType),
            "e" when context.EventArgsType is not null
                => XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.EventArgsType),
            "arg0" when context.SenderType is not null
                => XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.SenderType),
            "arg1" when context.EventArgsType is not null
                => XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.EventArgsType),
            _ => null
        };
    }

    private static INamedTypeSymbol? TryResolveRootType(XamlAnalysisResult analysis, XElement element)
    {
        var root = element.Document?.Root;
        var classAttribute = root?.Attributes().FirstOrDefault(static attribute =>
            string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal));
        if (classAttribute is not null)
        {
            var typeSymbol = XamlSemanticSourceTypeResolver.ResolveTypeSymbolByFullTypeName(
                analysis.Compilation,
                classAttribute.Value.Trim());
            if (typeSymbol is not null)
            {
                return typeSymbol;
            }
        }

        var classFullName = analysis.ParsedDocument?.ClassFullName;
        return string.IsNullOrWhiteSpace(classFullName)
            ? null
            : XamlSemanticSourceTypeResolver.ResolveTypeSymbolByFullTypeName(analysis.Compilation, classFullName);
    }

    private static INamedTypeSymbol? TryResolveTargetType(
        XamlAnalysisResult analysis,
        XElement scopeElement,
        XAttribute? attribute)
    {
        var isInlineCodeElementAttribute = IsInlineCodeElementAttribute(scopeElement, attribute);
        var targetElement = (attribute is null || isInlineCodeElementAttribute) && scopeElement.Parent is not null && IsInlineCSharpElement(scopeElement)
            ? ResolveOwnerElementForInlineCodeElement(scopeElement)
            : scopeElement;
        return targetElement is not null &&
               XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, targetElement, out var targetType)
            ? targetType
            : null;
    }

    private static INamedTypeSymbol? TryResolveEventHandlerType(
        XamlAnalysisResult analysis,
        XElement scopeElement,
        XAttribute? attribute)
    {
        if (IsInlineCodeElementAttribute(scopeElement, attribute))
        {
            var inlineOwnerElement = ResolveOwnerElementForInlineCodeElement(scopeElement);
            if (inlineOwnerElement is null)
            {
                return null;
            }

            if (!TryResolvePropertyElementMemberName(scopeElement.Parent, out var inlineMemberName))
            {
                return null;
            }

            if (!XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, inlineOwnerElement, out var inlineOwnerType))
            {
                return null;
            }

            return FindEvent(inlineOwnerType, inlineMemberName)?.Type as INamedTypeSymbol;
        }

        if (attribute is not null)
        {
            if (!XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, scopeElement, out var elementType))
            {
                return null;
            }

            return FindEvent(elementType, attribute.Name.LocalName)?.Type as INamedTypeSymbol;
        }

        var ownerElement = ResolveOwnerElementForInlineCodeElement(scopeElement);
        if (ownerElement is null)
        {
            return null;
        }

        if (!TryResolvePropertyElementMemberName(scopeElement.Parent, out var memberName))
        {
            return null;
        }

        if (!XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, ownerElement, out var ownerType))
        {
            return null;
        }

        return FindEvent(ownerType, memberName)?.Type as INamedTypeSymbol;
    }

    private static bool IsInlineCodeElementAttribute(XElement scopeElement, XAttribute? attribute)
    {
        return attribute is not null &&
               IsInlineCSharpElement(scopeElement) &&
               string.Equals(attribute.Name.LocalName, "Code", StringComparison.Ordinal);
    }

    private static XElement? ResolveOwnerElementForInlineCodeElement(XElement inlineCodeElement)
    {
        return inlineCodeElement.Parent?.Parent;
    }

    private static bool TryResolvePropertyElementMemberName(XElement? propertyElement, out string memberName)
    {
        memberName = string.Empty;
        var localName = propertyElement?.Name.LocalName;
        if (string.IsNullOrWhiteSpace(localName))
        {
            return false;
        }

        var dotIndex = localName.LastIndexOf('.');
        if (dotIndex < 0 || dotIndex >= localName.Length - 1)
        {
            return false;
        }

        memberName = localName.Substring(dotIndex + 1);
        return memberName.Length > 0;
    }

    private static bool IsInlineCSharpElement(XElement element)
    {
        if (!string.Equals(element.Name.LocalName, "CSharp", StringComparison.Ordinal))
        {
            return false;
        }

        var namespaceName = element.Name.NamespaceName;
        return string.Equals(namespaceName, RuntimeUsingNamespace, StringComparison.Ordinal) ||
               string.Equals(namespaceName, RuntimeClrNamespaceUri, StringComparison.Ordinal) ||
               string.Equals(namespaceName, RuntimeMarkupUsingNamespace, StringComparison.Ordinal) ||
               string.Equals(namespaceName, RuntimeMarkupClrNamespaceUri, StringComparison.Ordinal) ||
               string.Equals(namespaceName, AvaloniaDefaultXmlNamespace, StringComparison.Ordinal);
    }

    private static bool IsInlineCSharpMarkupExtension(
        XamlAnalysisResult analysis,
        XElement element,
        string extensionName)
    {
        if (string.IsNullOrWhiteSpace(extensionName))
        {
            return false;
        }

        var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        if (XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
                analysis,
                prefixMap,
                extensionName,
                out var typeReference))
        {
            return string.Equals(typeReference.FullTypeName, RuntimeClrNamespace + ".CSharpExtension", StringComparison.Ordinal) ||
                   string.Equals(typeReference.FullTypeName, RuntimeClrNamespace + ".CSharp", StringComparison.Ordinal) ||
                   string.Equals(typeReference.FullTypeName, RuntimeMarkupClrNamespace + ".CSharpExtension", StringComparison.Ordinal) ||
                   string.Equals(typeReference.FullTypeName, RuntimeMarkupClrNamespace + ".CSharp", StringComparison.Ordinal);
        }

        var separator = extensionName.IndexOf(':');
        if (separator < 0)
        {
            return string.Equals(extensionName, "CSharp", StringComparison.Ordinal) ||
                   string.Equals(extensionName, "CSharpExtension", StringComparison.Ordinal);
        }

        if (separator >= extensionName.Length - 1)
        {
            return false;
        }

        var prefix = extensionName.Substring(0, separator);
        var localName = extensionName.Substring(separator + 1);
        return (string.Equals(localName, "CSharp", StringComparison.Ordinal) ||
                string.Equals(localName, "CSharpExtension", StringComparison.Ordinal)) &&
               prefixMap.TryGetValue(prefix, out var namespaceValue) &&
               (string.Equals(namespaceValue, RuntimeUsingNamespace, StringComparison.Ordinal) ||
                string.Equals(namespaceValue, RuntimeClrNamespaceUri, StringComparison.Ordinal) ||
                string.Equals(namespaceValue, RuntimeMarkupUsingNamespace, StringComparison.Ordinal) ||
                string.Equals(namespaceValue, RuntimeMarkupClrNamespaceUri, StringComparison.Ordinal) ||
                string.Equals(namespaceValue, AvaloniaDefaultXmlNamespace, StringComparison.Ordinal));
    }

    private static string GetInlineCodeRawContent(XElement element)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText textNode)
            {
                builder.Append(textNode.Value);
            }
        }

        return builder.ToString();
    }

    private static SourceRange CreateRange(string text, int startOffset, int length)
    {
        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, startOffset),
            TextCoordinateHelper.GetPosition(text, startOffset + length));
    }

    private static IEventSymbol? FindEvent(INamedTypeSymbol typeSymbol, string eventName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    private static ISymbol NormalizeSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol methodSymbol => methodSymbol.OriginalDefinition,
            IPropertySymbol propertySymbol => propertySymbol.OriginalDefinition,
            IFieldSymbol fieldSymbol => fieldSymbol.OriginalDefinition,
            INamedTypeSymbol typeSymbol => typeSymbol.OriginalDefinition,
            _ => symbol
        };
    }

    private static bool AreEquivalentSymbols(ISymbol left, ISymbol right)
    {
        var normalizedLeft = NormalizeSymbol(left);
        var normalizedRight = NormalizeSymbol(right);
        if (SymbolEqualityComparer.Default.Equals(normalizedLeft, normalizedRight))
        {
            return true;
        }

        if (normalizedLeft.Kind != normalizedRight.Kind)
        {
            return false;
        }

        return normalizedLeft switch
        {
            INamedTypeSymbol leftType when normalizedRight is INamedTypeSymbol rightType =>
                string.Equals(
                    leftType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            IPropertySymbol leftProperty when normalizedRight is IPropertySymbol rightProperty =>
                string.Equals(leftProperty.Name, rightProperty.Name, StringComparison.Ordinal) &&
                string.Equals(
                    leftProperty.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightProperty.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            IFieldSymbol leftField when normalizedRight is IFieldSymbol rightField =>
                string.Equals(leftField.Name, rightField.Name, StringComparison.Ordinal) &&
                string.Equals(
                    leftField.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightField.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            IMethodSymbol leftMethod when normalizedRight is IMethodSymbol rightMethod =>
                string.Equals(leftMethod.Name, rightMethod.Name, StringComparison.Ordinal) &&
                leftMethod.Parameters.Length == rightMethod.Parameters.Length &&
                string.Equals(
                    leftMethod.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightMethod.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            _ => false
        };
    }
}
