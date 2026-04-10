using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class PropertyAliasResolutionService
{
    public delegate int GetTargetMatchScoreDelegate(ResolvedPropertyAliasRule rule, INamedTypeSymbol targetType);

    private readonly GetTargetMatchScoreDelegate _getTargetMatchScore;
    private readonly Func<string, string> _normalizePropertyName;
    private readonly Func<string, string> _propertyNameFromField;

    public PropertyAliasResolutionService(
        GetTargetMatchScoreDelegate getTargetMatchScore,
        Func<string, string> normalizePropertyName,
        Func<string, string> propertyNameFromField)
    {
        _getTargetMatchScore = getTargetMatchScore ?? throw new ArgumentNullException(nameof(getTargetMatchScore));
        _normalizePropertyName = normalizePropertyName ?? throw new ArgumentNullException(nameof(normalizePropertyName));
        _propertyNameFromField = propertyNameFromField ?? throw new ArgumentNullException(nameof(propertyNameFromField));
    }

    public PropertyAliasResolution Resolve(
        ImmutableArray<ResolvedPropertyAliasRule> aliases,
        INamedTypeSymbol targetType,
        string propertyToken)
    {
        var normalizedPropertyName = _normalizePropertyName(propertyToken);
        ResolvedPropertyAliasRule? bestRule = null;
        var bestScore = -1;

        foreach (var alias in aliases)
        {
            if (!string.Equals(
                    _normalizePropertyName(alias.XamlPropertyName),
                    normalizedPropertyName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var score = _getTargetMatchScore(alias, targetType);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestRule = alias;
        }

        if (bestRule is null)
        {
            return new PropertyAliasResolution(normalizedPropertyName);
        }

        var resolvedPropertyName = !string.IsNullOrWhiteSpace(bestRule.ClrPropertyName)
            ? bestRule.ClrPropertyName!
            : !string.IsNullOrWhiteSpace(bestRule.PropertyFieldName)
                ? _propertyNameFromField(bestRule.PropertyFieldName!)
                : normalizedPropertyName;

        return new PropertyAliasResolution(
            resolvedPropertyName,
            bestRule.FrameworkId,
            bestRule.PropertyOwnerTypeName,
            bestRule.PropertyOwnerTypeSymbol,
            bestRule.PropertyFieldName);
    }
}
