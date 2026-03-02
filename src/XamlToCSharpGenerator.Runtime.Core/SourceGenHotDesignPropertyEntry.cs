using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignPropertyEntry(
    string Name,
    string? Value,
    string TypeName,
    bool IsSet,
    bool IsAttached,
    bool IsMarkupExtension,
    IReadOnlyList<SourceGenHotDesignPropertyQuickSet> QuickSets,
    string Category = "General",
    string Source = "Local",
    string OwnerTypeName = "",
    string EditorKind = "Text",
    bool IsPinned = false,
    bool IsReadOnly = false,
    bool CanReset = true,
    IReadOnlyList<string>? EnumOptions = null);
