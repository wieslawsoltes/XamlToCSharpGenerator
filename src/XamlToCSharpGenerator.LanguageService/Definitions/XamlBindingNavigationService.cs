using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Parsing;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal enum XamlBindingNavigationTargetKind
{
    None = 0,
    Property,
    Type
}

internal enum XamlBindingHoverTargetKind
{
    None = 0,
    Extension,
    Argument,
    Property,
    Method,
    Type
}

internal readonly record struct XamlBindingNavigationTarget(
    XamlBindingNavigationTargetKind Kind,
    SourceRange UsageRange,
    AvaloniaTypeInfo? OwnerTypeInfo,
    AvaloniaPropertyInfo? PropertyInfo,
    XamlResolvedTypeReference? TypeReference);

internal readonly record struct XamlBindingHoverTarget(
    XamlBindingHoverTargetKind Kind,
    SourceRange UsageRange,
    string? ExtensionName,
    string? ArgumentName,
    bool IsCompiledBinding,
    AvaloniaTypeInfo? OwnerTypeInfo,
    AvaloniaPropertyInfo? PropertyInfo,
    XamlResolvedTypeReference? TypeReference,
    ISymbol? Symbol);

internal readonly record struct XamlBindingInlayHintTarget(
    SourceRange HintAnchorRange,
    string Path,
    string SourceTypeName,
    string ResultTypeName,
    AvaloniaSymbolSourceLocation? ResultTypeLocation);

internal static class XamlBindingNavigationService
{
    private static readonly MarkupExpressionParser MarkupParser = new(
        new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));

    public static bool TryResolveNavigationTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlBindingNavigationTarget target)
    {
        target = default;
        var documentOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (!TryFindBindingAttributeAtPosition(analysis, position, out var element, out var attribute, out var attributeValueRange) ||
            !TryCreateBindingContext(analysis, analysis.Document.Text, element, attribute, attributeValueRange, out var context))
        {
            return false;
        }

        context = context with { DocumentOffset = documentOffset };
        return TryResolveNavigationTarget(context, out target);
    }

    public static bool TryResolveHoverTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlBindingHoverTarget target)
    {
        target = default;
        var documentOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (!TryFindBindingAttributeAtPosition(analysis, position, out var element, out var attribute, out var attributeValueRange) ||
            !TryCreateBindingContext(analysis, analysis.Document.Text, element, attribute, attributeValueRange, out var context))
        {
            return false;
        }

        context = context with { DocumentOffset = documentOffset };

        if (ContainsOffset(
                CreateRange(context.SourceText, context.MarkupSpanInfo.ExtensionNameStart, context.MarkupSpanInfo.ExtensionNameLength),
                context.DocumentOffset,
                context.SourceText))
        {
            target = new XamlBindingHoverTarget(
                XamlBindingHoverTargetKind.Extension,
                CreateRange(context.SourceText, context.MarkupSpanInfo.ExtensionNameStart, context.MarkupSpanInfo.ExtensionNameLength),
                context.MarkupSpanInfo.ExtensionName,
                ArgumentName: null,
                IsCompiledBinding: context.BindingMarkup.IsCompiledBinding,
                OwnerTypeInfo: null,
                PropertyInfo: null,
                TypeReference: null,
                Symbol: null);
            return true;
        }

        foreach (var argument in context.MarkupSpanInfo.Arguments)
        {
            if (argument.Name is null || argument.NameLength <= 0)
            {
                continue;
            }

            if (!ContainsOffset(
                    CreateRange(context.SourceText, argument.NameStart, argument.NameLength),
                    context.DocumentOffset,
                    context.SourceText))
            {
                continue;
            }

            target = new XamlBindingHoverTarget(
                XamlBindingHoverTargetKind.Argument,
                CreateRange(context.SourceText, argument.NameStart, argument.NameLength),
                ExtensionName: context.MarkupSpanInfo.ExtensionName,
                ArgumentName: argument.Name,
                IsCompiledBinding: context.BindingMarkup.IsCompiledBinding,
                OwnerTypeInfo: null,
                PropertyInfo: null,
                TypeReference: null,
                Symbol: null);
            return true;
        }

        foreach (var candidate in EnumerateBindingTypeTargets(context))
        {
            if (ContainsOffset(candidate.UsageRange, context.DocumentOffset, context.SourceText))
            {
                target = new XamlBindingHoverTarget(
                    XamlBindingHoverTargetKind.Type,
                    candidate.UsageRange,
                    ExtensionName: context.MarkupSpanInfo.ExtensionName,
                    ArgumentName: null,
                    IsCompiledBinding: context.BindingMarkup.IsCompiledBinding,
                    OwnerTypeInfo: null,
                    PropertyInfo: null,
                    TypeReference: candidate.TypeReference,
                    Symbol: null);
                return true;
            }
        }

        return TryResolveBindingPathHoverTarget(context, out target);
    }

    public static ImmutableArray<SourceRange> FindPropertyReferenceRanges(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        AvaloniaTypeInfo targetOwnerType,
        AvaloniaPropertyInfo targetProperty)
    {
        if (!TryCreateBindingContext(analysis, sourceText, element, attribute, out var context))
        {
            return ImmutableArray<SourceRange>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SourceRange>();
        foreach (var candidate in EnumerateBindingPathMemberTargets(context))
        {
            if (candidate.Kind != XamlBindingNavigationTargetKind.Property ||
                candidate.OwnerTypeInfo is null ||
                candidate.PropertyInfo is null)
            {
                continue;
            }

            if (!string.Equals(candidate.OwnerTypeInfo.FullTypeName, targetOwnerType.FullTypeName, StringComparison.Ordinal) ||
                !string.Equals(candidate.PropertyInfo.Name, targetProperty.Name, StringComparison.Ordinal))
            {
                continue;
            }

            builder.Add(candidate.UsageRange);
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<SourceRange> FindTypeReferenceRanges(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        string targetFullTypeName)
    {
        if (!TryCreateBindingContext(analysis, sourceText, element, attribute, out var context))
        {
            return ImmutableArray<SourceRange>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SourceRange>();
        foreach (var candidate in EnumerateBindingTypeTargets(context))
        {
            if (candidate.Kind != XamlBindingNavigationTargetKind.Type ||
                candidate.TypeReference is null ||
                !string.Equals(candidate.TypeReference.Value.FullTypeName, targetFullTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            builder.Add(candidate.UsageRange);
        }

        return builder.ToImmutable();
    }

    public static bool TryResolveInlayHintTarget(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        out XamlBindingInlayHintTarget target)
    {
        target = default;
        if (!TryCreateBindingContext(analysis, sourceText, element, attribute, out var context))
        {
            return false;
        }

        return TryResolveInlayHintTarget(context, out target);
    }

    private static bool TryResolveNavigationTarget(
        BindingContext context,
        out XamlBindingNavigationTarget target)
    {
        foreach (var candidate in EnumerateBindingTypeTargets(context))
        {
            if (ContainsOffset(candidate.UsageRange, context.DocumentOffset, context.SourceText))
            {
                target = candidate;
                return true;
            }
        }

        foreach (var candidate in EnumerateBindingPathMemberTargets(context))
        {
            if (ContainsOffset(candidate.UsageRange, context.DocumentOffset, context.SourceText))
            {
                target = candidate;
                return true;
            }
        }

        target = default;
        return false;
    }

    private static bool TryResolveInlayHintTarget(
        BindingContext context,
        out XamlBindingInlayHintTarget target)
    {
        target = default;
        if (!TryResolveBindingPathSourceType(context, out var sourceTypeSymbol, out var currentPrefixMap) ||
            !TryResolveBindingResultType(
                context,
                sourceTypeSymbol,
                currentPrefixMap,
                out var resultTypeSymbol,
                out var resultTypeName))
        {
            return false;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(context.BindingMarkup.Path)
            ? "."
            : context.BindingMarkup.Path.Trim();
        var sourceTypeName = sourceTypeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        target = new XamlBindingInlayHintTarget(
            HintAnchorRange: context.AttributeValueRange,
            Path: normalizedPath,
            SourceTypeName: sourceTypeName,
            ResultTypeName: resultTypeName,
            ResultTypeLocation: resultTypeSymbol is null
                ? null
                : XamlClrNavigationLocationResolver.ResolveTypeLocation(context.Analysis, resultTypeSymbol));
        return true;
    }

    private static bool TryResolveBindingPathHoverTarget(
        BindingContext context,
        out XamlBindingHoverTarget target)
    {
        target = default;
        if (!TryResolveBindingPathSourceType(context, out var sourceTypeSymbol, out var currentPrefixMap) ||
            !TryGetNormalizedPathValueSpan(context.SourceText, context.MarkupSpanInfo, out var normalizedPathStart, out _, out var normalizedPath) ||
            normalizedPath.Length == 0 ||
            !TryTokenizeBindingPath(normalizedPath, out var segmentTokens))
        {
            return false;
        }

        ITypeSymbol currentType = sourceTypeSymbol;
        for (var index = 0; index < segmentTokens.Length; index++)
        {
            var segment = segmentTokens[index];

            if (segment.CastTypeToken is { Length: > 0 })
            {
                var castType = ResolveTypeSymbol(context.Analysis, currentPrefixMap, segment.CastTypeToken);
                if (castType is null)
                {
                    return false;
                }

                currentType = castType;
            }

            if (segment.IsAttachedProperty)
            {
                if (string.IsNullOrWhiteSpace(segment.AttachedOwnerTypeToken))
                {
                    return false;
                }

                var attachedOwnerTypeInfo = ResolveTypeInfo(context.Analysis, currentPrefixMap, segment.AttachedOwnerTypeToken);
                if (attachedOwnerTypeInfo is null)
                {
                    return false;
                }

                var attachedPropertyInfo = attachedOwnerTypeInfo.Properties.FirstOrDefault(property =>
                    string.Equals(property.Name, segment.MemberName, StringComparison.Ordinal) &&
                    property.IsAttached);
                if (attachedPropertyInfo is null ||
                    !TryResolvePropertyTypeSymbol(context, currentPrefixMap, attachedOwnerTypeInfo, attachedPropertyInfo, out var attachedPropertyType, out _) ||
                    attachedPropertyType is null)
                {
                    return false;
                }

                var usageRange = CreateRange(
                    context.SourceText,
                    normalizedPathStart + segment.MemberNameStart,
                    segment.MemberNameLength);
                if (ContainsOffset(usageRange, context.DocumentOffset, context.SourceText))
                {
                    target = new XamlBindingHoverTarget(
                        XamlBindingHoverTargetKind.Property,
                        usageRange,
                        ExtensionName: context.MarkupSpanInfo.ExtensionName,
                        ArgumentName: "Path",
                        IsCompiledBinding: context.BindingMarkup.IsCompiledBinding,
                        OwnerTypeInfo: attachedOwnerTypeInfo,
                        PropertyInfo: attachedPropertyInfo,
                        TypeReference: null,
                        Symbol: null);
                    return true;
                }

                currentType = attachedPropertyType;
                continue;
            }

            if (currentType is not INamedTypeSymbol currentNamedType)
            {
                return false;
            }

            var memberRange = CreateRange(
                context.SourceText,
                normalizedPathStart + segment.MemberNameStart,
                segment.MemberNameLength);

            if (segment.IsMethodCall)
            {
                var method = XamlClrMemberSymbolResolver.ResolveParameterlessMethod(currentNamedType, segment.MemberName);
                if (method is null)
                {
                    return false;
                }

                if (ContainsOffset(memberRange, context.DocumentOffset, context.SourceText))
                {
                    target = new XamlBindingHoverTarget(
                        XamlBindingHoverTargetKind.Method,
                        memberRange,
                        ExtensionName: context.MarkupSpanInfo.ExtensionName,
                        ArgumentName: "Path",
                        IsCompiledBinding: context.BindingMarkup.IsCompiledBinding,
                        OwnerTypeInfo: null,
                        PropertyInfo: null,
                        TypeReference: null,
                        Symbol: method);
                    return true;
                }

                currentType = segment.HasIndexers
                    ? XamlClrMemberSymbolResolver.ResolveIndexedElementType(method.ReturnType) ?? method.ReturnType
                    : method.ReturnType;
                continue;
            }

            var resolvedProperty = XamlClrMemberSymbolResolver.ResolveInstanceProperty(currentNamedType, segment.MemberName);
            if (resolvedProperty is null)
            {
                return false;
            }

            AvaloniaTypeInfo? ownerTypeInfo = null;
            AvaloniaPropertyInfo? propertyInfo = null;
            if (TryResolveTypeInfo(context.Analysis, currentNamedType, out ownerTypeInfo))
            {
                propertyInfo = ownerTypeInfo?.Properties.FirstOrDefault(property =>
                    string.Equals(property.Name, resolvedProperty.Name, StringComparison.Ordinal) &&
                    !property.IsAttached);
            }

            if (ContainsOffset(memberRange, context.DocumentOffset, context.SourceText))
            {
                target = new XamlBindingHoverTarget(
                    XamlBindingHoverTargetKind.Property,
                    memberRange,
                    ExtensionName: context.MarkupSpanInfo.ExtensionName,
                    ArgumentName: "Path",
                    IsCompiledBinding: context.BindingMarkup.IsCompiledBinding,
                    OwnerTypeInfo: ownerTypeInfo,
                    PropertyInfo: propertyInfo,
                    TypeReference: null,
                    Symbol: resolvedProperty);
                return true;
            }

            currentType = segment.HasIndexers
                ? XamlClrMemberSymbolResolver.ResolveIndexedElementType(resolvedProperty.Type) ?? resolvedProperty.Type
                : resolvedProperty.Type;
        }

        return false;
    }

    private static IEnumerable<XamlBindingNavigationTarget> EnumerateBindingTypeTargets(BindingContext context)
    {
        if (context.BindingMarkup.RelativeSource is { AncestorTypeToken: { } ancestorTypeToken } &&
            TryResolveArgumentSpan(context.MarkupSpanInfo.Arguments, "RelativeSource", out var relativeSourceArgumentSpan) &&
            TryParseMarkupExtensionSpan(relativeSourceArgumentSpan.ValueText, relativeSourceArgumentSpan.ValueStart, out var relativeSourceMarkupInfo))
        {
            foreach (var argument in relativeSourceMarkupInfo.Arguments)
            {
                if (!string.Equals(argument.Name, "AncestorType", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveTypeReference(
                        context.Analysis,
                        context.PrefixMap,
                        argument.ValueText,
                        out var ancestorTypeReference))
                {
                    yield return new XamlBindingNavigationTarget(
                        XamlBindingNavigationTargetKind.Type,
                        CreateRange(context.SourceText, argument.ValueStart, argument.ValueLength),
                        OwnerTypeInfo: null,
                        PropertyInfo: null,
                        ancestorTypeReference);
                }
            }
        }

        if (!TryResolveBindingPathSourceType(context, out _, out _))
        {
            yield break;
        }

        if (!TryGetNormalizedPathValueSpan(context.SourceText, context.MarkupSpanInfo, out var normalizedPathStart, out var normalizedPathLength, out var normalizedPath) ||
            normalizedPath.Length == 0 ||
            !TryTokenizeBindingPath(normalizedPath, out var segmentTokens))
        {
            yield break;
        }

        foreach (var segment in segmentTokens)
        {
            if (segment.CastTypeToken is { Length: > 0 } &&
                TryResolveTypeReference(
                    context.Analysis,
                    context.PrefixMap,
                    segment.CastTypeToken,
                    out var castTypeReference))
            {
                yield return new XamlBindingNavigationTarget(
                    XamlBindingNavigationTargetKind.Type,
                    CreateRange(
                        context.SourceText,
                        normalizedPathStart + segment.CastTypeTokenStart,
                        segment.CastTypeTokenLength),
                    OwnerTypeInfo: null,
                    PropertyInfo: null,
                    castTypeReference);
            }

            if (segment.AttachedOwnerTypeToken is { Length: > 0 } &&
                TryResolveTypeReference(
                    context.Analysis,
                    context.PrefixMap,
                    segment.AttachedOwnerTypeToken,
                    out var attachedOwnerReference))
            {
                yield return new XamlBindingNavigationTarget(
                    XamlBindingNavigationTargetKind.Type,
                    CreateRange(
                        context.SourceText,
                        normalizedPathStart + segment.AttachedOwnerTypeTokenStart,
                        segment.AttachedOwnerTypeTokenLength),
                    OwnerTypeInfo: null,
                    PropertyInfo: null,
                    attachedOwnerReference);
            }
        }
    }

    private static IEnumerable<XamlBindingNavigationTarget> EnumerateBindingPathMemberTargets(BindingContext context)
    {
        if (!TryResolveBindingPathSourceType(context, out var currentTypeSymbol, out var currentPrefixMap) ||
            !TryGetNormalizedPathValueSpan(context.SourceText, context.MarkupSpanInfo, out var normalizedPathStart, out _, out var normalizedPath) ||
            normalizedPath.Length == 0 ||
            !TryTokenizeBindingPath(normalizedPath, out var segmentTokens))
        {
            yield break;
        }

        var currentType = currentTypeSymbol;
        for (var index = 0; index < segmentTokens.Length; index++)
        {
            var segment = segmentTokens[index];

            if (segment.CastTypeToken is { Length: > 0 })
            {
                var castType = ResolveTypeSymbol(context.Analysis, currentPrefixMap, segment.CastTypeToken);
                if (castType is null)
                {
                    yield break;
                }

                currentType = castType;
            }

            if (segment.IsAttachedProperty)
            {
                if (string.IsNullOrWhiteSpace(segment.AttachedOwnerTypeToken))
                {
                    yield break;
                }

                var attachedOwnerTypeInfo = ResolveTypeInfo(context.Analysis, currentPrefixMap, segment.AttachedOwnerTypeToken);
                if (attachedOwnerTypeInfo is null)
                {
                    yield break;
                }

                var attachedPropertyInfo = attachedOwnerTypeInfo.Properties.FirstOrDefault(property =>
                    string.Equals(property.Name, segment.MemberName, StringComparison.Ordinal) &&
                    property.IsAttached);
                if (attachedPropertyInfo is null)
                {
                    yield break;
                }

                yield return new XamlBindingNavigationTarget(
                    XamlBindingNavigationTargetKind.Property,
                    CreateRange(
                        context.SourceText,
                        normalizedPathStart + segment.MemberNameStart,
                        segment.MemberNameLength),
                    attachedOwnerTypeInfo,
                    attachedPropertyInfo,
                    TypeReference: null);

                if (index < segmentTokens.Length - 1)
                {
                    yield break;
                }

                continue;
            }

            var resolvedProperty = XamlClrMemberSymbolResolver.ResolveInstanceProperty(currentType, segment.MemberName);
            if (resolvedProperty is null)
            {
                yield break;
            }

            if (TryResolveTypeInfo(context.Analysis, currentType, out var ownerTypeInfo) &&
                ownerTypeInfo?.Properties.FirstOrDefault(property =>
                    string.Equals(property.Name, resolvedProperty.Name, StringComparison.Ordinal) &&
                    !property.IsAttached) is { } propertyInfo)
            {
                yield return new XamlBindingNavigationTarget(
                    XamlBindingNavigationTargetKind.Property,
                    CreateRange(
                        context.SourceText,
                        normalizedPathStart + segment.MemberNameStart,
                        segment.MemberNameLength),
                    ownerTypeInfo,
                    propertyInfo,
                    TypeReference: null);
            }

            currentType = resolvedProperty.Type as INamedTypeSymbol;
            if (currentType is null)
            {
                yield break;
            }

            if ((segment.IsMethodCall || segment.HasIndexers) &&
                index < segmentTokens.Length - 1)
            {
                yield break;
            }
        }
    }

    private static bool TryResolveBindingResultType(
        BindingContext context,
        INamedTypeSymbol sourceTypeSymbol,
        ImmutableDictionary<string, string> currentPrefixMap,
        out ITypeSymbol? resultTypeSymbol,
        out string resultTypeName)
    {
        resultTypeSymbol = sourceTypeSymbol;
        resultTypeName = sourceTypeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (string.IsNullOrWhiteSpace(context.BindingMarkup.Path) ||
            string.Equals(context.BindingMarkup.Path.Trim(), ".", StringComparison.Ordinal))
        {
            return true;
        }

        if (!TryGetNormalizedPathValueSpan(
                context.SourceText,
                context.MarkupSpanInfo,
                out _,
                out _,
                out var normalizedPath) ||
            normalizedPath.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedPath, ".", StringComparison.Ordinal))
        {
            return true;
        }

        if (!TryTokenizeBindingPath(normalizedPath, out var segmentTokens))
        {
            return false;
        }

        var currentType = sourceTypeSymbol;
        ITypeSymbol? currentResult = sourceTypeSymbol;

        for (var index = 0; index < segmentTokens.Length; index++)
        {
            var segment = segmentTokens[index];

            if (segment.CastTypeToken is { Length: > 0 })
            {
                var castType = ResolveTypeSymbol(context.Analysis, currentPrefixMap, segment.CastTypeToken);
                if (castType is null)
                {
                    return false;
                }

                currentType = castType;
                currentResult = castType;
            }

            if (segment.IsMethodCall || segment.HasIndexers)
            {
                return false;
            }

            if (segment.IsAttachedProperty)
            {
                if (string.IsNullOrWhiteSpace(segment.AttachedOwnerTypeToken))
                {
                    return false;
                }

                var attachedOwnerTypeInfo = ResolveTypeInfo(context.Analysis, currentPrefixMap, segment.AttachedOwnerTypeToken);
                if (attachedOwnerTypeInfo is null)
                {
                    return false;
                }

                var attachedPropertyInfo = attachedOwnerTypeInfo.Properties.FirstOrDefault(property =>
                    string.Equals(property.Name, segment.MemberName, StringComparison.Ordinal) &&
                    property.IsAttached);
                if (attachedPropertyInfo is null)
                {
                    return false;
                }

                if (!TryResolvePropertyTypeSymbol(
                        context,
                        currentPrefixMap,
                        attachedOwnerTypeInfo,
                        attachedPropertyInfo,
                        out currentResult,
                        out resultTypeName))
                {
                    return false;
                }

                if (index < segmentTokens.Length - 1)
                {
                    currentType = currentResult as INamedTypeSymbol;
                    if (currentType is null)
                    {
                        return false;
                    }
                }

                continue;
            }

            var resolvedProperty = XamlClrMemberSymbolResolver.ResolveInstanceProperty(currentType, segment.MemberName);
            if (resolvedProperty is null)
            {
                return false;
            }

            currentResult = resolvedProperty.Type;
            resultTypeName = resolvedProperty.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

            if (index < segmentTokens.Length - 1)
            {
                currentType = resolvedProperty.Type as INamedTypeSymbol;
                if (currentType is null)
                {
                    return false;
                }
            }
        }

        resultTypeSymbol = currentResult;
        return true;
    }

    private static bool TryResolveBindingPathSourceType(
        BindingContext context,
        out INamedTypeSymbol sourceTypeSymbol,
        out ImmutableDictionary<string, string> prefixMap)
    {
        sourceTypeSymbol = null!;
        prefixMap = context.PrefixMap;

        if (!string.IsNullOrWhiteSpace(context.BindingMarkup.ElementName) &&
            TryResolveNamedElementType(context, context.BindingMarkup.ElementName!, out sourceTypeSymbol))
        {
            return true;
        }

        if (context.BindingMarkup.RelativeSource is { } relativeSource)
        {
            if (string.Equals(relativeSource.Mode, "Self", StringComparison.OrdinalIgnoreCase) &&
                TryResolveElementTypeSymbol(context.Analysis, context.Element, out sourceTypeSymbol))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken))
            {
                var ancestorPrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(context.Element);
                var ancestorType = ResolveTypeSymbol(context.Analysis, ancestorPrefixMap, relativeSource.AncestorTypeToken!);
                if (ancestorType is not null)
                {
                    sourceTypeSymbol = ancestorType;
                    prefixMap = ancestorPrefixMap;
                    return true;
                }
            }

            if (TryResolveBindingLocalDataType(context, out sourceTypeSymbol, out prefixMap))
            {
                return true;
            }

            return false;
        }

        if (TryResolveBindingLocalDataType(context, out sourceTypeSymbol, out prefixMap))
        {
            return true;
        }

        var current = context.Element;
        while (current is not null)
        {
            var dataTypeAttribute = current.Attributes()
                .FirstOrDefault(static attribute => string.Equals(attribute.Name.LocalName, "DataType", StringComparison.Ordinal));
            if (dataTypeAttribute is not null)
            {
                var dataTypePrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(current);
                var dataTypeType = ResolveTypeSymbol(context.Analysis, dataTypePrefixMap, dataTypeAttribute.Value);
                if (dataTypeType is not null)
                {
                    sourceTypeSymbol = dataTypeType;
                    prefixMap = dataTypePrefixMap;
                    return true;
                }
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool TryResolveBindingLocalDataType(
        BindingContext context,
        out INamedTypeSymbol sourceTypeSymbol,
        out ImmutableDictionary<string, string> prefixMap)
    {
        sourceTypeSymbol = null!;
        prefixMap = context.PrefixMap;

        if (string.IsNullOrWhiteSpace(context.BindingMarkup.DataType))
        {
            return false;
        }

        var localPrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(context.Element);
        var localType = ResolveTypeSymbol(context.Analysis, localPrefixMap, context.BindingMarkup.DataType!);
        if (localType is null)
        {
            return false;
        }

        sourceTypeSymbol = localType;
        prefixMap = localPrefixMap;
        return true;
    }

    private static bool TryResolveNamedElementType(
        BindingContext context,
        string elementName,
        out INamedTypeSymbol typeSymbol)
    {
        typeSymbol = null!;
        var documentRoot = context.Element.Document?.Root;
        if (documentRoot is null)
        {
            return false;
        }

        foreach (var candidateElement in documentRoot.DescendantsAndSelf())
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

            if (!TryResolveElementTypeSymbol(context.Analysis, candidateElement, out typeSymbol))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool TryResolveElementTypeSymbol(
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

        return TryResolveTypeInfoByXmlNamespace(
            analysis.TypeIndex,
            element.Name.NamespaceName,
            element.Name.LocalName,
            out typeInfo) &&
            typeInfo is not null;
    }

    private static AvaloniaTypeInfo? ResolveTypeInfo(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string typeToken)
    {
        if (analysis.TypeIndex is not null &&
            XamlClrSymbolResolver.TryResolveTypeInfo(
                analysis.TypeIndex,
                prefixMap,
                NormalizeTypeReferenceToken(typeToken),
                out var typeInfo) &&
            typeInfo is not null)
        {
            return typeInfo;
        }

        if (XamlTypeReferenceNavigationResolver.TryResolve(
                analysis,
                prefixMap,
                "DataType",
                typeToken,
                out var resolvedTypeReference) &&
            analysis.TypeIndex is not null &&
            analysis.TypeIndex.TryGetTypeByFullTypeName(resolvedTypeReference.FullTypeName, out typeInfo) &&
            typeInfo is not null)
        {
            return typeInfo;
        }

        return null;
    }

    private static bool TryResolvePropertyTypeSymbol(
        BindingContext context,
        ImmutableDictionary<string, string> currentPrefixMap,
        AvaloniaTypeInfo ownerTypeInfo,
        AvaloniaPropertyInfo propertyInfo,
        out ITypeSymbol? typeSymbol,
        out string typeName)
    {
        typeName = propertyInfo.TypeName;
        typeSymbol = ResolveDisplayTypeSymbol(
            context.Analysis.Compilation,
            ownerTypeInfo.ClrNamespace,
            propertyInfo.TypeName);
        if (typeSymbol is not null)
        {
            typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return true;
        }

        typeSymbol = ResolveTypeSymbol(context.Analysis, currentPrefixMap, propertyInfo.TypeName);
        if (typeSymbol is not null)
        {
            typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        return typeSymbol is not null;
    }

    private static bool TryResolveTypeInfo(
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

    private static INamedTypeSymbol? ResolveTypeSymbol(
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

        var typeInfo = ResolveTypeInfo(analysis, prefixMap, normalizedTypeToken);
        if (typeInfo is not null)
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

    private static bool TryResolveTypeReference(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string rawTypeToken,
        out XamlResolvedTypeReference typeReference)
    {
        typeReference = default;
        var normalizedTypeToken = NormalizeTypeReferenceToken(rawTypeToken);
        if (normalizedTypeToken.Length == 0)
        {
            return false;
        }

        if (XamlTypeReferenceNavigationResolver.TryResolve(
                analysis,
                prefixMap,
                "DataType",
                normalizedTypeToken,
                out typeReference))
        {
            return true;
        }

        if (ResolveTypeInfo(analysis, prefixMap, normalizedTypeToken) is { } typeInfo)
        {
            typeReference = new XamlResolvedTypeReference(
                typeInfo.FullTypeName,
                typeInfo.AssemblyName,
                typeInfo.SourceLocation);
            return true;
        }

        return false;
    }

    private static INamedTypeSymbol? ResolveTypeSymbolByFullTypeName(Compilation? compilation, string fullTypeName)
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

    private static ITypeSymbol? ResolveDisplayTypeSymbol(
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

    private static bool TryFindBindingAttributeAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XElement element,
        out XAttribute attribute,
        out SourceRange attributeValueRange)
    {
        element = null!;
        attribute = null!;
        attributeValueRange = default;
        if (analysis.XmlDocument?.Root is null)
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
                        out var candidateValueRange) ||
                    !ContainsPosition(analysis.Document.Text, candidateValueRange, position))
                {
                    continue;
                }

                element = candidateElement;
                attribute = candidateAttribute;
                attributeValueRange = candidateValueRange;
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateBindingContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        out BindingContext context)
    {
        context = default;
        if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(sourceText, attribute, out var attributeValueRange))
        {
            return false;
        }

        return TryCreateBindingContext(analysis, sourceText, element, attribute, attributeValueRange, out context);
    }

    private static bool TryCreateBindingContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        SourceRange attributeValueRange,
        out BindingContext context)
    {
        context = default;
        if (analysis.Compilation is null ||
            analysis.TypeIndex is null ||
            string.IsNullOrWhiteSpace(attribute.Value) ||
            !BindingEventMarkupParser.TryParseBindingMarkup(
                attribute.Value,
                TryParseMarkupExtension,
                out var bindingMarkup))
        {
            return false;
        }

        var valueStartOffset = TextCoordinateHelper.GetOffset(sourceText, attributeValueRange.Start);
        if (valueStartOffset < 0 ||
            !TryParseMarkupExtensionSpan(
                attribute.Value,
                valueStartOffset,
                out var markupSpanInfo))
        {
            return false;
        }

        context = new BindingContext(
            Analysis: analysis,
            SourceText: sourceText,
            Element: element,
            Attribute: attribute,
            BindingMarkup: bindingMarkup,
            PrefixMap: XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element),
            AttributeValueRange: attributeValueRange,
            MarkupSpanInfo: markupSpanInfo,
            DocumentOffset: -1);
        return true;
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return MarkupParser.TryParseMarkupExtension(value, out markupExtension);
    }

    private static bool TryParseMarkupExtensionSpan(
        string attributeValue,
        int valueStartOffset,
        out MarkupSpanInfo markupSpanInfo)
    {
        return XamlMarkupExtensionSpanParser.TryParse(attributeValue, valueStartOffset, out markupSpanInfo);
    }

    private static bool TryResolveArgumentSpan(
        ImmutableArray<MarkupArgumentSpan> arguments,
        string argumentName,
        out MarkupArgumentSpan argumentSpan)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index].Name, argumentName, StringComparison.OrdinalIgnoreCase))
            {
                argumentSpan = arguments[index];
                return true;
            }
        }

        argumentSpan = default;
        return false;
    }

    private static bool TryGetNormalizedPathValueSpan(
        string sourceText,
        MarkupSpanInfo markupSpanInfo,
        out int normalizedPathStart,
        out int normalizedPathLength,
        out string normalizedPath)
    {
        normalizedPathStart = 0;
        normalizedPathLength = 0;
        normalizedPath = string.Empty;

        MarkupArgumentSpan? candidateArgument = null;
        if (TryResolveArgumentSpan(markupSpanInfo.Arguments, "Path", out var pathArgument))
        {
            candidateArgument = pathArgument;
        }
        else if (markupSpanInfo.Arguments.FirstOrDefault(static argument => argument.Name is null) is { Length: > 0 } positionalArgument)
        {
            candidateArgument = positionalArgument;
        }

        if (candidateArgument is not { } argument ||
            argument.ValueLength <= 0)
        {
            return false;
        }

        normalizedPathStart = argument.ValueStart;
        normalizedPathLength = argument.ValueLength;
        NormalizeQuotedToken(sourceText, normalizedPathStart, normalizedPathLength, out normalizedPathStart, out normalizedPathLength);
        normalizedPath = normalizedPathLength > 0
            ? sourceText.Substring(normalizedPathStart, normalizedPathLength).Trim()
            : string.Empty;
        return normalizedPath.Length > 0;
    }

    private static bool TryTokenizeBindingPath(
        string path,
        out ImmutableArray<BindingPathSegmentToken> segmentTokens)
    {
        segmentTokens = ImmutableArray<BindingPathSegmentToken>.Empty;
        if (!CompiledBindingPathParser.TryParse(path, out _, out var leadingNotCount, out _))
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
                Start: trimmedStart,
                Length: trimmedEnd - trimmedStart,
                MemberName: attachedMemberName,
                MemberNameStart: trimmedStart + memberRawStart,
                MemberNameLength: memberRawLength,
                CastTypeToken: null,
                CastTypeTokenStart: 0,
                CastTypeTokenLength: 0,
                IsAttachedProperty: true,
                AttachedOwnerTypeToken: attachedOwnerTypeToken,
                AttachedOwnerTypeTokenStart: trimmedStart + ownerRawStart,
                AttachedOwnerTypeTokenLength: ownerRawLength,
                HasIndexers: segmentText.IndexOf('[', StringComparison.Ordinal) >= 0,
                IsMethodCall: false);
            return true;
        }

        var cursor = 0;
        string? castTypeToken = null;
        var castTypeTokenStart = 0;
        var castTypeTokenLength = 0;
        var requiresSegmentClosure = false;
        if (segmentText[cursor] == '(')
        {
            var castOpenIndex = 0;
            if (cursor + 1 < segmentText.Length && segmentText[cursor + 1] == '(')
            {
                requiresSegmentClosure = true;
                cursor++;
                castOpenIndex = cursor;
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

            castTypeTokenStart = trimmedStart + castOpenIndex + 1;
            castTypeTokenLength = rawCastTypeToken.Length;
            TrimToken(path, ref castTypeTokenStart, ref castTypeTokenLength);
            cursor = parseIndex;
            while (cursor < segmentText.Length && char.IsWhiteSpace(segmentText[cursor]))
            {
                cursor++;
            }
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
            Start: trimmedStart,
            Length: trimmedEnd - trimmedStart,
            MemberName: memberName,
            MemberNameStart: trimmedStart + memberStart,
            MemberNameLength: memberName.Length,
            CastTypeToken: castTypeToken,
            CastTypeTokenStart: castTypeTokenStart,
            CastTypeTokenLength: castTypeTokenLength,
            IsAttachedProperty: false,
            AttachedOwnerTypeToken: null,
            AttachedOwnerTypeTokenStart: 0,
            AttachedOwnerTypeTokenLength: 0,
            HasIndexers: segmentText.IndexOf('[', StringComparison.Ordinal) >= 0,
            IsMethodCall: isMethodCall);
        return true;
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

    private static void NormalizeQuotedToken(
        string sourceText,
        int start,
        int length,
        out int normalizedStart,
        out int normalizedLength)
    {
        normalizedStart = start;
        normalizedLength = length;
        while (normalizedLength > 0 && char.IsWhiteSpace(sourceText[normalizedStart]))
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

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        return ContainsOffset(range, offset, text);
    }

    private static bool ContainsOffset(SourceRange range, int offset, string text)
    {
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }

    private static SourceRange CreateRange(string text, int startOffset, int length)
    {
        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, startOffset),
            TextCoordinateHelper.GetPosition(text, startOffset + length));
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

    private readonly record struct BindingContext(
        XamlAnalysisResult Analysis,
        string SourceText,
        XElement Element,
        XAttribute Attribute,
        BindingMarkup BindingMarkup,
        ImmutableDictionary<string, string> PrefixMap,
        SourceRange AttributeValueRange,
        MarkupSpanInfo MarkupSpanInfo,
        int DocumentOffset);

    private readonly record struct BindingPathSegmentToken(
        int Start,
        int Length,
        string MemberName,
        int MemberNameStart,
        int MemberNameLength,
        string? CastTypeToken,
        int CastTypeTokenStart,
        int CastTypeTokenLength,
        bool IsAttachedProperty,
        string? AttachedOwnerTypeToken,
        int AttachedOwnerTypeTokenStart,
        int AttachedOwnerTypeTokenLength,
        bool HasIndexers,
        bool IsMethodCall);
}
