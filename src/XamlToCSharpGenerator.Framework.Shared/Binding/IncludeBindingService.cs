using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.Framework.Shared.Runtime;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class IncludeBindingService
{
    private readonly IXamlFrameworkDocumentUriResolver _documentUriResolver;
    private readonly XamlIncludeUriResolutionService _includeUriResolutionService;
    private readonly Func<ConditionalXamlExpression?, Compilation, XamlDocumentModel, ImmutableArray<DiagnosticInfo>.Builder, GeneratorOptions, bool> _shouldSkipBranch;

    public IncludeBindingService(
        IXamlFrameworkDocumentUriResolver documentUriResolver,
        XamlIncludeUriResolutionService includeUriResolutionService,
        Func<ConditionalXamlExpression?, Compilation, XamlDocumentModel, ImmutableArray<DiagnosticInfo>.Builder, GeneratorOptions, bool> shouldSkipBranch)
    {
        _documentUriResolver = documentUriResolver;
        _includeUriResolutionService = includeUriResolutionService;
        _shouldSkipBranch = shouldSkipBranch;
    }

    public ImmutableArray<ResolvedIncludeDefinition> BindIncludes(
        XamlDocumentModel document,
        Compilation compilation,
        string currentDocumentUri,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        if (document.Includes.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedIncludeDefinition>.Empty;
        }

        var resolvedIncludes = ImmutableArray.CreateBuilder<ResolvedIncludeDefinition>(document.Includes.Length);
        for (var index = 0; index < document.Includes.Length; index++)
        {
            var include = document.Includes[index];
            if (_shouldSkipBranch(include.Condition, compilation, document, diagnostics, options))
            {
                continue;
            }

            var normalizedSource = _includeUriResolutionService.NormalizeIncludeSource(include.Source);
            if (string.IsNullOrWhiteSpace(normalizedSource))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0400",
                    $"Include '{include.Kind}' must declare a non-empty Source.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            if (!XamlIncludeUriResolutionService.IsKnownMergeTarget(include.MergeTarget))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0402",
                    $"Include '{include.Kind}' uses unsupported merge target '{include.MergeTarget}'.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            var isAbsoluteUri = Uri.TryCreate(normalizedSource, UriKind.Absolute, out _);
            string? resolvedSourceUri = null;
            var isProjectLocal = false;
            if (_includeUriResolutionService.TryResolveIncludeUri(
                    normalizedSource,
                    document.TargetPath,
                    currentDocumentUri,
                    _documentUriResolver,
                    out var candidateResolvedUri,
                    out var candidateIsProjectLocal))
            {
                resolvedSourceUri = candidateResolvedUri;
                isProjectLocal = candidateIsProjectLocal;
            }

            resolvedIncludes.Add(new ResolvedIncludeDefinition(
                include.Kind,
                include.Source,
                include.MergeTarget,
                isAbsoluteUri,
                resolvedSourceUri,
                isProjectLocal,
                include.RawXaml,
                include.Line,
                include.Column,
                include.Condition));
        }

        return resolvedIncludes.ToImmutable();
    }
}
