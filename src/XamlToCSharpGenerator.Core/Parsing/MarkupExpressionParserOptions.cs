namespace XamlToCSharpGenerator.Core.Parsing;

public readonly record struct MarkupExpressionParserOptions(
    bool AllowLegacyInvalidNamedArgumentFallback = false);
