namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignOverlayItem(
    string? ActiveBuildUri,
    string? ElementId,
    SourceGenHotDesignElementNode? Element,
    SourceGenHotDesignOverlayBounds? Bounds,
    string? DisplayLabel);
