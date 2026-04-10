using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class SetterValuePolicyResolutionService
{
    public delegate bool TryBuildRuntimeXamlFragmentExpressionDelegate(string value, string baseUri, out string expression);
    public delegate bool TryConvertValueConversionDelegate(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion,
        bool allowObjectStringLiteralFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        ImmutableArray<AttributeData> converterAttributes);
    public delegate bool TryGetUnsetValueExpressionDelegate(string value, out string expression);

    private readonly TryBuildRuntimeXamlFragmentExpressionDelegate _tryBuildRuntimeXamlFragmentExpression;
    private readonly TryConvertValueConversionDelegate _tryConvertValueConversion;
    private readonly TryGetUnsetValueExpressionDelegate _tryGetUnsetValueExpression;
    private readonly Func<string, string> _escape;

    public SetterValuePolicyResolutionService(
        TryBuildRuntimeXamlFragmentExpressionDelegate tryBuildRuntimeXamlFragmentExpression,
        TryConvertValueConversionDelegate tryConvertValueConversion,
        TryGetUnsetValueExpressionDelegate tryGetUnsetValueExpression,
        Func<string, string> escape)
    {
        _tryBuildRuntimeXamlFragmentExpression = tryBuildRuntimeXamlFragmentExpression ?? throw new ArgumentNullException(nameof(tryBuildRuntimeXamlFragmentExpression));
        _tryConvertValueConversion = tryConvertValueConversion ?? throw new ArgumentNullException(nameof(tryConvertValueConversion));
        _tryGetUnsetValueExpression = tryGetUnsetValueExpression ?? throw new ArgumentNullException(nameof(tryGetUnsetValueExpression));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    public bool TryResolve(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        bool preferTypedStaticResourceCoercion,
        bool allowObjectStringLiteralFallbackDuringConversion,
        bool allowCompatibilityStringLiteralFallback,
        ImmutableArray<AttributeData> converterAttributes,
        out ResolvedValueConversionResult conversion)
    {
        if (_tryGetUnsetValueExpression(value, out var unsetValueExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: unsetValueExpression,
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        var trimmedValue = value.TrimStart();
        if (trimmedValue.Length > 0 &&
            trimmedValue[0] == '<' &&
            _tryBuildRuntimeXamlFragmentExpression(
                value,
                baseUri: document.TargetPath,
                out var objectElementRuntimeXamlExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: objectElementRuntimeXamlExpression,
                ValueKind: ResolvedValueKind.MarkupExtension,
                RequiresRuntimeServiceProvider: true,
                RequiresParentStack: true,
                RequiresProvideValueTarget: true,
                RequiresRootObject: true,
                RequiresBaseUri: true,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        if (_tryConvertValueConversion(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallbackDuringConversion,
                selectorNestingTypeHint: null,
                converterAttributes))
        {
            return true;
        }

        if (allowCompatibilityStringLiteralFallback &&
            _tryBuildRuntimeXamlFragmentExpression(
                value,
                baseUri: document.TargetPath,
                out var runtimeXamlExpression))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: runtimeXamlExpression,
                ValueKind: ResolvedValueKind.MarkupExtension,
                RequiresRuntimeServiceProvider: true,
                RequiresParentStack: true,
                RequiresProvideValueTarget: true,
                RequiresRootObject: true,
                RequiresBaseUri: true,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        if (allowCompatibilityStringLiteralFallback &&
            type.SpecialType == SpecialType.System_Object)
        {
            conversion = new ResolvedValueConversionResult(
                Expression: "\"" + _escape(value.Trim()) + "\"",
                ValueKind: ResolvedValueKind.Literal);
            return true;
        }

        conversion = default;
        return false;
    }
}
