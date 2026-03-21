using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedViewModel(
    XamlDocumentModel Document,
    string BuildUri,
    string ClassModifier,
    bool CreateSourceInfo,
    bool EnableHotReload,
    bool EnableHotDesign,
    ImmutableArray<string> PassExecutionTrace,
    bool EmitNameScopeRegistration,
    bool EmitStaticResourceResolver,
    bool HasXBind,
    ResolvedObjectNode RootObject,
    ImmutableArray<ResolvedNamedElement> NamedElements,
    ImmutableArray<ResolvedResourceDefinition> Resources,
    ImmutableArray<ResolvedTemplateDefinition> Templates,
    ImmutableArray<ResolvedCompiledBindingDefinition> CompiledBindings,
    ImmutableArray<ResolvedUnsafeAccessorDefinition> UnsafeAccessors,
    ImmutableArray<ResolvedStyleDefinition> Styles,
    ImmutableArray<ResolvedControlThemeDefinition> ControlThemes,
    ImmutableArray<ResolvedIncludeDefinition> Includes,
    ResolvedHotDesignArtifactKind HotDesignArtifactKind,
    ImmutableArray<string> HotDesignScopeHints);
