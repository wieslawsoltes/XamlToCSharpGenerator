using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public readonly struct SelectorBranchInfo
{
    public SelectorBranchInfo(string? lastTypeToken, int lastTypeOffset)
    {
        LastTypeToken = lastTypeToken;
        LastTypeOffset = lastTypeOffset;
    }

    public string? LastTypeToken { get; }

    public int LastTypeOffset { get; }
}

public readonly struct SelectorValidationResult
{
    public SelectorValidationResult(ImmutableArray<SelectorBranchInfo> branches)
    {
        IsValid = true;
        Branches = branches;
        ErrorMessage = string.Empty;
        ErrorOffset = 0;
    }

    public SelectorValidationResult(string errorMessage, int errorOffset)
    {
        IsValid = false;
        Branches = ImmutableArray<SelectorBranchInfo>.Empty;
        ErrorMessage = errorMessage;
        ErrorOffset = errorOffset;
    }

    public bool IsValid { get; }

    public ImmutableArray<SelectorBranchInfo> Branches { get; }

    public string ErrorMessage { get; }

    public int ErrorOffset { get; }
}

public static class SelectorSyntaxValidator
{
    public static SelectorValidationResult Validate(string selector)
    {
        if (!TryValidateSelectorList(
                selector ?? string.Empty,
                0,
                out var branches,
                out var errorMessage,
                out var errorOffset))
        {
            return new SelectorValidationResult(errorMessage, errorOffset);
        }

        return new SelectorValidationResult(branches);
    }

    private static bool TryValidateSelectorList(
        string selectorList,
        int offsetBase,
        out ImmutableArray<SelectorBranchInfo> branches,
        out string errorMessage,
        out int errorOffset)
    {
        branches = ImmutableArray<SelectorBranchInfo>.Empty;
        errorMessage = string.Empty;
        errorOffset = 0;

        var rawBranches = TopLevelTextParser.SplitTopLevelSegments(selectorList, ',', trimTokens: false, removeEmpty: false);
        if (rawBranches.Length == 0)
        {
            errorMessage = "Unexpected end of selector";
            errorOffset = offsetBase;
            return false;
        }

        var results = ImmutableArray.CreateBuilder<SelectorBranchInfo>(rawBranches.Length);
        for (var branchIndex = 0; branchIndex < rawBranches.Length; branchIndex++)
        {
            var rawBranch = rawBranches[branchIndex];
            var rawText = rawBranch.Text;
            var branchStartOffset = offsetBase + rawBranch.Start;

            var trimStart = 0;
            while (trimStart < rawText.Length && char.IsWhiteSpace(rawText[trimStart]))
            {
                trimStart++;
            }

            var trimEnd = rawText.Length;
            while (trimEnd > trimStart && char.IsWhiteSpace(rawText[trimEnd - 1]))
            {
                trimEnd--;
            }

            if (trimStart == trimEnd)
            {
                errorMessage = "Unexpected end of selector";
                errorOffset = branchStartOffset + trimStart;
                return false;
            }

            var trimmedBranch = rawText.Substring(trimStart, trimEnd - trimStart);
            var trimmedBranchOffset = branchStartOffset + trimStart;
            if (!TryValidateBranch(
                    trimmedBranch,
                    trimmedBranchOffset,
                    out var branchInfo,
                    out errorMessage,
                    out errorOffset))
            {
                return false;
            }

            results.Add(branchInfo);
        }

        branches = results.ToImmutable();
        return true;
    }

    private static bool TryValidateBranch(
        string selectorBranch,
        int branchOffset,
        out SelectorBranchInfo branchInfo,
        out string errorMessage,
        out int errorOffset)
    {
        branchInfo = default;
        errorMessage = string.Empty;
        errorOffset = 0;

        if (!SelectorBranchTokenizer.TryTokenize(selectorBranch, out var segments, out var tokenizationError, out var tokenizationOffset))
        {
            errorMessage = tokenizationError;
            errorOffset = branchOffset + tokenizationOffset;
            return false;
        }

        string? branchTypeToken = null;
        var branchTypeOffset = 0;
        var currentTypeContext = false;

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var absoluteSegmentOffset = branchOffset + segment.Start;
            if (index > 0 || segment.Combinator != SelectorCombinatorKind.None)
            {
                currentTypeContext = false;
                branchTypeToken = null;
                branchTypeOffset = 0;
            }

            if (!TryValidateSimpleSelectorSegment(
                    segment.Text,
                    absoluteSegmentOffset,
                    ref currentTypeContext,
                    ref branchTypeToken,
                    ref branchTypeOffset,
                    out errorMessage,
                    out errorOffset))
            {
                return false;
            }
        }

        branchInfo = new SelectorBranchInfo(branchTypeToken, branchTypeOffset);
        return true;
    }

    private static bool TryValidateSimpleSelectorSegment(
        string segmentText,
        int segmentOffset,
        ref bool currentTypeContext,
        ref string? branchTypeToken,
        ref int branchTypeOffset,
        out string errorMessage,
        out int errorOffset)
    {
        errorMessage = string.Empty;
        errorOffset = 0;

        var segmentApplied = false;
        var index = 0;
        while (index < segmentText.Length && segmentText[index] == '^')
        {
            index++;
            segmentApplied = true;
            currentTypeContext = true;
            branchTypeToken = null;
            branchTypeOffset = 0;
        }

        var typeProbeIndex = index;
        if (!SelectorBranchTokenizer.TryReadTypeToken(segmentText, ref index, out var typeToken))
        {
            errorMessage = "Expected an identifier, got end of selector.";
            errorOffset = segmentOffset + typeProbeIndex;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(typeToken))
        {
            var typeTokenOffset = typeProbeIndex;
            while (typeTokenOffset < segmentText.Length && char.IsWhiteSpace(segmentText[typeTokenOffset]))
            {
                typeTokenOffset++;
            }

            segmentApplied = true;
            currentTypeContext = true;
            branchTypeToken = typeToken;
            branchTypeOffset = segmentOffset + typeTokenOffset;
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

            var token = segmentText[index];
            if (token == '^')
            {
                index++;
                segmentApplied = true;
                currentTypeContext = true;
                branchTypeToken = null;
                branchTypeOffset = 0;
                continue;
            }

            if (token == '[')
            {
                if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref index, '[', ']', out var predicateText))
                {
                    errorMessage = "Expected ']', got end of selector.";
                    errorOffset = segmentOffset + index;
                    return false;
                }

                if (!TryValidatePropertySelector(predicateText, currentTypeContext, segmentOffset, out errorMessage, out errorOffset))
                {
                    return false;
                }

                segmentApplied = true;
                continue;
            }

            if (token == '.')
            {
                index++;
                if (!SelectorTokenSyntax.TryParseStyleClassToken(segmentText, ref index, out _))
                {
                    errorMessage = "Expected a class name after '.'.";
                    errorOffset = segmentOffset + index;
                    return false;
                }

                segmentApplied = true;
                continue;
            }

            if (token == '#')
            {
                index++;
                if (!SelectorTokenSyntax.TryParseIdentifierToken(segmentText, ref index, out _))
                {
                    errorMessage = "Expected a name after '#'.";
                    errorOffset = segmentOffset + index;
                    return false;
                }

                segmentApplied = true;
                continue;
            }

            if (token == ':')
            {
                index++;
                var pseudoNameStart = index;
                if (!SelectorTokenSyntax.TryParseStyleClassToken(segmentText, ref index, out var pseudoName))
                {
                    errorMessage = "Expected class name, is, nth-child or nth-last-child selector after ':'.";
                    errorOffset = segmentOffset + pseudoNameStart;
                    return false;
                }

                if (index < segmentText.Length && segmentText[index] == '(')
                {
                    var pseudoArgumentOffset = segmentOffset + index + 1;
                    if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref index, '(', ')', out var pseudoArgument))
                    {
                        errorMessage = "Expected ')', got end of selector.";
                        errorOffset = segmentOffset + index;
                        return false;
                    }

                    if (!TryValidatePseudoFunction(
                            pseudoName,
                            pseudoArgument,
                            pseudoArgumentOffset,
                            ref currentTypeContext,
                            ref branchTypeToken,
                            ref branchTypeOffset,
                            out errorMessage,
                            out errorOffset))
                    {
                        return false;
                    }
                }

                segmentApplied = true;
                continue;
            }

            errorMessage = "Unexpected token in selector.";
            errorOffset = segmentOffset + index;
            return false;
        }

        if (!segmentApplied)
        {
            errorMessage = "Unexpected end of selector";
            errorOffset = segmentOffset + index;
            return false;
        }

        return true;
    }

    private static bool TryValidatePseudoFunction(
        string pseudoName,
        string pseudoArgument,
        int pseudoArgumentOffset,
        ref bool currentTypeContext,
        ref string? branchTypeToken,
        ref int branchTypeOffset,
        out string errorMessage,
        out int errorOffset)
    {
        errorMessage = string.Empty;
        errorOffset = 0;

        switch (SelectorPseudoSyntax.ClassifyPseudoFunction(pseudoName))
        {
            case SelectorPseudoFunctionKind.Is:
            {
                var typeArgument = pseudoArgument.Trim();
                var typeArgumentOffset = pseudoArgumentOffset;
                while (typeArgumentOffset < pseudoArgumentOffset + pseudoArgument.Length &&
                       char.IsWhiteSpace(pseudoArgument[typeArgumentOffset - pseudoArgumentOffset]))
                {
                    typeArgumentOffset++;
                }

                if (!SelectorTokenSyntax.TryReadStandaloneTypeToken(typeArgument, out var typeToken))
                {
                    errorMessage = "Expected an identifier, got end of selector.";
                    errorOffset = typeArgumentOffset;
                    return false;
                }

                currentTypeContext = true;
                branchTypeToken = typeToken;
                branchTypeOffset = typeArgumentOffset;
                return true;
            }
            case SelectorPseudoFunctionKind.Not:
            {
                if (!TryValidateSelectorList(
                        pseudoArgument,
                        pseudoArgumentOffset,
                        out _,
                        out errorMessage,
                        out errorOffset))
                {
                    return false;
                }

                return true;
            }
            case SelectorPseudoFunctionKind.NthChild:
            case SelectorPseudoFunctionKind.NthLastChild:
            {
                if (!SelectorPseudoSyntax.TryParseNthChildExpression(pseudoArgument, out _, out _))
                {
                    errorMessage = "Couldn't parse nth-child arguments.";
                    errorOffset = pseudoArgumentOffset;
                    return false;
                }

                return true;
            }
        }

        errorMessage = "Expected class name, is, nth-child or nth-last-child selector after ':'.";
        errorOffset = pseudoArgumentOffset;
        return false;
    }

    private static bool TryValidatePropertySelector(
        string predicateText,
        bool currentTypeContext,
        int segmentOffset,
        out string errorMessage,
        out int errorOffset)
    {
        errorMessage = string.Empty;
        errorOffset = 0;

        if (!SelectorPropertyPredicateSyntax.TrySplit(predicateText, out var propertyText, out _))
        {
            errorMessage = "Expected '=', got end of selector.";
            errorOffset = segmentOffset;
            return false;
        }

        var equalsIndex = TopLevelTextParser.IndexOfTopLevel(predicateText, '=');

        var isAttachedProperty = false;
        var attachedParseKind = SelectorPropertyPredicateSyntax.TryParseAttachedProperty(
            propertyText,
            out _,
            out _);
        if (attachedParseKind == SelectorAttachedPropertyParseKind.ValidAttached)
        {
            isAttachedProperty = true;
        }
        else if (attachedParseKind == SelectorAttachedPropertyParseKind.InvalidAttached)
        {
            errorMessage = "Expected Attached Property Name, got end of selector.";
            errorOffset = segmentOffset;
            return false;
        }
        else if (!SelectorTokenSyntax.IsValidIdentifier(propertyText))
        {
            errorMessage = "Expected property name, got end of selector.";
            errorOffset = segmentOffset;
            return false;
        }

        if (!currentTypeContext)
        {
            errorMessage = isAttachedProperty
                ? "Attached Property selectors must be applied to a type."
                : "Property selectors must be applied to a type.";
            errorOffset = segmentOffset + equalsIndex + 1;
            return false;
        }

        return true;
    }
}
