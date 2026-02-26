using System;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public enum CompiledBindingAttachedPropertyParseStatus
{
    NotAttached = 0,
    Parsed,
    Invalid
}

public static class CompiledBindingPathSegmentSemantics
{
    public static CompiledBindingAttachedPropertyParseStatus TryParseAttachedPropertySegment(
        string path,
        int index,
        out string ownerTypeToken,
        out string memberName,
        out int nextIndex)
    {
        ownerTypeToken = string.Empty;
        memberName = string.Empty;
        nextIndex = index;

        if (string.IsNullOrEmpty(path) ||
            index < 0 ||
            index >= path.Length ||
            path[index] != '(' ||
            (index + 1 < path.Length && path[index + 1] == '('))
        {
            return CompiledBindingAttachedPropertyParseStatus.NotAttached;
        }

        var cursor = index;
        if (!TopLevelTextParser.TryReadBalancedContent(path, ref cursor, '(', ')', out var inner))
        {
            return CompiledBindingAttachedPropertyParseStatus.NotAttached;
        }

        if (!IsAttachedPropertyTail(path, cursor))
        {
            return CompiledBindingAttachedPropertyParseStatus.NotAttached;
        }

        if (!TrySplitAttachedOwnerAndMember(inner, out ownerTypeToken, out memberName))
        {
            return inner.IndexOf('.', StringComparison.Ordinal) >= 0
                ? CompiledBindingAttachedPropertyParseStatus.Invalid
                : CompiledBindingAttachedPropertyParseStatus.NotAttached;
        }

        nextIndex = cursor;
        return CompiledBindingAttachedPropertyParseStatus.Parsed;
    }

    public static bool TryParseCastTypeToken(
        string path,
        ref int index,
        out string castTypeToken,
        out bool requiresSegmentClosure,
        out string errorMessage)
    {
        castTypeToken = string.Empty;
        requiresSegmentClosure = false;
        errorMessage = string.Empty;

        if (string.IsNullOrEmpty(path) ||
            index < 0 ||
            index >= path.Length ||
            path[index] != '(')
        {
            errorMessage = "cast segment must start with '('";
            return false;
        }

        if (index + 1 < path.Length && path[index + 1] == '(')
        {
            requiresSegmentClosure = true;
            index++;
        }

        if (!TopLevelTextParser.TryReadBalancedContent(path, ref index, '(', ')', out var rawCastTypeToken))
        {
            errorMessage = "unterminated cast segment";
            return false;
        }

        castTypeToken = rawCastTypeToken.Trim();
        if (castTypeToken.Length == 0)
        {
            errorMessage = "cast type token cannot be empty";
            return false;
        }

        return true;
    }

    private static bool IsAttachedPropertyTail(string path, int index)
    {
        return index >= path.Length ||
               path[index] == '.' ||
               path[index] == '?' ||
               path[index] == '[' ||
               path[index] == '^';
    }

    private static bool TrySplitAttachedOwnerAndMember(
        string token,
        out string ownerTypeToken,
        out string memberName)
    {
        ownerTypeToken = string.Empty;
        memberName = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        var separatorIndex = trimmed.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1)
        {
            return false;
        }

        ownerTypeToken = trimmed.Substring(0, separatorIndex).Trim();
        memberName = trimmed.Substring(separatorIndex + 1).Trim();
        return ownerTypeToken.Length > 0 && memberName.Length > 0;
    }
}
