using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class RelativeSourceBindingPlanService
{
    public delegate bool HasRelativeSourceSupportDelegate(Compilation compilation);
    public delegate bool TryMapTokenDelegate(string token, out string expression);
    public delegate INamedTypeSymbol? ResolveTypeTokenDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        string fallbackClrNamespace);

    private readonly HasRelativeSourceSupportDelegate _hasRelativeSourceSupport;
    private readonly TryMapTokenDelegate _tryMapRelativeSourceMode;
    private readonly TryMapTokenDelegate _tryMapTreeType;
    private readonly ResolveTypeTokenDelegate _resolveTypeToken;

    public RelativeSourceBindingPlanService(
        HasRelativeSourceSupportDelegate hasRelativeSourceSupport,
        TryMapTokenDelegate tryMapRelativeSourceMode,
        TryMapTokenDelegate tryMapTreeType,
        ResolveTypeTokenDelegate resolveTypeToken)
    {
        _hasRelativeSourceSupport = hasRelativeSourceSupport ?? throw new ArgumentNullException(nameof(hasRelativeSourceSupport));
        _tryMapRelativeSourceMode = tryMapRelativeSourceMode ?? throw new ArgumentNullException(nameof(tryMapRelativeSourceMode));
        _tryMapTreeType = tryMapTreeType ?? throw new ArgumentNullException(nameof(tryMapTreeType));
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
    }

    public bool HasRelativeSourceSupport(Compilation compilation) => _hasRelativeSourceSupport(compilation);
    public bool TryMapRelativeSourceMode(string token, out string expression) => _tryMapRelativeSourceMode(token, out expression);
    public bool TryMapTreeType(string token, out string expression) => _tryMapTreeType(token, out expression);
    public INamedTypeSymbol? ResolveTypeToken(Compilation compilation, XamlDocumentModel document, string token, string fallbackClrNamespace) => _resolveTypeToken(compilation, document, token, fallbackClrNamespace);
}
