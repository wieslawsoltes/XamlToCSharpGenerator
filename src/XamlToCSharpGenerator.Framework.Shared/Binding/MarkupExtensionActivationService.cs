using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class MarkupExtensionActivationService
{
    public delegate bool TryConvertMarkupArgumentDelegate(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);

    private readonly Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> _resolveTypeToken;
    private readonly Func<Compilation, INamedTypeSymbol?> _resolveMarkupExtensionBaseType;
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;
    private readonly TryConvertMarkupArgumentDelegate _tryConvertValueExpression;
    private readonly TryConvertMarkupArgumentDelegate _tryConvertMarkupExtensionExpression;
    private readonly Func<string, string> _escape;
    private readonly MarkupTypeConversionService _markupTypeConversionService;
    private readonly MarkupContextTokenSet _markupContextTokens;

    public MarkupExtensionActivationService(
        Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> resolveTypeToken,
        Func<Compilation, INamedTypeSymbol?> resolveMarkupExtensionBaseType,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        TryConvertMarkupArgumentDelegate tryConvertValueExpression,
        TryConvertMarkupArgumentDelegate tryConvertMarkupExtensionExpression,
        Func<string, string> escape,
        MarkupTypeConversionService markupTypeConversionService,
        MarkupContextTokenSet markupContextTokens)
    {
        _resolveTypeToken = resolveTypeToken;
        _resolveMarkupExtensionBaseType = resolveMarkupExtensionBaseType;
        _isTypeAssignableTo = isTypeAssignableTo;
        _tryConvertValueExpression = tryConvertValueExpression;
        _tryConvertMarkupExtensionExpression = tryConvertMarkupExtensionExpression;
        _escape = escape;
        _markupTypeConversionService = markupTypeConversionService;
        _markupContextTokens = markupContextTokens;
    }

    public bool TryResolveExtensionType(
        Compilation compilation,
        XamlDocumentModel document,
        string markupName,
        out INamedTypeSymbol? extensionType)
    {
        extensionType = null;
        var markupExtensionBaseType = _resolveMarkupExtensionBaseType(compilation);
        foreach (var candidateToken in XamlMarkupExtensionNameSemantics.EnumerateClrExtensionTypeTokens(markupName))
        {
            var candidateType = _resolveTypeToken(compilation, document, candidateToken);
            if (candidateType is null)
            {
                continue;
            }

            if (markupExtensionBaseType is not null && !_isTypeAssignableTo(candidateType, markupExtensionBaseType))
            {
                continue;
            }

            extensionType = candidateType;
            return true;
        }

        return false;
    }

    public bool TryConvertGenericExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryResolveExtensionType(compilation, document, markup.Name, out var extensionType) || extensionType is null)
        {
            return false;
        }

        var constructor = extensionType.InstanceConstructors
            .Where(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                !candidate.IsStatic)
            .Where(candidate => candidate.Parameters.Length == markup.PositionalArguments.Length)
            .OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
        if (constructor is null && markup.PositionalArguments.Length > 0)
        {
            return false;
        }

        var positionalExpressions = new List<string>(markup.PositionalArguments.Length);
        for (var index = 0; index < markup.PositionalArguments.Length; index++)
        {
            var positionalTargetType = constructor?.Parameters[index].Type;
            if (!TryConvertMarkupArgumentExpression(
                    markup.PositionalArguments[index],
                    positionalTargetType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var positionalExpression))
            {
                return false;
            }

            positionalExpressions.Add(positionalExpression);
        }

        var objectCreationExpression =
            "new " +
            extensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" +
            string.Join(", ", positionalExpressions) +
            ")";

        var initializerExpressions = new List<string>();
        foreach (var namedArgument in markup.NamedArguments)
        {
            var property = extensionType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate =>
                    candidate.SetMethod is not null &&
                    candidate.Name.Equals(namedArgument.Key, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                return false;
            }

            if (!TryConvertMarkupArgumentExpression(
                    namedArgument.Value,
                    property.Type,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var propertyExpression))
            {
                return false;
            }

            initializerExpressions.Add(property.Name + " = " + propertyExpression);
        }

        if (initializerExpressions.Count > 0)
        {
            objectCreationExpression += " { " + string.Join(", ", initializerExpressions) + " }";
        }

        expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(" +
            objectCreationExpression + ", " +
            _markupContextTokens.ServiceProviderToken + ", " +
            _markupContextTokens.RootObjectToken + ", " +
            _markupContextTokens.IntermediateRootObjectToken + ", " +
            _markupContextTokens.TargetObjectToken + ", " +
            _markupContextTokens.TargetPropertyToken + ", " +
            _markupContextTokens.BaseUriToken + ", " +
            _markupContextTokens.ParentStackToken + ")";
        expression = _markupTypeConversionService.WrapWithTargetTypeCast(targetType, expression);
        return true;
    }

    private bool TryConvertMarkupArgumentExpression(
        string rawValue,
        ITypeSymbol? targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            expression = "null";
            return true;
        }

        var conversionTargetType = targetType ?? compilation.ObjectType;
        if (conversionTargetType is null)
        {
            return false;
        }

        var unquotedValue = XamlQuotedValueSemantics.TrimAndUnquote(value);
        if (_tryConvertValueExpression(
                unquotedValue,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (_tryConvertMarkupExtensionExpression(
                value,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (conversionTargetType.SpecialType == SpecialType.System_Object)
        {
            expression = "\"" + _escape(unquotedValue) + "\"";
            return true;
        }

        return false;
    }
}
