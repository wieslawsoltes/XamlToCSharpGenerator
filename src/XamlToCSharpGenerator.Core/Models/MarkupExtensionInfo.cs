using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public readonly record struct MarkupExtensionArgument(
    string? Name,
    string Value,
    bool IsNamed,
    int Position);

public readonly record struct MarkupExtensionInfo(
    string Name,
    ImmutableArray<string> PositionalArguments,
    ImmutableDictionary<string, string> NamedArguments,
    ImmutableArray<MarkupExtensionArgument> Arguments);
