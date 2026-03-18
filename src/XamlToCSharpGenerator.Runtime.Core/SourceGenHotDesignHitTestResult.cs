namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignHitTestResult(
    bool Succeeded,
    bool SelectionChanged,
    string? ActiveBuildUri,
    string? ElementId,
    SourceGenHotDesignElementNode? Element,
    SourceGenHotDesignOverlaySnapshot Overlay,
    string? Message = null);
