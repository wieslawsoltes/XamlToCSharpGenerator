using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public enum SelectorReferenceKind
{
    Unknown = 0,
    Type = 1,
    StyleClass = 2,
    PseudoClass = 3,
    NamedElement = 4,
}

public readonly struct SelectorReference
{
    public SelectorReference(
        SelectorReferenceKind kind,
        string name,
        int start,
        int length,
        string? typeContextToken)
    {
        Kind = kind;
        Name = name;
        Start = start;
        Length = length;
        TypeContextToken = typeContextToken;
    }

    public SelectorReferenceKind Kind { get; }

    public string Name { get; }

    public int Start { get; }

    public int Length { get; }

    public string? TypeContextToken { get; }
}

public static class SelectorReferenceSemantics
{
    public static ImmutableArray<SelectorReference> EnumerateReferences(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return ImmutableArray<SelectorReference>.Empty;
        }

        var validation = SelectorSyntaxValidator.Validate(selector);
        if (!validation.IsValid)
        {
            return ImmutableArray<SelectorReference>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SelectorReference>();
        EnumerateSelectorList(selector, 0, builder);
        return builder.ToImmutable();
    }

    public static bool TryFindReferenceAtOffset(
        string selector,
        int offset,
        out SelectorReference reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(selector) || offset < 0)
        {
            return false;
        }

        foreach (var current in EnumerateReferences(selector))
        {
            var start = current.Start;
            var end = current.Start + current.Length;
            if (offset < start || offset > end)
            {
                continue;
            }

            reference = current;
            return true;
        }

        return false;
    }

    private static void EnumerateSelectorList(
        string selectorList,
        int offsetBase,
        ImmutableArray<SelectorReference>.Builder builder)
    {
        var rawBranches = TopLevelTextParser.SplitTopLevelSegments(
            selectorList,
            ',',
            trimTokens: false,
            removeEmpty: false);
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
                continue;
            }

            EnumerateBranch(
                rawText.Substring(trimStart, trimEnd - trimStart),
                branchStartOffset + trimStart,
                builder);
        }
    }

    private static void EnumerateBranch(
        string selectorBranch,
        int branchOffset,
        ImmutableArray<SelectorReference>.Builder builder)
    {
        if (!SelectorBranchTokenizer.TryTokenize(selectorBranch, out var segments))
        {
            return;
        }

        var currentTypeToken = default(string);
        var currentTypeContext = false;

        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            var absoluteSegmentOffset = branchOffset + segment.Start;
            if (segmentIndex > 0 || segment.Combinator != SelectorCombinatorKind.None)
            {
                currentTypeToken = null;
                currentTypeContext = false;
            }

            EnumerateSegmentReferences(
                segment.Text,
                absoluteSegmentOffset,
                ref currentTypeContext,
                ref currentTypeToken,
                builder);
        }
    }

    private static void EnumerateSegmentReferences(
        string segmentText,
        int segmentOffset,
        ref bool currentTypeContext,
        ref string? currentTypeToken,
        ImmutableArray<SelectorReference>.Builder builder)
    {
        var index = 0;
        while (index < segmentText.Length && segmentText[index] == '^')
        {
            index++;
            currentTypeContext = true;
            currentTypeToken = null;
        }

        var typeProbeIndex = index;
        if (!SelectorBranchTokenizer.TryReadTypeToken(segmentText, ref index, out var typeToken))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(typeToken))
        {
            var resolvedTypeToken = typeToken!;
            var typeTokenOffset = typeProbeIndex;
            while (typeTokenOffset < segmentText.Length && char.IsWhiteSpace(segmentText[typeTokenOffset]))
            {
                typeTokenOffset++;
            }

            currentTypeContext = true;
            currentTypeToken = resolvedTypeToken;
            builder.Add(new SelectorReference(
                SelectorReferenceKind.Type,
                resolvedTypeToken,
                segmentOffset + typeTokenOffset,
                resolvedTypeToken.Length,
                currentTypeToken));
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
                currentTypeContext = true;
                currentTypeToken = null;
                continue;
            }

            if (token == '[')
            {
                if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref index, '[', ']', out _))
                {
                    return;
                }

                continue;
            }

            if (token == '.')
            {
                index++;
                var classNameStart = index;
                if (!SelectorTokenSyntax.TryParseStyleClassToken(segmentText, ref index, out var className))
                {
                    return;
                }

                builder.Add(new SelectorReference(
                    SelectorReferenceKind.StyleClass,
                    className,
                    segmentOffset + classNameStart,
                    className.Length,
                    currentTypeToken));
                continue;
            }

            if (token == '#')
            {
                index++;
                var nameStart = index;
                if (!SelectorTokenSyntax.TryParseIdentifierToken(segmentText, ref index, out var name))
                {
                    return;
                }

                builder.Add(new SelectorReference(
                    SelectorReferenceKind.NamedElement,
                    name,
                    segmentOffset + nameStart,
                    name.Length,
                    currentTypeToken));

                continue;
            }

            if (token == ':')
            {
                index++;
                var pseudoNameStart = index;
                if (!SelectorTokenSyntax.TryParseStyleClassToken(segmentText, ref index, out var pseudoName))
                {
                    return;
                }

                if (index < segmentText.Length && segmentText[index] == '(')
                {
                    var pseudoArgumentOffset = segmentOffset + index + 1;
                    if (!TopLevelTextParser.TryReadBalancedContent(segmentText, ref index, '(', ')', out var pseudoArgument))
                    {
                        return;
                    }

                    if (!EnumeratePseudoFunction(
                            pseudoName,
                            pseudoArgument,
                            pseudoArgumentOffset,
                            ref currentTypeContext,
                            ref currentTypeToken,
                            builder))
                    {
                        return;
                    }

                    continue;
                }

                builder.Add(new SelectorReference(
                    SelectorReferenceKind.PseudoClass,
                    NormalizePseudoClassName(pseudoName),
                    segmentOffset + pseudoNameStart,
                    pseudoName.Length,
                    currentTypeToken));
                continue;
            }

            return;
        }
    }

    private static bool EnumeratePseudoFunction(
        string pseudoName,
        string pseudoArgument,
        int pseudoArgumentOffset,
        ref bool currentTypeContext,
        ref string? currentTypeToken,
        ImmutableArray<SelectorReference>.Builder builder)
    {
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
                    return false;
                }

                currentTypeContext = true;
                currentTypeToken = typeToken;
                builder.Add(new SelectorReference(
                    SelectorReferenceKind.Type,
                    typeToken,
                    typeArgumentOffset,
                    typeToken.Length,
                    currentTypeToken));
                return true;
            }
            case SelectorPseudoFunctionKind.Not:
                EnumerateSelectorList(pseudoArgument, pseudoArgumentOffset, builder);
                return true;
            case SelectorPseudoFunctionKind.NthChild:
            case SelectorPseudoFunctionKind.NthLastChild:
                return true;
            default:
                return false;
        }
    }

    private static string NormalizePseudoClassName(string pseudoName)
    {
        if (string.IsNullOrWhiteSpace(pseudoName))
        {
            return string.Empty;
        }

        return pseudoName[0] == ':'
            ? pseudoName
            : ":" + pseudoName;
    }
}
