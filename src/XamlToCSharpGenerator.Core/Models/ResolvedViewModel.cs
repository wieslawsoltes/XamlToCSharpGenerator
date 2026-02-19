using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedViewModel(
    XamlDocumentModel Document,
    string BuildUri,
    string ClassModifier,
    bool CreateSourceInfo,
    bool EnableHotReload,
    ImmutableArray<string> PassExecutionTrace,
    bool EmitNameScopeRegistration,
    bool EmitStaticResourceResolver,
    ResolvedObjectNode RootObject,
    ImmutableArray<ResolvedNamedElement> NamedElements,
    ImmutableArray<ResolvedResourceDefinition> Resources,
    ImmutableArray<ResolvedTemplateDefinition> Templates,
    ImmutableArray<ResolvedCompiledBindingDefinition> CompiledBindings,
    ImmutableArray<ResolvedStyleDefinition> Styles,
    ImmutableArray<ResolvedControlThemeDefinition> ControlThemes,
    ImmutableArray<ResolvedIncludeDefinition> Includes);
