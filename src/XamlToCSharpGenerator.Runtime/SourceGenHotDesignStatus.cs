namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignStatus(
    bool IsEnabled,
    int RegisteredDocumentCount,
    int RegisteredApplierCount,
    SourceGenHotDesignOptions Options);
