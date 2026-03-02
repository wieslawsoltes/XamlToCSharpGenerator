using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public interface ISelectorExpressionEmitter
{
    string EmitOr(ImmutableArray<string> branchExpressions);

    string EmitDescendant(string previousExpression);

    string EmitChild(string previousExpression);

    string EmitTemplate(string previousExpression);

    string EmitNesting(string previousExpressionOrNull);

    string EmitOfType(string previousExpressionOrNull, INamedTypeSymbol type);

    string EmitClass(string previousExpressionOrNull, string className);

    string EmitName(string previousExpressionOrNull, string name);

    string EmitPseudoClass(string previousExpressionOrNull, string pseudoClassName);

    string EmitIs(string previousExpressionOrNull, INamedTypeSymbol type);

    string EmitNot(string previousExpressionOrNull, string argumentExpression);

    string EmitNthChild(string previousExpressionOrNull, int step, int offset);

    string EmitNthLastChild(string previousExpressionOrNull, int step, int offset);

    string EmitPropertyEquals(string previousExpressionOrNull, string propertyExpression, string valueExpression);
}

public delegate bool SelectorPropertyPredicateResolver(
    string predicateText,
    INamedTypeSymbol? defaultOwnerType,
    out string propertyExpression,
    out string valueExpression);

public static class SelectorExpressionBuildSemantics
{
    public static bool TryBuildSelectorExpression(
        string selector,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        Func<string, INamedTypeSymbol?> resolveTypeToken,
        ISelectorExpressionEmitter emitter,
        SelectorPropertyPredicateResolver tryResolvePropertyPredicate,
        out string expression)
    {
        if (resolveTypeToken is null)
        {
            throw new ArgumentNullException(nameof(resolveTypeToken));
        }

        if (emitter is null)
        {
            throw new ArgumentNullException(nameof(emitter));
        }

        if (tryResolvePropertyPredicate is null)
        {
            throw new ArgumentNullException(nameof(tryResolvePropertyPredicate));
        }

        return TryBuildSelectorExpressionCore(
            selector,
            selectorTypeFallback,
            selectorNestingTypeHint,
            resolveTypeToken,
            emitter,
            tryResolvePropertyPredicate,
            out expression);
    }

    private static bool TryBuildSelectorExpressionCore(
        string selector,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        Func<string, INamedTypeSymbol?> resolveTypeToken,
        ISelectorExpressionEmitter emitter,
        SelectorPropertyPredicateResolver tryResolvePropertyPredicate,
        out string expression)
    {
        expression = string.Empty;
        var text = selector.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var validation = SelectorSyntaxValidator.Validate(text);
        if (!validation.IsValid)
        {
            return false;
        }

        var branchExpressions = ImmutableArray.CreateBuilder<string>();
        foreach (var branch in TopLevelTextParser.SplitTopLevel(text, ','))
        {
            var trimmedBranch = branch.Trim();
            if (trimmedBranch.Length == 0)
            {
                return false;
            }

            if (!TryBuildSelectorBranchExpression(
                    trimmedBranch,
                    selectorTypeFallback,
                    selectorNestingTypeHint,
                    resolveTypeToken,
                    emitter,
                    tryResolvePropertyPredicate,
                    out var branchExpression))
            {
                return false;
            }

            branchExpressions.Add(branchExpression);
        }

        if (branchExpressions.Count == 0)
        {
            return false;
        }

        expression = branchExpressions.Count == 1
            ? branchExpressions[0]
            : emitter.EmitOr(branchExpressions.ToImmutable());
        return true;
    }

    private static bool TryBuildSelectorBranchExpression(
        string selectorBranch,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        Func<string, INamedTypeSymbol?> resolveTypeToken,
        ISelectorExpressionEmitter emitter,
        SelectorPropertyPredicateResolver tryResolvePropertyPredicate,
        out string expression)
    {
        expression = string.Empty;
        if (!SelectorBranchTokenizer.TryTokenize(selectorBranch, out var segments))
        {
            return false;
        }

        var currentExpression = "null";
        var hasExpression = false;
        foreach (var segment in segments)
        {
            if (segment.Combinator != SelectorCombinatorKind.None)
            {
                if (!hasExpression)
                {
                    return false;
                }

                currentExpression = segment.Combinator switch
                {
                    SelectorCombinatorKind.Descendant => emitter.EmitDescendant(currentExpression),
                    SelectorCombinatorKind.Child => emitter.EmitChild(currentExpression),
                    SelectorCombinatorKind.Template => emitter.EmitTemplate(currentExpression),
                    _ => currentExpression
                };
            }

            var segmentText = segment.Text.Trim();
            if (segmentText.Length == 0)
            {
                return false;
            }

            var segmentApplied = false;
            var index = 0;
            INamedTypeSymbol? selectorTypeHint = selectorTypeFallback;
            while (index < segmentText.Length && segmentText[index] == '^')
            {
                if (selectorNestingTypeHint is null)
                {
                    return false;
                }

                currentExpression = emitter.EmitNesting(hasExpression ? currentExpression : "null");
                selectorTypeHint = selectorNestingTypeHint;
                hasExpression = true;
                segmentApplied = true;
                index++;
            }

            while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
            {
                index++;
            }

            if (SelectorBranchTokenizer.TryReadTypeToken(segmentText, ref index, out var typeToken))
            {
                if (!string.IsNullOrWhiteSpace(typeToken))
                {
                    var resolvedType = resolveTypeToken(typeToken!);
                    if (resolvedType is null)
                    {
                        return false;
                    }

                    currentExpression = emitter.EmitOfType(hasExpression ? currentExpression : "null", resolvedType);
                    selectorTypeHint = resolvedType;
                    hasExpression = true;
                    segmentApplied = true;
                }
            }
            else
            {
                return false;
            }

            while (index < segmentText.Length)
            {
                while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
                {
                    index++;
                }

                if (index >= segmentText.Length)
                {
                    break;
                }

                var tokenType = segmentText[index];
                if (tokenType == '^')
                {
                    if (selectorNestingTypeHint is null)
                    {
                        return false;
                    }

                    index++;
                    currentExpression = emitter.EmitNesting(hasExpression ? currentExpression : "null");
                    selectorTypeHint = selectorNestingTypeHint;
                    hasExpression = true;
                    segmentApplied = true;
                    continue;
                }

                if (tokenType == '[')
                {
                    if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref index, '[', ']', out var predicateText) ||
                        !tryResolvePropertyPredicate(
                            predicateText,
                            selectorTypeHint,
                            out var propertyExpression,
                            out var valueExpression))
                    {
                        return false;
                    }

                    currentExpression = emitter.EmitPropertyEquals(
                        hasExpression ? currentExpression : "null",
                        propertyExpression,
                        valueExpression);
                    hasExpression = true;
                    segmentApplied = true;
                    continue;
                }

                if (tokenType == '.')
                {
                    index++;
                    if (!SelectorTokenSyntax.TryParseStyleClassToken(segmentText, ref index, out var className))
                    {
                        return false;
                    }

                    currentExpression = emitter.EmitClass(hasExpression ? currentExpression : "null", className);
                    hasExpression = true;
                    segmentApplied = true;
                    continue;
                }

                if (tokenType == '#')
                {
                    index++;
                    if (!SelectorTokenSyntax.TryParseIdentifierToken(segmentText, ref index, out var name))
                    {
                        return false;
                    }

                    currentExpression = emitter.EmitName(hasExpression ? currentExpression : "null", name);
                    hasExpression = true;
                    segmentApplied = true;
                    continue;
                }

                if (tokenType != ':')
                {
                    return false;
                }

                index++;
                if (!SelectorTokenSyntax.TryParseStyleClassToken(segmentText, ref index, out var tokenValue))
                {
                    return false;
                }

                if (index < segmentText.Length && segmentText[index] == '(')
                {
                    if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref index, '(', ')', out var pseudoArgument) ||
                        !TryApplyPseudoFunction(
                            tokenValue,
                            pseudoArgument,
                            selectorTypeFallback,
                            selectorNestingTypeHint,
                            resolveTypeToken,
                            emitter,
                            tryResolvePropertyPredicate,
                            ref currentExpression,
                            ref hasExpression,
                            ref selectorTypeHint))
                    {
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                currentExpression = emitter.EmitPseudoClass(hasExpression ? currentExpression : "null", tokenValue);
                hasExpression = true;
                segmentApplied = true;
            }

            if (!segmentApplied)
            {
                return false;
            }
        }

        if (!hasExpression)
        {
            return false;
        }

        expression = currentExpression;
        return true;
    }

    private static bool TryApplyPseudoFunction(
        string pseudoName,
        string pseudoArgument,
        INamedTypeSymbol? selectorTypeFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        Func<string, INamedTypeSymbol?> resolveTypeToken,
        ISelectorExpressionEmitter emitter,
        SelectorPropertyPredicateResolver tryResolvePropertyPredicate,
        ref string currentExpression,
        ref bool hasExpression,
        ref INamedTypeSymbol? selectorTypeHint)
    {
        var pseudoKind = SelectorPseudoSyntax.ClassifyPseudoFunction(pseudoName);
        if (pseudoKind == SelectorPseudoFunctionKind.Unknown)
        {
            return false;
        }

        var previousExpression = hasExpression ? currentExpression : "null";
        if (pseudoKind == SelectorPseudoFunctionKind.Is)
        {
            if (!SelectorTokenSyntax.TryReadStandaloneTypeToken(pseudoArgument.Trim(), out var typeToken))
            {
                return false;
            }

            var resolvedType = resolveTypeToken(typeToken);
            if (resolvedType is null)
            {
                return false;
            }

            currentExpression = emitter.EmitIs(previousExpression, resolvedType);
            selectorTypeHint = resolvedType;
            hasExpression = true;
            return true;
        }

        if (pseudoKind == SelectorPseudoFunctionKind.Not)
        {
            if (!TryBuildSelectorExpressionCore(
                    pseudoArgument,
                    selectorTypeHint ?? selectorTypeFallback,
                    selectorNestingTypeHint,
                    resolveTypeToken,
                    emitter,
                    tryResolvePropertyPredicate,
                    out var argumentExpression))
            {
                return false;
            }

            currentExpression = emitter.EmitNot(previousExpression, argumentExpression);
            hasExpression = true;
            return true;
        }

        if (pseudoKind == SelectorPseudoFunctionKind.NthChild &&
            SelectorPseudoSyntax.TryParseNthChildExpression(pseudoArgument, out var nthChildStep, out var nthChildOffset))
        {
            currentExpression = emitter.EmitNthChild(previousExpression, nthChildStep, nthChildOffset);
            hasExpression = true;
            return true;
        }

        if (pseudoKind == SelectorPseudoFunctionKind.NthLastChild &&
            SelectorPseudoSyntax.TryParseNthChildExpression(pseudoArgument, out var nthLastChildStep, out var nthLastChildOffset))
        {
            currentExpression = emitter.EmitNthLastChild(previousExpression, nthLastChildStep, nthLastChildOffset);
            hasExpression = true;
            return true;
        }

        return false;
    }
}
