using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed record FrameworkObjectGraphEmissionContext(
    StringBuilder SourceBuilder,
    string Indent,
    string RootReference,
    IReadOnlyDictionary<string, string> NamedFieldMap,
    IReadOnlyDictionary<string, string> EmittedEventBindingMethodNames,
    bool EmitNameScopeRegistration,
    string? NameScopeReference,
    string? BindingXmlNamespaceMapReference,
    string ServiceProviderReference,
    string BaseUriExpression,
    ImmutableArray<string> ParentStackReferences,
    string IntermediateRootReference,
    bool EmitDebugLineDirectives,
    string LineDirectiveFilePath);
