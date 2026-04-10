using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ResourceDefinitionBindingService
{
    public delegate bool ShouldSkipBranchDelegate(
        ConditionalXamlExpression? condition,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options);

    public delegate INamedTypeSymbol? ResolveResourceTypeSymbolDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        XamlResourceDefinition resource);

    private readonly ShouldSkipBranchDelegate _shouldSkipBranch;
    private readonly ResolveResourceTypeSymbolDelegate _resolveResourceTypeSymbol;

    public ResourceDefinitionBindingService(
        ShouldSkipBranchDelegate shouldSkipBranch,
        ResolveResourceTypeSymbolDelegate resolveResourceTypeSymbol)
    {
        _shouldSkipBranch = shouldSkipBranch ?? throw new ArgumentNullException(nameof(shouldSkipBranch));
        _resolveResourceTypeSymbol = resolveResourceTypeSymbol ?? throw new ArgumentNullException(nameof(resolveResourceTypeSymbol));
    }

    public ImmutableArray<ResolvedResourceDefinition> BindResources(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var resources = ImmutableArray.CreateBuilder<ResolvedResourceDefinition>(document.Resources.Length);
        foreach (var resource in document.Resources)
        {
            if (_shouldSkipBranch(resource.Condition, compilation, document, diagnostics, options))
            {
                continue;
            }

            var resourceType = _resolveResourceTypeSymbol(compilation, document, resource);
            resources.Add(new ResolvedResourceDefinition(
                Key: resource.Key,
                TypeName: resourceType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? resource.XmlTypeName,
                RawXaml: resource.RawXaml,
                Line: resource.Line,
                Column: resource.Column,
                Condition: resource.Condition));
        }

        return resources.ToImmutable();
    }
}
