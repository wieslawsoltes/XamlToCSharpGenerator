using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignElementNode(
    string Id,
    string DisplayName,
    string TypeName,
    string? XamlName,
    string? Classes,
    int Depth,
    bool IsSelected,
    int Line,
    IReadOnlyList<SourceGenHotDesignElementNode> Children,
    bool IsExpanded = false,
    int DescendantCount = 0,
    string? SourceBuildUri = null,
    string? SourceElementId = null,
    bool IsLive = false,
    SourceGenHotDesignTextRange? SourceRange = null);
