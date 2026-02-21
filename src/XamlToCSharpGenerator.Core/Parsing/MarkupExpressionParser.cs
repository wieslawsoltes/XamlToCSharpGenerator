using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Parsing;

public sealed class MarkupExpressionParser
{
    private readonly MarkupExpressionParserOptions _options;

    public MarkupExpressionParser()
        : this(new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true))
    {
    }

    public MarkupExpressionParser(MarkupExpressionParserOptions options)
    {
        _options = options;
    }

    public bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        markupExtension = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (inner.Length == 0)
        {
            return false;
        }

        var headLength = 0;
        while (headLength < inner.Length &&
               !char.IsWhiteSpace(inner[headLength]) &&
               inner[headLength] != ',')
        {
            headLength++;
        }

        var name = inner.Substring(0, headLength).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        var argumentsText = headLength < inner.Length ? inner.Substring(headLength).Trim() : string.Empty;
        if (argumentsText.StartsWith(",", StringComparison.Ordinal))
        {
            argumentsText = argumentsText.Substring(1).TrimStart();
        }

        var positional = ImmutableArray.CreateBuilder<string>();
        var named = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        var arguments = ImmutableArray.CreateBuilder<MarkupExtensionArgument>();

        if (!string.IsNullOrWhiteSpace(argumentsText))
        {
            var position = 0;
            foreach (var token in SplitTopLevel(argumentsText, ','))
            {
                var argument = token.Trim();
                if (argument.Length == 0)
                {
                    continue;
                }

                var equalsIndex = IndexOfTopLevel(argument, '=');
                if (equalsIndex == 0)
                {
                    if (_options.AllowLegacyInvalidNamedArgumentFallback)
                    {
                        positional.Add(argument);
                        arguments.Add(new MarkupExtensionArgument(
                            Name: null,
                            Value: argument,
                            IsNamed: false,
                            Position: position++));
                        continue;
                    }

                    return false;
                }

                if (equalsIndex > 0)
                {
                    var key = argument.Substring(0, equalsIndex).Trim();
                    var argumentValue = argument.Substring(equalsIndex + 1).Trim();
                    if (key.Length == 0 && _options.AllowLegacyInvalidNamedArgumentFallback)
                    {
                        positional.Add(argument);
                        arguments.Add(new MarkupExtensionArgument(
                            Name: null,
                            Value: argument,
                            IsNamed: false,
                            Position: position++));
                        continue;
                    }

                    if (key.Length == 0)
                    {
                        return false;
                    }

                    named[key] = argumentValue;
                    arguments.Add(new MarkupExtensionArgument(
                        Name: key,
                        Value: argumentValue,
                        IsNamed: true,
                        Position: position++));
                    continue;
                }

                positional.Add(argument);
                arguments.Add(new MarkupExtensionArgument(
                    Name: null,
                    Value: argument,
                    IsNamed: false,
                    Position: position++));
            }
        }

        markupExtension = new MarkupExtensionInfo(
            Name: name,
            PositionalArguments: positional.ToImmutable(),
            NamedArguments: named.ToImmutable(),
            Arguments: arguments.ToImmutable());
        return true;
    }

    public static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        var result = new List<string>();
        var start = 0;
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
                    if (ch == separator &&
                        braceDepth == 0 &&
                        bracketDepth == 0 &&
                        parenthesisDepth == 0)
                    {
                        result.Add(value.Substring(start, index - start));
                        start = index + 1;
                    }
                    break;
            }
        }

        if (start <= value.Length)
        {
            result.Add(value.Substring(start));
        }

        return result;
    }

    public static int IndexOfTopLevel(string value, char token)
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

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    continue;
                case '(':
                    parenthesisDepth++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    continue;
            }

            if (ch == token &&
                braceDepth == 0 &&
                bracketDepth == 0 &&
                parenthesisDepth == 0)
            {
                return index;
            }
        }

        return -1;
    }
}
