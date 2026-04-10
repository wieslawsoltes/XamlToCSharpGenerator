using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ResolveByNameBindingService
{
    private readonly Func<INamedTypeSymbol, string, IPropertySymbol?> _findProperty;
    private readonly Func<string, string> _escape;
    private readonly Func<ITypeSymbol, string, string> _wrapWithTargetTypeCast;
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly MarkupContextTokenSet _markupContextTokens;
    private readonly ImmutableHashSet<string> _attributeNames;

    public ResolveByNameBindingService(
        Func<INamedTypeSymbol, string, IPropertySymbol?> findProperty,
        Func<string, string> escape,
        Func<ITypeSymbol, string, string> wrapWithTargetTypeCast,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        MarkupContextTokenSet markupContextTokens,
        ImmutableArray<string> attributeNames)
    {
        _findProperty = findProperty;
        _escape = escape;
        _wrapWithTargetTypeCast = wrapWithTargetTypeCast;
        _tryParseMarkupExtension = tryParseMarkupExtension;
        _markupContextTokens = markupContextTokens;
        _attributeNames = attributeNames.ToImmutableHashSet(StringComparer.Ordinal);
    }

    public bool HasSemantics(INamedTypeSymbol ownerType, string propertyName)
    {
        var property = _findProperty(ownerType, propertyName);
        if (property is not null)
        {
            foreach (var attribute in property.GetAttributes())
            {
                var attributeName = attribute.AttributeClass?.Name;
                if (!string.IsNullOrWhiteSpace(attributeName) && _attributeNames.Contains(attributeName!))
                {
                    return true;
                }
            }
        }

        foreach (var method in ownerType.GetMembers().OfType<IMethodSymbol>())
        {
            if (!method.IsStatic)
            {
                continue;
            }

            if (!string.Equals(method.Name, "Set" + propertyName, StringComparison.Ordinal) &&
                !string.Equals(method.Name, "Get" + propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var attribute in method.GetAttributes())
            {
                var attributeName = attribute.AttributeClass?.Name;
                if (!string.IsNullOrWhiteSpace(attributeName) && _attributeNames.Contains(attributeName!))
                {
                    return true;
                }
            }
        }

        foreach (var attribute in ownerType.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.Name;
            if (!string.IsNullOrWhiteSpace(attributeName) && _attributeNames.Contains(attributeName!))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryBuildLiteralExpression(
        string rawValue,
        ITypeSymbol targetType,
        out string expression)
    {
        expression = string.Empty;
        if (!BindingEventMarkupParser.TryParseResolveByNameReferenceToken(rawValue, _tryParseMarkupExtension, out var referenceToken))
        {
            return false;
        }

        expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReference(\"" +
            _escape(referenceToken.Name) +
            "\", " +
            _markupContextTokens.ServiceProviderToken +
            ", " +
            _markupContextTokens.RootObjectToken +
            ", " +
            _markupContextTokens.IntermediateRootObjectToken +
            ", " +
            _markupContextTokens.TargetObjectToken +
            ", " +
            _markupContextTokens.TargetPropertyToken +
            ", " +
            _markupContextTokens.BaseUriToken +
            ", " +
            _markupContextTokens.ParentStackToken +
            ")";
        expression = _wrapWithTargetTypeCast(targetType, expression);
        return true;
    }
}
