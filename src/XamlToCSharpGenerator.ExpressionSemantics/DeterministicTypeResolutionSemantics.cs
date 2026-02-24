using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public readonly struct DeterministicTypeSelectionResult
{
    public DeterministicTypeSelectionResult(
        INamedTypeSymbol? selectedCandidate,
        TypeResolutionAmbiguityInfo? ambiguity)
    {
        SelectedCandidate = selectedCandidate;
        Ambiguity = ambiguity;
    }

    public INamedTypeSymbol? SelectedCandidate { get; }

    public TypeResolutionAmbiguityInfo? Ambiguity { get; }
}

public sealed class TypeResolutionAmbiguityInfo
{
    public TypeResolutionAmbiguityInfo(
        string token,
        string strategy,
        ImmutableArray<string> candidateDisplayNames)
    {
        Token = token ?? string.Empty;
        Strategy = strategy ?? string.Empty;
        CandidateDisplayNames = candidateDisplayNames.IsDefault
            ? ImmutableArray<string>.Empty
            : candidateDisplayNames;

        var selectedName = CandidateDisplayNames.Length > 0
            ? CandidateDisplayNames[0]
            : string.Empty;
        DedupeKey = Token + "|" + Strategy + "|" + string.Join("|", CandidateDisplayNames);
        Message = $"Type resolution for '{Token}' is ambiguous via {Strategy}. Candidates: {string.Join(", ", CandidateDisplayNames)}. Using '{selectedName}' deterministically.";
    }

    public string Token { get; }

    public string Strategy { get; }

    public ImmutableArray<string> CandidateDisplayNames { get; }

    public string DedupeKey { get; }

    public string Message { get; }
}

public static class DeterministicTypeResolutionSemantics
{
    public static ImmutableArray<INamedTypeSymbol> CollectCandidatesFromNamespacePrefixes(
        Compilation compilation,
        IEnumerable<string> namespacePrefixes,
        string typeName,
        int? genericArity = null,
        bool extensionSuffix = false)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(namespacePrefixes);

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return ImmutableArray<INamedTypeSymbol>.Empty;
        }

        var candidates = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var effectiveTypeName = extensionSuffix
            ? typeName + "Extension"
            : AppendGenericArity(typeName, genericArity);

        foreach (var namespacePrefix in namespacePrefixes)
        {
            if (string.IsNullOrWhiteSpace(namespacePrefix))
            {
                continue;
            }

            var candidate = compilation.GetTypeByMetadataName(namespacePrefix + effectiveTypeName);
            if (candidate is not null && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates.ToImmutable();
    }

    public static DeterministicTypeSelectionResult SelectDeterministicCandidate(
        ImmutableArray<INamedTypeSymbol> candidates,
        string token,
        string strategy)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return default;
        }

        if (candidates.Length == 1)
        {
            return new DeterministicTypeSelectionResult(candidates[0], null);
        }

        var candidateNames = ImmutableArray.CreateBuilder<string>(candidates.Length);
        foreach (var candidate in candidates)
        {
            candidateNames.Add(candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        var ambiguity = new TypeResolutionAmbiguityInfo(
            token,
            strategy,
            candidateNames.ToImmutable());
        return new DeterministicTypeSelectionResult(candidates[0], ambiguity);
    }

    public static string AppendGenericArity(string typeName, int? genericArity)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return typeName ?? string.Empty;
        }

        if (!genericArity.HasValue || genericArity.Value <= 0 || typeName.IndexOf('`') >= 0)
        {
            return typeName;
        }

        return typeName + "`" + genericArity.Value.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParseGenericTypeToken(
        string token,
        out string typeToken,
        out ImmutableArray<string> argumentTokens)
    {
        typeToken = string.Empty;
        argumentTokens = ImmutableArray<string>.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = token.Trim();
        var openingIndex = IndexOfTopLevelOpeningParenthesis(normalized);
        if (openingIndex <= 0 || !normalized.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var typePart = normalized.Substring(0, openingIndex).Trim();
        var argumentPart = normalized.Substring(openingIndex + 1, normalized.Length - openingIndex - 2).Trim();
        if (typePart.Length == 0 || argumentPart.Length == 0)
        {
            return false;
        }

        var parsedArguments = TopLevelTextParser.SplitTopLevel(argumentPart, ',')
            .Select(static argument => argument.Trim())
            .Where(static argument => argument.Length > 0)
            .ToImmutableArray();
        if (parsedArguments.Length == 0)
        {
            return false;
        }

        typeToken = typePart;
        argumentTokens = parsedArguments;
        return true;
    }

    private static int IndexOfTopLevelOpeningParenthesis(string value)
    {
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            if (ch == '(' &&
                braceDepth == 0 &&
                bracketDepth == 0 &&
                parenthesisDepth == 0)
            {
                return index;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    break;
                default:
                    break;
            }
        }

        return -1;
    }

    public static string? TryBuildClrNamespaceMetadataName(
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity)
    {
        if (xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal) ||
            xmlNamespace.StartsWith("using:", StringComparison.Ordinal))
        {
            var segment = xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)
                ? xmlNamespace.Substring("clr-namespace:".Length)
                : xmlNamespace.Substring("using:".Length);
            var separatorIndex = segment.IndexOf(';');
            var clrNamespace = separatorIndex < 0 ? segment : segment.Substring(0, separatorIndex);
            if (!string.IsNullOrWhiteSpace(clrNamespace))
            {
                return clrNamespace + "." + AppendGenericArity(xmlTypeName, genericArity);
            }
        }

        return null;
    }
}
