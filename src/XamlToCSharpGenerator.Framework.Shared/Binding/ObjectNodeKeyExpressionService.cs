using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ObjectNodeKeyExpressionService
{
    private readonly ResourceKeyResolutionService _resourceKeyResolutionService;
    private readonly Func<string, string> _escape;

    public ObjectNodeKeyExpressionService(
        ResourceKeyResolutionService resourceKeyResolutionService,
        Func<string, string> escape)
    {
        _resourceKeyResolutionService = resourceKeyResolutionService ?? throw new ArgumentNullException(nameof(resourceKeyResolutionService));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    public string? BuildObjectNodeKeyExpression(
        string? rawKey,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        if (_resourceKeyResolutionService.TryBuildResourceKeyExpression(rawKey!, compilation, document, out var resourceKey))
        {
            return resourceKey.Expression;
        }

        return "\"" + _escape(rawKey!.Trim()) + "\"";
    }
}
