using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class CompiledBindingSourceTypeResolutionService
{
    public delegate INamedTypeSymbol? ResolveTypeExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace);

    public delegate INamedTypeSymbol? ResolveRootTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document);

    private readonly ResolveTypeExpressionDelegate _resolveTypeFromTypeExpression;
    private readonly ResolveTypeExpressionDelegate _resolveTypeToken;
    private readonly ResolveRootTypeDelegate _resolveRootType;

    public CompiledBindingSourceTypeResolutionService(
        ResolveTypeExpressionDelegate resolveTypeFromTypeExpression,
        ResolveTypeExpressionDelegate resolveTypeToken,
        ResolveRootTypeDelegate resolveRootType)
    {
        _resolveTypeFromTypeExpression = resolveTypeFromTypeExpression ?? throw new ArgumentNullException(nameof(resolveTypeFromTypeExpression));
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _resolveRootType = resolveRootType ?? throw new ArgumentNullException(nameof(resolveRootType));
    }

    public INamedTypeSymbol? TryResolveFromTypeExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace)
    {
        return _resolveTypeFromTypeExpression(compilation, document, typeExpression, fallbackClrNamespace);
    }

    public INamedTypeSymbol? TryResolveTypeToken(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeToken,
        string? fallbackClrNamespace)
    {
        return _resolveTypeToken(compilation, document, typeToken, fallbackClrNamespace);
    }

    public INamedTypeSymbol? ResolveRootType(
        Compilation compilation,
        XamlDocumentModel document)
    {
        return _resolveRootType(compilation, document);
    }
}
