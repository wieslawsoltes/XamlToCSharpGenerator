using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class MarkupRuntimeOperationEmissionService
{
    private readonly Func<Compilation, bool> _hasDynamicResourceExtensionSupport;
    private readonly Func<ResolvedResourceKeyExpression, string> _buildStaticResourceOperationExpression;
    private readonly Func<ResolvedResourceKeyExpression, string> _buildDynamicResourceOperationExpression;
    private readonly Func<string, string> _buildReferenceOperationExpression;
    private readonly Func<ITypeSymbol, string, string> _buildTypedStaticResourceCoercionExpression;
    private readonly Func<ITypeSymbol, string, string> _wrapWithTargetTypeCast;

    public MarkupRuntimeOperationEmissionService(
        Func<Compilation, bool> hasDynamicResourceExtensionSupport,
        Func<ResolvedResourceKeyExpression, string> buildStaticResourceOperationExpression,
        Func<ResolvedResourceKeyExpression, string> buildDynamicResourceOperationExpression,
        Func<string, string> buildReferenceOperationExpression,
        Func<ITypeSymbol, string, string> buildTypedStaticResourceCoercionExpression,
        Func<ITypeSymbol, string, string> wrapWithTargetTypeCast)
    {
        _hasDynamicResourceExtensionSupport = hasDynamicResourceExtensionSupport;
        _buildStaticResourceOperationExpression = buildStaticResourceOperationExpression;
        _buildDynamicResourceOperationExpression = buildDynamicResourceOperationExpression;
        _buildReferenceOperationExpression = buildReferenceOperationExpression;
        _buildTypedStaticResourceCoercionExpression = buildTypedStaticResourceCoercionExpression;
        _wrapWithTargetTypeCast = wrapWithTargetTypeCast;
    }

    public bool TryConvert(
        ResolvedMarkupRuntimeOperation operation,
        ITypeSymbol targetType,
        Compilation compilation,
        bool preferTypedStaticResourceCoercion,
        out ResolvedValueConversionResult conversion)
    {
        conversion = default;
        switch (operation.Kind)
        {
            case ResolvedMarkupRuntimeOperationKind.StaticResource:
                if (operation.ResourceKey is null)
                {
                    return false;
                }

                var staticResourceExpression = _buildStaticResourceOperationExpression(operation.ResourceKey.Value);
                conversion = new ResolvedValueConversionResult(
                    preferTypedStaticResourceCoercion
                        ? _buildTypedStaticResourceCoercionExpression(targetType, staticResourceExpression)
                        : staticResourceExpression,
                    ResolvedValueKind.MarkupExtension,
                    RequiresRuntimeServiceProvider: true,
                    RequiresProvideValueTarget: true,
                    RequiresRootObject: true,
                    RequiresBaseUri: true,
                    RequiresStaticResourceResolver: true,
                    ResourceKey: operation.ResourceKey,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
                return true;

            case ResolvedMarkupRuntimeOperationKind.DynamicResource:
                if (operation.ResourceKey is null || !_hasDynamicResourceExtensionSupport(compilation))
                {
                    return false;
                }

                conversion = new ResolvedValueConversionResult(
                    _buildDynamicResourceOperationExpression(operation.ResourceKey.Value),
                    ResolvedValueKind.DynamicResourceBinding,
                    RequiresRuntimeServiceProvider: true,
                    RequiresProvideValueTarget: true,
                    RequiresRootObject: true,
                    RequiresBaseUri: true,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
                return true;

            case ResolvedMarkupRuntimeOperationKind.Reference:
                if (string.IsNullOrWhiteSpace(operation.ReferenceName))
                {
                    return false;
                }

                conversion = new ResolvedValueConversionResult(
                    _wrapWithTargetTypeCast(targetType, _buildReferenceOperationExpression(operation.ReferenceName!)),
                    ResolvedValueKind.MarkupExtension,
                    RequiresRuntimeServiceProvider: true,
                    RequiresProvideValueTarget: true,
                    RequiresRootObject: true,
                    RequiresBaseUri: true,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
                return true;

            default:
                return false;
        }
    }
}
