using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenStudioScopeDescriptor(
    SourceGenStudioScopeKind ScopeKind,
    string Id,
    string DisplayName,
    Type? TargetType = null,
    string? BuildUri = null);
