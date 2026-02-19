using System;
using System.Collections.Immutable;
using System.Globalization;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal static class SelectorSyntaxValidator
{
    internal readonly struct BranchInfo
    {
        public BranchInfo(string? lastTypeToken, int lastTypeOffset)
        {
            LastTypeToken = lastTypeToken;
            LastTypeOffset = lastTypeOffset;
        }

        public string? LastTypeToken { get; }

        public int LastTypeOffset { get; }
    }

    internal readonly struct ValidationResult
    {
        public ValidationResult(ImmutableArray<BranchInfo> branches)
        {
            IsValid = true;
            Branches = branches;
            ErrorMessage = string.Empty;
            ErrorOffset = 0;
        }

        public ValidationResult(string errorMessage, int errorOffset)
        {
            IsValid = false;
            Branches = ImmutableArray<BranchInfo>.Empty;
            ErrorMessage = errorMessage;
            ErrorOffset = errorOffset;
        }

        public bool IsValid { get; }

        public ImmutableArray<BranchInfo> Branches { get; }

        public string ErrorMessage { get; }

        public int ErrorOffset { get; }
    }

    public static ValidationResult Validate(string selector)
    {
        var parser = new Parser(selector ?? string.Empty);
        if (!parser.TryParseSelectorList(endToken: null, out var branches, out var errorMessage, out var errorOffset))
        {
            return new ValidationResult(errorMessage, errorOffset);
        }

        parser.SkipWhitespace();
        if (!parser.End)
        {
            return new ValidationResult("Unexpected token in selector.", parser.Position);
        }

        return new ValidationResult(branches);
    }

    private sealed class Parser
    {
        private const string TemplateAxisToken = "/template/";
        private readonly string _text;
        private int _index;

        public Parser(string text)
        {
            _text = text;
            _index = 0;
        }

        public bool End => _index >= _text.Length;

        public int Position => _index;

        public char Peek => End ? '\0' : _text[_index];

        public bool TryTake(char token)
        {
            if (!End && _text[_index] == token)
            {
                _index++;
                return true;
            }

            return false;
        }

        public int SkipWhitespace()
        {
            var start = _index;
            while (!End && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }

            return _index - start;
        }

        public bool TryParseSelectorList(
            char? endToken,
            out ImmutableArray<BranchInfo> branches,
            out string errorMessage,
            out int errorOffset)
        {
            var result = ImmutableArray.CreateBuilder<BranchInfo>();
            while (true)
            {
                SkipWhitespace();
                if (End || (endToken.HasValue && Peek == endToken.Value))
                {
                    if (result.Count == 0)
                    {
                        errorMessage = "Unexpected end of selector";
                        errorOffset = Position;
                        branches = ImmutableArray<BranchInfo>.Empty;
                        return false;
                    }

                    branches = result.ToImmutable();
                    errorMessage = string.Empty;
                    errorOffset = 0;
                    return true;
                }

                if (!TryParseBranch(endToken, out var branchInfo, out errorMessage, out errorOffset))
                {
                    branches = ImmutableArray<BranchInfo>.Empty;
                    return false;
                }

                result.Add(branchInfo);
                SkipWhitespace();
                if (!TryTake(','))
                {
                    break;
                }
            }

            branches = result.ToImmutable();
            errorMessage = string.Empty;
            errorOffset = 0;
            return true;
        }

        private bool TryParseBranch(
            char? endToken,
            out BranchInfo branchInfo,
            out string errorMessage,
            out int errorOffset)
        {
            var currentTypeContext = false;
            string? branchTypeToken = null;
            var branchTypeOffset = 0;

            if (!TryParseSimpleSelector(
                    endToken,
                    ref currentTypeContext,
                    ref branchTypeToken,
                    ref branchTypeOffset,
                    out errorMessage,
                    out errorOffset))
            {
                branchInfo = default;
                return false;
            }

            while (true)
            {
                var whitespaceLength = SkipWhitespace();
                if (End || Peek == ',' || (endToken.HasValue && Peek == endToken.Value))
                {
                    branchInfo = new BranchInfo(branchTypeToken, branchTypeOffset);
                    return true;
                }

                if (TryTake('>'))
                {
                    SkipWhitespace();
                    currentTypeContext = false;
                    if (!TryParseSimpleSelector(
                            endToken,
                            ref currentTypeContext,
                            ref branchTypeToken,
                            ref branchTypeOffset,
                            out errorMessage,
                            out errorOffset))
                    {
                        branchInfo = default;
                        return false;
                    }

                    continue;
                }

                if (IsTemplateAxisAt(Position))
                {
                    _index += TemplateAxisToken.Length;
                    SkipWhitespace();
                    currentTypeContext = false;
                    if (!TryParseSimpleSelector(
                            endToken,
                            ref currentTypeContext,
                            ref branchTypeToken,
                            ref branchTypeOffset,
                            out errorMessage,
                            out errorOffset))
                    {
                        branchInfo = default;
                        return false;
                    }

                    continue;
                }

                if (whitespaceLength > 0)
                {
                    currentTypeContext = false;
                    if (!TryParseSimpleSelector(
                            endToken,
                            ref currentTypeContext,
                            ref branchTypeToken,
                            ref branchTypeOffset,
                            out errorMessage,
                            out errorOffset))
                    {
                        branchInfo = default;
                        return false;
                    }

                    continue;
                }

                branchInfo = default;
                errorMessage = "Unexpected token in selector.";
                errorOffset = Position;
                return false;
            }
        }

        private bool TryParseSimpleSelector(
            char? endToken,
            ref bool currentTypeContext,
            ref string? branchTypeToken,
            ref int branchTypeOffset,
            out string errorMessage,
            out int errorOffset)
        {
            var segmentApplied = false;
            while (TryTake('^'))
            {
                segmentApplied = true;
                currentTypeContext = true;
            }

            if (TryTake('*'))
            {
                segmentApplied = true;
                currentTypeContext = true;
            }

            if (!End && IsIdentifierStart(Peek))
            {
                var typeOffset = Position;
                if (!TryParseTypeToken(out var typeToken, out errorMessage, out errorOffset))
                {
                    return false;
                }

                segmentApplied = true;
                currentTypeContext = true;
                branchTypeToken = typeToken;
                branchTypeOffset = typeOffset;
            }

            while (true)
            {
                if (End || Peek == ',' || Peek == '>' || (endToken.HasValue && Peek == endToken.Value))
                {
                    break;
                }

                if (char.IsWhiteSpace(Peek) || IsTemplateAxisAt(Position))
                {
                    break;
                }

                if (TryTake('.'))
                {
                    var className = ParseStyleClass();
                    if (className.Length == 0)
                    {
                        errorMessage = "Expected a class name after '.'.";
                        errorOffset = Position;
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                if (TryTake('#'))
                {
                    var name = ParseIdentifier();
                    if (name.Length == 0)
                    {
                        errorMessage = "Expected a name after '#'.";
                        errorOffset = Position;
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                if (TryTake(':'))
                {
                    if (!TryParseColonSelector(
                            ref currentTypeContext,
                            ref branchTypeToken,
                            ref branchTypeOffset,
                            out errorMessage,
                            out errorOffset))
                    {
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                if (TryTake('['))
                {
                    if (!TryParsePropertySelector(currentTypeContext, out errorMessage, out errorOffset))
                    {
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                errorMessage = "Unexpected token in selector.";
                errorOffset = Position;
                return false;
            }

            if (!segmentApplied)
            {
                errorMessage = "Unexpected end of selector";
                errorOffset = Position;
                return false;
            }

            errorMessage = string.Empty;
            errorOffset = 0;
            return true;
        }

        private bool TryParseColonSelector(
            ref bool currentTypeContext,
            ref string? branchTypeToken,
            ref int branchTypeOffset,
            out string errorMessage,
            out int errorOffset)
        {
            var identifier = ParseStyleClass();
            if (identifier.Length == 0)
            {
                errorMessage = "Expected class name, is, nth-child or nth-last-child selector after ':'.";
                errorOffset = Position;
                return false;
            }

            if (identifier.Equals("is", StringComparison.Ordinal) && TryTake('('))
            {
                SkipWhitespace();
                var typeOffset = Position;
                if (!TryParseTypeToken(out var typeToken, out errorMessage, out errorOffset))
                {
                    return false;
                }

                SkipWhitespace();
                if (!TryTake(')'))
                {
                    errorMessage = End
                        ? "Expected ')', got end of selector."
                        : "Expected ')', got '" + Peek + "'.";
                    errorOffset = Position;
                    return false;
                }

                currentTypeContext = true;
                branchTypeToken = typeToken;
                branchTypeOffset = typeOffset;
                errorMessage = string.Empty;
                errorOffset = 0;
                return true;
            }

            if (identifier.Equals("not", StringComparison.Ordinal) && TryTake('('))
            {
                if (!TryParseSelectorList(
                        endToken: ')',
                        out _,
                        out errorMessage,
                        out errorOffset))
                {
                    return false;
                }

                if (!TryTake(')'))
                {
                    errorMessage = End
                        ? "Expected ')', got end of selector."
                        : "Expected ')', got '" + Peek + "'.";
                    errorOffset = Position;
                    return false;
                }

                errorMessage = string.Empty;
                errorOffset = 0;
                return true;
            }

            if ((identifier.Equals("nth-child", StringComparison.Ordinal) ||
                 identifier.Equals("nth-last-child", StringComparison.Ordinal)) &&
                TryTake('('))
            {
                if (!TryParseNthChildArguments(out errorMessage, out errorOffset))
                {
                    return false;
                }

                errorMessage = string.Empty;
                errorOffset = 0;
                return true;
            }

            errorMessage = string.Empty;
            errorOffset = 0;
            return true;
        }

        private bool TryParsePropertySelector(
            bool currentTypeContext,
            out string errorMessage,
            out int errorOffset)
        {
            SkipWhitespace();
            if (TryTake('('))
            {
                if (!TryParseTypeToken(out _, out errorMessage, out errorOffset))
                {
                    return false;
                }

                if (!TryTake('.'))
                {
                    errorMessage = End
                        ? "Expected '.', got end of selector."
                        : "Expected '.', got '" + Peek + "'";
                    errorOffset = Position;
                    return false;
                }

                var propertyName = ParseIdentifier();
                if (propertyName.Length == 0)
                {
                    errorMessage = End
                        ? "Expected Attached Property Name, got end of selector."
                        : "Expected Attached Property Name, got '" + Peek + "'";
                    errorOffset = Position;
                    return false;
                }

                if (!TryTake(')'))
                {
                    errorMessage = End
                        ? "Expected ')', got end of selector."
                        : "Expected ')', got '" + Peek + "'.";
                    errorOffset = Position;
                    return false;
                }

                if (!TryTake('='))
                {
                    errorMessage = End
                        ? "Expected '=', got end of selector."
                        : "Expected '=', got '" + Peek + "'";
                    errorOffset = Position;
                    return false;
                }

                var valueStart = Position;
                var value = TakeUntil(']');
                if (!TryTake(']'))
                {
                    errorMessage = "Expected ']', got end of selector.";
                    errorOffset = Position;
                    return false;
                }

                if (value.Trim().Length == 0)
                {
                    errorMessage = "Expected attached property value.";
                    errorOffset = valueStart;
                    return false;
                }

                if (!currentTypeContext)
                {
                    errorMessage = "Attached Property selectors must be applied to a type.";
                    errorOffset = valueStart;
                    return false;
                }

                errorMessage = string.Empty;
                errorOffset = 0;
                return true;
            }

            var property = ParseIdentifier();
            if (property.Length == 0)
            {
                errorMessage = End
                    ? "Expected property name, got end of selector."
                    : "Expected property name, got '" + Peek + "'";
                errorOffset = Position;
                return false;
            }

            if (!TryTake('='))
            {
                errorMessage = End
                    ? "Expected '=', got end of selector."
                    : "Expected '=', got '" + Peek + "'";
                errorOffset = Position;
                return false;
            }

            var propertyValueStart = Position;
            var propertyValue = TakeUntil(']');
            if (!TryTake(']'))
            {
                errorMessage = "Expected ']', got end of selector.";
                errorOffset = Position;
                return false;
            }

            if (propertyValue.Trim().Length == 0)
            {
                errorMessage = "Expected property selector value.";
                errorOffset = propertyValueStart;
                return false;
            }

            if (!currentTypeContext)
            {
                errorMessage = "Property selectors must be applied to a type.";
                errorOffset = propertyValueStart;
                return false;
            }

            errorMessage = string.Empty;
            errorOffset = 0;
            return true;
        }

        private bool TryParseNthChildArguments(out string errorMessage, out int errorOffset)
        {
            var argumentStart = Position;
            var argument = TakeUntil(')');
            if (!TryTake(')'))
            {
                errorMessage = "Expected ')', got end of selector.";
                errorOffset = Position;
                return false;
            }

            if (!TryParseNthChildExpression(argument, out _, out _))
            {
                errorMessage = "Couldn't parse nth-child arguments.";
                errorOffset = argumentStart;
                return false;
            }

            errorMessage = string.Empty;
            errorOffset = 0;
            return true;
        }

        private bool TryParseTypeToken(out string typeToken, out string errorMessage, out int errorOffset)
        {
            var namespaceOrTypeName = ParseIdentifier();
            if (namespaceOrTypeName.Length == 0)
            {
                typeToken = string.Empty;
                errorMessage = End
                    ? "Expected an identifier, got end of selector."
                    : "Expected an identifier, got '" + Peek + "'";
                errorOffset = Position;
                return false;
            }

            if (TryTake('|'))
            {
                if (End)
                {
                    typeToken = string.Empty;
                    errorMessage = "Unexpected end of selector.";
                    errorOffset = Position;
                    return false;
                }

                var typeName = ParseIdentifier();
                if (typeName.Length == 0)
                {
                    typeToken = string.Empty;
                    errorMessage = End
                        ? "Unexpected end of selector."
                        : "Expected an identifier, got '" + Peek + "'";
                    errorOffset = Position;
                    return false;
                }

                typeToken = namespaceOrTypeName + ":" + typeName;
                errorMessage = string.Empty;
                errorOffset = 0;
                return true;
            }

            typeToken = namespaceOrTypeName;
            errorMessage = string.Empty;
            errorOffset = 0;
            return true;
        }

        private string ParseStyleClass()
        {
            if (End || !IsIdentifierStart(Peek))
            {
                return string.Empty;
            }

            var start = Position;
            _index++;
            while (!End && IsStyleClassChar(Peek))
            {
                _index++;
            }

            return _text.Substring(start, Position - start);
        }

        private string ParseIdentifier()
        {
            if (End || !IsIdentifierStart(Peek))
            {
                return string.Empty;
            }

            var start = Position;
            _index++;
            while (!End && IsIdentifierPart(Peek))
            {
                _index++;
            }

            return _text.Substring(start, Position - start);
        }

        private string TakeUntil(char token)
        {
            var start = Position;
            while (!End && Peek != token)
            {
                _index++;
            }

            return _text.Substring(start, Position - start);
        }

        private bool IsTemplateAxisAt(int position)
        {
            if (position < 0 || position + TemplateAxisToken.Length > _text.Length)
            {
                return false;
            }

            return _text.Substring(position, TemplateAxisToken.Length)
                .Equals(TemplateAxisToken, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseNthChildExpression(string pseudoArgument, out int step, out int offset)
        {
            step = 0;
            offset = 0;

            var text = pseudoArgument.Trim().ToLowerInvariant();
            if (text.Length == 0)
            {
                return false;
            }

            if (text == "odd")
            {
                step = 2;
                offset = 1;
                return true;
            }

            if (text == "even")
            {
                step = 2;
                offset = 0;
                return true;
            }

            var compact = text.Replace(" ", string.Empty);
            var nIndex = compact.IndexOf('n');
            if (nIndex < 0)
            {
                if (!int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
                {
                    return false;
                }

                step = 0;
                return true;
            }

            var stepToken = compact.Substring(0, nIndex);
            if (stepToken.Length == 0 || stepToken == "+")
            {
                step = 1;
            }
            else if (stepToken == "-")
            {
                step = -1;
            }
            else if (!int.TryParse(stepToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out step))
            {
                return false;
            }

            var offsetToken = compact.Substring(nIndex + 1);
            if (offsetToken.Length == 0)
            {
                offset = 0;
                return true;
            }

            return int.TryParse(offsetToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
        }

        private static bool IsIdentifierStart(char ch)
        {
            return char.IsLetter(ch) || ch == '_';
        }

        private static bool IsIdentifierPart(char ch)
        {
            return char.IsLetterOrDigit(ch) || ch == '_';
        }

        private static bool IsStyleClassChar(char ch)
        {
            if (IsIdentifierPart(ch) || ch == '-')
            {
                return true;
            }

            var category = char.GetUnicodeCategory(ch);
            return category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.Format
                or UnicodeCategory.DecimalDigitNumber;
        }
    }
}
