namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed record FrameworkHotReloadScaffoldContext(
    string RootTypeName,
    string ClassName,
    string EscapedUri,
    string EscapedSourcePath,
    string CollectionCleanupDescriptorArrayExpression,
    string ClrPropertyCleanupDescriptorArrayExpression,
    string FrameworkPropertyCleanupDescriptorArrayExpression,
    string EventCleanupDescriptorArrayExpression,
    bool ClearsRootCollection,
    bool HasXBind,
    bool EnableHotReload,
    bool EnableHotDesign,
    string HotDesignDocumentRoleExpression,
    string HotDesignArtifactKindExpression,
    string HotDesignScopeHintsExpression);

