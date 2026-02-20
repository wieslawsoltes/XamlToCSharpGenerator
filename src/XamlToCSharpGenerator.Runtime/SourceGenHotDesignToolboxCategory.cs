using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignToolboxCategory(
    string Name,
    IReadOnlyList<SourceGenHotDesignToolboxItem> Items);
