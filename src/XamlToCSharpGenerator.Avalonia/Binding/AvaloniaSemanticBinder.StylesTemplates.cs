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
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Framework.Shared.Binding;
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
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        var styles = ImmutableArray.CreateBuilder<ResolvedStyleDefinition>(document.Styles.Length);

        foreach (var style in document.Styles)
        {
            if (ConditionalXamlEvaluationService.ShouldSkipBranch(
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
                        typeToken => ResolveSelectorTypeToken(compilation, document, typeToken),
                        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
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
            var rootContextType = ResolveObjectTypeSymbol(compilation, document, document.RootObject);
            var compileBindingsEnabled = style.CompileBindings ??
                                         document.RootObject.CompileBindings ??
                                         options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(style.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in style.Setters)
            {
                if (ConditionalXamlEvaluationService.ShouldSkipBranch(
                        setter.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                var setterPropertyPlan = SetterPropertyBindingPlanService.BuildPlan(
                    setter.PropertyName,
                    targetType,
                    compilation,
                    document);

                if (setterPropertyPlan.IsMissingOnTargetType)
                {
                    var styleTargetDisplayName = targetType?.ToDisplayString() ?? "style";
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0301",
                        $"Style setter property '{setter.PropertyName}' was not found on '{styleTargetDisplayName}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                if (!seenSetterProperties.Add(setterPropertyPlan.SetterIdentityPlan.DuplicateIdentityKey))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"Style setter property '{setterPropertyPlan.ResolvedPropertyName}' is duplicated in selector '{selector}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var conversionTargetType = setterPropertyPlan.SetterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                if (!SetterValuePlanningService.TryBuildPlan(
                        rawValue: setter.Value,
                        authoredPropertyName: setter.PropertyName,
                        resolvedPropertyName: setterPropertyPlan.ResolvedPropertyName,
                        compilation: compilation,
                        document: document,
                        options: options,
                        scopeDataType: styleDataType,
                        rootContextType: rootContextType,
                        targetType: targetType,
                        setterValueType: setterPropertyPlan.SetterValueType,
                        conversionTargetType: conversionTargetType,
                        ownerDisplayName: targetType?.ToDisplayString() ?? "style",
                        contextDisplayName: "style",
                        bindingPriorityScope: (int)BindingPriorityScope.Style,
                        compileBindingsEnabled: compileBindingsEnabled,
                        preferTypedStaticResourceCoercion: setterPropertyPlan.PreferTypedStaticResourceCoercion,
                        allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                        conversionTargetType.SpecialType == SpecialType.System_Object,
                        allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                 conversionTargetType.SpecialType == SpecialType.System_Object,
                        line: setter.Line,
                        column: setter.Column,
                        diagnostics: diagnostics,
                        unsafeAccessors: unsafeAccessors,
                        converterAttributes: setterPropertyPlan.TargetProperty?.GetAttributes() ?? default,
                        plan: out var setterValuePlan))
                {
                    if (!options.StrictMode &&
                        setterPropertyPlan.FrameworkPropertyOperation is not null)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0102",
                            $"Could not convert setter value '{setter.Value}' for '{setterPropertyPlan.ResolvedPropertyName}'. Strategy=AvaloniaProperty.UnsetValueFallback.",
                            document.FilePath,
                            setter.Line,
                            setter.Column,
                            options.StrictMode));

                        setters.Add(new ResolvedSetterDefinition(
                            PropertyName: setterPropertyPlan.ResolvedPropertyName,
                            ValueExpression: "global::Avalonia.AvaloniaProperty.UnsetValue",
                            IsCompiledBinding: false,
                            CompiledBindingPath: null,
                            CompiledBindingSourceTypeName: null,
                            FrameworkPropertyOperation: setterPropertyPlan.FrameworkPropertyOperation,
                            Line: setter.Line,
                            Column: setter.Column,
                            Condition: setter.Condition,
                            ValueKind: ResolvedValueKind.Literal));
                    }

                    continue;
                }

                compiledBindings.AddRange(setterValuePlan.CompiledBindings);

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: setterPropertyPlan.ResolvedPropertyName,
                    ValueExpression: setterValuePlan.ValueExpression,
                    IsCompiledBinding: setterValuePlan.IsCompiledBinding,
                    CompiledBindingPath: setterValuePlan.CompiledBindingPath,
                    CompiledBindingSourceTypeName: setterValuePlan.CompiledBindingSourceTypeName,
                    FrameworkPropertyOperation: setterPropertyPlan.FrameworkPropertyOperation,
                    Line: setter.Line,
                    Column: setter.Column,
                    Condition: setter.Condition,
                    ValueKind: setterValuePlan.ValueKind,
                    RequiresStaticResourceResolver: setterValuePlan.RequiresStaticResourceResolver,
                    ValueRequirements: setterValuePlan.ValueRequirements));
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
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        var controlThemes = ImmutableArray.CreateBuilder<ResolvedControlThemeDefinition>(document.ControlThemes.Length);

        foreach (var controlTheme in document.ControlThemes)
        {
            if (ConditionalXamlEvaluationService.ShouldSkipBranch(
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
            var rootContextType = ResolveObjectTypeSymbol(compilation, document, document.RootObject);
            var compileBindingsEnabled = controlTheme.CompileBindings ??
                                         document.RootObject.CompileBindings ??
                                         options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(controlTheme.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in controlTheme.Setters)
            {
                if (ConditionalXamlEvaluationService.ShouldSkipBranch(
                        setter.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                var setterPropertyPlan = SetterPropertyBindingPlanService.BuildPlan(
                    setter.PropertyName,
                    targetType,
                    compilation,
                    document);

                if (setterPropertyPlan.IsMissingOnTargetType)
                {
                    var themeTargetDisplayName = targetType?.ToDisplayString() ?? "control theme";
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0303",
                        $"ControlTheme setter property '{setter.PropertyName}' was not found on '{themeTargetDisplayName}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                if (!seenSetterProperties.Add(setterPropertyPlan.SetterIdentityPlan.DuplicateIdentityKey))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"ControlTheme setter property '{setterPropertyPlan.ResolvedPropertyName}' is duplicated.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                if (!options.StrictMode &&
                    setterPropertyPlan.FrameworkPropertyOperation is not null &&
                    setterPropertyPlan.SetterValueType is null &&
                    !string.IsNullOrWhiteSpace(setter.Value))
                {
                    var trimmedSetterValue = setter.Value.TrimStart();
                    if (trimmedSetterValue.Length > 0 && trimmedSetterValue[0] != '{')
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0102",
                            $"Could not convert setter value '{setter.Value}' for '{setterPropertyPlan.ResolvedPropertyName}'. Strategy=AvaloniaProperty.UnsetValueFallback.",
                            document.FilePath,
                            setter.Line,
                            setter.Column,
                            options.StrictMode));

                        setters.Add(new ResolvedSetterDefinition(
                            PropertyName: setterPropertyPlan.ResolvedPropertyName,
                            ValueExpression: "global::Avalonia.AvaloniaProperty.UnsetValue",
                            IsCompiledBinding: false,
                            CompiledBindingPath: null,
                            CompiledBindingSourceTypeName: null,
                            FrameworkPropertyOperation: setterPropertyPlan.FrameworkPropertyOperation,
                            Line: setter.Line,
                            Column: setter.Column,
                            Condition: setter.Condition,
                            ValueKind: ResolvedValueKind.Literal));
                        continue;
                    }
                }

                var conversionTargetType = setterPropertyPlan.SetterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                var hasKnownSetterValueType = setterPropertyPlan.SetterValueType is not null;
                if (!SetterValuePlanningService.TryBuildPlan(
                        rawValue: setter.Value,
                        authoredPropertyName: setter.PropertyName,
                        resolvedPropertyName: setterPropertyPlan.ResolvedPropertyName,
                        compilation: compilation,
                        document: document,
                        options: options,
                        scopeDataType: themeDataType,
                        rootContextType: rootContextType,
                        targetType: targetType,
                        setterValueType: setterPropertyPlan.SetterValueType,
                        conversionTargetType: conversionTargetType,
                        ownerDisplayName: targetType?.ToDisplayString() ?? "control theme",
                        contextDisplayName: "control theme",
                        bindingPriorityScope: (int)BindingPriorityScope.Style,
                        compileBindingsEnabled: compileBindingsEnabled,
                        preferTypedStaticResourceCoercion: setterPropertyPlan.PreferTypedStaticResourceCoercion,
                        allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                        hasKnownSetterValueType &&
                                                                        conversionTargetType.SpecialType == SpecialType.System_Object,
                        allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                 conversionTargetType.SpecialType == SpecialType.System_Object,
                        line: setter.Line,
                        column: setter.Column,
                        diagnostics: diagnostics,
                        unsafeAccessors: unsafeAccessors,
                        converterAttributes: setterPropertyPlan.TargetProperty?.GetAttributes() ?? default,
                        plan: out var setterValuePlan))
                {
                    continue;
                }

                compiledBindings.AddRange(setterValuePlan.CompiledBindings);

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: setterPropertyPlan.ResolvedPropertyName,
                    ValueExpression: setterValuePlan.ValueExpression,
                    IsCompiledBinding: setterValuePlan.IsCompiledBinding,
                    CompiledBindingPath: setterValuePlan.CompiledBindingPath,
                    CompiledBindingSourceTypeName: setterValuePlan.CompiledBindingSourceTypeName,
                    FrameworkPropertyOperation: setterPropertyPlan.FrameworkPropertyOperation,
                    Line: setter.Line,
                    Column: setter.Column,
                    Condition: setter.Condition,
                    ValueKind: setterValuePlan.ValueKind,
                    RequiresStaticResourceResolver: setterValuePlan.RequiresStaticResourceResolver,
                    ValueRequirements: setterValuePlan.ValueRequirements));
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
            document);

        return resolvedControlThemes;
    }

}
