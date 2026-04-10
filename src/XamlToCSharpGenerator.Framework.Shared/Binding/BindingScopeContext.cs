using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record BindingScopeContext(
    XamlObjectNode Node,
    INamedTypeSymbol? NodeType,
    INamedTypeSymbol? InheritedDataType,
    INamedTypeSymbol? SetterTargetType,
    bool CompileBindingsEnabled,
    INamedTypeSymbol? RootTypeSymbol,
    string XBindDefaultMode,
    bool IsInsideDataTemplate,
    BindingScopeContext? Parent,
    string? ParentPropertyName)
{
    public INamedTypeSymbol? NodeDataType { get; init; }
}
