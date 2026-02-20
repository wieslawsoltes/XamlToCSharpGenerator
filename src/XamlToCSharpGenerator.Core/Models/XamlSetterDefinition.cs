namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlSetterDefinition(
    string PropertyName,
    string Value,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
