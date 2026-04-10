using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ResolvedSetterIdentityPlan(string DuplicateIdentityKey);

public sealed record SetterPropertyBindingPlan(
    string ResolvedPropertyName,
    ResolvedSetterIdentityPlan SetterIdentityPlan,
    IPropertySymbol? TargetProperty,
    ITypeSymbol? SetterValueType,
    bool PreferTypedStaticResourceCoercion,
    ResolvedFrameworkPropertyOperation? FrameworkPropertyOperation,
    bool IsMissingOnTargetType);

public sealed class SetterPropertyBindingPlanService
{
    public delegate PropertyAliasResolution ResolvePropertyAliasDelegate(INamedTypeSymbol? targetType, string propertyToken);
    public delegate bool TrySplitOwnerQualifiedPropertyTokenDelegate(string propertyToken, out string ownerToken, out string propertyName);
    public delegate INamedTypeSymbol? ResolveTypeTokenDelegate(Compilation compilation, XamlDocumentModel document, string token, string fallbackClrNamespace);
    public delegate IPropertySymbol? FindPropertyDelegate(INamedTypeSymbol ownerType, string propertyName);
    public delegate bool TryFindFrameworkPropertyFieldDelegate(
        INamedTypeSymbol ownerType,
        string propertyName,
        string? explicitFieldName,
        out INamedTypeSymbol resolvedOwnerType,
        out IFieldSymbol propertyField);
    public delegate ITypeSymbol? TryGetFrameworkPropertyValueTypeDelegate(ITypeSymbol propertyFieldType);
    public delegate ResolvedFrameworkPropertyOperation CreateFrameworkPropertyOperationDelegate(string propertyOwnerTypeName, string propertyFieldName);

    private readonly string _frameworkId;
    private readonly ResolvePropertyAliasDelegate _resolvePropertyAlias;
    private readonly TrySplitOwnerQualifiedPropertyTokenDelegate _trySplitOwnerQualifiedPropertyToken;
    private readonly ResolveTypeTokenDelegate _resolveTypeToken;
    private readonly FindPropertyDelegate _findProperty;
    private readonly TryFindFrameworkPropertyFieldDelegate _tryFindFrameworkPropertyField;
    private readonly TryGetFrameworkPropertyValueTypeDelegate _tryGetFrameworkPropertyValueType;
    private readonly SetterIdentityPlanningService _setterIdentityPlanningService;
    private readonly CreateFrameworkPropertyOperationDelegate _createFrameworkPropertyOperation;

    public SetterPropertyBindingPlanService(
        string frameworkId,
        ResolvePropertyAliasDelegate resolvePropertyAlias,
        TrySplitOwnerQualifiedPropertyTokenDelegate trySplitOwnerQualifiedPropertyToken,
        ResolveTypeTokenDelegate resolveTypeToken,
        FindPropertyDelegate findProperty,
        TryFindFrameworkPropertyFieldDelegate tryFindFrameworkPropertyField,
        TryGetFrameworkPropertyValueTypeDelegate tryGetFrameworkPropertyValueType,
        SetterIdentityPlanningService setterIdentityPlanningService,
        CreateFrameworkPropertyOperationDelegate createFrameworkPropertyOperation)
    {
        _frameworkId = frameworkId ?? throw new ArgumentNullException(nameof(frameworkId));
        _resolvePropertyAlias = resolvePropertyAlias ?? throw new ArgumentNullException(nameof(resolvePropertyAlias));
        _trySplitOwnerQualifiedPropertyToken = trySplitOwnerQualifiedPropertyToken ?? throw new ArgumentNullException(nameof(trySplitOwnerQualifiedPropertyToken));
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _findProperty = findProperty ?? throw new ArgumentNullException(nameof(findProperty));
        _tryFindFrameworkPropertyField = tryFindFrameworkPropertyField ?? throw new ArgumentNullException(nameof(tryFindFrameworkPropertyField));
        _tryGetFrameworkPropertyValueType = tryGetFrameworkPropertyValueType ?? throw new ArgumentNullException(nameof(tryGetFrameworkPropertyValueType));
        _setterIdentityPlanningService = setterIdentityPlanningService ?? throw new ArgumentNullException(nameof(setterIdentityPlanningService));
        _createFrameworkPropertyOperation = createFrameworkPropertyOperation ?? throw new ArgumentNullException(nameof(createFrameworkPropertyOperation));
    }

    public SetterPropertyBindingPlan BuildPlan(
        string propertyName,
        INamedTypeSymbol? targetType,
        Compilation compilation,
        XamlDocumentModel document)
    {
        var alias = _resolvePropertyAlias(targetType, propertyName);
        var resolvedPropertyName = alias.ResolvedPropertyName;
        var identityKey = _setterIdentityPlanningService.BuildIdentityKey(resolvedPropertyName);
        var frameworkPropertyOwnerType = alias.GetFrameworkPropertyOwnerTypeSymbol(_frameworkId);
        var frameworkPropertyFieldName = alias.GetFrameworkPropertyFieldName(_frameworkId);

        if (frameworkPropertyOwnerType is not null &&
            frameworkPropertyFieldName is not null &&
            _tryFindFrameworkPropertyField(
                frameworkPropertyOwnerType,
                resolvedPropertyName,
                frameworkPropertyFieldName,
                out var resolvedOwnerType,
                out var propertyField))
        {
            var fallbackClrProperty = _findProperty(resolvedOwnerType, resolvedPropertyName);
            var frameworkSetterValueType = _tryGetFrameworkPropertyValueType(propertyField.Type);
            var setterValueType = frameworkSetterValueType ?? fallbackClrProperty?.Type;
            return new SetterPropertyBindingPlan(
                ResolvedPropertyName: resolvedPropertyName,
                SetterIdentityPlan: new ResolvedSetterIdentityPlan(identityKey),
                TargetProperty: frameworkSetterValueType is null ? fallbackClrProperty : null,
                SetterValueType: setterValueType,
                PreferTypedStaticResourceCoercion: true,
                FrameworkPropertyOperation: _createFrameworkPropertyOperation(
                    resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    propertyField.Name),
                IsMissingOnTargetType: false);
        }

        if (targetType is not null)
        {
            if (TryResolveOwnerQualifiedFrameworkSetterPlan(
                    propertyName,
                    compilation,
                    document,
                    out var ownerQualifiedPlan) ||
                (resolvedPropertyName != propertyName &&
                 TryResolveOwnerQualifiedFrameworkSetterPlan(
                     resolvedPropertyName,
                     compilation,
                     document,
                     out ownerQualifiedPlan)))
            {
                return ownerQualifiedPlan;
            }

            if (_tryFindFrameworkPropertyField(
                    targetType,
                    resolvedPropertyName,
                    frameworkPropertyFieldName,
                    out var resolvedTargetOwnerType,
                    out var targetPropertyField))
            {
                var fallbackClrProperty = _findProperty(resolvedTargetOwnerType, resolvedPropertyName) ??
                                          _findProperty(targetType, resolvedPropertyName);
                var frameworkSetterValueType = _tryGetFrameworkPropertyValueType(targetPropertyField.Type);
                var setterValueType = frameworkSetterValueType ?? fallbackClrProperty?.Type;
                return new SetterPropertyBindingPlan(
                    ResolvedPropertyName: resolvedPropertyName,
                    SetterIdentityPlan: new ResolvedSetterIdentityPlan(identityKey),
                    TargetProperty: frameworkSetterValueType is null ? fallbackClrProperty : null,
                    SetterValueType: setterValueType,
                    PreferTypedStaticResourceCoercion: true,
                    FrameworkPropertyOperation: _createFrameworkPropertyOperation(
                        resolvedTargetOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        targetPropertyField.Name),
                    IsMissingOnTargetType: false);
            }

            var clrProperty = _findProperty(targetType, resolvedPropertyName);
            if (clrProperty is not null)
            {
                return new SetterPropertyBindingPlan(
                    ResolvedPropertyName: resolvedPropertyName,
                    SetterIdentityPlan: new ResolvedSetterIdentityPlan(identityKey),
                    TargetProperty: clrProperty,
                    SetterValueType: clrProperty.Type,
                    PreferTypedStaticResourceCoercion: true,
                    FrameworkPropertyOperation: null,
                    IsMissingOnTargetType: false);
            }
        }

        return new SetterPropertyBindingPlan(
            ResolvedPropertyName: resolvedPropertyName,
            SetterIdentityPlan: new ResolvedSetterIdentityPlan(identityKey),
            TargetProperty: null,
            SetterValueType: null,
            PreferTypedStaticResourceCoercion: true,
            FrameworkPropertyOperation: null,
            IsMissingOnTargetType: targetType is not null);

        bool TryResolveOwnerQualifiedFrameworkSetterPlan(
            string propertyToken,
            Compilation currentCompilation,
            XamlDocumentModel currentDocument,
            out SetterPropertyBindingPlan resolvedPlan)
        {
            if (_trySplitOwnerQualifiedPropertyToken(propertyToken, out var ownerToken, out var memberName))
            {
                var ownerType = _resolveTypeToken(currentCompilation, currentDocument, ownerToken, currentDocument.ClassNamespace);
                if (ownerType is not null &&
                    _tryFindFrameworkPropertyField(ownerType, memberName, explicitFieldName: null, out var ownerTypeForField, out var ownerPropertyField))
                {
                    var fallbackClrProperty = _findProperty(ownerTypeForField, memberName);
                    var frameworkSetterValueType = _tryGetFrameworkPropertyValueType(ownerPropertyField.Type);
                    var setterValueType = frameworkSetterValueType ?? fallbackClrProperty?.Type;
                    resolvedPlan = new SetterPropertyBindingPlan(
                        ResolvedPropertyName: memberName,
                        SetterIdentityPlan: new ResolvedSetterIdentityPlan(_setterIdentityPlanningService.BuildIdentityKey(ownerToken + "." + memberName)),
                        TargetProperty: frameworkSetterValueType is null ? fallbackClrProperty : null,
                        SetterValueType: setterValueType,
                        PreferTypedStaticResourceCoercion: true,
                        FrameworkPropertyOperation: _createFrameworkPropertyOperation(
                            ownerTypeForField.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            ownerPropertyField.Name),
                        IsMissingOnTargetType: false);
                    return true;
                }
            }

            resolvedPlan = default!;
            return false;
        }
    }
}
