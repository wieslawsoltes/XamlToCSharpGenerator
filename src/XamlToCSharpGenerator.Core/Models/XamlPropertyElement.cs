using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlPropertyElement(
    string PropertyName,
    ImmutableArray<XamlObjectNode> ObjectValues,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null,
    string? TextContent = null,
    string? RawTextContent = null);
