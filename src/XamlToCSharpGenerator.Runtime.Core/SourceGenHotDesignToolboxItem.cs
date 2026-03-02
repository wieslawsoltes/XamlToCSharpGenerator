using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignToolboxItem(
    string Name,
    string DisplayName,
    string Category,
    string XamlSnippet,
    bool IsProjectControl,
    IReadOnlyList<string>? Tags = null);
