using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryBindClrPropertyAssignment(
        ClrPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        if (!ClrPropertyAssignmentBindingService.TryBind(request, diagnostics, out resolvedAssignment))
        {
            resolvedAssignment = null;
            return false;
        }

        return true;
    }

    private static bool IsPotentialCSharpExpressionMarkup(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        bool cSharpExpressionsEnabled,
        bool implicitCSharpExpressionsEnabled)
    {
        return ExpressionClassificationService.TryParseCSharpExpressionMarkup(
            value,
            compilation,
            document,
            cSharpExpressionsEnabled,
            implicitCSharpExpressionsEnabled,
            out _,
            out _);
    }

    private static bool TryResolveClrPropertySetterValueWithPolicy(
        ClrPropertyAssignmentBindingRequest request,
        ITypeSymbol conversionTargetType,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedValueConversionResult resolution)
    {
        return TryResolveSetterValueWithPolicy(
            rawValue: request.Assignment.Value,
            conversionTargetType: conversionTargetType,
            compilation: request.Compilation,
            document: request.Document,
            setterTargetType: request.CurrentSetterTargetType,
            bindingPriorityScope: (BindingPriorityScope)request.BindingPriorityScope,
            strictMode: request.Options.StrictMode,
            preferTypedStaticResourceCoercion: true,
            allowObjectStringLiteralFallbackDuringConversion: !request.Options.StrictMode &&
                                                            conversionTargetType.SpecialType == SpecialType.System_Object,
            allowCompatibilityStringLiteralFallback: !request.Options.StrictMode &&
                                                     conversionTargetType.SpecialType == SpecialType.System_Object,
            propertyName: request.Property.Name,
            ownerDisplayName: request.OwnerType.ToDisplayString(),
            line: request.Assignment.Line,
            column: request.Assignment.Column,
            diagnostics: diagnostics,
            resolution: out resolution,
            selectorNestingTypeHint: request.SelectorNestingTypeHint,
            setterContext: false,
            converterAttributes: request.Property.GetAttributes());
    }

    private static bool TryConvertClrPropertyLiteralValue(
        string rawValue,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool allowObjectStringLiteralFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        ImmutableArray<AttributeData> converterAttributes)
    {
        return TryConvertValueConversion(
            rawValue,
            targetType,
            compilation,
            document,
            setterTargetType,
            (BindingPriorityScope)bindingPriorityScope,
            out conversion,
            allowObjectStringLiteralFallback: allowObjectStringLiteralFallback,
            selectorNestingTypeHint: selectorNestingTypeHint,
            converterAttributes: converterAttributes);
    }

    private static ResolvedPropertyAssignment CreateClrPropertyAssignment(
        ClrPropertyAssignmentBindingRequest request,
        string valueExpression,
        ResolvedValueKind valueKind,
        bool requiresStaticResourceResolver,
        ResolvedValueRequirements valueRequirements,
        bool preserveBindingValue)
    {
        return CreateClrPropertyAssignment(
            request.Property,
            valueExpression,
            request.Compilation,
            request.UnsafeAccessors,
            request.Assignment.Line,
            request.Assignment.Column,
            request.Assignment.Condition,
            valueKind,
            requiresStaticResourceResolver,
            valueRequirements,
            preserveBindingValue);
    }
}
