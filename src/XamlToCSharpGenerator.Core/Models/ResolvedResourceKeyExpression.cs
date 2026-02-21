namespace XamlToCSharpGenerator.Core.Models;

public readonly record struct ResolvedResourceKeyExpression(
    string Expression,
    ResolvedResourceKeyKind Kind);
