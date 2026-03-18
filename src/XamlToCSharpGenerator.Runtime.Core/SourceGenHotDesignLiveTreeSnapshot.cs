using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignLiveTreeSnapshot(
    SourceGenHotDesignHitTestMode Mode,
    string? ActiveBuildUri,
    string? SelectedElementId,
    IReadOnlyList<SourceGenHotDesignElementNode> Elements);
