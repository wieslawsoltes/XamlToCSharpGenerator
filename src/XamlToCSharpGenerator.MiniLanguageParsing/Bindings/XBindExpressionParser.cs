using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public static class XBindExpressionParser
{
    public static bool TryParse(
        string text,
        out XBindExpressionNode? expression,
        out string errorMessage)
    {
        expression = null;
        errorMessage = string.Empty;

        if (text is null)
        {
            errorMessage = "x:Bind expression cannot be null";
            return false;
        }

        var parser = new Parser(text);
        return parser.TryParse(out expression, out errorMessage);
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _index;

        public Parser(string text)
        {
            _text = text;
        }

        public bool TryParse(
            out XBindExpressionNode? expression,
            out string errorMessage)
        {
            expression = null;
            errorMessage = string.Empty;

            SkipWhitespace();
            if (_index >= _text.Length)
            {
                errorMessage = "x:Bind expression is empty";
                return false;
            }

            if (!TryParseExpression(out expression, out errorMessage))
            {
                return false;
            }

            SkipWhitespace();
            if (_index < _text.Length)
            {
                errorMessage = $"unexpected token '{_text[_index]}'";
                return false;
            }

            return true;
        }

        private bool TryParseExpression(
            out XBindExpressionNode? expression,
            out string errorMessage)
        {
            if (!TryParsePrimaryExpression(out expression, out errorMessage))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                {
                    return true;
                }

                if (TryConsume("?."))
                {
                    SkipWhitespace();
                    if (!TryParseIdentifier(out var memberName))
                    {
                        errorMessage = "member name expected after '?.'";
                        return false;
                    }

                    expression = new XBindMemberAccessExpression(expression!, memberName, IsConditional: true);
                    continue;
                }

                if (TryConsume("."))
                {
                    SkipWhitespace();
                    if (_index < _text.Length && _text[_index] == '(')
                    {
                        if (!TryParseAttachedPropertySegment(
                                expression!,
                                isConditional: false,
                                out expression,
                                out errorMessage))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!TryParseIdentifier(out var memberName))
                    {
                        errorMessage = "member name expected after '.'";
                        return false;
                    }

                    expression = new XBindMemberAccessExpression(expression!, memberName, IsConditional: false);
                    continue;
                }

                if (_index < _text.Length && _text[_index] == '(')
                {
                    if (!TryParseArgumentList(out var arguments, out errorMessage))
                    {
                        return false;
                    }

                    expression = new XBindInvocationExpression(expression!, arguments);
                    continue;
                }

                if (_index < _text.Length && _text[_index] == '[')
                {
                    if (!TryParseIndexerArguments(out var arguments, out errorMessage))
                    {
                        return false;
                    }

                    expression = new XBindIndexerExpression(expression!, arguments);
                    continue;
                }

                return true;
            }
        }

        private bool TryParsePrimaryExpression(
            out XBindExpressionNode? expression,
            out string errorMessage)
        {
            expression = null;
            errorMessage = string.Empty;

            SkipWhitespace();
            if (_index >= _text.Length)
            {
                errorMessage = "expression expected";
                return false;
            }

            var current = _text[_index];
            if (current is '\'' or '"')
            {
                if (!TryParseStringLiteral(out var literal, out errorMessage))
                {
                    return false;
                }

                expression = new XBindLiteralExpression(XBindLiteralKind.String, literal);
                return true;
            }

            if (IsNumberStart())
            {
                if (!TryParseNumberLiteral(out var literal, out errorMessage))
                {
                    return false;
                }

                expression = new XBindLiteralExpression(XBindLiteralKind.Number, literal);
                return true;
            }

            if (current == '(')
            {
                var savedIndex = _index;
                if (TryParseCastExpression(out expression, out errorMessage))
                {
                    return true;
                }

                _index = savedIndex;
                errorMessage = string.Empty;

                _index++;
                if (!TryParseExpression(out expression, out errorMessage))
                {
                    return false;
                }

                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != ')')
                {
                    errorMessage = "missing closing ')'";
                    return false;
                }

                _index++;
                return true;
            }

            if (!TryParseIdentifier(out var identifier))
            {
                errorMessage = "identifier expected";
                return false;
            }

            if (IsNullToken(identifier))
            {
                expression = new XBindLiteralExpression(XBindLiteralKind.Null, "null");
                return true;
            }

            if (IsTrueToken(identifier))
            {
                expression = new XBindLiteralExpression(XBindLiteralKind.Boolean, "true");
                return true;
            }

            if (IsFalseToken(identifier))
            {
                expression = new XBindLiteralExpression(XBindLiteralKind.Boolean, "false");
                return true;
            }

            expression = new XBindIdentifierExpression(identifier);
            return true;
        }

        private bool TryParseCastExpression(
            out XBindExpressionNode? expression,
            out string errorMessage)
        {
            expression = null;
            errorMessage = string.Empty;

            var savedIndex = _index;
            if (_text[_index] != '(')
            {
                return false;
            }

            _index++;
            SkipWhitespace();
            if (!TryParseTypeToken(out var typeToken))
            {
                _index = savedIndex;
                return false;
            }

            SkipWhitespace();
            if (_index >= _text.Length || _text[_index] != ')')
            {
                _index = savedIndex;
                return false;
            }

            _index++;
            SkipWhitespace();

            if (CanStartPrimaryExpression())
            {
                if (!TryParsePrimaryExpression(out var operand, out errorMessage))
                {
                    _index = savedIndex;
                    return false;
                }

                expression = new XBindCastExpression(typeToken, operand);
                return true;
            }

            expression = new XBindCastExpression(typeToken, Operand: null);
            return true;
        }

        private bool TryParseAttachedPropertySegment(
            XBindExpressionNode target,
            bool isConditional,
            out XBindExpressionNode? expression,
            out string errorMessage)
        {
            expression = null;
            errorMessage = string.Empty;

            if (_index >= _text.Length || _text[_index] != '(')
            {
                errorMessage = "attached property segment expected";
                return false;
            }

            _index++;
            SkipWhitespace();
            if (!TryParseTypeToken(out var ownerTypeToken))
            {
                errorMessage = "attached property owner type expected";
                return false;
            }

            SkipWhitespace();
            if (_index >= _text.Length || _text[_index] != '.')
            {
                errorMessage = "'.' expected in attached property segment";
                return false;
            }

            _index++;
            SkipWhitespace();
            if (!TryParseIdentifier(out var propertyName))
            {
                errorMessage = "attached property name expected";
                return false;
            }

            SkipWhitespace();
            if (_index >= _text.Length || _text[_index] != ')')
            {
                errorMessage = "missing closing ')' for attached property segment";
                return false;
            }

            _index++;
            expression = new XBindAttachedPropertyAccessExpression(
                target,
                ownerTypeToken,
                propertyName,
                isConditional);
            return true;
        }

        private bool TryParseArgumentList(
            out ImmutableArray<XBindExpressionNode> arguments,
            out string errorMessage)
        {
            arguments = ImmutableArray<XBindExpressionNode>.Empty;
            errorMessage = string.Empty;
            if (_index >= _text.Length || _text[_index] != '(')
            {
                errorMessage = "argument list expected";
                return false;
            }

            _index++;
            SkipWhitespace();
            var builder = ImmutableArray.CreateBuilder<XBindExpressionNode>();
            if (_index < _text.Length && _text[_index] == ')')
            {
                _index++;
                arguments = builder.ToImmutable();
                return true;
            }

            while (true)
            {
                if (!TryParseExpression(out var argument, out errorMessage))
                {
                    return false;
                }

                builder.Add(argument!);
                SkipWhitespace();
                if (_index >= _text.Length)
                {
                    errorMessage = "missing closing ')' for argument list";
                    return false;
                }

                if (_text[_index] == ')')
                {
                    _index++;
                    arguments = builder.ToImmutable();
                    return true;
                }

                if (_text[_index] != ',')
                {
                    errorMessage = $"unexpected token '{_text[_index]}' in argument list";
                    return false;
                }

                _index++;
                SkipWhitespace();
            }
        }

        private bool TryParseIndexerArguments(
            out ImmutableArray<XBindExpressionNode> arguments,
            out string errorMessage)
        {
            arguments = ImmutableArray<XBindExpressionNode>.Empty;
            errorMessage = string.Empty;
            if (_index >= _text.Length || _text[_index] != '[')
            {
                errorMessage = "indexer expected";
                return false;
            }

            _index++;
            SkipWhitespace();
            var builder = ImmutableArray.CreateBuilder<XBindExpressionNode>();
            if (_index < _text.Length && _text[_index] == ']')
            {
                errorMessage = "empty indexer is not supported";
                return false;
            }

            while (true)
            {
                if (!TryParseExpression(out var argument, out errorMessage))
                {
                    return false;
                }

                builder.Add(argument!);
                SkipWhitespace();
                if (_index >= _text.Length)
                {
                    errorMessage = "missing closing ']'";
                    return false;
                }

                if (_text[_index] == ']')
                {
                    _index++;
                    arguments = builder.ToImmutable();
                    return true;
                }

                if (_text[_index] != ',')
                {
                    errorMessage = $"unexpected token '{_text[_index]}' in indexer";
                    return false;
                }

                _index++;
                SkipWhitespace();
            }
        }

        private bool TryParseStringLiteral(out string literal, out string errorMessage)
        {
            literal = string.Empty;
            errorMessage = string.Empty;

            var quote = _text[_index];
            var builder = new StringBuilder();
            _index++;
            while (_index < _text.Length)
            {
                var current = _text[_index++];
                if (current == quote)
                {
                    literal = builder.ToString();
                    return true;
                }

                if (quote == '\'' && current == '^' && _index < _text.Length)
                {
                    builder.Append(_text[_index]);
                    _index++;
                    continue;
                }

                if (quote == '"' && current == '\\' && _index < _text.Length)
                {
                    var escaped = _text[_index++];
                    builder.Append(escaped switch
                    {
                        '\\' => '\\',
                        '"' => '"',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped
                    });
                    continue;
                }

                builder.Append(current);
            }

            errorMessage = "missing closing quote";
            return false;
        }

        private bool TryParseNumberLiteral(out string literal, out string errorMessage)
        {
            literal = string.Empty;
            errorMessage = string.Empty;

            var start = _index;
            if (_text[_index] is '+' or '-')
            {
                _index++;
            }

            var hasDigits = false;
            while (_index < _text.Length && char.IsDigit(_text[_index]))
            {
                hasDigits = true;
                _index++;
            }

            if (_index < _text.Length && _text[_index] == '.')
            {
                _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                {
                    hasDigits = true;
                    _index++;
                }
            }

            if (!hasDigits)
            {
                errorMessage = "numeric literal expected";
                return false;
            }

            literal = _text.Substring(start, _index - start);
            return true;
        }

        private bool TryParseIdentifier(out string identifier)
        {
            identifier = string.Empty;
            if (_index >= _text.Length)
            {
                return false;
            }

            var start = _index;
            if (!IsIdentifierTokenStart(_text[_index]))
            {
                return false;
            }

            _index++;
            while (_index < _text.Length && IsIdentifierTokenPart(_text[_index]))
            {
                _index++;
            }

            identifier = _text.Substring(start, _index - start);
            return true;
        }

        private bool TryParseTypeToken(out string typeToken)
        {
            typeToken = string.Empty;
            var savedIndex = _index;
            if (!TryParseIdentifier(out var firstSegment))
            {
                return false;
            }

            var builder = new StringBuilder(firstSegment);
            while (true)
            {
                var segmentStart = _index;
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != '.')
                {
                    _index = segmentStart;
                    break;
                }

                _index++;
                SkipWhitespace();
                if (!TryParseIdentifier(out var nextSegment))
                {
                    _index = savedIndex;
                    return false;
                }

                builder.Append('.');
                builder.Append(nextSegment);
            }

            typeToken = builder.ToString();
            return true;
        }

        private bool CanStartPrimaryExpression()
        {
            SkipWhitespace();
            return _index < _text.Length &&
                   (_text[_index] == '(' ||
                    _text[_index] == '\'' ||
                    _text[_index] == '"' ||
                    IsNumberStart() ||
                    IsIdentifierTokenStart(_text[_index]));
        }

        private bool IsNumberStart()
        {
            if (_index >= _text.Length)
            {
                return false;
            }

            if (char.IsDigit(_text[_index]))
            {
                return true;
            }

            return (_text[_index] is '+' or '-') &&
                   _index + 1 < _text.Length &&
                   char.IsDigit(_text[_index + 1]);
        }

        private bool TryConsume(string token)
        {
            if (_index + token.Length > _text.Length ||
                !string.Equals(_text.Substring(_index, token.Length), token, StringComparison.Ordinal))
            {
                return false;
            }

            _index += token.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }
        }

        private static bool IsIdentifierTokenStart(char ch)
        {
            return MiniLanguageSyntaxFacts.IsIdentifierStart(ch);
        }

        private static bool IsIdentifierTokenPart(char ch)
        {
            return MiniLanguageSyntaxFacts.IsIdentifierPart(ch) ||
                   ch is ':' or '_';
        }

        private static bool IsNullToken(string token)
        {
            return token.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("x:Null", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrueToken(string token)
        {
            return token.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("x:True", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFalseToken(string token)
        {
            return token.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("x:False", StringComparison.OrdinalIgnoreCase);
        }
    }
}
