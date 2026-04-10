using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class MarkupRuntimeOperationResolutionService
{
    private readonly ResourceKeyResolutionService _resourceKeyResolutionService;

    public MarkupRuntimeOperationResolutionService(ResourceKeyResolutionService resourceKeyResolutionService)
    {
        _resourceKeyResolutionService = resourceKeyResolutionService;
    }

    public bool TryResolve(
        MarkupExtensionInfo markup,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedMarkupRuntimeOperation operation)
    {
        operation = default;
        switch (XamlMarkupExtensionNameSemantics.Classify(markup.Name))
        {
            case XamlMarkupExtensionKind.StaticResource:
                if (_resourceKeyResolutionService.TryBuildResourceKeyExpression(markup, compilation, document, out var staticKey))
                {
                    operation = new ResolvedMarkupRuntimeOperation(ResolvedMarkupRuntimeOperationKind.StaticResource, staticKey);
                    return true;
                }

                return false;

            case XamlMarkupExtensionKind.DynamicResource:
                if (_resourceKeyResolutionService.TryBuildResourceKeyExpression(markup, compilation, document, out var dynamicKey))
                {
                    operation = new ResolvedMarkupRuntimeOperation(ResolvedMarkupRuntimeOperationKind.DynamicResource, dynamicKey);
                    return true;
                }

                return false;

            case XamlMarkupExtensionKind.Reference:
            case XamlMarkupExtensionKind.ResolveByName:
            {
                var rawName = BindingEventMarkupParser.TryGetNamedMarkupArgument(markup, "Name", "ElementName") ??
                              (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
                if (!XamlReferenceNameSemantics.TryNormalizeReferenceName(rawName, out var normalizedName))
                {
                    return false;
                }

                operation = new ResolvedMarkupRuntimeOperation(
                    ResolvedMarkupRuntimeOperationKind.Reference,
                    ReferenceName: normalizedName);
                return true;
            }

            default:
                return false;
        }
    }
}
