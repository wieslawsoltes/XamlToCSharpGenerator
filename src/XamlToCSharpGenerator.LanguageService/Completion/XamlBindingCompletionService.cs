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
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.LanguageService.Completion;

internal static class XamlBindingCompletionService
{
    private static readonly MarkupExpressionParser MarkupParser = new(
        new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));

    public static bool TryGetCompletions(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out ImmutableArray<XamlCompletionItem> completions)
    {
        completions = ImmutableArray<XamlCompletionItem>.Empty;
        if (!TryFindAttributeContext(analysis, position, out var element, out var attribute, out var caretOffsetInValue) ||
            !TryCreateBindingEditContext(attribute.Value, caretOffsetInValue, out var context))
        {
            return false;
        }

        var isXBind = context.ExtensionKind == XamlMarkupExtensionKind.XBind;
        if (!(isXBind
                ? XamlSemanticSourceTypeResolver.TryResolveXBindSourceType(analysis, element, context.XBindMarkup, out var sourceType, out var prefixMap)
                : XamlSemanticSourceTypeResolver.TryResolveBindingSourceType(analysis, element, context.BindingMarkup, out sourceType, out prefixMap)))
        {
            return false;
        }

        if (isXBind &&
            TryGetTopLevelXBindCompletions(
                analysis,
                element,
                context.PathPrefix,
                sourceType,
                out completions))
        {
            return completions.Length > 0;
        }

        if (!TryResolveReceiverType(
                analysis,
                element,
                context.ExtensionKind,
                context.PathPrefix,
                sourceType,
                prefixMap,
                out var receiverType,
                out var memberPrefix,
                out var staticOnly))
        {
            return false;
        }

        completions = XamlClrMemberCompletionFactory.CreateMemberCompletions(
            receiverType,
            memberPrefix,
            XamlMemberCompletionMode.BindingPath,
            staticOnly: staticOnly,
            includeFieldsInBindingPath: isXBind,
            allowMethodsWithParameters: isXBind);
        return completions.Length > 0;
    }

    private static bool TryFindAttributeContext(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XElement element,
        out XAttribute attribute,
        out int caretOffsetInValue)
    {
        element = null!;
        attribute = null!;
        caretOffsetInValue = -1;
        if (analysis.XmlDocument?.Root is null)
        {
            return false;
        }

        var absoluteOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (absoluteOffset < 0)
        {
            return false;
        }

        foreach (var candidateElement in analysis.XmlDocument.Root.DescendantsAndSelf())
        {
            foreach (var candidateAttribute in candidateElement.Attributes())
            {
                if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(
                        analysis.Document.Text,
                        candidateAttribute,
                        out var valueRange))
                {
                    continue;
                }

                var valueStart = TextCoordinateHelper.GetOffset(analysis.Document.Text, valueRange.Start);
                var valueEnd = TextCoordinateHelper.GetOffset(analysis.Document.Text, valueRange.End);
                if (valueStart < 0 || valueEnd < valueStart || absoluteOffset < valueStart || absoluteOffset > valueEnd)
                {
                    continue;
                }

                element = candidateElement;
                attribute = candidateAttribute;
                caretOffsetInValue = absoluteOffset - valueStart;
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateBindingEditContext(
        string attributeValue,
        int caretOffsetInValue,
        out BindingEditContext context)
    {
        context = default;
        if (!TryExtractEditableMarkup(attributeValue, out var innerText, out _, out var innerStartInValue) ||
            !XamlMarkupArgumentSemantics.TryParseHead(innerText, out var extensionName, out _) ||
            XamlMarkupExtensionNameSemantics.Classify(extensionName) is not (XamlMarkupExtensionKind.Binding or XamlMarkupExtensionKind.CompiledBinding or XamlMarkupExtensionKind.ReflectionBinding or XamlMarkupExtensionKind.XBind))
        {
            return false;
        }

        var extensionKind = XamlMarkupExtensionNameSemantics.Classify(extensionName);
        if (!TryParseBindingMarkupForEditing(
                attributeValue,
                innerText,
                extensionName,
                extensionKind,
                innerStartInValue,
                caretOffsetInValue,
                out var bindingMarkup,
                out var xBindMarkup,
                out var pathPrefix))
        {
            return false;
        }

        context = new BindingEditContext(extensionKind, bindingMarkup, xBindMarkup, pathPrefix);
        return true;
    }

    private static bool TryParseBindingMarkupForEditing(
        string attributeValue,
        string innerText,
        string extensionName,
        XamlMarkupExtensionKind extensionKind,
        int innerStartInValue,
        int caretOffsetInValue,
        out BindingMarkup bindingMarkup,
        out XBindMarkup xBindMarkup,
        out string pathPrefix)
    {
        bindingMarkup = default;
        xBindMarkup = default;
        pathPrefix = string.Empty;

        if (!TryParseHeadSpan(innerText, out _, out var pathStartInInner, out var argumentsStartInInner))
        {
            return false;
        }

        var argumentsText = argumentsStartInInner < innerText.Length
            ? innerText.Substring(argumentsStartInInner)
            : string.Empty;
        var argumentSegments = ParseArgumentSegments(attributeValue, argumentsText, innerStartInValue + argumentsStartInInner);

        if (!TryCreateMarkupExtensionInfo(extensionName, argumentSegments, out var markupInfo))
        {
            return false;
        }

        if (extensionKind == XamlMarkupExtensionKind.XBind)
        {
            if (!BindingEventMarkupParser.TryParseXBindMarkupCore(markupInfo, out xBindMarkup))
            {
                return false;
            }
        }
        else if (!BindingEventMarkupParser.TryParseBindingMarkupCore(markupInfo, extensionKind, TryParseMarkupExtension, out bindingMarkup))
        {
            return false;
        }

        if (TryResolveEditablePathContext(caretOffsetInValue, argumentSegments, out pathPrefix))
        {
            return true;
        }

        var pathStartInValue = innerStartInValue + pathStartInInner;
        var argumentsStartInValue = innerStartInValue + argumentsStartInInner;
        if (caretOffsetInValue >= pathStartInValue &&
            caretOffsetInValue < argumentsStartInValue)
        {
            pathPrefix = string.Empty;
            return true;
        }

        if (argumentSegments.Length == 0)
        {
            var firstArgumentOffsetInValue = innerStartInValue + argumentsStartInInner;
            if (caretOffsetInValue >= firstArgumentOffsetInValue)
            {
                pathPrefix = string.Empty;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveEditablePathContext(
        int caretOffsetInValue,
        ImmutableArray<MarkupArgumentEditSpan> argumentSegments,
        out string pathPrefix)
    {
        pathPrefix = string.Empty;
        if (argumentSegments.Length == 0)
        {
            return false;
        }

        MarkupArgumentEditSpan? positionalPathArgument = null;
        foreach (var argument in argumentSegments)
        {
            if (string.Equals(argument.Name, "Path", StringComparison.OrdinalIgnoreCase))
            {
                if (!ContainsCaret(argument.ValueStart, argument.ValueLength, caretOffsetInValue))
                {
                    return false;
                }

                pathPrefix = ExtractPrefix(argument.ValueText, argument.ValueStart, caretOffsetInValue);
                return true;
            }

            if (argument.Name is null && argument.Ordinal == 0)
            {
                positionalPathArgument = argument;
            }
        }

        if (positionalPathArgument is { } positionalPath &&
            ContainsCaret(positionalPath.ValueStart, positionalPath.ValueLength, caretOffsetInValue))
        {
            pathPrefix = ExtractPrefix(positionalPath.ValueText, positionalPath.ValueStart, caretOffsetInValue);
            return true;
        }

        return false;
    }

    private static ImmutableArray<MarkupArgumentEditSpan> ParseArgumentSegments(
        string attributeValue,
        string argumentsText,
        int argumentsStartInValue)
    {
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            return ImmutableArray<MarkupArgumentEditSpan>.Empty;
        }

        var segments = MarkupExtensionTextParser.SplitTopLevelSegments(
            argumentsText,
            ',',
            trimTokens: true,
            removeEmpty: false);
        if (segments.Length == 0)
        {
            return ImmutableArray<MarkupArgumentEditSpan>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<MarkupArgumentEditSpan>(segments.Length);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var absoluteSegmentStart = argumentsStartInValue + segment.Start;
            var valueText = segment.Text;
            string? name = null;
            var valueStart = absoluteSegmentStart;
            var valueLength = segment.Length;

            var parseStatus = XamlMarkupArgumentSemantics.TryParseNamedArgument(
                segment.Text,
                out var parsedName,
                out var parsedValue);
            if (parseStatus == XamlMarkupNamedArgumentParseStatus.Parsed)
            {
                name = parsedName;
                valueText = parsedValue;

                var equalsIndex = MarkupExtensionTextParser.IndexOfTopLevel(segment.Text, '=');
                var valueStartInSegment = equalsIndex + 1;
                while (valueStartInSegment < segment.Text.Length && char.IsWhiteSpace(segment.Text[valueStartInSegment]))
                {
                    valueStartInSegment++;
                }

                valueStart = absoluteSegmentStart + valueStartInSegment;
                valueLength = segment.Text.Length - valueStartInSegment;
            }

            NormalizeQuotedToken(attributeValue, valueStart, valueLength, out valueStart, out valueLength);
            valueText = valueLength > 0
                ? attributeValue.Substring(valueStart, valueLength)
                : string.Empty;

            builder.Add(new MarkupArgumentEditSpan(
                Name: name,
                Start: absoluteSegmentStart,
                Length: segment.Length,
                ValueStart: valueStart,
                ValueLength: valueLength,
                ValueText: valueText,
                Ordinal: index));
        }

        return builder.ToImmutable();
    }

    private static bool TryCreateMarkupExtensionInfo(
        string extensionName,
        ImmutableArray<MarkupArgumentEditSpan> argumentSegments,
        out MarkupExtensionInfo markupInfo)
    {
        var positional = ImmutableArray.CreateBuilder<string>();
        var named = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        var arguments = ImmutableArray.CreateBuilder<MarkupExtensionArgument>();

        foreach (var segment in argumentSegments)
        {
            if (!string.IsNullOrWhiteSpace(segment.Name))
            {
                var normalizedValueText = XamlMarkupArgumentSemantics.NormalizeValueToken(segment.ValueText);
                named[segment.Name!] = normalizedValueText;
                arguments.Add(new MarkupExtensionArgument(segment.Name, normalizedValueText, IsNamed: true, segment.Ordinal));
                continue;
            }

            var normalizedPositionalValue = XamlMarkupArgumentSemantics.NormalizeValueToken(segment.ValueText);
            positional.Add(normalizedPositionalValue);
            arguments.Add(new MarkupExtensionArgument(null, normalizedPositionalValue, IsNamed: false, segment.Ordinal));
        }

        markupInfo = new MarkupExtensionInfo(
            extensionName,
            positional.ToImmutable(),
            named.ToImmutable(),
            arguments.ToImmutable());
        return true;
    }

    private static bool TryGetTopLevelXBindCompletions(
        XamlAnalysisResult analysis,
        XElement element,
        string pathPrefix,
        INamedTypeSymbol sourceType,
        out ImmutableArray<XamlCompletionItem> completions)
    {
        completions = ImmutableArray<XamlCompletionItem>.Empty;

        var trimmedPathPrefix = pathPrefix.Trim();
        SplitPathForCompletion(trimmedPathPrefix, out var completedPath, out var memberPrefix);
        if (completedPath.Length != 0 || !IsMemberPrefix(memberPrefix))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<XamlCompletionItem>();
        AddXBindReceiverCompletions(builder, sourceType, memberPrefix);

        if (XamlSemanticSourceTypeResolver.TryResolveRootType(analysis, out var rootType) &&
            !SymbolEqualityComparer.Default.Equals(rootType, sourceType))
        {
            AddXBindReceiverCompletions(builder, rootType, memberPrefix);
        }

        if (XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, element, out var targetType) &&
            !SymbolEqualityComparer.Default.Equals(targetType, sourceType))
        {
            AddXBindReceiverCompletions(builder, targetType, memberPrefix);
        }

        AddNamedElementCompletions(builder, analysis, memberPrefix);
        completions = builder
            .DistinctBy(static item => item.Label, StringComparer.Ordinal)
            .OrderBy(static item => item.Label, StringComparer.Ordinal)
            .ToImmutableArray();
        return true;
    }

    private static void AddXBindReceiverCompletions(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        INamedTypeSymbol receiverType,
        string memberPrefix)
    {
        builder.AddRange(XamlClrMemberCompletionFactory.CreateMemberCompletions(
            receiverType,
            memberPrefix,
            XamlMemberCompletionMode.BindingPath,
            staticOnly: false,
            includeFieldsInBindingPath: true,
            allowMethodsWithParameters: true));
    }

    private static void AddNamedElementCompletions(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        XamlAnalysisResult analysis,
        string memberPrefix)
    {
        var documentRoot = analysis.XmlDocument?.Root;
        if (documentRoot is null)
        {
            return;
        }

        foreach (var candidateElement in documentRoot.DescendantsAndSelf())
        {
            var nameAttribute = candidateElement.Attributes()
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(attribute.Value));
            if (nameAttribute is null ||
                !MatchesPrefix(nameAttribute.Value, memberPrefix) ||
                !XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, candidateElement, out var typeSymbol))
            {
                continue;
            }

            builder.Add(new XamlCompletionItem(
                nameAttribute.Value,
                nameAttribute.Value,
                XamlCompletionItemKind.Element,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                "Named element in the current namescope."));
        }
    }

    private static bool TryResolveReceiverType(
        XamlAnalysisResult analysis,
        XElement element,
        XamlMarkupExtensionKind extensionKind,
        string pathPrefix,
        INamedTypeSymbol sourceType,
        ImmutableDictionary<string, string> prefixMap,
        out INamedTypeSymbol receiverType,
        out string memberPrefix,
        out bool staticOnly)
    {
        receiverType = sourceType;
        memberPrefix = string.Empty;
        staticOnly = false;

        var trimmedPathPrefix = pathPrefix.Trim();
        if (trimmedPathPrefix.Length == 0 || string.Equals(trimmedPathPrefix, ".", StringComparison.Ordinal))
        {
            return true;
        }

        SplitPathForCompletion(trimmedPathPrefix, out var completedPath, out memberPrefix);
        if (memberPrefix.Length > 0 && !IsMemberPrefix(memberPrefix))
        {
            return false;
        }

        if (completedPath.Length == 0 || string.Equals(completedPath, ".", StringComparison.Ordinal))
        {
            return true;
        }

        return TryResolveCompletedPathReceiverType(
            analysis,
            element,
            extensionKind == XamlMarkupExtensionKind.XBind,
            completedPath,
            sourceType,
            prefixMap,
            out receiverType,
            out staticOnly);
    }

    private static bool TryResolveCompletedPathReceiverType(
        XamlAnalysisResult analysis,
        XElement element,
        bool isXBind,
        string completedPath,
        INamedTypeSymbol sourceType,
        ImmutableDictionary<string, string> prefixMap,
        out INamedTypeSymbol receiverType,
        out bool staticOnly)
    {
        receiverType = sourceType;
        staticOnly = false;
        if (!isXBind &&
            TryResolveNamedElementType(analysis, element, completedPath, out var namedElementType))
        {
            receiverType = namedElementType;
            return true;
        }

        if (!TryTokenizeBindingPath(completedPath, out var segmentTokens))
        {
            return false;
        }

        ITypeSymbol currentType = sourceType;
        var currentIsStatic = false;
        for (var index = 0; index < segmentTokens.Length; index++)
        {
            var segment = segmentTokens[index];
            if (segment.CastTypeToken is { Length: > 0 })
            {
                var castType = XamlSemanticSourceTypeResolver.ResolveTypeSymbol(analysis, prefixMap, segment.CastTypeToken);
                if (castType is null)
                {
                    return false;
                }

                currentType = castType;
                currentIsStatic = false;
            }

            if (segment.IsTypeReferenceSegment)
            {
                var typeReference = XamlSemanticSourceTypeResolver.ResolveTypeSymbol(analysis, prefixMap, segment.TypeReferenceToken ?? segment.MemberName);
                if (typeReference is null)
                {
                    return false;
                }

                currentType = typeReference;
                currentIsStatic = true;
                continue;
            }

            if (segment.IsAttachedProperty)
            {
                if (string.IsNullOrWhiteSpace(segment.AttachedOwnerTypeToken))
                {
                    return false;
                }

                var attachedOwnerTypeInfo = ResolveTypeInfo(analysis, prefixMap, segment.AttachedOwnerTypeToken);
                if (attachedOwnerTypeInfo is null)
                {
                    return false;
                }

                var attachedPropertyInfo = attachedOwnerTypeInfo.Properties.FirstOrDefault(property =>
                    string.Equals(property.Name, segment.MemberName, StringComparison.Ordinal) &&
                    property.IsAttached);
                if (attachedPropertyInfo is null ||
                    !TryResolvePropertyTypeSymbol(analysis, prefixMap, attachedOwnerTypeInfo, attachedPropertyInfo, out var attachedPropertyType) ||
                    attachedPropertyType is null)
                {
                    return false;
                }

                currentType = attachedPropertyType;
                currentIsStatic = false;
                continue;
            }

            if (currentType is not INamedTypeSymbol currentNamedType)
            {
                return false;
            }

            if (isXBind &&
                index == 0 &&
                TryResolveXBindInitialSegmentType(
                    analysis,
                    element,
                    segment,
                    sourceType,
                    prefixMap,
                    out var initialType,
                    out currentIsStatic))
            {
                currentType = initialType;
                continue;
            }

            if (!TryResolveClrSegmentType(currentNamedType, currentIsStatic, segment, out var nextType))
            {
                return false;
            }

            currentType = nextType;
            currentIsStatic = false;
        }

        receiverType = currentType as INamedTypeSymbol ?? sourceType;
        staticOnly = currentIsStatic;
        return currentType is INamedTypeSymbol;
    }

    private static bool TryResolveXBindInitialSegmentType(
        XamlAnalysisResult analysis,
        XElement element,
        BindingPathSegmentToken segment,
        INamedTypeSymbol sourceType,
        ImmutableDictionary<string, string> prefixMap,
        out ITypeSymbol resultType,
        out bool staticOnly)
    {
        resultType = sourceType;
        staticOnly = false;

        if (TryResolveClrSegmentType(sourceType, staticOnly: false, segment, out resultType))
        {
            return true;
        }

        if (XamlSemanticSourceTypeResolver.TryResolveRootType(analysis, out var rootType) &&
            !SymbolEqualityComparer.Default.Equals(rootType, sourceType) &&
            TryResolveClrSegmentType(rootType, staticOnly: false, segment, out resultType))
        {
            return true;
        }

        if (XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, element, out var targetType) &&
            TryResolveClrSegmentType(targetType, staticOnly: false, segment, out resultType))
        {
            return true;
        }

        if (!segment.IsMethodCall &&
            !segment.HasIndexers &&
            TryResolveNamedElementType(analysis, element, segment.MemberName, out var namedElementType))
        {
            resultType = namedElementType;
            return true;
        }

        if (!segment.IsMethodCall &&
            !segment.HasIndexers &&
            XamlSemanticSourceTypeResolver.ResolveTypeSymbol(analysis, prefixMap, segment.MemberName) is { } staticType)
        {
            resultType = staticType;
            staticOnly = true;
            return true;
        }

        return false;
    }

    private static bool TryResolveClrSegmentType(
        INamedTypeSymbol currentType,
        bool staticOnly,
        BindingPathSegmentToken segment,
        out ITypeSymbol resultType)
    {
        resultType = currentType;

        if (segment.IsMethodCall)
        {
            var method = XamlClrMemberSymbolResolver.ResolveMethod(
                currentType,
                segment.MemberName,
                staticOnly,
                segment.ArgumentCount,
                allowVoidReturn: false);
            if (method is null)
            {
                return false;
            }

            resultType = segment.HasIndexers
                ? XamlClrMemberSymbolResolver.ResolveIndexedElementType(method.ReturnType) ?? method.ReturnType
                : method.ReturnType;
            return true;
        }

        var property = staticOnly
            ? XamlClrMemberSymbolResolver.ResolveStaticProperty(currentType, segment.MemberName)
            : XamlClrMemberSymbolResolver.ResolveInstanceProperty(currentType, segment.MemberName);
        if (property is not null)
        {
            resultType = segment.HasIndexers
                ? XamlClrMemberSymbolResolver.ResolveIndexedElementType(property.Type) ?? property.Type
                : property.Type;
            return true;
        }

        var field = staticOnly
            ? XamlClrMemberSymbolResolver.ResolveStaticField(currentType, segment.MemberName)
            : XamlClrMemberSymbolResolver.ResolveInstanceField(currentType, segment.MemberName);
        if (field is null)
        {
            return false;
        }

        resultType = segment.HasIndexers
            ? XamlClrMemberSymbolResolver.ResolveIndexedElementType(field.Type) ?? field.Type
            : field.Type;
        return true;
    }

    private static bool TryResolveNamedElementType(
        XamlAnalysisResult analysis,
        XElement element,
        string elementName,
        out INamedTypeSymbol typeSymbol)
    {
        return XamlSemanticSourceTypeResolver.TryResolveNamedElementType(analysis, element, elementName, out typeSymbol);
    }

    private static AvaloniaTypeInfo? ResolveTypeInfo(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string typeToken)
    {
        if (analysis.TypeIndex is null)
        {
            return null;
        }

        if (XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex, prefixMap, typeToken, out var typeInfo))
        {
            return typeInfo;
        }

        if (XamlTypeReferenceNavigationResolver.TryResolve(analysis, prefixMap, "DataType", typeToken, out var resolvedTypeReference) &&
            analysis.TypeIndex.TryGetTypeByFullTypeName(resolvedTypeReference.FullTypeName, out typeInfo))
        {
            return typeInfo;
        }

        return null;
    }

    private static bool TryResolvePropertyTypeSymbol(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        AvaloniaTypeInfo ownerTypeInfo,
        AvaloniaPropertyInfo propertyInfo,
        out ITypeSymbol? typeSymbol)
    {
        typeSymbol = XamlSemanticSourceTypeResolver.ResolveDisplayTypeSymbol(
            analysis.Compilation,
            ownerTypeInfo.ClrNamespace,
            propertyInfo.TypeName);
        if (typeSymbol is not null)
        {
            return true;
        }

        typeSymbol = XamlSemanticSourceTypeResolver.ResolveTypeSymbol(analysis, prefixMap, propertyInfo.TypeName);
        return typeSymbol is not null;
    }

    private static void SplitPathForCompletion(string pathPrefix, out string completedPath, out string memberPrefix)
    {
        completedPath = string.Empty;
        memberPrefix = pathPrefix;
        var lastSeparator = FindLastTopLevelPathSeparator(pathPrefix, out var separatorLength);
        if (lastSeparator < 0)
        {
            return;
        }

        completedPath = pathPrefix.Substring(0, lastSeparator).TrimEnd();
        memberPrefix = pathPrefix.Substring(lastSeparator + separatorLength).TrimStart();
    }

    private static int FindLastTopLevelPathSeparator(string path, out int separatorLength)
    {
        separatorLength = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';
        var lastSeparator = -1;

        for (var index = 0; index < path.Length; index++)
        {
            var ch = path[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

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
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    continue;
            }

            if (braceDepth != 0 || bracketDepth != 0 || parenthesisDepth != 0)
            {
                continue;
            }

            if (ch == '.')
            {
                lastSeparator = index;
                separatorLength = 1;
                continue;
            }

            if (ch == '?' &&
                index + 1 < path.Length &&
                path[index + 1] == '.')
            {
                lastSeparator = index;
                separatorLength = 2;
                index++;
            }
        }

        return lastSeparator;
    }

    private static bool TryTokenizeBindingPath(
        string path,
        out ImmutableArray<BindingPathSegmentToken> segmentTokens)
    {
        segmentTokens = ImmutableArray<BindingPathSegmentToken>.Empty;
        if (path.Length == 0 ||
            !TryGetLeadingNotCount(path, out var leadingNotCount))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<BindingPathSegmentToken>();
        var index = leadingNotCount;
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

            var segmentStart = index;
            var roundDepth = 0;
            var squareDepth = 0;
            var quoteChar = '\0';
            var escaped = false;

            while (index < path.Length)
            {
                var current = path[index];
                if (quoteChar != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == quoteChar)
                    {
                        quoteChar = '\0';
                    }

                    index++;
                    continue;
                }

                if (current is '"' or '\'')
                {
                    quoteChar = current;
                    index++;
                    continue;
                }

                if (current == '(')
                {
                    roundDepth++;
                    index++;
                    continue;
                }

                if (current == ')')
                {
                    if (roundDepth > 0)
                    {
                        roundDepth--;
                    }

                    index++;
                    continue;
                }

                if (current == '[')
                {
                    squareDepth++;
                    index++;
                    continue;
                }

                if (current == ']')
                {
                    if (squareDepth > 0)
                    {
                        squareDepth--;
                    }

                    index++;
                    continue;
                }

                if (roundDepth == 0 &&
                    squareDepth == 0 &&
                    (current == '.' ||
                     (current == '?' && index + 1 < path.Length && path[index + 1] == '.')))
                {
                    break;
                }

                index++;
            }

            if (!TryParseBindingPathSegmentToken(path, segmentStart, index, out var segmentToken))
            {
                return false;
            }

            builder.Add(segmentToken);

            if (index < path.Length &&
                path[index] == '?' &&
                index + 1 < path.Length &&
                path[index + 1] == '.')
            {
                index += 2;
            }
            else if (index < path.Length && path[index] == '.')
            {
                index++;
            }
        }

        segmentTokens = builder.ToImmutable();
        return true;
    }

    private static bool TryGetLeadingNotCount(string path, out int leadingNotCount)
    {
        leadingNotCount = 0;
        if (CompiledBindingPathParser.TryParse(path, out _, out leadingNotCount, out _))
        {
            return true;
        }

        while (leadingNotCount < path.Length &&
               (path[leadingNotCount] == '!' || char.IsWhiteSpace(path[leadingNotCount])))
        {
            if (path[leadingNotCount] == '!')
            {
                leadingNotCount++;
                continue;
            }

            leadingNotCount++;
        }

        return leadingNotCount < path.Length;
    }

    private static bool TryParseBindingPathSegmentToken(
        string path,
        int segmentStart,
        int segmentEnd,
        out BindingPathSegmentToken token)
    {
        token = default;
        var trimmedStart = segmentStart;
        while (trimmedStart < segmentEnd && char.IsWhiteSpace(path[trimmedStart]))
        {
            trimmedStart++;
        }

        var trimmedEnd = segmentEnd;
        while (trimmedEnd > trimmedStart && char.IsWhiteSpace(path[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd <= trimmedStart)
        {
            return false;
        }

        var segmentText = path.Substring(trimmedStart, trimmedEnd - trimmedStart);
        if (CompiledBindingPathSegmentSemantics.TryParseAttachedPropertySegment(
                segmentText,
                0,
                out var attachedOwnerTypeToken,
                out var attachedMemberName,
                out var attachedNextIndex) == CompiledBindingAttachedPropertyParseStatus.Parsed)
        {
            var inner = segmentText.Substring(1, attachedNextIndex - 2);
            var separator = inner.LastIndexOf('.');
            if (separator <= 0)
            {
                return false;
            }

            var ownerRawStart = 1;
            var ownerRawLength = separator;
            var memberRawStart = separator + 2;
            var memberRawLength = inner.Length - separator - 1;
            TrimToken(segmentText, ref ownerRawStart, ref ownerRawLength);
            TrimToken(segmentText, ref memberRawStart, ref memberRawLength);

            token = new BindingPathSegmentToken(
                MemberName: attachedMemberName,
                CastTypeToken: null,
                TypeReferenceToken: null,
                IsTypeReferenceSegment: false,
                IsAttachedProperty: true,
                AttachedOwnerTypeToken: attachedOwnerTypeToken,
                HasIndexers: segmentText.IndexOf('[', StringComparison.Ordinal) >= 0,
                IsMethodCall: false,
                ArgumentCount: 0);
            return true;
        }

        var cursor = 0;
        string? castTypeToken = null;
        var requiresSegmentClosure = false;
        if (segmentText[cursor] == '(')
        {
            if (cursor + 1 < segmentText.Length && segmentText[cursor + 1] == '(')
            {
                requiresSegmentClosure = true;
                cursor++;
            }

            var parseIndex = cursor;
            if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref parseIndex, '(', ')', out var rawCastTypeToken))
            {
                return false;
            }

            castTypeToken = rawCastTypeToken.Trim();
            if (castTypeToken.Length == 0)
            {
                return false;
            }

            cursor = parseIndex;
            while (cursor < segmentText.Length && char.IsWhiteSpace(segmentText[cursor]))
            {
                cursor++;
            }
        }

        if (cursor < segmentText.Length &&
            LooksLikeTypeReferenceToken(segmentText.Substring(cursor)))
        {
            token = new BindingPathSegmentToken(
                MemberName: segmentText.Substring(cursor),
                CastTypeToken: castTypeToken,
                TypeReferenceToken: segmentText.Substring(cursor),
                IsTypeReferenceSegment: true,
                IsAttachedProperty: false,
                AttachedOwnerTypeToken: null,
                HasIndexers: false,
                IsMethodCall: false,
                ArgumentCount: 0);
            return true;
        }

        if (cursor >= segmentText.Length ||
            !MiniLanguageSyntaxFacts.IsIdentifierStart(segmentText[cursor]))
        {
            return false;
        }

        var memberStart = cursor;
        cursor++;
        while (cursor < segmentText.Length && MiniLanguageSyntaxFacts.IsIdentifierPart(segmentText[cursor]))
        {
            cursor++;
        }

        var memberName = segmentText.Substring(memberStart, cursor - memberStart);
        var isMethodCall = cursor < segmentText.Length && segmentText[cursor] == '(';
        var argumentCount = 0;
        if (isMethodCall &&
            !TryCountInvocationArguments(segmentText, cursor, out argumentCount))
        {
            return false;
        }

        if (requiresSegmentClosure)
        {
            while (cursor < segmentText.Length && segmentText[cursor] != ')')
            {
                cursor++;
            }

            if (cursor >= segmentText.Length || segmentText[cursor] != ')')
            {
                return false;
            }
        }

        token = new BindingPathSegmentToken(
            MemberName: memberName,
            CastTypeToken: castTypeToken,
            TypeReferenceToken: null,
            IsTypeReferenceSegment: false,
            IsAttachedProperty: false,
            AttachedOwnerTypeToken: null,
            HasIndexers: segmentText.IndexOf('[', StringComparison.Ordinal) >= 0,
            IsMethodCall: isMethodCall,
            ArgumentCount: argumentCount);
        return true;
    }

    private static bool TryCountInvocationArguments(string segmentText, int openParenIndex, out int argumentCount)
    {
        argumentCount = 0;
        var parseIndex = openParenIndex;
        if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref parseIndex, '(', ')', out var argumentsText))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            argumentCount = 0;
            return true;
        }

        argumentCount = TopLevelTextParser.SplitTopLevelSegments(
                argumentsText,
                ',',
                trimTokens: true,
                removeEmpty: true)
            .Length;
        return true;
    }

    private static bool LooksLikeTypeReferenceToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var colonIndex = token.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= token.Length - 1)
        {
            return false;
        }

        for (var index = 0; index < token.Length; index++)
        {
            var current = token[index];
            if (current == ':')
            {
                continue;
            }

            if (current == '.' || MiniLanguageSyntaxFacts.IsIdentifierPart(current))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryExtractEditableMarkup(
        string attributeValue,
        out string innerText,
        out string extensionName,
        out int innerStartInValue)
    {
        innerText = string.Empty;
        extensionName = string.Empty;
        innerStartInValue = 0;
        if (string.IsNullOrWhiteSpace(attributeValue))
        {
            return false;
        }

        var trimmedStart = 0;
        while (trimmedStart < attributeValue.Length && char.IsWhiteSpace(attributeValue[trimmedStart]))
        {
            trimmedStart++;
        }

        if (trimmedStart >= attributeValue.Length || attributeValue[trimmedStart] != '{')
        {
            return false;
        }

        var trimmedEnd = attributeValue.Length;
        while (trimmedEnd > trimmedStart && char.IsWhiteSpace(attributeValue[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd > trimmedStart && attributeValue[trimmedEnd - 1] == '}')
        {
            trimmedEnd--;
        }

        innerStartInValue = trimmedStart + 1;
        while (innerStartInValue < trimmedEnd && char.IsWhiteSpace(attributeValue[innerStartInValue]))
        {
            innerStartInValue++;
        }

        if (innerStartInValue >= trimmedEnd)
        {
            return false;
        }

        innerText = attributeValue.Substring(innerStartInValue, trimmedEnd - innerStartInValue);
        return XamlMarkupArgumentSemantics.TryParseHead(innerText, out extensionName, out _);
    }

    private static bool TryParseHeadSpan(
        string innerText,
        out string extensionName,
        out int pathStartInInner,
        out int argumentsStartInInner)
    {
        extensionName = string.Empty;
        pathStartInInner = 0;
        argumentsStartInInner = 0;
        if (!XamlMarkupArgumentSemantics.TryParseHead(innerText, out extensionName, out _))
        {
            return false;
        }

        var headLength = 0;
        while (headLength < innerText.Length &&
               !char.IsWhiteSpace(innerText[headLength]) &&
               innerText[headLength] != ',')
        {
            headLength++;
        }

        pathStartInInner = headLength;
        argumentsStartInInner = headLength;
        while (argumentsStartInInner < innerText.Length && char.IsWhiteSpace(innerText[argumentsStartInInner]))
        {
            argumentsStartInInner++;
        }

        if (argumentsStartInInner < innerText.Length && innerText[argumentsStartInInner] == ',')
        {
            argumentsStartInInner++;
            while (argumentsStartInInner < innerText.Length && char.IsWhiteSpace(innerText[argumentsStartInInner]))
            {
                argumentsStartInInner++;
            }
        }

        return true;
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return MarkupParser.TryParseMarkupExtension(value, out markupExtension);
    }

    private static string ExtractPrefix(string valueText, int valueStart, int caretOffsetInValue)
    {
        var relativeLength = Math.Clamp(caretOffsetInValue - valueStart, 0, valueText.Length);
        return relativeLength == 0 ? string.Empty : valueText.Substring(0, relativeLength);
    }

    private static bool ContainsCaret(int valueStart, int valueLength, int caretOffsetInValue)
    {
        var valueEnd = valueStart + valueLength;
        return caretOffsetInValue >= valueStart && caretOffsetInValue <= valueEnd;
    }

    private static bool IsMemberPrefix(string prefix)
    {
        for (var index = 0; index < prefix.Length; index++)
        {
            if (!MiniLanguageSyntaxFacts.IsIdentifierPart(prefix[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesPrefix(string candidate, string prefix)
    {
        return prefix.Length == 0 || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void NormalizeQuotedToken(
        string sourceText,
        int start,
        int length,
        out int normalizedStart,
        out int normalizedLength)
    {
        normalizedStart = start;
        normalizedLength = length;
        while (normalizedLength > 0 && normalizedStart < sourceText.Length && char.IsWhiteSpace(sourceText[normalizedStart]))
        {
            normalizedStart++;
            normalizedLength--;
        }

        while (normalizedLength > 0 && char.IsWhiteSpace(sourceText[normalizedStart + normalizedLength - 1]))
        {
            normalizedLength--;
        }

        if (normalizedLength >= 2)
        {
            var first = sourceText[normalizedStart];
            var last = sourceText[normalizedStart + normalizedLength - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                normalizedStart++;
                normalizedLength -= 2;
            }
        }
    }

    private static void TrimToken(string sourceText, ref int start, ref int length)
    {
        while (length > 0 && char.IsWhiteSpace(sourceText[start]))
        {
            start++;
            length--;
        }

        while (length > 0 && char.IsWhiteSpace(sourceText[start + length - 1]))
        {
            length--;
        }
    }

    private readonly record struct BindingEditContext(
        XamlMarkupExtensionKind ExtensionKind,
        BindingMarkup BindingMarkup,
        XBindMarkup XBindMarkup,
        string PathPrefix);

    private readonly record struct MarkupArgumentEditSpan(
        string? Name,
        int Start,
        int Length,
        int ValueStart,
        int ValueLength,
        string ValueText,
        int Ordinal);

    private readonly record struct BindingPathSegmentToken(
        string MemberName,
        string? CastTypeToken,
        string? TypeReferenceToken,
        bool IsTypeReferenceSegment,
        bool IsAttachedProperty,
        string? AttachedOwnerTypeToken,
        bool HasIndexers,
        bool IsMethodCall,
        int ArgumentCount);
}
