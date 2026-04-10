using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class CommonMarkupExtensionConversionService
{
    public delegate string BuildRelativeSourceExpressionDelegate(RelativeSourceMarkup relativeSource, ITypeSymbol targetType);

    public delegate bool TryConvertMarkupOptionDelegate(
        string? rawToken,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);

    private readonly Func<Compilation, TypeContractId, INamedTypeSymbol?> _resolveContractType;
    private readonly Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> _resolveTypeToken;
    private readonly ResourceKeyResolutionService.TryResolveStaticMemberExpressionDelegate _tryResolveStaticMemberExpression;
    private readonly TryConvertMarkupOptionDelegate _tryConvertOptionValue;
    private readonly TryParseRelativeSourceMarkupDelegate _tryParseRelativeSourceMarkup;
    private readonly BuildRelativeSourceExpressionDelegate _buildRelativeSourceExpression;

    public delegate bool TryParseRelativeSourceMarkupDelegate(
        string value,
        out RelativeSourceMarkup relativeSource);

    public CommonMarkupExtensionConversionService(
        Func<Compilation, TypeContractId, INamedTypeSymbol?> resolveContractType,
        Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> resolveTypeToken,
        ResourceKeyResolutionService.TryResolveStaticMemberExpressionDelegate tryResolveStaticMemberExpression,
        TryConvertMarkupOptionDelegate tryConvertOptionValue,
        TryParseRelativeSourceMarkupDelegate tryParseRelativeSourceMarkup,
        BuildRelativeSourceExpressionDelegate buildRelativeSourceExpression)
    {
        _resolveContractType = resolveContractType;
        _resolveTypeToken = resolveTypeToken;
        _tryResolveStaticMemberExpression = tryResolveStaticMemberExpression;
        _tryConvertOptionValue = tryConvertOptionValue;
        _tryParseRelativeSourceMarkup = tryParseRelativeSourceMarkup;
        _buildRelativeSourceExpression = buildRelativeSourceExpression;
    }

    public bool TryConvert(
        string rawValue,
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        string serviceProviderReference,
        out ResolvedValueConversionResult conversion)
    {
        _ = rawValue;
        _ = serviceProviderReference;
        conversion = default;
        switch (XamlMarkupExtensionNameSemantics.Classify(markup.Name))
        {
            case XamlMarkupExtensionKind.Null:
                conversion = new ResolvedValueConversionResult("null", ResolvedValueKind.Literal);
                return true;

            case XamlMarkupExtensionKind.Type:
            {
                var typeToken = markup.NamedArguments.TryGetValue("TypeName", out var namedTypeName)
                    ? namedTypeName
                    : (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : string.Empty);
                var resolvedType = string.IsNullOrWhiteSpace(typeToken) ? null : _resolveTypeToken(compilation, document, typeToken);
                if (resolvedType is null)
                {
                    return false;
                }

                conversion = new ResolvedValueConversionResult(
                    "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
                    ResolvedValueKind.MarkupExtension);
                return true;
            }

            case XamlMarkupExtensionKind.Static:
            {
                var memberToken = markup.NamedArguments.TryGetValue("Member", out var namedMember)
                    ? namedMember
                    : (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : string.Empty);
                if (!_tryResolveStaticMemberExpression(memberToken, compilation, document, out var staticMemberExpression))
                {
                    return false;
                }

                conversion = new ResolvedValueConversionResult(
                    staticMemberExpression,
                    ResolvedValueKind.MarkupExtension);
                return true;
            }

            case XamlMarkupExtensionKind.RelativeSource:
            {
                if (!_tryParseRelativeSourceMarkup("{" + markup.Name + "}", out var relativeSource))
                {
                    return false;
                }

                conversion = new ResolvedValueConversionResult(
                    _buildRelativeSourceExpression(relativeSource, targetType),
                    ResolvedValueKind.MarkupExtension,
                    RequiresRuntimeServiceProvider: true,
                    RequiresProvideValueTarget: true,
                    RequiresRootObject: true,
                    RequiresBaseUri: true);
                return true;
            }

            case XamlMarkupExtensionKind.OnPlatform:
                if (_tryConvertOptionValue(markup.NamedArguments.TryGetValue("Default", out var platformDefault) ? platformDefault : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var platformDefaultExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("Windows", out var windows) ? windows : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var windowsExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("macOS", out var macOs) ? macOs : markup.NamedArguments.TryGetValue("MacOS", out var macOsAlt) ? macOsAlt : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var macOsExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("Linux", out var linux) ? linux : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var linuxExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("Android", out var android) ? android : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var androidExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("iOS", out var ios) ? ios : markup.NamedArguments.TryGetValue("IOS", out var iosAlt) ? iosAlt : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var iosExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("Browser", out var browser) ? browser : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var browserExpression))
                {
                    var onPlatformExpression =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnPlatform(" +
                        platformDefaultExpression + ", " +
                        windowsExpression + ", " +
                        macOsExpression + ", " +
                        linuxExpression + ", " +
                        androidExpression + ", " +
                        iosExpression + ", " +
                        browserExpression + ")";
                    conversion = new ResolvedValueConversionResult(
                        WrapMarkupExtensionValue(targetType, onPlatformExpression),
                        ResolvedValueKind.MarkupExtension);
                    return true;
                }

                return false;

            case XamlMarkupExtensionKind.OnFormFactor:
                if (_tryConvertOptionValue(markup.NamedArguments.TryGetValue("Default", out var formFactorDefault) ? formFactorDefault : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var defaultExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("Desktop", out var desktop) ? desktop : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var desktopExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("Mobile", out var mobile) ? mobile : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var mobileExpression) &&
                    _tryConvertOptionValue(markup.NamedArguments.TryGetValue("TV", out var tv) ? tv : null, targetType, compilation, document, setterTargetType, bindingPriorityScope, out var tvExpression))
                {
                    var onFormFactorExpression =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(" +
                        defaultExpression + ", " +
                        desktopExpression + ", " +
                        mobileExpression + ", " +
                        tvExpression + ", " +
                        serviceProviderReference + ")";
                    conversion = new ResolvedValueConversionResult(
                        WrapMarkupExtensionValue(targetType, onFormFactorExpression),
                        ResolvedValueKind.MarkupExtension,
                        RequiresRuntimeServiceProvider: true);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static string WrapMarkupExtensionValue(ITypeSymbol targetType, string expression)
    {
        var effectiveTargetType = targetType.WithNullableAnnotation(NullableAnnotation.None);
        return effectiveTargetType.SpecialType == SpecialType.System_Object
            ? expression
            : "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<" +
              effectiveTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
              ">(" +
              expression +
              ")";
    }
}
