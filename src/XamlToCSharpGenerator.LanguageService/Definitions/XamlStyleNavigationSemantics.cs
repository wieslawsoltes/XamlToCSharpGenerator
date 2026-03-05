using System;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlStyleNavigationSemantics
{
    public static bool IsSelectorAttribute(string? attributeName)
    {
        return string.Equals(GetLocalName(attributeName), "Selector", StringComparison.Ordinal);
    }

    public static bool IsSetterPropertyAttribute(string? elementName, string? attributeName)
    {
        return string.Equals(GetLocalName(elementName), "Setter", StringComparison.Ordinal) &&
               string.Equals(GetLocalName(attributeName), "Property", StringComparison.Ordinal);
    }

    public static bool TryResolveSelectorTypeToken(
        string? selectorToken,
        string identifier,
        out string typeToken)
    {
        typeToken = string.Empty;
        if (string.IsNullOrWhiteSpace(selectorToken) || string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        foreach (var reference in SelectorReferenceSemantics.EnumerateReferences(selectorToken))
        {
            if (reference.Kind != SelectorReferenceKind.Type ||
                string.IsNullOrWhiteSpace(reference.Name))
            {
                continue;
            }

            if (!IdentifierMatchesTypeToken(reference.Name, identifier))
            {
                continue;
            }

            typeToken = reference.Name;
            return true;
        }

        return false;
    }

    public static bool TryExtractTargetTypeTokenFromSelector(string? selector, out string typeToken)
    {
        typeToken = string.Empty;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        var validation = SelectorSyntaxValidator.Validate(selector);
        if (!validation.IsValid || validation.Branches.IsEmpty)
        {
            return false;
        }

        string? candidate = null;
        foreach (var branch in validation.Branches)
        {
            if (string.IsNullOrWhiteSpace(branch.LastTypeToken))
            {
                return false;
            }

            if (candidate is null)
            {
                candidate = branch.LastTypeToken;
                continue;
            }

            if (!string.Equals(candidate, branch.LastTypeToken, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        typeToken = candidate;
        return true;
    }

    public static bool TryResolveStyleSetterOwnerTypeToken(
        XamlAnalysisResult analysis,
        SourcePosition position,
        string? propertyToken,
        out string ownerTypeToken)
    {
        ownerTypeToken = string.Empty;
        if (analysis.ParsedDocument is null || string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        var line = position.Line + 1;
        foreach (var style in analysis.ParsedDocument.Styles)
        {
            if (!ContainsSetter(style.Setters, line, propertyToken))
            {
                continue;
            }

            if (TryExtractTargetTypeTokenFromSelector(style.Selector, out ownerTypeToken))
            {
                return true;
            }
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            if (!ContainsSetter(controlTheme.Setters, line, propertyToken))
            {
                continue;
            }

            if (TryNormalizeControlThemeTargetType(controlTheme.TargetType, out ownerTypeToken))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryNormalizeControlThemeTargetType(string? rawTargetType, out string targetTypeToken)
    {
        targetTypeToken = string.Empty;
        if (string.IsNullOrWhiteSpace(rawTargetType))
        {
            return false;
        }

        var candidate = rawTargetType.Trim();
        if (MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(candidate, out var markupContent))
        {
            var inner = markupContent.Trim();
            if (!inner.StartsWith("x:Type", StringComparison.Ordinal))
            {
                return false;
            }

            candidate = inner.Substring("x:Type".Length).Trim();
            var commaIndex = candidate.IndexOf(',');
            if (commaIndex >= 0)
            {
                candidate = candidate.Substring(0, commaIndex).Trim();
            }
        }

        if (candidate.Length == 0)
        {
            return false;
        }

        targetTypeToken = candidate;
        return true;
    }

    private static bool ContainsSetter(
        System.Collections.Immutable.ImmutableArray<XamlSetterDefinition> setters,
        int line,
        string propertyToken)
    {
        foreach (var setter in setters)
        {
            if (setter.Line != line)
            {
                continue;
            }

            if (DoesSetterMatchPropertyToken(setter.PropertyName, propertyToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoesSetterMatchPropertyToken(string? setterPropertyName, string propertyToken)
    {
        if (string.IsNullOrWhiteSpace(setterPropertyName))
        {
            return false;
        }

        var trimmedSetterProperty = setterPropertyName.Trim();
        var trimmedPropertyToken = propertyToken.Trim();
        if (string.Equals(trimmedSetterProperty, trimmedPropertyToken, StringComparison.Ordinal))
        {
            return true;
        }

        if (!trimmedSetterProperty.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var separator = trimmedSetterProperty.LastIndexOf('.');
        if (separator < 0 || separator + 1 >= trimmedSetterProperty.Length)
        {
            return false;
        }

        return string.Equals(
            trimmedSetterProperty.Substring(separator + 1),
            trimmedPropertyToken,
            StringComparison.Ordinal);
    }

    private static bool IdentifierMatchesTypeToken(string typeToken, string identifier)
    {
        if (string.Equals(typeToken, identifier, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedIdentifier = identifier;
        var selectorSuffixSeparator = normalizedIdentifier.IndexOf('.');
        if (selectorSuffixSeparator > 0)
        {
            normalizedIdentifier = normalizedIdentifier.Substring(0, selectorSuffixSeparator);
        }

        if (string.Equals(typeToken, normalizedIdentifier, StringComparison.Ordinal))
        {
            return true;
        }

        var localToken = typeToken;
        var prefixSeparator = localToken.IndexOf(':');
        if (prefixSeparator >= 0 && prefixSeparator + 1 < localToken.Length)
        {
            localToken = localToken.Substring(prefixSeparator + 1);
        }

        if (string.Equals(localToken, normalizedIdentifier, StringComparison.Ordinal))
        {
            return true;
        }

        var nestedSeparator = localToken.LastIndexOf('.');
        return nestedSeparator >= 0 &&
               nestedSeparator + 1 < localToken.Length &&
               string.Equals(localToken.Substring(nestedSeparator + 1), normalizedIdentifier, StringComparison.Ordinal);
    }

    private static string GetLocalName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var separator = name.IndexOf(':');
        return separator >= 0 && separator + 1 < name.Length
            ? name.Substring(separator + 1)
            : name;
    }
}
