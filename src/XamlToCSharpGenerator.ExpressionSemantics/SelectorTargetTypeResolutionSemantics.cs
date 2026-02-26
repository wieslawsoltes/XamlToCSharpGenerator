using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public readonly struct SelectorTargetTypeResolutionResult
{
    public SelectorTargetTypeResolutionResult(
        INamedTypeSymbol? targetType,
        string? unresolvedTypeToken,
        int unresolvedTypeOffset)
    {
        TargetType = targetType;
        UnresolvedTypeToken = unresolvedTypeToken;
        UnresolvedTypeOffset = unresolvedTypeOffset;
    }

    public INamedTypeSymbol? TargetType { get; }

    public string? UnresolvedTypeToken { get; }

    public int UnresolvedTypeOffset { get; }
}

public static class SelectorTargetTypeResolutionSemantics
{
    public static SelectorTargetTypeResolutionResult ResolveTargetType(
        ImmutableArray<SelectorBranchInfo> branches,
        Func<string, INamedTypeSymbol?> resolveTypeToken,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo)
    {
        if (resolveTypeToken is null)
        {
            throw new ArgumentNullException(nameof(resolveTypeToken));
        }

        if (isTypeAssignableTo is null)
        {
            throw new ArgumentNullException(nameof(isTypeAssignableTo));
        }

        if (branches.Length == 0)
        {
            return default;
        }

        var resolvedTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>(branches.Length);
        foreach (var branch in branches)
        {
            if (string.IsNullOrWhiteSpace(branch.LastTypeToken))
            {
                return default;
            }

            var resolved = resolveTypeToken(branch.LastTypeToken!);
            if (resolved is null)
            {
                return new SelectorTargetTypeResolutionResult(
                    targetType: null,
                    unresolvedTypeToken: branch.LastTypeToken,
                    unresolvedTypeOffset: branch.LastTypeOffset);
            }

            resolvedTypes.Add(resolved);
        }

        if (resolvedTypes.Count == 0)
        {
            return default;
        }

        var targetType = resolvedTypes[0];
        for (var index = 1; index < resolvedTypes.Count && targetType is not null; index++)
        {
            var candidate = resolvedTypes[index];
            while (targetType is not null && !isTypeAssignableTo(candidate, targetType))
            {
                targetType = targetType.BaseType;
            }
        }

        return new SelectorTargetTypeResolutionResult(
            targetType: targetType,
            unresolvedTypeToken: null,
            unresolvedTypeOffset: 0);
    }
}
