using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlStyleDefinition(
    string? Key,
    string Selector,
    int SelectorLine,
    int SelectorColumn,
    string? DataType,
    bool? CompileBindings,
    ImmutableArray<XamlSetterDefinition> Setters,
    string RawXaml,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
