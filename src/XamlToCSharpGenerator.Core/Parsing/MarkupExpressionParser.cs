using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public sealed class MarkupExpressionParser
{
    private readonly MarkupExpressionParserOptions _options;

    public MarkupExpressionParser()
        : this(default)
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

        if (!MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(value, out var inner))
        {
            return false;
        }

        if (!XamlMarkupArgumentSemantics.TryParseHead(inner, out var name, out var argumentsText))
        {
            return false;
        }

        var positional = ImmutableArray.CreateBuilder<string>();
        var named = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        var arguments = ImmutableArray.CreateBuilder<MarkupExtensionArgument>();

        if (argumentsText.Length > 0)
        {
            var position = 0;
            foreach (var argument in XamlMarkupArgumentSemantics.SplitArguments(argumentsText))
            {
                var namedParseStatus = XamlMarkupArgumentSemantics.TryParseNamedArgument(
                    argument,
                    out var key,
                    out var argumentValue);
                switch (namedParseStatus)
                {
                    case XamlMarkupNamedArgumentParseStatus.LeadingEquals:
                    case XamlMarkupNamedArgumentParseStatus.EmptyName:
                        if (!_options.AllowLegacyInvalidNamedArgumentFallback)
                        {
                            return false;
                        }

                        positional.Add(argument);
                        arguments.Add(new MarkupExtensionArgument(
                            Name: null,
                            Value: argument,
                            IsNamed: false,
                            Position: position++));
                        continue;
                    case XamlMarkupNamedArgumentParseStatus.Parsed:
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
        return TopLevelTextParser.SplitTopLevel(value, separator);
    }

    public static int IndexOfTopLevel(string value, char token)
    {
        return TopLevelTextParser.IndexOfTopLevel(value, token);
    }
}
