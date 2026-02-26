using System;
using System.Collections.Immutable;
using System.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlMemberPathSemantics
{
    public static ImmutableArray<string> SplitPathSegments(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ImmutableArray<string>.Empty;
        }

        var trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        if (trimmed == ".")
        {
            return [trimmed];
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        var tokenBuilder = new StringBuilder(trimmed.Length);
        var roundDepth = 0;
        var squareDepth = 0;
        var quoteChar = '\0';
        var escaped = false;

        void FlushSegment()
        {
            if (tokenBuilder.Length == 0)
            {
                return;
            }

            var segment = tokenBuilder.ToString().Trim();
            tokenBuilder.Clear();
            if (segment.Length > 0)
            {
                segments.Add(segment);
            }
        }

        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            if (quoteChar != '\0')
            {
                tokenBuilder.Append(current);
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quoteChar)
                {
                    quoteChar = '\0';
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                quoteChar = current;
                tokenBuilder.Append(current);
                continue;
            }

            if (current == '(')
            {
                roundDepth++;
                tokenBuilder.Append(current);
                continue;
            }

            if (current == ')')
            {
                if (roundDepth > 0)
                {
                    roundDepth--;
                }

                tokenBuilder.Append(current);
                continue;
            }

            if (current == '[')
            {
                squareDepth++;
                tokenBuilder.Append(current);
                continue;
            }

            if (current == ']')
            {
                if (squareDepth > 0)
                {
                    squareDepth--;
                }

                tokenBuilder.Append(current);
                continue;
            }

            if (current == '.' && roundDepth == 0 && squareDepth == 0)
            {
                FlushSegment();
                continue;
            }

            tokenBuilder.Append(current);
        }

        FlushSegment();
        return segments.Count == 0
            ? ImmutableArray<string>.Empty
            : segments.ToImmutable();
    }

    public static string NormalizeSegmentForMemberLookup(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var normalized = segment.Trim();
        if (normalized.Length >= 2 &&
            normalized[0] == '(' &&
            normalized[normalized.Length - 1] == ')')
        {
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        }

        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(normalized, out _, out var propertyName))
        {
            normalized = propertyName;
        }

        var indexerStart = normalized.IndexOf('[');
        if (indexerStart > 0)
        {
            normalized = normalized.Substring(0, indexerStart).Trim();
        }

        return normalized;
    }
}
