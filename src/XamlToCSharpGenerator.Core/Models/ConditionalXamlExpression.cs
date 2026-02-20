using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ConditionalXamlExpression(
    string RawExpression,
    string MethodName,
    ImmutableArray<string> Arguments,
    int Line,
    int Column);
