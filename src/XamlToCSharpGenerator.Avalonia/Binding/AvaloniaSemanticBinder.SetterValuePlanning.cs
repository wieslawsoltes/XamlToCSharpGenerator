using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryResolveCompiledBindingSourceType(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? ambientDataType,
        INamedTypeSymbol? targetType,
        out INamedTypeSymbol? sourceType,
        out bool requiresAmbientDataType,
        out bool hasInvalidLocalDataType)
    {
        requiresAmbientDataType = false;
        hasInvalidLocalDataType = false;
        sourceType = null;

        if (bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        bindingMarkup = NormalizeBindingQuerySyntax(bindingMarkup);

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName) ||
            !string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            return false;
        }

        if (bindingMarkup.RelativeSource is { } relativeSource)
        {
            if (string.Equals(relativeSource.Mode, "Self", StringComparison.OrdinalIgnoreCase))
            {
                sourceType = targetType;
                return sourceType is not null;
            }

            if (!string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken))
            {
                sourceType = ResolveTypeToken(compilation, document, relativeSource.AncestorTypeToken!, document.ClassNamespace);
                return sourceType is not null;
            }

            if (string.Equals(relativeSource.Mode, "DataContext", StringComparison.OrdinalIgnoreCase))
            {
                if (TryResolveBindingMarkupDataType(
                        compilation,
                        document,
                        bindingMarkup,
                        out sourceType,
                        out var hasExplicitLocalDataType))
                {
                    return true;
                }

                if (hasExplicitLocalDataType)
                {
                    hasInvalidLocalDataType = true;
                    return false;
                }

                sourceType = ambientDataType;
                requiresAmbientDataType = sourceType is null;
                return sourceType is not null;
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.DataType))
        {
            sourceType = ResolveTypeFromTypeExpression(compilation, document, bindingMarkup.DataType, document.ClassNamespace);
            if (sourceType is not null)
            {
                return true;
            }

            hasInvalidLocalDataType = true;
            return false;
        }

        sourceType = ambientDataType;
        requiresAmbientDataType = sourceType is null;
        return sourceType is not null;
    }

    private static bool TryBuildSetterCompiledBindingAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CompiledBindingAccessorResolutionResult resolution,
        out string errorMessage)
    {
        return CompiledBindingAccessorResolutionService.TryBuildAccessorExpression(
            compilation,
            document,
            sourceType,
            rawPath,
            targetPropertyType,
            unsafeAccessors,
            out resolution,
            out errorMessage);
    }

    private static bool TryResolveSetterShorthandPlan(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? scopeDataType,
        INamedTypeSymbol? rootContextType,
        INamedTypeSymbol? targetType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out ResolvedSetterShorthandPlan plan)
    {
        plan = default!;
        if (!TryResolveImplicitCSharpShorthandExpression(
                rawValue,
                compilation,
                document,
                options,
                scopeDataType,
                rootContextType,
                targetType,
                unsafeAccessors,
                out var isShorthandExpression,
                out var shorthandResolution) ||
            !isShorthandExpression)
        {
            return false;
        }

        plan = new ResolvedSetterShorthandPlan(
            ValueExpression: shorthandResolution.ValueExpression ?? string.Empty,
            ResolutionKind: shorthandResolution.Kind,
            IsCompiledBinding: false,
            CompiledBindingPath: shorthandResolution.Path,
            CompiledBindingSourceTypeName: shorthandResolution.SourceTypeName,
            AccessorExpression: shorthandResolution.AccessorExpression,
            DiagnosticId: shorthandResolution.DiagnosticId,
            DiagnosticMessage: shorthandResolution.DiagnosticMessage,
            CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
            ValueKind: ResolvedValueKind.Binding,
            RequiresStaticResourceResolver: false,
            ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
        return true;
    }

    private static bool TryResolveSetterValueWithSharedPolicy(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        bool preferTypedStaticResourceCoercion,
        bool allowObjectStringLiteralFallbackDuringConversion,
        bool allowCompatibilityStringLiteralFallback,
        ImmutableArray<AttributeData> converterAttributes,
        out ResolvedValueConversionResult conversion)
    {
        return SetterValuePolicyResolutionService.TryResolve(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            preferTypedStaticResourceCoercion,
            allowObjectStringLiteralFallbackDuringConversion,
            allowCompatibilityStringLiteralFallback,
            converterAttributes,
            out conversion);
    }

    private static bool TryGetAvaloniaUnsetValueExpression(
        string value,
        out string expression)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals("UnsetValue", StringComparison.Ordinal) ||
            trimmed.Equals("AvaloniaProperty.UnsetValue", StringComparison.Ordinal) ||
            trimmed.Equals("{x:Static AvaloniaProperty.UnsetValue}", StringComparison.Ordinal))
        {
            expression = "global::Avalonia.AvaloniaProperty.UnsetValue";
            return true;
        }

        expression = string.Empty;
        return false;
    }
}
