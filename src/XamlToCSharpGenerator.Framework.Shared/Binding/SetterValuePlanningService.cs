using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ResolvedSetterShorthandPlan(
    string ValueExpression,
    CSharpShorthandResolutionKind ResolutionKind,
    bool IsCompiledBinding,
    string? CompiledBindingPath,
    string? CompiledBindingSourceTypeName,
    string? AccessorExpression,
    string? DiagnosticId,
    string? DiagnosticMessage,
    ImmutableArray<ResolvedCompiledBindingDefinition> CompiledBindings,
    ResolvedValueKind ValueKind,
    bool RequiresStaticResourceResolver,
    ResolvedValueRequirements ValueRequirements);

public sealed record ResolvedSetterValuePlan(
    string ValueExpression,
    bool IsCompiledBinding,
    string? CompiledBindingPath,
    string? CompiledBindingSourceTypeName,
    ImmutableArray<ResolvedCompiledBindingDefinition> CompiledBindings,
    ResolvedValueKind ValueKind,
    bool RequiresStaticResourceResolver,
    ResolvedValueRequirements ValueRequirements);

public sealed class SetterValuePlanningService
{
    public delegate bool TryParseInlineCSharpMarkupExtensionCodeDelegate(string value, out string code);
    public delegate bool TryBuildInlineCodeBindingExpressionDelegate(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        string rawCode,
        out string bindingExpression,
        out string normalizedExpression,
        out string? resultTypeName,
        out string errorMessage);
    public delegate bool TryResolveSetterShorthandPlanDelegate(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? scopeDataType,
        INamedTypeSymbol? rootContextType,
        INamedTypeSymbol? targetType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out ResolvedSetterShorthandPlan plan);
    public delegate bool TryBuildRuntimeBindingExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? targetType,
        int bindingPriorityScope,
        out string expression);
    public delegate bool TryConvertCSharpExpressionMarkupToBindingExpressionDelegate(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? sourceType,
        string? accessorPlaceholderToken,
        out bool isExpressionMarkup,
        out string expressionBindingValueExpression,
        out string accessorExpression,
        out string normalizedExpression,
        out string? resultTypeName,
        out string diagnosticId,
        out string diagnosticMessage);
    public delegate bool TryParseBindingMarkupDelegate(string value, out BindingMarkup bindingMarkup);
    public delegate bool TryReportBindingSourceConflictDelegate(
        BindingMarkup bindingMarkup,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        int line,
        int column,
        bool strictMode);
    public delegate bool TryResolveCompiledBindingSourceTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? ambientDataType,
        INamedTypeSymbol? targetType,
        out INamedTypeSymbol? sourceType,
        out bool requiresAmbientDataType,
        out bool hasInvalidLocalDataType);
    public delegate bool TryBuildSetterCompiledBindingAccessorExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CompiledBindingAccessorResolutionResult resolution,
        out string errorMessage);
    public delegate bool TryResolveSetterValueWithSharedPolicyDelegate(
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
        out ResolvedValueConversionResult conversion);
    public delegate string BuildCompiledBindingAccessorPlaceholderTokenDelegate(int line, int column);

    private readonly TryParseInlineCSharpMarkupExtensionCodeDelegate _tryParseInlineCSharpMarkupExtensionCode;
    private readonly TryBuildInlineCodeBindingExpressionDelegate _tryBuildInlineCodeBindingExpression;
    private readonly TryResolveSetterShorthandPlanDelegate _tryResolveSetterShorthandPlan;
    private readonly TryBuildRuntimeBindingExpressionDelegate _tryBuildRuntimeBindingExpression;
    private readonly TryConvertCSharpExpressionMarkupToBindingExpressionDelegate _tryConvertCSharpExpressionMarkupToBindingExpression;
    private readonly TryParseBindingMarkupDelegate _tryParseBindingMarkup;
    private readonly TryReportBindingSourceConflictDelegate _tryReportBindingSourceConflict;
    private readonly TryResolveCompiledBindingSourceTypeDelegate _tryResolveCompiledBindingSourceType;
    private readonly TryBuildSetterCompiledBindingAccessorExpressionDelegate _tryBuildSetterCompiledBindingAccessorExpression;
    private readonly TryResolveSetterValueWithSharedPolicyDelegate _tryResolveSetterValueWithSharedPolicy;
    private readonly BuildCompiledBindingAccessorPlaceholderTokenDelegate _buildCompiledBindingAccessorPlaceholderToken;

    public SetterValuePlanningService(
        TryParseInlineCSharpMarkupExtensionCodeDelegate tryParseInlineCSharpMarkupExtensionCode,
        TryBuildInlineCodeBindingExpressionDelegate tryBuildInlineCodeBindingExpression,
        TryResolveSetterShorthandPlanDelegate tryResolveSetterShorthandPlan,
        TryBuildRuntimeBindingExpressionDelegate tryBuildRuntimeBindingExpression,
        TryConvertCSharpExpressionMarkupToBindingExpressionDelegate tryConvertCSharpExpressionMarkupToBindingExpression,
        TryParseBindingMarkupDelegate tryParseBindingMarkup,
        TryReportBindingSourceConflictDelegate tryReportBindingSourceConflict,
        TryResolveCompiledBindingSourceTypeDelegate tryResolveCompiledBindingSourceType,
        TryBuildSetterCompiledBindingAccessorExpressionDelegate tryBuildSetterCompiledBindingAccessorExpression,
        TryResolveSetterValueWithSharedPolicyDelegate tryResolveSetterValueWithSharedPolicy,
        BuildCompiledBindingAccessorPlaceholderTokenDelegate buildCompiledBindingAccessorPlaceholderToken)
    {
        _tryParseInlineCSharpMarkupExtensionCode = tryParseInlineCSharpMarkupExtensionCode ?? throw new ArgumentNullException(nameof(tryParseInlineCSharpMarkupExtensionCode));
        _tryBuildInlineCodeBindingExpression = tryBuildInlineCodeBindingExpression ?? throw new ArgumentNullException(nameof(tryBuildInlineCodeBindingExpression));
        _tryResolveSetterShorthandPlan = tryResolveSetterShorthandPlan ?? throw new ArgumentNullException(nameof(tryResolveSetterShorthandPlan));
        _tryBuildRuntimeBindingExpression = tryBuildRuntimeBindingExpression ?? throw new ArgumentNullException(nameof(tryBuildRuntimeBindingExpression));
        _tryConvertCSharpExpressionMarkupToBindingExpression = tryConvertCSharpExpressionMarkupToBindingExpression ?? throw new ArgumentNullException(nameof(tryConvertCSharpExpressionMarkupToBindingExpression));
        _tryParseBindingMarkup = tryParseBindingMarkup ?? throw new ArgumentNullException(nameof(tryParseBindingMarkup));
        _tryReportBindingSourceConflict = tryReportBindingSourceConflict ?? throw new ArgumentNullException(nameof(tryReportBindingSourceConflict));
        _tryResolveCompiledBindingSourceType = tryResolveCompiledBindingSourceType ?? throw new ArgumentNullException(nameof(tryResolveCompiledBindingSourceType));
        _tryBuildSetterCompiledBindingAccessorExpression = tryBuildSetterCompiledBindingAccessorExpression ?? throw new ArgumentNullException(nameof(tryBuildSetterCompiledBindingAccessorExpression));
        _tryResolveSetterValueWithSharedPolicy = tryResolveSetterValueWithSharedPolicy ?? throw new ArgumentNullException(nameof(tryResolveSetterValueWithSharedPolicy));
        _buildCompiledBindingAccessorPlaceholderToken = buildCompiledBindingAccessorPlaceholderToken ?? throw new ArgumentNullException(nameof(buildCompiledBindingAccessorPlaceholderToken));
    }

    public bool TryBuildPlan(
        string rawValue,
        string authoredPropertyName,
        string resolvedPropertyName,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? scopeDataType,
        INamedTypeSymbol? rootContextType,
        INamedTypeSymbol? targetType,
        ITypeSymbol? setterValueType,
        ITypeSymbol conversionTargetType,
        string ownerDisplayName,
        string contextDisplayName,
        int bindingPriorityScope,
        bool compileBindingsEnabled,
        bool preferTypedStaticResourceCoercion,
        bool allowObjectStringLiteralFallbackDuringConversion,
        bool allowCompatibilityStringLiteralFallback,
        int line,
        int column,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        ImmutableArray<AttributeData> converterAttributes,
        out ResolvedSetterValuePlan plan)
    {
        _ = authoredPropertyName;
        _ = resolvedPropertyName;
        _ = ownerDisplayName;
        _ = contextDisplayName;
        var trimmedRawValue = rawValue.TrimStart();

        // Plain authored setter values should prefer typed literal conversion over
        // implicit C# shorthand so enum/string/numeric XAML literals keep standard semantics.
        if (conversionTargetType.SpecialType != SpecialType.System_Object &&
            (trimmedRawValue.Length == 0 || trimmedRawValue[0] != '{') &&
            _tryResolveSetterValueWithSharedPolicy(
                rawValue,
                conversionTargetType,
                compilation,
                document,
                targetType,
                bindingPriorityScope,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallbackDuringConversion,
                allowCompatibilityStringLiteralFallback,
                converterAttributes,
                out var earlyLiteralConversion))
        {
            plan = new ResolvedSetterValuePlan(
                ValueExpression: earlyLiteralConversion.Expression,
                IsCompiledBinding: false,
                CompiledBindingPath: null,
                CompiledBindingSourceTypeName: null,
                CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
                ValueKind: earlyLiteralConversion.ValueKind,
                RequiresStaticResourceResolver: earlyLiteralConversion.RequiresStaticResourceResolver,
                ValueRequirements: earlyLiteralConversion.EffectiveRequirements);
            return true;
        }

        if (_tryResolveSetterShorthandPlan(
                rawValue,
                compilation,
                document,
                options,
                scopeDataType,
                rootContextType,
                targetType,
                unsafeAccessors,
                out var shorthandPlan))
        {
            if (!string.IsNullOrWhiteSpace(shorthandPlan.DiagnosticId) &&
                !string.IsNullOrWhiteSpace(shorthandPlan.DiagnosticMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    shorthandPlan.DiagnosticId!,
                    shorthandPlan.DiagnosticMessage!,
                    document.FilePath,
                    line,
                    column,
                    options.StrictMode));
                plan = default!;
                return false;
            }

            var shorthandValueExpression = shorthandPlan.ValueExpression;
            var shorthandCompiledBindings = shorthandPlan.CompiledBindings;
            if (shorthandPlan.ResolutionKind == CSharpShorthandResolutionKind.BindingPath &&
                !string.IsNullOrWhiteSpace(shorthandPlan.CompiledBindingPath))
            {
                var shorthandBindingMarkup = new BindingMarkup(
                    isCompiledBinding: false,
                    path: shorthandPlan.CompiledBindingPath!,
                    mode: null,
                    elementName: null,
                    relativeSource: null,
                    source: null,
                    dataType: null,
                    converter: null,
                    converterCulture: null,
                    converterParameter: null,
                    stringFormat: null,
                    fallbackValue: null,
                    targetNullValue: null,
                    delay: null,
                    priority: null,
                    updateSourceTrigger: null,
                    hasSourceConflict: false,
                    sourceConflictMessage: null);

                if (_tryBuildRuntimeBindingExpression(
                        compilation,
                        document,
                        shorthandBindingMarkup,
                        targetType,
                        bindingPriorityScope,
                        out var runtimeBindingExpression))
                {
                    shorthandValueExpression = runtimeBindingExpression;
                }

                if (string.IsNullOrWhiteSpace(shorthandPlan.AccessorExpression) is false &&
                    string.IsNullOrWhiteSpace(shorthandPlan.CompiledBindingSourceTypeName) is false)
                {
                    shorthandCompiledBindings = shorthandCompiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? ownerDisplayName,
                        TargetPropertyName: resolvedPropertyName,
                        Path: shorthandPlan.CompiledBindingPath!,
                        SourceTypeName: shorthandPlan.CompiledBindingSourceTypeName!,
                        ResultTypeName: null,
                        AccessorExpression: shorthandPlan.AccessorExpression!,
                        IsSetterBinding: true,
                        Line: line,
                        Column: column,
                        AccessorPlaceholderToken: _buildCompiledBindingAccessorPlaceholderToken(line, column)));
                }
            }
            else if (shorthandPlan.ResolutionKind == CSharpShorthandResolutionKind.RootExpression &&
                     !string.IsNullOrWhiteSpace(shorthandPlan.CompiledBindingPath) &&
                     !string.IsNullOrWhiteSpace(shorthandPlan.AccessorExpression) &&
                     !string.IsNullOrWhiteSpace(shorthandPlan.CompiledBindingSourceTypeName))
            {
                shorthandCompiledBindings = shorthandCompiledBindings.Add(new ResolvedCompiledBindingDefinition(
                    TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? ownerDisplayName,
                    TargetPropertyName: resolvedPropertyName,
                    Path: "{= " + shorthandPlan.CompiledBindingPath + " }",
                    SourceTypeName: shorthandPlan.CompiledBindingSourceTypeName!,
                    ResultTypeName: null,
                    AccessorExpression: shorthandPlan.AccessorExpression!,
                    IsSetterBinding: true,
                    Line: line,
                    Column: column,
                    AccessorPlaceholderToken: _buildCompiledBindingAccessorPlaceholderToken(line, column)));
            }

            plan = new ResolvedSetterValuePlan(
                shorthandValueExpression,
                shorthandPlan.IsCompiledBinding || !shorthandCompiledBindings.IsDefaultOrEmpty,
                shorthandPlan.CompiledBindingPath,
                shorthandPlan.CompiledBindingSourceTypeName,
                shorthandCompiledBindings,
                shorthandPlan.ValueKind,
                shorthandPlan.RequiresStaticResourceResolver,
                shorthandPlan.ValueRequirements);
            return true;
        }

        if (_tryParseInlineCSharpMarkupExtensionCode(rawValue, out var inlineCode))
        {
            if (!_tryBuildInlineCodeBindingExpression(
                    compilation,
                    scopeDataType,
                    rootContextType,
                    targetType,
                    inlineCode,
                    out var bindingExpression,
                    out var normalizedExpression,
                    out var resultTypeName,
                    out var errorMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0110",
                    errorMessage,
                    document.FilePath,
                    line,
                    column,
                    options.StrictMode));
                plan = default!;
                return false;
            }

            plan = new ResolvedSetterValuePlan(
                ValueExpression: bindingExpression,
                IsCompiledBinding: false,
                CompiledBindingPath: normalizedExpression,
                CompiledBindingSourceTypeName: resultTypeName,
                CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
                ValueKind: ResolvedValueKind.Binding,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        if (_tryParseBindingMarkup(rawValue, out var bindingMarkup))
        {
            if (_tryReportBindingSourceConflict(
                    bindingMarkup,
                    diagnostics,
                    document,
                    line,
                    column,
                    options.StrictMode))
            {
                plan = default!;
                return false;
            }

            if (_tryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    bindingMarkup,
                    targetType,
                    bindingPriorityScope,
                    out var bindingExpression))
            {
                var compiledBindings = ImmutableArray<ResolvedCompiledBindingDefinition>.Empty;
                string? compiledBindingPath = null;
                string? compiledBindingSourceTypeName = null;
                if ((bindingMarkup.IsCompiledBinding ||
                     (compileBindingsEnabled && !BindingEventMarkupParser.HasExplicitBindingSource(bindingMarkup))) &&
                    _tryResolveCompiledBindingSourceType(
                        compilation,
                        document,
                        bindingMarkup,
                        scopeDataType,
                        targetType,
                        out var compiledBindingSourceType,
                        out _,
                        out _) &&
                    compiledBindingSourceType is not null &&
                    _tryBuildSetterCompiledBindingAccessorExpression(
                        compilation,
                        document,
                        compiledBindingSourceType,
                        bindingMarkup.Path,
                        setterValueType,
                        unsafeAccessors,
                        out var resolution,
                        out _))
                {
                    var placeholderToken = _buildCompiledBindingAccessorPlaceholderToken(line, column);
                    compiledBindings = ImmutableArray.Create(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? ownerDisplayName,
                        TargetPropertyName: resolvedPropertyName,
                        Path: resolution.NormalizedPath,
                        SourceTypeName: compiledBindingSourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResultTypeName: resolution.ResultTypeName,
                        AccessorExpression: resolution.AccessorExpression,
                        IsSetterBinding: true,
                        Line: line,
                        Column: column,
                        AccessorPlaceholderToken: placeholderToken));
                    compiledBindingPath = resolution.NormalizedPath;
                    compiledBindingSourceTypeName = compiledBindingSourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }

                plan = new ResolvedSetterValuePlan(
                    ValueExpression: bindingExpression,
                    IsCompiledBinding: !compiledBindings.IsDefaultOrEmpty,
                    CompiledBindingPath: compiledBindingPath,
                    CompiledBindingSourceTypeName: compiledBindingSourceTypeName,
                    CompiledBindings: compiledBindings,
                    ValueKind: ResolvedValueKind.Binding,
                    RequiresStaticResourceResolver: false,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
                return true;
            }
        }

        if (_tryConvertCSharpExpressionMarkupToBindingExpression(
                rawValue,
                compilation,
                document,
                options,
                scopeDataType,
                accessorPlaceholderToken: compileBindingsEnabled && scopeDataType is not null
                    ? _buildCompiledBindingAccessorPlaceholderToken(line, column)
                    : null,
                out var isExpressionMarkup,
                out var expressionBindingValueExpression,
                out var expressionAccessorExpression,
                out var expressionNormalizedPath,
                out var expressionResultTypeName,
                out var diagnosticId,
                out var diagnosticMessage) &&
            isExpressionMarkup)
        {
            if (!string.IsNullOrWhiteSpace(diagnosticId) &&
                !string.IsNullOrWhiteSpace(diagnosticMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    diagnosticId,
                    diagnosticMessage,
                    document.FilePath,
                    line,
                    column,
                    options.StrictMode));
                plan = default!;
                return false;
            }

            var expressionCompiledBindings = ImmutableArray<ResolvedCompiledBindingDefinition>.Empty;
            var expressionCompiledBindingPath = expressionNormalizedPath;
            var expressionCompiledBindingSourceTypeName = expressionResultTypeName;
            if (compileBindingsEnabled &&
                scopeDataType is not null &&
                !string.IsNullOrWhiteSpace(expressionBindingValueExpression) &&
                !string.IsNullOrWhiteSpace(expressionAccessorExpression))
            {
                expressionCompiledBindingPath = "{= " + expressionNormalizedPath + " }";
                expressionCompiledBindingSourceTypeName =
                    scopeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                expressionCompiledBindings = ImmutableArray.Create(new ResolvedCompiledBindingDefinition(
                    TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? ownerDisplayName,
                    TargetPropertyName: resolvedPropertyName,
                    Path: expressionCompiledBindingPath,
                    SourceTypeName: expressionCompiledBindingSourceTypeName,
                    ResultTypeName: expressionResultTypeName,
                    AccessorExpression: expressionAccessorExpression,
                    IsSetterBinding: true,
                    Line: line,
                    Column: column,
                    AccessorPlaceholderToken: _buildCompiledBindingAccessorPlaceholderToken(line, column)));
            }

            plan = new ResolvedSetterValuePlan(
                ValueExpression: expressionBindingValueExpression,
                IsCompiledBinding: !expressionCompiledBindings.IsDefaultOrEmpty,
                CompiledBindingPath: expressionCompiledBindingPath,
                CompiledBindingSourceTypeName: expressionCompiledBindingSourceTypeName,
                CompiledBindings: expressionCompiledBindings,
                ValueKind: ResolvedValueKind.Binding,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        if (_tryResolveSetterValueWithSharedPolicy(
                rawValue,
                conversionTargetType,
                compilation,
                document,
                targetType,
                bindingPriorityScope,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallbackDuringConversion,
                allowCompatibilityStringLiteralFallback,
                converterAttributes,
                out var conversion))
        {
            plan = new ResolvedSetterValuePlan(
                ValueExpression: conversion.Expression,
                IsCompiledBinding: false,
                CompiledBindingPath: null,
                CompiledBindingSourceTypeName: null,
                CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
                ValueKind: conversion.ValueKind,
                RequiresStaticResourceResolver: conversion.RequiresStaticResourceResolver,
                ValueRequirements: conversion.EffectiveRequirements);
            return true;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0102",
            $"Could not convert setter value '{rawValue}' for '{resolvedPropertyName}'. Strategy={(options.StrictMode ? "StrictError" : "CompatibilityFallback")}.",
            document.FilePath,
            line,
            column,
            options.StrictMode));
        plan = default!;
        return false;
    }
}
