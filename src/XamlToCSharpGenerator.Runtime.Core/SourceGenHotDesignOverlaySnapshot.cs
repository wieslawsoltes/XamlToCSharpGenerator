namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignOverlaySnapshot(
    SourceGenHotDesignHitTestMode Mode,
    string? ActiveBuildUri,
    string? SelectedElementId,
    double RootWidth,
    double RootHeight,
    SourceGenHotDesignOverlayItem? Selected,
    SourceGenHotDesignOverlayItem? Hover);
