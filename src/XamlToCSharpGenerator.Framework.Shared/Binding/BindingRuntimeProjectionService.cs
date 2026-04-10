using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class BindingRuntimeProjectionService
{
    private readonly Func<Compilation, TypeContractId, INamedTypeSymbol?> _resolveContractType;
    private readonly BindingInitializerPlanService _bindingInitializerPlanService;
    private readonly ObjectInitializerExpressionService _objectInitializerExpressionService;
    private readonly Func<string, string> _escape;

    public BindingRuntimeProjectionService(
        Func<Compilation, TypeContractId, INamedTypeSymbol?> resolveContractType,
        BindingInitializerPlanService bindingInitializerPlanService,
        ObjectInitializerExpressionService objectInitializerExpressionService,
        Func<string, string> escape)
    {
        _resolveContractType = resolveContractType ?? throw new ArgumentNullException(nameof(resolveContractType));
        _bindingInitializerPlanService = bindingInitializerPlanService ?? throw new ArgumentNullException(nameof(bindingInitializerPlanService));
        _objectInitializerExpressionService = objectInitializerExpressionService ?? throw new ArgumentNullException(nameof(objectInitializerExpressionService));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    public bool TryBuildRuntimeBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression)
    {
        var bindingType = _resolveContractType(compilation, TypeContractId.AvaloniaBinding);
        var normalizedPath = _bindingInitializerPlanService.NormalizeRuntimeBindingPath(bindingMarkup.Path);
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            _bindingInitializerPlanService.TryMapBindingMode(bindingMarkup.Mode!, out var modeExpression))
        {
            assignments["Mode"] = modeExpression;
        }

        if (bindingMarkup.RelativeSource is RelativeSourceMarkup relativeSource &&
            _bindingInitializerPlanService.TryBuildRelativeSourceExpression(
                compilation,
                document,
                relativeSource,
                out var relativeSourceExpression,
                out _))
        {
            assignments["RelativeSource"] = relativeSourceExpression;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            assignments["ElementName"] = "\"" + _escape(bindingMarkup.ElementName!) + "\"";
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            AddConvertedBindingAssignment(
                assignments,
                bindingType,
                "Source",
                bindingMarkup.Source,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                fallbackType: compilation.ObjectType);
        }

        AddConvertedAssignment(
            assignments,
            "FallbackValue",
            bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope);
        AddConvertedAssignment(
            assignments,
            "TargetNullValue",
            bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope);
        AddConvertedAssignment(
            assignments,
            "ConverterParameter",
            bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope);

        if (!string.IsNullOrWhiteSpace(bindingMarkup.StringFormat))
        {
            assignments["StringFormat"] = "\"" + _escape(bindingMarkup.StringFormat!) + "\"";
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Delay))
        {
            AddConvertedBindingAssignment(
                assignments,
                bindingType,
                "Delay",
                bindingMarkup.Delay,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Priority))
        {
            AddConvertedBindingAssignment(
                assignments,
                bindingType,
                "Priority",
                bindingMarkup.Priority,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                fallbackExpression: bindingMarkup.Priority!.Trim());
        }
        else if (_bindingInitializerPlanService.GetDefaultBindingPriorityToken(bindingPriorityScope) is { } defaultPriority)
        {
            assignments["Priority"] = "global::Avalonia.Data.BindingPriority." + defaultPriority;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.UpdateSourceTrigger))
        {
            AddConvertedBindingAssignment(
                assignments,
                bindingType,
                "UpdateSourceTrigger",
                bindingMarkup.UpdateSourceTrigger,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                fallbackExpression: bindingMarkup.UpdateSourceTrigger!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Converter))
        {
            AddConvertedBindingAssignment(
                assignments,
                bindingType,
                "Converter",
                bindingMarkup.Converter,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                fallbackType: compilation.ObjectType);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ConverterCulture))
        {
            AddConvertedBindingAssignment(
                assignments,
                bindingType,
                "ConverterCulture",
                bindingMarkup.ConverterCulture,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                fallbackExpression: bindingMarkup.ConverterCulture!.Trim());
        }

        var constructorExpression = "new global::Avalonia.Data.Binding(\"" + _escape(normalizedPath) + "\")";
        expression = _objectInitializerExpressionService.BuildObjectCreationExpression(
            "global::Avalonia.Data.Binding",
            constructorExpression,
            assignments);
        return true;
    }

    private void AddConvertedAssignment(
        Dictionary<string, string> assignments,
        string propertyName,
        string? rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        if (_bindingInitializerPlanService.TryConvertValueExpression(
                rawValue!,
                compilation.ObjectType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var expression))
        {
            assignments[propertyName] = expression;
        }
    }

    private void AddConvertedBindingAssignment(
        Dictionary<string, string> assignments,
        INamedTypeSymbol? bindingType,
        string propertyName,
        string? rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        ITypeSymbol? fallbackType = null,
        string? fallbackExpression = null)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        ITypeSymbol? targetType = fallbackType;
        if (bindingType is not null &&
            _bindingInitializerPlanService.TryGetWritableProperty(bindingType, propertyName, out var property) &&
            property is not null)
        {
            targetType = property.Type;
        }

        if (targetType is not null &&
            _bindingInitializerPlanService.TryConvertValueExpression(
                rawValue!,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var expression))
        {
            assignments[propertyName] = expression;
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallbackExpression))
        {
            assignments[propertyName] = fallbackExpression!;
        }
    }
}
