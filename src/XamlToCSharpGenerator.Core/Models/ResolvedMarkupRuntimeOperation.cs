namespace XamlToCSharpGenerator.Core.Models;

public readonly record struct ResolvedMarkupRuntimeOperation(
    ResolvedMarkupRuntimeOperationKind Kind,
    ResolvedResourceKeyExpression? ResourceKey = null,
    string? ReferenceName = null);
