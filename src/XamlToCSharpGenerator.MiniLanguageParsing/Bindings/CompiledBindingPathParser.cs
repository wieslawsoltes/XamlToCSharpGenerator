using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public static class CompiledBindingPathParser
{
    public static bool TryParse(
        string path,
        out ImmutableArray<CompiledBindingPathSegment> segments,
        out int leadingNotCount,
        out string errorMessage)
    {
        segments = ImmutableArray<CompiledBindingPathSegment>.Empty;
        leadingNotCount = 0;
        errorMessage = string.Empty;

        if (path is null)
        {
            errorMessage = "compiled binding path cannot be null";
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<CompiledBindingPathSegment>();
        var index = 0;
        while (index < path.Length && path[index] == '!')
        {
            leadingNotCount++;
            index++;
        }

        if (leadingNotCount > 0 && index >= path.Length)
        {
            errorMessage = "compiled binding path cannot end after '!'";
            return false;
        }

        var nextSegmentAcceptsNull = false;
        while (index < path.Length)
        {
            while (index < path.Length && path[index] == '.')
            {
                index++;
            }

            if (index >= path.Length)
            {
                break;
            }

            if (!TryParseMemberSegment(
                    path,
                    ref index,
                    out var memberName,
                    out var castTypeToken,
                    out var isMethodCall,
                    out var methodArguments,
                    out var isAttachedProperty,
                    out var attachedOwnerTypeToken,
                    out errorMessage))
            {
                return false;
            }

            var indexers = ImmutableArray.CreateBuilder<string>();
            while (index < path.Length && path[index] == '[')
            {
                if (!TopLevelTextParser.TryReadBalancedContent(path, ref index, '[', ']', out var token))
                {
                    errorMessage = "unterminated indexer segment";
                    return false;
                }

                token = token.Trim();
                if (token.Length == 0)
                {
                    errorMessage = "empty indexer segment is not supported";
                    return false;
                }

                indexers.Add(token);
            }

            var streamCount = 0;
            while (index < path.Length && path[index] == '^')
            {
                streamCount++;
                index++;
            }

            builder.Add(new CompiledBindingPathSegment(
                memberName,
                indexers.ToImmutable(),
                castTypeToken,
                isMethodCall,
                nextSegmentAcceptsNull,
                methodArguments,
                isAttachedProperty,
                attachedOwnerTypeToken,
                streamCount));
            nextSegmentAcceptsNull = false;

            if (index >= path.Length)
            {
                break;
            }

            if (path[index] == '.')
            {
                index++;
                continue;
            }

            if (path[index] == '?' &&
                index + 1 < path.Length &&
                path[index + 1] == '.')
            {
                index += 2;
                nextSegmentAcceptsNull = true;
                continue;
            }

            errorMessage = $"unexpected token '{path[index]}' in binding path";
            return false;
        }

        segments = builder.ToImmutable();
        return true;
    }

    private static bool TryParseMemberSegment(
        string path,
        ref int index,
        out string memberName,
        out string? castTypeToken,
        out bool isMethodCall,
        out ImmutableArray<string> methodArguments,
        out bool isAttachedProperty,
        out string? attachedOwnerTypeToken,
        out string errorMessage)
    {
        memberName = string.Empty;
        castTypeToken = null;
        isMethodCall = false;
        methodArguments = ImmutableArray<string>.Empty;
        isAttachedProperty = false;
        attachedOwnerTypeToken = null;
        errorMessage = string.Empty;

        if (index >= path.Length)
        {
            errorMessage = "expected member segment";
            return false;
        }

        var castRequiresSegmentClosure = false;
        if (path[index] == '(')
        {
            if (index + 1 < path.Length && path[index + 1] != '(')
            {
                var attachedClosing = path.IndexOf(')', index + 1);
                if (attachedClosing > index + 1)
                {
                    var attachedInner = path.Substring(index + 1, attachedClosing - index - 1).Trim();
                    var attachedSeparator = attachedInner.LastIndexOf('.');
                    var isAttachedTail = attachedClosing + 1 >= path.Length ||
                                         path[attachedClosing + 1] == '.' ||
                                         path[attachedClosing + 1] == '?' ||
                                         path[attachedClosing + 1] == '[' ||
                                         path[attachedClosing + 1] == '^';
                    if (attachedSeparator > 0 &&
                        attachedSeparator < attachedInner.Length - 1 &&
                        isAttachedTail)
                    {
                        attachedOwnerTypeToken = attachedInner.Substring(0, attachedSeparator).Trim();
                        memberName = attachedInner.Substring(attachedSeparator + 1).Trim();
                        if (attachedOwnerTypeToken.Length == 0 || memberName.Length == 0)
                        {
                            errorMessage = "invalid attached property segment";
                            return false;
                        }

                        isAttachedProperty = true;
                        index = attachedClosing + 1;
                        return true;
                    }
                }
            }

            if (index + 1 < path.Length && path[index + 1] == '(')
            {
                castRequiresSegmentClosure = true;
                index += 2;
            }
            else
            {
                index++;
            }

            var castStart = index;
            while (index < path.Length && path[index] != ')')
            {
                index++;
            }

            if (index >= path.Length || path[index] != ')')
            {
                errorMessage = "unterminated cast segment";
                return false;
            }

            castTypeToken = path.Substring(castStart, index - castStart).Trim();
            if (castTypeToken.Length == 0)
            {
                errorMessage = "cast type token cannot be empty";
                return false;
            }

            index++;
            while (index < path.Length && char.IsWhiteSpace(path[index]))
            {
                index++;
            }
        }

        if (index >= path.Length || !MiniLanguageSyntaxFacts.IsIdentifierStart(path[index]))
        {
            errorMessage = "member name expected in binding path segment";
            return false;
        }

        var nameStart = index;
        index++;
        while (index < path.Length && MiniLanguageSyntaxFacts.IsIdentifierPart(path[index]))
        {
            index++;
        }

        memberName = path.Substring(nameStart, index - nameStart);

        if (index < path.Length && path[index] == '(')
        {
            if (!TryParseMethodArguments(path, ref index, out methodArguments, out errorMessage))
            {
                return false;
            }

            isMethodCall = true;
        }

        if (castRequiresSegmentClosure)
        {
            while (index < path.Length && char.IsWhiteSpace(path[index]))
            {
                index++;
            }

            if (index >= path.Length || path[index] != ')')
            {
                errorMessage = "expected ')' to close casted member segment";
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool TryParseMethodArguments(
        string path,
        ref int index,
        out ImmutableArray<string> methodArguments,
        out string errorMessage)
    {
        methodArguments = ImmutableArray<string>.Empty;
        errorMessage = string.Empty;

        if (index >= path.Length || path[index] != '(')
        {
            errorMessage = "method argument list must start with '('";
            return false;
        }

        if (!TopLevelTextParser.TryReadBalancedContent(path, ref index, '(', ')', out var argumentsText))
        {
            errorMessage = "unterminated method argument list";
            return false;
        }

        argumentsText = argumentsText.Trim();
        if (argumentsText.Length == 0)
        {
            return true;
        }

        var parsedArguments = TopLevelTextParser.SplitTopLevel(argumentsText, ',');
        var arguments = ImmutableArray.CreateBuilder<string>(parsedArguments.Length);
        for (var i = 0; i < parsedArguments.Length; i++)
        {
            var argument = parsedArguments[i].Trim();
            if (argument.Length == 0)
            {
                errorMessage = "method argument list contains an empty argument";
                return false;
            }

            arguments.Add(argument);
        }

        methodArguments = arguments.ToImmutable();
        return true;
    }
}
