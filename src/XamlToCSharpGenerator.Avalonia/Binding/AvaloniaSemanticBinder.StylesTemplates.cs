using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static ImmutableArray<ResolvedStyleDefinition> BindStyles(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings)
    {
        var styles = ImmutableArray.CreateBuilder<ResolvedStyleDefinition>(document.Styles.Length);

        foreach (var style in document.Styles)
        {
            if (ShouldSkipConditionalBranch(
                    style.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var selector = style.Selector.Trim();
            INamedTypeSymbol? targetType = null;

            if (!string.IsNullOrWhiteSpace(selector))
            {
                var selectorValidation = SelectorSyntaxValidator.Validate(selector);
                if (!selectorValidation.IsValid)
                {
                    var (line, column) = AdvanceLineAndColumn(
                        style.SelectorLine,
                        style.SelectorColumn,
                        selector,
                        selectorValidation.ErrorOffset);
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0300",
                        "Unable to parse selector: " + selectorValidation.ErrorMessage,
                        document.FilePath,
                        line,
                        column,
                        options.StrictMode));
                }
                else
                {
                    targetType = AvaloniaSelectorSemanticAdapter.TryResolveSelectorTargetType(
                        selectorValidation.Branches,
                        typeToken => ResolveTypeToken(compilation, document, typeToken, document.ClassNamespace),
                        IsTypeAssignableTo,
                        out var unresolvedTypeToken,
                        out var unresolvedTypeOffset);
                    if (!string.IsNullOrWhiteSpace(unresolvedTypeToken))
                    {
                        var (line, column) = AdvanceLineAndColumn(
                            style.SelectorLine,
                            style.SelectorColumn,
                            selector,
                            unresolvedTypeOffset);
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0300",
                            $"Style selector target '{unresolvedTypeToken}' could not be resolved.",
                            document.FilePath,
                            line,
                            column,
                            options.StrictMode));
                    }
                }
            }

            var styleDataType = ResolveTypeFromTypeExpression(compilation, document, style.DataType, document.ClassNamespace);
            var compileBindingsEnabled = style.CompileBindings ?? options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(style.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in style.Setters)
            {
                if (ShouldSkipConditionalBranch(
                        setter.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                var propertyAlias = ResolvePropertyAlias(targetType, setter.PropertyName);
                var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
                var resolvedPropertyName = normalizedPropertyName;
                IPropertySymbol? targetProperty = null;
                string? setterPropertyOwnerTypeName = null;
                string? setterPropertyFieldName = null;
                ITypeSymbol? setterValueType = null;

                if (propertyAlias.HasAvaloniaPropertyAlias &&
                    propertyAlias.AvaloniaPropertyOwnerType is not null &&
                    TryFindAvaloniaPropertyField(
                        propertyAlias.AvaloniaPropertyOwnerType,
                        propertyAlias.ResolvedPropertyName,
                        out var aliasedOwnerType,
                        out var aliasedPropertyField,
                        propertyAlias.AvaloniaPropertyFieldName))
                {
                    resolvedPropertyName = propertyAlias.ResolvedPropertyName;
                    setterPropertyOwnerTypeName = aliasedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = aliasedPropertyField.Name;
                    setterValueType = TryGetAvaloniaPropertyValueType(aliasedPropertyField.Type);
                }

                if (targetType is not null &&
                    TrySplitOwnerQualifiedPropertyToken(
                        setter.PropertyName,
                        out var ownerToken,
                        out var attachedPropertyName))
                {
                    var explicitOwnerType = ResolveTypeToken(
                        compilation,
                        document,
                        ownerToken,
                        document.ClassNamespace);
                    if (explicitOwnerType is not null &&
                        TryFindAvaloniaPropertyField(
                            explicitOwnerType,
                            attachedPropertyName,
                            out var attachedOwnerType,
                            out var attachedPropertyField))
                    {
                        resolvedPropertyName = attachedPropertyName;
                        setterPropertyOwnerTypeName =
                            attachedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        setterPropertyFieldName = attachedPropertyField.Name;
                        setterValueType = TryGetAvaloniaPropertyValueType(attachedPropertyField.Type);
                    }
                }

                if (targetType is not null)
                {
                    targetProperty = FindProperty(targetType, normalizedPropertyName);
                    if (targetProperty is not null)
                    {
                        resolvedPropertyName = targetProperty.Name;
                    }
                }

                setterValueType ??= targetProperty?.Type;
                if (targetType is not null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                    TryFindAvaloniaPropertyField(targetType, resolvedPropertyName, out var stylePropertyOwnerType, out var stylePropertyField))
                {
                    setterPropertyOwnerTypeName =
                        stylePropertyOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = stylePropertyField.Name;
                    setterValueType ??= TryGetAvaloniaPropertyValueType(stylePropertyField.Type);
                }

                if (targetType is not null &&
                    targetProperty is null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0301",
                        $"Style setter property '{setter.PropertyName}' was not found on '{targetType.ToDisplayString()}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var duplicatePropertyKey = !string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                                           !string.IsNullOrWhiteSpace(setterPropertyFieldName)
                    ? setterPropertyOwnerTypeName + "." + setterPropertyFieldName
                    : resolvedPropertyName;
                if (!seenSetterProperties.Add(duplicatePropertyKey))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"Style setter property '{resolvedPropertyName}' is duplicated in selector '{selector}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var valueExpression = string.Empty;
                var valueResolvedFromMarkup = false;
                var valueKind = ResolvedValueKind.Literal;
                var requiresStaticResourceResolver = false;
                var valueRequirements = ResolvedValueRequirements.None;

                var isCompiledBinding = false;
                string? compiledBindingPath = null;
                string? compiledBindingSourceType = null;
                if (TryConvertCSharpExpressionMarkupToBindingExpression(
                        setter.Value,
                        compilation,
                        document,
                        options,
                        styleDataType,
                        out var isExpressionMarkup,
                        out var expressionBindingValueExpression,
                        out var expressionAccessorExpression,
                        out var normalizedExpression,
                        out var expressionErrorCode,
                        out var expressionErrorMessage))
                {
                    var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                    var expressionPath = "{= " + normalizedExpression + " }";
                    var sourceTypeName = styleDataType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: resolvedPropertyName,
                        Path: expressionPath,
                        SourceTypeName: sourceTypeName,
                        AccessorExpression: expressionAccessorExpression,
                        IsSetterBinding: true,
                        Line: setter.Line,
                        Column: setter.Column));

                    setters.Add(new ResolvedSetterDefinition(
                        PropertyName: resolvedPropertyName,
                        ValueExpression: expressionBindingValueExpression,
                        IsCompiledBinding: true,
                        CompiledBindingPath: expressionPath,
                        CompiledBindingSourceTypeName: sourceTypeName,
                        AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                        AvaloniaPropertyFieldName: setterPropertyFieldName,
                        Line: setter.Line,
                        Column: setter.Column,
                        Condition: setter.Condition,
                        ValueKind: ResolvedValueKind.Binding,
                        ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true)));
                    continue;
                }

                if (isExpressionMarkup)
                {
                    var message = expressionErrorCode == "AXSG0110"
                        ? $"Expression binding for style setter '{setter.PropertyName}' requires x:DataType on the style."
                        : $"Expression binding '{setter.Value}' is invalid for source type '{styleDataType?.ToDisplayString() ?? "unknown"}': {expressionErrorMessage}";
                    diagnostics.Add(new DiagnosticInfo(
                        expressionErrorCode,
                        message,
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                    continue;
                }

                if (TryParseBindingMarkup(setter.Value, out var bindingMarkup))
                {
                    if (!TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            setter.Line,
                            setter.Column,
                            options.StrictMode) &&
                        CanUseCompiledBinding(bindingMarkup) &&
                        (bindingMarkup.IsCompiledBinding || compileBindingsEnabled))
                    {
                        if (styleDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for style setter '{setter.PropertyName}' requires x:DataType on the style.",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else if (!TryBuildCompiledBindingAccessorExpression(
                                     compilation,
                                     document,
                                     styleDataType,
                                     bindingMarkup.Path,
                                     out var accessorExpression,
                                     out var normalizedPath,
                                     out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{styleDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else
                        {
                            var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                            compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                                TargetTypeName: targetTypeName,
                                TargetPropertyName: resolvedPropertyName,
                                Path: normalizedPath,
                                SourceTypeName: styleDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                AccessorExpression: accessorExpression,
                                IsSetterBinding: true,
                                Line: setter.Line,
                                Column: setter.Column));

                            isCompiledBinding = true;
                            compiledBindingPath = normalizedPath;
                            compiledBindingSourceType = styleDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                    }

                    if (!isCompiledBinding &&
                        !bindingMarkup.HasSourceConflict &&
                        TryBuildRuntimeBindingExpression(
                            compilation,
                            document,
                            bindingMarkup,
                            targetType,
                            BindingPriorityScope.Style,
                            out var runtimeBindingExpression))
                    {
                        valueExpression = runtimeBindingExpression;
                        valueResolvedFromMarkup = true;
                        valueKind = ResolvedValueKind.Binding;
                        valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
                    }
                }

                var conversionTargetType = setterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                if (!valueResolvedFromMarkup &&
                    TryResolveSetterValueWithPolicy(
                        rawValue: setter.Value,
                        conversionTargetType: conversionTargetType,
                        compilation: compilation,
                        document: document,
                        setterTargetType: targetType,
                        bindingPriorityScope: BindingPriorityScope.Style,
                        strictMode: options.StrictMode,
                        preferTypedStaticResourceCoercion: string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName),
                        allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                        conversionTargetType.SpecialType == SpecialType.System_Object,
                        allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                 conversionTargetType.SpecialType == SpecialType.System_Object,
                        propertyName: resolvedPropertyName,
                        ownerDisplayName: targetType?.ToDisplayString() ?? "style",
                        line: setter.Line,
                        column: setter.Column,
                        diagnostics: diagnostics,
                        resolution: out var styleSetterResolution,
                        setterContext: true))
                {
                    valueExpression = styleSetterResolution.Expression;
                    valueResolvedFromMarkup = true;
                    valueKind = styleSetterResolution.ValueKind;
                    requiresStaticResourceResolver = styleSetterResolution.RequiresStaticResourceResolver;
                    valueRequirements = styleSetterResolution.ValueRequirements;
                }

                if (!valueResolvedFromMarkup)
                {
                    continue;
                }

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: resolvedPropertyName,
                    ValueExpression: valueExpression,
                    IsCompiledBinding: isCompiledBinding,
                    CompiledBindingPath: compiledBindingPath,
                    CompiledBindingSourceTypeName: compiledBindingSourceType,
                    AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                    AvaloniaPropertyFieldName: setterPropertyFieldName,
                    Line: setter.Line,
                    Column: setter.Column,
                    Condition: setter.Condition,
                    ValueKind: isCompiledBinding
                        ? ResolvedValueKind.Binding
                        : valueKind,
                    RequiresStaticResourceResolver: requiresStaticResourceResolver,
                    ValueRequirements: valueRequirements));
            }

            styles.Add(new ResolvedStyleDefinition(
                Key: style.Key,
                Selector: selector,
                TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Setters: setters.ToImmutable(),
                RawXaml: style.RawXaml,
                Line: style.Line,
                Column: style.Column,
                Condition: style.Condition));
        }

        return styles.ToImmutable();
    }

    private static ImmutableArray<ResolvedControlThemeDefinition> BindControlThemes(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings)
    {
        var controlThemes = ImmutableArray.CreateBuilder<ResolvedControlThemeDefinition>(document.ControlThemes.Length);

        foreach (var controlTheme in document.ControlThemes)
        {
            if (ShouldSkipConditionalBranch(
                    controlTheme.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var targetType = ResolveTypeFromTypeExpression(
                compilation,
                document,
                controlTheme.TargetType,
                document.ClassNamespace);
            var themeVariant = string.IsNullOrWhiteSpace(controlTheme.ThemeVariant)
                ? null
                : controlTheme.ThemeVariant!.Trim();

            if (!string.IsNullOrWhiteSpace(controlTheme.TargetType) && targetType is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0302",
                    $"ControlTheme target type '{controlTheme.TargetType}' could not be resolved.",
                    document.FilePath,
                    controlTheme.Line,
                    controlTheme.Column,
                    options.StrictMode));
            }

            var themeDataType = ResolveTypeFromTypeExpression(compilation, document, controlTheme.DataType, document.ClassNamespace);
            var compileBindingsEnabled = controlTheme.CompileBindings ?? options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(controlTheme.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in controlTheme.Setters)
            {
                if (ShouldSkipConditionalBranch(
                        setter.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                var propertyAlias = ResolvePropertyAlias(targetType, setter.PropertyName);
                var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
                var resolvedPropertyName = normalizedPropertyName;
                IPropertySymbol? targetProperty = null;
                string? setterPropertyOwnerTypeName = null;
                string? setterPropertyFieldName = null;
                ITypeSymbol? setterValueType = null;

                if (propertyAlias.HasAvaloniaPropertyAlias &&
                    propertyAlias.AvaloniaPropertyOwnerType is not null &&
                    TryFindAvaloniaPropertyField(
                        propertyAlias.AvaloniaPropertyOwnerType,
                        propertyAlias.ResolvedPropertyName,
                        out var aliasedOwnerType,
                        out var aliasedPropertyField,
                        propertyAlias.AvaloniaPropertyFieldName))
                {
                    resolvedPropertyName = propertyAlias.ResolvedPropertyName;
                    setterPropertyOwnerTypeName = aliasedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = aliasedPropertyField.Name;
                    setterValueType = TryGetAvaloniaPropertyValueType(aliasedPropertyField.Type);
                }

                if (targetType is not null &&
                    TrySplitOwnerQualifiedPropertyToken(
                        setter.PropertyName,
                        out var ownerToken,
                        out var attachedPropertyName))
                {
                    var explicitOwnerType = ResolveTypeToken(
                        compilation,
                        document,
                        ownerToken,
                        document.ClassNamespace);
                    if (explicitOwnerType is not null &&
                        TryFindAvaloniaPropertyField(
                            explicitOwnerType,
                            attachedPropertyName,
                            out var attachedOwnerType,
                            out var attachedPropertyField))
                    {
                        resolvedPropertyName = attachedPropertyName;
                        setterPropertyOwnerTypeName =
                            attachedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        setterPropertyFieldName = attachedPropertyField.Name;
                        setterValueType = TryGetAvaloniaPropertyValueType(attachedPropertyField.Type);
                    }
                }

                if (targetType is not null)
                {
                    targetProperty = FindProperty(targetType, normalizedPropertyName);
                    if (targetProperty is not null)
                    {
                        resolvedPropertyName = targetProperty.Name;
                    }
                }

                setterValueType ??= targetProperty?.Type;
                if (targetType is not null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                    TryFindAvaloniaPropertyField(targetType, resolvedPropertyName, out var themePropertyOwnerType, out var themePropertyField))
                {
                    setterPropertyOwnerTypeName =
                        themePropertyOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = themePropertyField.Name;
                    setterValueType ??= TryGetAvaloniaPropertyValueType(themePropertyField.Type);
                }

                if (targetType is not null &&
                    targetProperty is null &&
                    string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0303",
                        $"ControlTheme setter property '{setter.PropertyName}' was not found on '{targetType.ToDisplayString()}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var duplicatePropertyKey = !string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName) &&
                                           !string.IsNullOrWhiteSpace(setterPropertyFieldName)
                    ? setterPropertyOwnerTypeName + "." + setterPropertyFieldName
                    : resolvedPropertyName;
                if (!seenSetterProperties.Add(duplicatePropertyKey))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"ControlTheme setter property '{resolvedPropertyName}' is duplicated.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var valueExpression = string.Empty;
                var valueResolvedFromMarkup = false;
                var valueKind = ResolvedValueKind.Literal;
                var requiresStaticResourceResolver = false;
                var valueRequirements = ResolvedValueRequirements.None;

                var isCompiledBinding = false;
                string? compiledBindingPath = null;
                string? compiledBindingSourceType = null;
                if (TryConvertCSharpExpressionMarkupToBindingExpression(
                        setter.Value,
                        compilation,
                        document,
                        options,
                        themeDataType,
                        out var isExpressionMarkup,
                        out var expressionBindingValueExpression,
                        out var expressionAccessorExpression,
                        out var normalizedExpression,
                        out var expressionErrorCode,
                        out var expressionErrorMessage))
                {
                    var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                    var expressionPath = "{= " + normalizedExpression + " }";
                    var sourceTypeName = themeDataType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: resolvedPropertyName,
                        Path: expressionPath,
                        SourceTypeName: sourceTypeName,
                        AccessorExpression: expressionAccessorExpression,
                        IsSetterBinding: true,
                        Line: setter.Line,
                        Column: setter.Column));

                    setters.Add(new ResolvedSetterDefinition(
                        PropertyName: resolvedPropertyName,
                        ValueExpression: expressionBindingValueExpression,
                        IsCompiledBinding: true,
                        CompiledBindingPath: expressionPath,
                        CompiledBindingSourceTypeName: sourceTypeName,
                        AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                        AvaloniaPropertyFieldName: setterPropertyFieldName,
                        Line: setter.Line,
                        Column: setter.Column,
                        Condition: setter.Condition,
                        ValueKind: ResolvedValueKind.Binding,
                        ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true)));
                    continue;
                }

                if (isExpressionMarkup)
                {
                    var message = expressionErrorCode == "AXSG0110"
                        ? $"Expression binding for control theme setter '{setter.PropertyName}' requires x:DataType on the control theme."
                        : $"Expression binding '{setter.Value}' is invalid for source type '{themeDataType?.ToDisplayString() ?? "unknown"}': {expressionErrorMessage}";
                    diagnostics.Add(new DiagnosticInfo(
                        expressionErrorCode,
                        message,
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                    continue;
                }

                if (TryParseBindingMarkup(setter.Value, out var bindingMarkup))
                {
                    if (!TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            setter.Line,
                            setter.Column,
                            options.StrictMode) &&
                        CanUseCompiledBinding(bindingMarkup) &&
                        (bindingMarkup.IsCompiledBinding || compileBindingsEnabled))
                    {
                        if (themeDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for control theme setter '{setter.PropertyName}' requires x:DataType on the control theme.",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else if (!TryBuildCompiledBindingAccessorExpression(
                                     compilation,
                                     document,
                                     themeDataType,
                                     bindingMarkup.Path,
                                     out var accessorExpression,
                                     out var normalizedPath,
                                     out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{themeDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else
                        {
                            var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                            compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                                TargetTypeName: targetTypeName,
                                TargetPropertyName: resolvedPropertyName,
                                Path: normalizedPath,
                                SourceTypeName: themeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                AccessorExpression: accessorExpression,
                                IsSetterBinding: true,
                                Line: setter.Line,
                                Column: setter.Column));

                            isCompiledBinding = true;
                            compiledBindingPath = normalizedPath;
                            compiledBindingSourceType = themeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                    }

                    if (!isCompiledBinding &&
                        !bindingMarkup.HasSourceConflict &&
                        TryBuildRuntimeBindingExpression(
                            compilation,
                            document,
                            bindingMarkup,
                            targetType,
                            BindingPriorityScope.Style,
                            out var runtimeBindingExpression))
                    {
                        valueExpression = runtimeBindingExpression;
                        valueResolvedFromMarkup = true;
                        valueKind = ResolvedValueKind.Binding;
                        valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
                    }
                }

                var conversionTargetType = setterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                var hasKnownSetterValueType = setterValueType is not null;
                if (!valueResolvedFromMarkup &&
                    TryResolveSetterValueWithPolicy(
                        rawValue: setter.Value,
                        conversionTargetType: conversionTargetType,
                        compilation: compilation,
                        document: document,
                        setterTargetType: targetType,
                        bindingPriorityScope: BindingPriorityScope.Style,
                        strictMode: options.StrictMode,
                        preferTypedStaticResourceCoercion: string.IsNullOrWhiteSpace(setterPropertyOwnerTypeName),
                        allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                        hasKnownSetterValueType &&
                                                                        conversionTargetType.SpecialType == SpecialType.System_Object,
                        allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                 conversionTargetType.SpecialType == SpecialType.System_Object,
                        propertyName: resolvedPropertyName,
                        ownerDisplayName: targetType?.ToDisplayString() ?? "control theme",
                        line: setter.Line,
                        column: setter.Column,
                        diagnostics: diagnostics,
                        resolution: out var controlThemeSetterResolution,
                        setterContext: true))
                {
                    valueExpression = controlThemeSetterResolution.Expression;
                    valueResolvedFromMarkup = true;
                    valueKind = controlThemeSetterResolution.ValueKind;
                    requiresStaticResourceResolver = controlThemeSetterResolution.RequiresStaticResourceResolver;
                    valueRequirements = controlThemeSetterResolution.ValueRequirements;
                }

                if (!valueResolvedFromMarkup)
                {
                    continue;
                }

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: resolvedPropertyName,
                    ValueExpression: valueExpression,
                    IsCompiledBinding: isCompiledBinding,
                    CompiledBindingPath: compiledBindingPath,
                    CompiledBindingSourceTypeName: compiledBindingSourceType,
                    AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                    AvaloniaPropertyFieldName: setterPropertyFieldName,
                    Line: setter.Line,
                    Column: setter.Column,
                    Condition: setter.Condition,
                    ValueKind: isCompiledBinding
                        ? ResolvedValueKind.Binding
                        : valueKind,
                    RequiresStaticResourceResolver: requiresStaticResourceResolver,
                    ValueRequirements: valueRequirements));
            }

            controlThemes.Add(new ResolvedControlThemeDefinition(
                Key: controlTheme.Key,
                TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                BasedOn: controlTheme.BasedOn,
                ThemeVariant: themeVariant,
                Setters: setters.ToImmutable(),
                RawXaml: controlTheme.RawXaml,
                Line: controlTheme.Line,
                Column: controlTheme.Column,
                Condition: controlTheme.Condition));
        }

        var resolvedControlThemes = controlThemes.ToImmutable();
        ValidateControlThemeBasedOnChains(
            resolvedControlThemes,
            diagnostics,
            document,
            options);

        return resolvedControlThemes;
    }

    private static void ValidateControlThemeBasedOnChains(
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options)
    {
        if (controlThemes.IsDefaultOrEmpty)
        {
            return;
        }

        var byKey = new Dictionary<string, ResolvedControlThemeDefinition>(StringComparer.Ordinal);
        foreach (var controlTheme in controlThemes)
        {
            if (string.IsNullOrWhiteSpace(controlTheme.Key))
            {
                continue;
            }

            byKey[controlTheme.Key.Trim()] = controlTheme;
        }

        foreach (var controlTheme in controlThemes)
        {
            var basedOnKey = TryExtractControlThemeBasedOnKey(controlTheme.BasedOn);
            if (string.IsNullOrWhiteSpace(basedOnKey))
            {
                continue;
            }

            if (!byKey.ContainsKey(basedOnKey) &&
                !TryParseMarkupExtension(basedOnKey, out _))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0305",
                    $"ControlTheme '{controlTheme.Key ?? "<unnamed>"}' references missing BasedOn theme key '{basedOnKey}'.",
                    document.FilePath,
                    controlTheme.Line,
                    controlTheme.Column,
                    options.StrictMode));
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in byKey.Keys)
        {
            DetectCycle(key, key);
        }

        void DetectCycle(string key, string startKey)
        {
            if (!byKey.TryGetValue(key, out var currentTheme))
            {
                return;
            }

            if (state.TryGetValue(key, out var currentState))
            {
                if (currentState == 2)
                {
                    return;
                }

                if (currentState == 1)
                {
                    var cycleKey = startKey + "->" + key;
                    if (emitted.Add(cycleKey))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0306",
                            $"ControlTheme BasedOn chain contains a cycle at key '{key}'.",
                            document.FilePath,
                            currentTheme.Line,
                            currentTheme.Column,
                            true));
                    }

                    return;
                }
            }

            state[key] = 1;
            var basedOnKey = TryExtractControlThemeBasedOnKey(currentTheme.BasedOn);
            if (!string.IsNullOrWhiteSpace(basedOnKey) && byKey.ContainsKey(basedOnKey))
            {
                DetectCycle(basedOnKey, startKey);
            }

            state[key] = 2;
        }
    }

    private static string? TryExtractControlThemeBasedOnKey(string? basedOnExpression)
    {
        if (string.IsNullOrWhiteSpace(basedOnExpression))
        {
            return null;
        }

        var trimmed = basedOnExpression.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) &&
            trimmed.EndsWith("}", StringComparison.Ordinal) &&
            TryParseMarkupExtension(trimmed, out var markup))
        {
            var markupName = markup.Name.ToLowerInvariant();
            if (markupName != "staticresource" && markupName != "dynamicresource")
            {
                return null;
            }

            var resourceKey = TryGetNamedMarkupArgument(markup, "ResourceKey", "Key");
            if (string.IsNullOrWhiteSpace(resourceKey) && markup.PositionalArguments.Length > 0)
            {
                resourceKey = Unquote(markup.PositionalArguments[0]);
            }

            return string.IsNullOrWhiteSpace(resourceKey)
                ? null
                : resourceKey.Trim();
        }

        return Unquote(trimmed);
    }

    private static ImmutableArray<ResolvedResourceDefinition> BindResources(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedResourceDefinition>(document.Resources.Length);

        foreach (var resource in document.Resources)
        {
            if (ShouldSkipConditionalBranch(
                    resource.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var typeName = ResolveTypeName(compilation, resource.XmlNamespace, resource.XmlTypeName, out var symbol);
            if (symbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0100",
                    $"Could not resolve resource type '{resource.XmlTypeName}' for key '{resource.Key}'. Falling back to System.Object metadata.",
                    document.FilePath,
                    resource.Line,
                    resource.Column,
                    false));
                typeName = "global::System.Object";
            }

            result.Add(new ResolvedResourceDefinition(
                Key: resource.Key,
                TypeName: typeName!,
                RawXaml: resource.RawXaml,
                Line: resource.Line,
                Column: resource.Column,
                Condition: resource.Condition));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<ResolvedTemplateDefinition> BindTemplates(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedTemplateDefinition>(document.Templates.Length);

        foreach (var template in document.Templates)
        {
            if (ShouldSkipConditionalBranch(
                    template.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            string? targetTypeName = null;
            INamedTypeSymbol? controlTemplateTargetType = null;

            if (!IsKnownTemplateKind(template.Kind))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Template kind '{template.Kind}' is not recognized as a template node.",
                    document.FilePath,
                    template.Line,
                    template.Column,
                    false));
                continue;
            }

            if ((template.Kind.Equals("DataTemplate", StringComparison.Ordinal) ||
                 template.Kind.Equals("TreeDataTemplate", StringComparison.Ordinal)) &&
                string.IsNullOrWhiteSpace(template.DataType))
            {
                if (options.StrictMode)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0500",
                        $"Template '{template.Kind}' should declare x:DataType for compiled-binding safety.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        true));
                }
            }

            if (template.Kind.Equals("ControlTemplate", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(template.TargetType))
                {
                    if (options.StrictMode)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0501",
                            "ControlTemplate requires TargetType for source-generated validation.",
                            document.FilePath,
                            template.Line,
                            template.Column,
                            true));
                    }
                }
                else
                {
                    controlTemplateTargetType = ResolveTypeFromTypeExpression(
                        compilation,
                        document,
                        template.TargetType,
                        document.ClassNamespace);
                    if (controlTemplateTargetType is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0501",
                            $"ControlTemplate target type '{template.TargetType}' could not be resolved.",
                            document.FilePath,
                            template.Line,
                            template.Column,
                            options.StrictMode));
                    }
                    else
                    {
                        targetTypeName = controlTemplateTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        ValidateControlTemplateParts(
                            template,
                            controlTemplateTargetType,
                            compilation,
                            document,
                            diagnostics,
                            options);
                    }
                }
            }

            ValidateTemplateContentRootType(
                template,
                compilation,
                document,
                diagnostics,
                options);

            result.Add(new ResolvedTemplateDefinition(
                Kind: template.Kind,
                Key: template.Key,
                TargetTypeName: targetTypeName,
                DataType: template.DataType,
                RawXaml: template.RawXaml,
                Line: template.Line,
                Column: template.Column,
                Condition: template.Condition));
        }

        return result.ToImmutable();
    }

    private static void ValidateControlTemplateParts(
        XamlTemplateDefinition template,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var expectedParts = ResolveControlTemplatePartExpectations(targetType);
        if (expectedParts.Count == 0)
        {
            return;
        }

        if (!TryFindTemplateNode(document, template, out var templateNode))
        {
            return;
        }

        if (!TryCollectControlTemplateNamedParts(templateNode, compilation, out var actualParts))
        {
            return;
        }

        foreach (var pair in expectedParts)
        {
            var partName = pair.Key;
            var expectation = pair.Value;
            if (!actualParts.TryGetValue(partName, out var actual))
            {
                if (expectation.IsRequired)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0502",
                        $"Required template part with x:Name '{partName}' must be defined on '{targetType.Name}' ControlTemplate.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        true));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0504",
                        $"Optional template part with x:Name '{partName}' can be defined on '{targetType.Name}' ControlTemplate.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        options.StrictMode));
                }

                continue;
            }

            if (expectation.ExpectedType is not null &&
                actual.Type is not null &&
                !IsTypeAssignableTo(actual.Type, expectation.ExpectedType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0503",
                    $"Template part '{partName}' is expected to be assignable to '{expectation.ExpectedType.Name}', but actual type is {actual.Type.Name}.",
                    document.FilePath,
                    actual.Line,
                    actual.Column,
                    true));
            }
        }
    }

    private static void ValidateTemplateContentRootType(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var expectedTypeMetadataName = template.Kind switch
        {
            "ItemsPanelTemplate" => "Avalonia.Controls.Panel",
            "ControlTemplate" => "Avalonia.Controls.Control",
            "DataTemplate" => "Avalonia.Controls.Control",
            "TreeDataTemplate" => "Avalonia.Controls.Control",
            _ => null
        };

        if (expectedTypeMetadataName is null)
        {
            return;
        }

        var expectedType = compilation.GetTypeByMetadataName(expectedTypeMetadataName);
        if (expectedType is null)
        {
            return;
        }

        if (!TryFindTemplateNode(document, template, out var templateNode))
        {
            return;
        }

        if (!TryGetTemplateContentRootNode(templateNode, out var contentRoot))
        {
            return;
        }

        var actualType = ResolveTypeSymbol(compilation, contentRoot.XmlNamespace, contentRoot.XmlTypeName);
        if (actualType is null || IsTypeAssignableTo(actualType, expectedType))
        {
            return;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0506",
            $"Template '{template.Kind}' content root '{contentRoot.XmlTypeName}' is expected to be assignable to '{expectedType.Name}', but actual type is '{actualType.Name}'.",
            document.FilePath,
            contentRoot.Line,
            contentRoot.Column,
            options.StrictMode));
    }

    private static bool TryGetTemplateContentRootNode(XamlObjectNode templateNode, out XamlObjectNode contentRoot)
    {
        foreach (var propertyElement in templateNode.PropertyElements)
        {
            if (!NormalizePropertyName(propertyElement.PropertyName).Equals("Content", StringComparison.Ordinal))
            {
                continue;
            }

            if (propertyElement.ObjectValues.Length > 0)
            {
                contentRoot = propertyElement.ObjectValues[0];
                return true;
            }
        }

        if (templateNode.ChildObjects.Length > 0)
        {
            contentRoot = templateNode.ChildObjects[0];
            return true;
        }

        contentRoot = default!;
        return false;
    }

    private static void ValidateItemContainerInsideTemplateWarning(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyElement propertyElement,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        if (!property.Name.Equals("ItemTemplate", StringComparison.Ordinal) &&
            !property.Name.Equals("DataTemplates", StringComparison.Ordinal))
        {
            return;
        }

        var itemsControlType = compilation.GetTypeByMetadataName("Avalonia.Controls.ItemsControl");
        if (itemsControlType is null || !IsTypeAssignableTo(objectType, itemsControlType))
        {
            return;
        }

        var knownContainerType = ResolveKnownItemContainerType(objectType, compilation);
        if (knownContainerType is null)
        {
            return;
        }

        var contentControlType = compilation.GetTypeByMetadataName("Avalonia.Controls.ContentControl");
        if (contentControlType is null)
        {
            return;
        }

        foreach (var templateNode in propertyElement.ObjectValues)
        {
            if (!IsDataTemplateNode(templateNode))
            {
                continue;
            }

            var contentNode = TryGetTemplateContentNode(templateNode);
            if (contentNode is null)
            {
                continue;
            }

            var contentType = ResolveTypeSymbol(compilation, contentNode.XmlNamespace, contentNode.XmlTypeName);
            if (contentType is null ||
                !IsTypeAssignableTo(contentType, contentControlType) ||
                !IsTypeAssignableTo(contentType, knownContainerType))
            {
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0505",
                $"Unexpected '{knownContainerType.Name}' inside of '{objectType.Name}.{property.Name}'. '{objectType.Name}.{property.Name}' defines template of the container content, not the container itself.",
                document.FilePath,
                contentNode.Line,
                contentNode.Column,
                options.StrictMode));
        }
    }

    private static INamedTypeSymbol? ResolveKnownItemContainerType(
        INamedTypeSymbol itemsControlType,
        Compilation compilation)
    {
        foreach (var mapping in KnownItemContainerTypeMappings)
        {
            var mappedControlType = compilation.GetTypeByMetadataName(mapping.ItemsControlMetadataName);
            if (mappedControlType is null || !IsTypeAssignableTo(itemsControlType, mappedControlType))
            {
                continue;
            }

            return compilation.GetTypeByMetadataName(mapping.ItemContainerMetadataName);
        }

        return null;
    }

    private static bool IsDataTemplateNode(XamlObjectNode node)
    {
        return node.XmlTypeName is "DataTemplate" or "TreeDataTemplate";
    }

    private static XamlObjectNode? TryGetTemplateContentNode(XamlObjectNode templateNode)
    {
        foreach (var propertyElement in templateNode.PropertyElements)
        {
            if (NormalizePropertyName(propertyElement.PropertyName).Equals("Content", StringComparison.Ordinal) &&
                propertyElement.ObjectValues.Length > 0)
            {
                return propertyElement.ObjectValues[0];
            }
        }

        if (templateNode.ChildObjects.Length > 0)
        {
            return templateNode.ChildObjects[0];
        }

        return null;
    }

    private static Dictionary<string, TemplatePartExpectation> ResolveControlTemplatePartExpectations(INamedTypeSymbol targetType)
    {
        var result = new Dictionary<string, TemplatePartExpectation>(StringComparer.Ordinal);
        for (var current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!attribute.AttributeClass?.Name.Equals("TemplatePartAttribute", StringComparison.Ordinal) ?? true)
                {
                    continue;
                }

                var partName = TryGetTemplatePartName(attribute);
                if (string.IsNullOrWhiteSpace(partName) || result.ContainsKey(partName!))
                {
                    continue;
                }

                result.Add(partName!, new TemplatePartExpectation(
                    expectedType: TryGetTemplatePartType(attribute),
                    isRequired: TryGetTemplatePartIsRequired(attribute)));
            }
        }

        return result;
    }

    private static string? TryGetTemplatePartName(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("Name", StringComparison.Ordinal) &&
                namedArgument.Value.Value is string namedName &&
                !string.IsNullOrWhiteSpace(namedName))
            {
                return namedName;
            }
        }

        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is string ctorName &&
            !string.IsNullOrWhiteSpace(ctorName))
        {
            return ctorName;
        }

        return null;
    }

    private static ITypeSymbol? TryGetTemplatePartType(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("Type", StringComparison.Ordinal) &&
                namedArgument.Value.Kind == TypedConstantKind.Type &&
                namedArgument.Value.Value is ITypeSymbol namedType)
            {
                return namedType;
            }
        }

        if (attribute.ConstructorArguments.Length > 1 &&
            attribute.ConstructorArguments[1].Kind == TypedConstantKind.Type &&
            attribute.ConstructorArguments[1].Value is ITypeSymbol ctorType)
        {
            return ctorType;
        }

        return null;
    }

    private static bool TryGetTemplatePartIsRequired(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("IsRequired", StringComparison.Ordinal) &&
                namedArgument.Value.Value is bool isRequired)
            {
                return isRequired;
            }
        }

        return false;
    }

    private static bool TryCollectControlTemplateNamedParts(
        XamlObjectNode templateNode,
        Compilation compilation,
        out ImmutableDictionary<string, TemplatePartActual> parts)
    {
        var result = ImmutableDictionary.CreateBuilder<string, TemplatePartActual>(StringComparer.Ordinal);
        CollectControlTemplateNamedPartsRecursive(templateNode, compilation, result, isTemplateRoot: true);
        parts = result.ToImmutable();
        return true;
    }

    private static void CollectControlTemplateNamedPartsRecursive(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableDictionary<string, TemplatePartActual>.Builder result,
        bool isTemplateRoot)
    {
        if (!isTemplateRoot && IsTemplateLikeElement(node.XmlTypeName))
        {
            return;
        }

        if (TryGetNodeNameScopeRegistration(node, out var partName, out var line, out var column) &&
            !result.ContainsKey(partName))
        {
            var type = ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
            result[partName] = new TemplatePartActual(
                type: type,
                line: line,
                column: column);
        }

        foreach (var constructorArgument in node.ConstructorArguments)
        {
            CollectControlTemplateNamedPartsRecursive(constructorArgument, compilation, result, isTemplateRoot: false);
        }

        foreach (var child in node.ChildObjects)
        {
            CollectControlTemplateNamedPartsRecursive(child, compilation, result, isTemplateRoot: false);
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                CollectControlTemplateNamedPartsRecursive(objectValue, compilation, result, isTemplateRoot: false);
            }
        }
    }

    private static bool TryGetNodeNameScopeRegistration(
        XamlObjectNode node,
        out string name,
        out int line,
        out int column)
    {
        name = string.Empty;
        line = node.Line;
        column = node.Column;

        if (TryParseNameScopeRegistrationValue(node.Name ?? string.Empty, out var parsedNodeName))
        {
            name = parsedNodeName;
            return true;
        }

        foreach (var assignment in node.PropertyAssignments)
        {
            if (!NormalizePropertyName(assignment.PropertyName).Equals("Name", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseNameScopeRegistrationValue(assignment.Value, out var parsedName))
            {
                continue;
            }

            name = parsedName;
            line = assignment.Line;
            column = assignment.Column;
            return true;
        }

        return false;
    }

    private static bool TryParseNameScopeRegistrationValue(string rawValue, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (TryParseMarkupExtension(trimmed, out _))
        {
            return false;
        }

        if (!TryNormalizeReferenceName(trimmed, out var normalizedName))
        {
            return false;
        }

        name = normalizedName;
        return true;
    }

    private static string? NormalizeObjectNodeName(string? rawName)
    {
        return TryParseNameScopeRegistrationValue(rawName ?? string.Empty, out var name)
            ? name
            : null;
    }

    private static string? ResolveObjectNodeNameScopeRegistration(
        XamlObjectNode node,
        INamedTypeSymbol? resolvedType,
        Compilation compilation)
    {
        var normalizedName = NormalizeObjectNodeName(node.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        // Bare Name attributes should only be promoted to name-scope registrations
        // for control types; for style primitives (for example ContainerQuery.Name),
        // Name is a normal property and can be intentionally duplicated.
        if (!HasExplicitNamePropertyAssignment(node))
        {
            return normalizedName;
        }

        return SupportsNameScopeRegistrationFromNameProperty(resolvedType, compilation)
            ? normalizedName
            : null;
    }

    private static bool HasExplicitNamePropertyAssignment(XamlObjectNode node)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (NormalizePropertyName(assignment.PropertyName).Equals("Name", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SupportsNameScopeRegistrationFromNameProperty(
        INamedTypeSymbol? resolvedType,
        Compilation compilation)
    {
        if (resolvedType is null)
        {
            return true;
        }

        var controlType = compilation.GetTypeByMetadataName("Avalonia.Controls.Control");
        return controlType is not null && IsTypeAssignableTo(resolvedType, controlType);
    }

    private static bool TryFindTemplateNode(
        XamlDocumentModel document,
        XamlTemplateDefinition template,
        out XamlObjectNode templateNode)
    {
        foreach (var node in EnumerateObjectNodeSubtree(document.RootObject))
        {
            if (!node.XmlTypeName.Equals(template.Kind, StringComparison.Ordinal))
            {
                continue;
            }

            if (node.Line == template.Line &&
                node.Column == template.Column)
            {
                templateNode = node;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(template.Key))
        {
            foreach (var node in EnumerateObjectNodeSubtree(document.RootObject))
            {
                if (!node.XmlTypeName.Equals(template.Kind, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(node.Key, template.Key, StringComparison.Ordinal))
                {
                    templateNode = node;
                    return true;
                }
            }
        }

        foreach (var node in EnumerateObjectNodeSubtree(document.RootObject))
        {
            if (node.XmlTypeName.Equals(template.Kind, StringComparison.Ordinal))
            {
                templateNode = node;
                return true;
            }
        }

        templateNode = default!;
        return false;
    }

    private static IEnumerable<XamlObjectNode> EnumerateObjectNodeSubtree(XamlObjectNode node)
    {
        yield return node;

        foreach (var constructorArgument in node.ConstructorArguments)
        {
            foreach (var descendant in EnumerateObjectNodeSubtree(constructorArgument))
            {
                yield return descendant;
            }
        }

        foreach (var child in node.ChildObjects)
        {
            foreach (var descendant in EnumerateObjectNodeSubtree(child))
            {
                yield return descendant;
            }
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                foreach (var descendant in EnumerateObjectNodeSubtree(objectValue))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static bool IsTemplateLikeElement(string localName)
    {
        return IsKnownTemplateKind(localName);
    }

    private static bool IsKnownTemplateKind(string kind)
    {
        return kind is "DataTemplate" or "ControlTemplate" or "ItemsPanelTemplate" or "TreeDataTemplate";
    }
}
