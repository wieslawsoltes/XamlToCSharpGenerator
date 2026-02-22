using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignWorkspaceSnapshot(
    SourceGenHotDesignStatus Status,
    SourceGenHotDesignWorkspaceMode Mode,
    SourceGenHotDesignPropertyFilterMode PropertyFilterMode,
    SourceGenHotDesignPanelState Panels,
    SourceGenHotDesignCanvasState Canvas,
    string? ActiveBuildUri,
    string? SelectedElementId,
    bool CanUndo,
    bool CanRedo,
    string? CurrentXamlText,
    IReadOnlyList<SourceGenHotDesignDocumentDescriptor> Documents,
    IReadOnlyList<SourceGenHotDesignElementNode> Elements,
    IReadOnlyList<SourceGenHotDesignPropertyEntry> Properties,
    IReadOnlyList<SourceGenHotDesignToolboxCategory> Toolbox);
