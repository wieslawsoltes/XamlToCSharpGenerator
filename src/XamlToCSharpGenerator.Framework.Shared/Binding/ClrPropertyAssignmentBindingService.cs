using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ClrPropertyAssignmentBindingRequest(
    INamedTypeSymbol OwnerType,
    string OwnerTypeName,
    IPropertySymbol Property,
    XamlPropertyAssignment Assignment,
    Compilation Compilation,
    XamlDocumentModel Document,
    GeneratorOptions Options,
    ImmutableArray<ResolvedCompiledBindingDefinition>.Builder CompiledBindings,
    ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder UnsafeAccessors,
    bool CompileBindingsEnabled,
    INamedTypeSymbol? AssignmentDataType,
    INamedTypeSymbol? CurrentSetterTargetType,
    int BindingPriorityScope,
    bool IsTemplateBindingPriorityScope,
    INamedTypeSymbol? RootTypeSymbol,
    bool IsInsideDataTemplate,
    string XBindDefaultMode,
    XamlObjectNode CurrentNode,
    ITypeSymbol? InferredSetterValueType,
    INamedTypeSymbol? SelectorNestingTypeHint,
    string FrameworkPropertyMetadataTypeName);

public sealed class ClrPropertyAssignmentBindingService
{
    public delegate bool IsFrameworkPropertyTypeDelegate(ITypeSymbol type);
    public delegate bool TryResolveFrameworkPropertyReferenceExpressionDelegate(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        out string expression);
    public delegate bool TryBindFrameworkPropertyAssignmentDelegate(
        ClrPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        bool allowCompiledBindingRegistration,
        string? compiledBindingAccessorPlaceholderToken,
        out ResolvedPropertyAssignment? resolvedAssignment);
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
    public delegate bool IsPotentialCSharpExpressionMarkupDelegate(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        bool cSharpExpressionsEnabled,
        bool implicitCSharpExpressionsEnabled);
    public delegate bool TryResolveImplicitCSharpShorthandExpressionDelegate(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? targetType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out bool isShorthandExpression,
        out CSharpShorthandResolutionResult result);
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
    public delegate bool TryParseXBindMarkupDelegate(string value, out XBindMarkup xBindMarkup);
    public delegate bool TryBuildXBindBindingExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        XBindMarkup markup,
        INamedTypeSymbol? ambientDataContextType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        ITypeSymbol bindingValueType,
        int bindingPriorityScope,
        bool isInsideDataTemplate,
        string defaultMode,
        out string bindingExpression,
        out string bindBackExpression,
        out string errorCode,
        out string errorMessage);
    public delegate bool CanAssignBindingValueDelegate(ITypeSymbol propertyType, Compilation compilation);
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
    public delegate bool TryBuildCompiledBindingAccessorExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CompiledBindingAccessorResolutionResult resolution,
        out string errorMessage);
    public delegate string BuildCompiledBindingAccessorPlaceholderTokenDelegate(int line, int column);
    public delegate bool TryBuildRuntimeBindingExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        ITypeSymbol targetType,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);
    public delegate bool TryParseMarkupExtensionDelegate(string value, out MarkupExtensionInfo markup);
    public delegate bool IsSetterTypeDelegate(INamedTypeSymbol type);
    public delegate bool TryBuildRuntimeXamlFragmentExpressionDelegate(
        string rawValue,
        ITypeSymbol targetType,
        XamlDocumentModel document,
        out string expression);
    public delegate bool HasResolveByNameSemanticsDelegate(INamedTypeSymbol targetType, string propertyName);
    public delegate bool TryBuildResolveByNameLiteralExpressionDelegate(
        string rawValue,
        ITypeSymbol targetType,
        out string expression);
    public delegate bool TryBuildDelegateMethodGroupValueExpressionDelegate(
        string rawValue,
        INamedTypeSymbol delegateType,
        INamedTypeSymbol? rootTypeSymbol,
        out string expression);
    public delegate bool TryResolveSetterValueWithPolicyDelegate(
        ClrPropertyAssignmentBindingRequest request,
        ITypeSymbol conversionTargetType,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedValueConversionResult resolution);
    public delegate bool TryConvertLiteralValueDelegate(
        string rawValue,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool allowObjectStringLiteralFallback,
        INamedTypeSymbol? selectorNestingTypeHint,
        ImmutableArray<AttributeData> converterAttributes);
    public delegate ResolvedPropertyAssignment CreateClrPropertyAssignmentDelegate(
        ClrPropertyAssignmentBindingRequest request,
        string valueExpression,
        ResolvedValueKind valueKind,
        bool requiresStaticResourceResolver,
        ResolvedValueRequirements valueRequirements,
        bool preserveBindingValue);
    public delegate bool HasAssignBindingAttributeDelegate(IPropertySymbol property);

    private readonly IsFrameworkPropertyTypeDelegate _isFrameworkPropertyType;
    private readonly TryResolveFrameworkPropertyReferenceExpressionDelegate _tryResolveFrameworkPropertyReferenceExpression;
    private readonly TryBindFrameworkPropertyAssignmentDelegate _tryBindFrameworkPropertyAssignment;
    private readonly TryParseInlineCSharpMarkupExtensionCodeDelegate _tryParseInlineCSharpMarkupExtensionCode;
    private readonly TryBuildInlineCodeBindingExpressionDelegate _tryBuildInlineCodeBindingExpression;
    private readonly IsPotentialCSharpExpressionMarkupDelegate _isPotentialCSharpExpressionMarkup;
    private readonly TryResolveImplicitCSharpShorthandExpressionDelegate _tryResolveImplicitCSharpShorthandExpression;
    private readonly TryConvertCSharpExpressionMarkupToBindingExpressionDelegate _tryConvertCSharpExpressionMarkupToBindingExpression;
    private readonly TryParseXBindMarkupDelegate _tryParseXBindMarkup;
    private readonly TryBuildXBindBindingExpressionDelegate _tryBuildXBindBindingExpression;
    private readonly CanAssignBindingValueDelegate _canAssignBindingValue;
    private readonly TryParseBindingMarkupDelegate _tryParseBindingMarkup;
    private readonly TryReportBindingSourceConflictDelegate _tryReportBindingSourceConflict;
    private readonly TryResolveCompiledBindingSourceTypeDelegate _tryResolveCompiledBindingSourceType;
    private readonly TryBuildCompiledBindingAccessorExpressionDelegate _tryBuildCompiledBindingAccessorExpression;
    private readonly BuildCompiledBindingAccessorPlaceholderTokenDelegate _buildCompiledBindingAccessorPlaceholderToken;
    private readonly TryBuildRuntimeBindingExpressionDelegate _tryBuildRuntimeBindingExpression;
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly IsSetterTypeDelegate _isSetterType;
    private readonly TryBuildRuntimeXamlFragmentExpressionDelegate _tryBuildRuntimeXamlFragmentExpression;
    private readonly HasResolveByNameSemanticsDelegate _hasResolveByNameSemantics;
    private readonly TryBuildResolveByNameLiteralExpressionDelegate _tryBuildResolveByNameLiteralExpression;
    private readonly TryBuildDelegateMethodGroupValueExpressionDelegate _tryBuildDelegateMethodGroupValueExpression;
    private readonly TryResolveSetterValueWithPolicyDelegate _tryResolveSetterValueWithPolicy;
    private readonly TryConvertLiteralValueDelegate _tryConvertLiteralValue;
    private readonly CreateClrPropertyAssignmentDelegate _createClrPropertyAssignment;
    private readonly HasAssignBindingAttributeDelegate _hasAssignBindingAttribute;

    public ClrPropertyAssignmentBindingService(
        IsFrameworkPropertyTypeDelegate isFrameworkPropertyType,
        TryResolveFrameworkPropertyReferenceExpressionDelegate tryResolveFrameworkPropertyReferenceExpression,
        TryBindFrameworkPropertyAssignmentDelegate tryBindFrameworkPropertyAssignment,
        TryParseInlineCSharpMarkupExtensionCodeDelegate tryParseInlineCSharpMarkupExtensionCode,
        TryBuildInlineCodeBindingExpressionDelegate tryBuildInlineCodeBindingExpression,
        IsPotentialCSharpExpressionMarkupDelegate isPotentialCSharpExpressionMarkup,
        TryResolveImplicitCSharpShorthandExpressionDelegate tryResolveImplicitCSharpShorthandExpression,
        TryConvertCSharpExpressionMarkupToBindingExpressionDelegate tryConvertCSharpExpressionMarkupToBindingExpression,
        TryParseXBindMarkupDelegate tryParseXBindMarkup,
        TryBuildXBindBindingExpressionDelegate tryBuildXBindBindingExpression,
        CanAssignBindingValueDelegate canAssignBindingValue,
        TryParseBindingMarkupDelegate tryParseBindingMarkup,
        TryReportBindingSourceConflictDelegate tryReportBindingSourceConflict,
        TryResolveCompiledBindingSourceTypeDelegate tryResolveCompiledBindingSourceType,
        TryBuildCompiledBindingAccessorExpressionDelegate tryBuildCompiledBindingAccessorExpression,
        BuildCompiledBindingAccessorPlaceholderTokenDelegate buildCompiledBindingAccessorPlaceholderToken,
        TryBuildRuntimeBindingExpressionDelegate tryBuildRuntimeBindingExpression,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        IsSetterTypeDelegate isSetterType,
        TryBuildRuntimeXamlFragmentExpressionDelegate tryBuildRuntimeXamlFragmentExpression,
        HasResolveByNameSemanticsDelegate hasResolveByNameSemantics,
        TryBuildResolveByNameLiteralExpressionDelegate tryBuildResolveByNameLiteralExpression,
        TryBuildDelegateMethodGroupValueExpressionDelegate tryBuildDelegateMethodGroupValueExpression,
        TryResolveSetterValueWithPolicyDelegate tryResolveSetterValueWithPolicy,
        TryConvertLiteralValueDelegate tryConvertLiteralValue,
        CreateClrPropertyAssignmentDelegate createClrPropertyAssignment,
        HasAssignBindingAttributeDelegate hasAssignBindingAttribute)
    {
        _isFrameworkPropertyType = isFrameworkPropertyType ?? throw new ArgumentNullException(nameof(isFrameworkPropertyType));
        _tryResolveFrameworkPropertyReferenceExpression = tryResolveFrameworkPropertyReferenceExpression ?? throw new ArgumentNullException(nameof(tryResolveFrameworkPropertyReferenceExpression));
        _tryBindFrameworkPropertyAssignment = tryBindFrameworkPropertyAssignment ?? throw new ArgumentNullException(nameof(tryBindFrameworkPropertyAssignment));
        _tryParseInlineCSharpMarkupExtensionCode = tryParseInlineCSharpMarkupExtensionCode ?? throw new ArgumentNullException(nameof(tryParseInlineCSharpMarkupExtensionCode));
        _tryBuildInlineCodeBindingExpression = tryBuildInlineCodeBindingExpression ?? throw new ArgumentNullException(nameof(tryBuildInlineCodeBindingExpression));
        _isPotentialCSharpExpressionMarkup = isPotentialCSharpExpressionMarkup ?? throw new ArgumentNullException(nameof(isPotentialCSharpExpressionMarkup));
        _tryResolveImplicitCSharpShorthandExpression = tryResolveImplicitCSharpShorthandExpression ?? throw new ArgumentNullException(nameof(tryResolveImplicitCSharpShorthandExpression));
        _tryConvertCSharpExpressionMarkupToBindingExpression = tryConvertCSharpExpressionMarkupToBindingExpression ?? throw new ArgumentNullException(nameof(tryConvertCSharpExpressionMarkupToBindingExpression));
        _tryParseXBindMarkup = tryParseXBindMarkup ?? throw new ArgumentNullException(nameof(tryParseXBindMarkup));
        _tryBuildXBindBindingExpression = tryBuildXBindBindingExpression ?? throw new ArgumentNullException(nameof(tryBuildXBindBindingExpression));
        _canAssignBindingValue = canAssignBindingValue ?? throw new ArgumentNullException(nameof(canAssignBindingValue));
        _tryParseBindingMarkup = tryParseBindingMarkup ?? throw new ArgumentNullException(nameof(tryParseBindingMarkup));
        _tryReportBindingSourceConflict = tryReportBindingSourceConflict ?? throw new ArgumentNullException(nameof(tryReportBindingSourceConflict));
        _tryResolveCompiledBindingSourceType = tryResolveCompiledBindingSourceType ?? throw new ArgumentNullException(nameof(tryResolveCompiledBindingSourceType));
        _tryBuildCompiledBindingAccessorExpression = tryBuildCompiledBindingAccessorExpression ?? throw new ArgumentNullException(nameof(tryBuildCompiledBindingAccessorExpression));
        _buildCompiledBindingAccessorPlaceholderToken = buildCompiledBindingAccessorPlaceholderToken ?? throw new ArgumentNullException(nameof(buildCompiledBindingAccessorPlaceholderToken));
        _tryBuildRuntimeBindingExpression = tryBuildRuntimeBindingExpression ?? throw new ArgumentNullException(nameof(tryBuildRuntimeBindingExpression));
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
        _isSetterType = isSetterType ?? throw new ArgumentNullException(nameof(isSetterType));
        _tryBuildRuntimeXamlFragmentExpression = tryBuildRuntimeXamlFragmentExpression ?? throw new ArgumentNullException(nameof(tryBuildRuntimeXamlFragmentExpression));
        _hasResolveByNameSemantics = hasResolveByNameSemantics ?? throw new ArgumentNullException(nameof(hasResolveByNameSemantics));
        _tryBuildResolveByNameLiteralExpression = tryBuildResolveByNameLiteralExpression ?? throw new ArgumentNullException(nameof(tryBuildResolveByNameLiteralExpression));
        _tryBuildDelegateMethodGroupValueExpression = tryBuildDelegateMethodGroupValueExpression ?? throw new ArgumentNullException(nameof(tryBuildDelegateMethodGroupValueExpression));
        _tryResolveSetterValueWithPolicy = tryResolveSetterValueWithPolicy ?? throw new ArgumentNullException(nameof(tryResolveSetterValueWithPolicy));
        _tryConvertLiteralValue = tryConvertLiteralValue ?? throw new ArgumentNullException(nameof(tryConvertLiteralValue));
        _createClrPropertyAssignment = createClrPropertyAssignment ?? throw new ArgumentNullException(nameof(createClrPropertyAssignment));
        _hasAssignBindingAttribute = hasAssignBindingAttribute ?? throw new ArgumentNullException(nameof(hasAssignBindingAttribute));
    }

    public bool TryBind(
        ClrPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? assignment)
    {
        assignment = null;
        var property = request.Property;
        var assignmentSource = request.Assignment;
        var isSetterValueProperty = property.Name.Equals("Value", StringComparison.Ordinal) &&
                                    _isSetterType(request.OwnerType);
        var conversionTargetType = property.Type;
        if (isSetterValueProperty &&
            request.InferredSetterValueType is not null &&
            conversionTargetType.SpecialType == SpecialType.System_Object)
        {
            conversionTargetType = request.InferredSetterValueType;
        }

        var trimmedAssignmentValue = assignmentSource.Value.TrimStart();
        var preserveBindingValue = _hasAssignBindingAttribute(property);
        var canAssignBindingValue = preserveBindingValue ||
                                    _canAssignBindingValue(property.Type, request.Compilation);
        if (isSetterValueProperty &&
            conversionTargetType.SpecialType != SpecialType.System_Object &&
            (trimmedAssignmentValue.Length == 0 || trimmedAssignmentValue[0] != '{') &&
            _tryResolveSetterValueWithPolicy(
                request,
                conversionTargetType,
                diagnostics,
                out var earlySetterResolution))
        {
            assignment = _createClrPropertyAssignment(
                request,
                earlySetterResolution.Expression,
                earlySetterResolution.ValueKind,
                earlySetterResolution.RequiresStaticResourceResolver,
                earlySetterResolution.EffectiveRequirements,
                preserveBindingValue: false);
            return true;
        }

        var clrFrameworkPropertyReferenceOwnerType = request.CurrentSetterTargetType ?? request.OwnerType;
        if (_isFrameworkPropertyType(property.Type) &&
            _tryResolveFrameworkPropertyReferenceExpression(
                assignmentSource.Value,
                request.Compilation,
                request.Document,
                clrFrameworkPropertyReferenceOwnerType,
                out var clrFrameworkPropertyReferenceExpression))
        {
            assignment = _createClrPropertyAssignment(
                request,
                clrFrameworkPropertyReferenceExpression,
                ResolvedValueKind.Literal,
                requiresStaticResourceResolver: false,
                ResolvedValueRequirements.None,
                preserveBindingValue: false);
            return true;
        }

        if (_tryParseInlineCSharpMarkupExtensionCode(assignmentSource.Value, out var inlineCode))
        {
            if (_tryBindFrameworkPropertyAssignment(
                    request,
                    diagnostics,
                    allowCompiledBindingRegistration: false,
                    compiledBindingAccessorPlaceholderToken: null,
                    out var frameworkInlineCodeAssignment))
            {
                assignment = frameworkInlineCodeAssignment;
                return true;
            }

            if (!_tryBuildInlineCodeBindingExpression(
                    request.Compilation,
                    request.AssignmentDataType,
                    request.RootTypeSymbol,
                    request.OwnerType,
                    inlineCode,
                    out var inlineBindingExpression,
                    out _,
                    out _,
                    out var inlineErrorMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0112",
                    $"Inline C# for '{property.Name}' is invalid: {inlineErrorMessage}",
                    request.Document.FilePath,
                    assignmentSource.Line,
                    assignmentSource.Column,
                    request.Options.StrictMode));
                return true;
            }

            assignment = _createClrPropertyAssignment(
                request,
                inlineBindingExpression,
                ResolvedValueKind.Binding,
                requiresStaticResourceResolver: false,
                ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                preserveBindingValue: preserveBindingValue);
            return true;
        }

        var isPotentialCSharpExpressionMarkup = _isPotentialCSharpExpressionMarkup(
            assignmentSource.Value,
            request.Compilation,
            request.Document,
            request.Options.CSharpExpressionsEnabled,
            request.Options.ImplicitCSharpExpressionsEnabled);

        if (isPotentialCSharpExpressionMarkup &&
            _tryBindFrameworkPropertyAssignment(
                request,
                diagnostics,
                allowCompiledBindingRegistration: true,
                compiledBindingAccessorPlaceholderToken: null,
                out var frameworkShorthandAssignment))
        {
            assignment = frameworkShorthandAssignment;
            return true;
        }

        if (isPotentialCSharpExpressionMarkup &&
            _tryResolveImplicitCSharpShorthandExpression(
                assignmentSource.Value,
                request.Compilation,
                request.Document,
                request.Options,
                request.AssignmentDataType,
                request.RootTypeSymbol,
                request.CurrentSetterTargetType ?? request.OwnerType,
                request.UnsafeAccessors,
                out var isShorthandExpression,
                out var shorthandResolution) &&
            isShorthandExpression)
        {
            if (!string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticId) &&
                !string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    shorthandResolution.DiagnosticId!,
                    shorthandResolution.DiagnosticMessage!,
                    request.Document.FilePath,
                    assignmentSource.Line,
                    assignmentSource.Column,
                    request.Options.StrictMode));
                return true;
            }

            if (!string.IsNullOrWhiteSpace(shorthandResolution.ValueExpression))
            {
                if (canAssignBindingValue)
                {
                    assignment = _createClrPropertyAssignment(
                        request,
                        shorthandResolution.ValueExpression!,
                        ResolvedValueKind.Binding,
                        requiresStaticResourceResolver: false,
                        ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                        preserveBindingValue: preserveBindingValue);
                    return true;
                }
            }
        }

        var shouldTryClrExpressionMarkup = canAssignBindingValue ||
                                           (trimmedAssignmentValue.Length > 0 && trimmedAssignmentValue[0] == '{');
        var isExpressionMarkup = false;
        var expressionBindingValueExpression = string.Empty;
        var expressionErrorCode = string.Empty;
        var expressionErrorMessage = string.Empty;
        if (shouldTryClrExpressionMarkup &&
            _tryConvertCSharpExpressionMarkupToBindingExpression(
                assignmentSource.Value,
                request.Compilation,
                request.Document,
                request.Options,
                request.AssignmentDataType,
                accessorPlaceholderToken: null,
                out isExpressionMarkup,
                out expressionBindingValueExpression,
                out _,
                out _,
                out _,
                out expressionErrorCode,
                out expressionErrorMessage))
        {
            if (_tryBindFrameworkPropertyAssignment(
                    request,
                    diagnostics,
                    allowCompiledBindingRegistration: true,
                    compiledBindingAccessorPlaceholderToken: null,
                    out var frameworkExpressionAssignment))
            {
                assignment = frameworkExpressionAssignment;
                return true;
            }

            assignment = _createClrPropertyAssignment(
                request,
                expressionBindingValueExpression,
                ResolvedValueKind.Binding,
                requiresStaticResourceResolver: false,
                ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                preserveBindingValue: preserveBindingValue);
            return true;
        }

        if (isExpressionMarkup)
        {
            var message = expressionErrorCode == "AXSG0110"
                ? $"Expression binding for '{property.Name}' requires x:DataType in scope."
                : $"Expression binding for '{property.Name}' is invalid: {expressionErrorMessage}";
            diagnostics.Add(new DiagnosticInfo(
                expressionErrorCode,
                message,
                request.Document.FilePath,
                assignmentSource.Line,
                assignmentSource.Column,
                request.Options.StrictMode));
            return true;
        }

        if (_tryParseXBindMarkup(assignmentSource.Value, out var xBindMarkup))
        {
            var xBindErrorCode = "AXSG0117";
            var xBindErrorMessage = $"x:Bind expression '{xBindMarkup.Path}' could not be converted.";

            if (_tryBindFrameworkPropertyAssignment(
                    request,
                    diagnostics,
                    allowCompiledBindingRegistration: false,
                    compiledBindingAccessorPlaceholderToken: null,
                    out var xBindFrameworkAssignment))
            {
                assignment = xBindFrameworkAssignment;
                return true;
            }

            if (_canAssignBindingValue(property.Type, request.Compilation) &&
                _tryBuildXBindBindingExpression(
                    request.Compilation,
                    request.Document,
                    request.CurrentNode,
                    xBindMarkup,
                    request.AssignmentDataType,
                    request.RootTypeSymbol,
                    request.CurrentSetterTargetType ?? request.OwnerType,
                    property.Type,
                    request.BindingPriorityScope,
                    request.IsInsideDataTemplate,
                    request.XBindDefaultMode,
                    out var xBindValueExpression,
                    out _,
                    out xBindErrorCode,
                    out xBindErrorMessage))
            {
                assignment = _createClrPropertyAssignment(
                    request,
                    xBindValueExpression,
                    ResolvedValueKind.Binding,
                    requiresStaticResourceResolver: false,
                    ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    preserveBindingValue: preserveBindingValue);
                return true;
            }

            diagnostics.Add(new DiagnosticInfo(
                xBindErrorCode,
                xBindErrorMessage,
                request.Document.FilePath,
                assignmentSource.Line,
                assignmentSource.Column,
                request.Options.StrictMode));
            return true;
        }

        if (_tryParseBindingMarkup(assignmentSource.Value, out var bindingMarkup))
        {
            if (_tryReportBindingSourceConflict(
                    bindingMarkup,
                    diagnostics,
                    request.Document,
                    assignmentSource.Line,
                    assignmentSource.Column,
                    request.Options.StrictMode))
            {
                return true;
            }

            var wantsCompiledBinding = bindingMarkup.IsCompiledBinding ||
                                      (request.CompileBindingsEnabled &&
                                       !BindingEventMarkupParser.HasExplicitBindingSource(bindingMarkup));
            INamedTypeSymbol? compiledBindingSourceType = null;
            var requiresAmbientDataType = false;
            var hasInvalidLocalDataType = false;
            var shouldCompileBinding = wantsCompiledBinding &&
                                       _tryResolveCompiledBindingSourceType(
                                           request.Compilation,
                                           request.Document,
                                           bindingMarkup,
                                           request.AssignmentDataType,
                                           request.CurrentSetterTargetType ?? request.OwnerType,
                                           out compiledBindingSourceType,
                                           out requiresAmbientDataType,
                                           out hasInvalidLocalDataType);
            if (shouldCompileBinding)
            {
                if (!_tryBuildCompiledBindingAccessorExpression(
                        request.Compilation,
                        request.Document,
                        compiledBindingSourceType!,
                        bindingMarkup.Path,
                        property.Type,
                        request.UnsafeAccessors,
                        out var compiledBindingResolution,
                        out var errorMessage))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0111",
                        $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{compiledBindingSourceType!.ToDisplayString()}': {errorMessage}",
                        request.Document.FilePath,
                        assignmentSource.Line,
                        assignmentSource.Column,
                        request.Options.StrictMode));
                    return true;
                }

                var compiledBindingAccessorPlaceholderToken = _buildCompiledBindingAccessorPlaceholderToken(
                    assignmentSource.Line,
                    assignmentSource.Column);
                request.CompiledBindings.Add(new ResolvedCompiledBindingDefinition(
                    TargetTypeName: request.OwnerTypeName,
                    TargetPropertyName: property.Name,
                    Path: compiledBindingResolution.NormalizedPath,
                    SourceTypeName: compiledBindingSourceType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ResultTypeName: compiledBindingResolution.ResultTypeName,
                    AccessorExpression: compiledBindingResolution.AccessorExpression,
                    IsSetterBinding: false,
                    Line: assignmentSource.Line,
                    Column: assignmentSource.Column,
                    AccessorPlaceholderToken: compiledBindingAccessorPlaceholderToken));

                if (_tryBindFrameworkPropertyAssignment(
                        request,
                        diagnostics,
                        allowCompiledBindingRegistration: false,
                        compiledBindingAccessorPlaceholderToken,
                        out var compiledBindingFrameworkAssignment))
                {
                    assignment = compiledBindingFrameworkAssignment;
                    return true;
                }
            }
            else if (wantsCompiledBinding && hasInvalidLocalDataType)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0110",
                    $"Compiled binding for '{property.Name}' specifies invalid DataType '{bindingMarkup.DataType}'.",
                    request.Document.FilePath,
                    assignmentSource.Line,
                    assignmentSource.Column,
                    request.Options.StrictMode));
                return true;
            }
            else if (wantsCompiledBinding && requiresAmbientDataType)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0110",
                    $"Compiled binding for '{property.Name}' requires x:DataType in scope.",
                    request.Document.FilePath,
                    assignmentSource.Line,
                    assignmentSource.Column,
                    request.Options.StrictMode));
                return true;
            }

            if (_tryBindFrameworkPropertyAssignment(
                    request,
                    diagnostics,
                    allowCompiledBindingRegistration: false,
                    compiledBindingAccessorPlaceholderToken: null,
                    out var bindingFrameworkAssignment))
            {
                assignment = bindingFrameworkAssignment;
                return true;
            }

            if (_tryBuildRuntimeBindingExpression(
                    request.Compilation,
                    request.Document,
                    bindingMarkup,
                    property.Type,
                    request.CurrentSetterTargetType,
                    request.BindingPriorityScope,
                    out var runtimeBindingExpression))
            {
                assignment = _createClrPropertyAssignment(
                    request,
                    runtimeBindingExpression,
                    ResolvedValueKind.Binding,
                    requiresStaticResourceResolver: false,
                    ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    preserveBindingValue: _hasAssignBindingAttribute(property));
                return true;
            }

            if (shouldCompileBinding)
            {
                return true;
            }
        }

        if (request.IsTemplateBindingPriorityScope &&
            _tryBindFrameworkPropertyAssignment(
                request,
                diagnostics,
                allowCompiledBindingRegistration: false,
                compiledBindingAccessorPlaceholderToken: null,
                out var templatePriorityAssignment))
        {
            assignment = templatePriorityAssignment;
            return true;
        }

        if (_tryParseMarkupExtension(assignmentSource.Value, out _) &&
            _tryBindFrameworkPropertyAssignment(
                request,
                diagnostics,
                allowCompiledBindingRegistration: false,
                compiledBindingAccessorPlaceholderToken: null,
                out var markupExtensionAssignment))
        {
            assignment = markupExtensionAssignment;
            return true;
        }

        var valueExpression = string.Empty;
        var valueKind = ResolvedValueKind.Literal;
        var requiresStaticResourceResolver = false;
        var valueRequirements = ResolvedValueRequirements.None;

        if (isSetterValueProperty &&
            _tryBuildRuntimeXamlFragmentExpression(
                assignmentSource.Value,
                conversionTargetType,
                request.Document,
                out var runtimeXamlSetterValueExpression))
        {
            valueExpression = runtimeXamlSetterValueExpression;
            valueKind = ResolvedValueKind.RuntimeXamlFallback;
            valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
        }

        if (valueExpression.Length == 0 &&
            _hasResolveByNameSemantics(request.OwnerType, property.Name) &&
            _tryBuildResolveByNameLiteralExpression(
                assignmentSource.Value,
                conversionTargetType,
                out var resolveByNameValueExpression))
        {
            valueExpression = resolveByNameValueExpression;
            valueKind = ResolvedValueKind.MarkupExtension;
        }

        if (valueExpression.Length == 0 &&
            property.Type is INamedTypeSymbol delegateType &&
            delegateType.TypeKind == TypeKind.Delegate &&
            _tryBuildDelegateMethodGroupValueExpression(
                assignmentSource.Value,
                delegateType,
                request.RootTypeSymbol,
                out var delegateMethodExpression))
        {
            valueExpression = delegateMethodExpression;
        }

        if (valueExpression.Length == 0 && isSetterValueProperty)
        {
            if (!_tryResolveSetterValueWithPolicy(
                    request,
                    conversionTargetType,
                    diagnostics,
                    out var setterResolution))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0102",
                    $"Could not convert setter value '{assignmentSource.Value}' for '{request.CurrentSetterTargetType?.ToDisplayString() ?? request.OwnerType.ToDisplayString()}.{property.Name}'.",
                    request.Document.FilePath,
                    assignmentSource.Line,
                    assignmentSource.Column,
                    request.Options.StrictMode));
                return true;
            }

            valueExpression = setterResolution.Expression;
            valueKind = setterResolution.ValueKind;
            requiresStaticResourceResolver = setterResolution.RequiresStaticResourceResolver;
            valueRequirements = setterResolution.EffectiveRequirements;
        }
        else if (valueExpression.Length == 0)
        {
            if (!_tryConvertLiteralValue(
                    assignmentSource.Value,
                    conversionTargetType,
                    request.Compilation,
                    request.Document,
                    request.CurrentSetterTargetType,
                    request.BindingPriorityScope,
                    out var convertedValue,
                    allowObjectStringLiteralFallback: !request.Options.StrictMode &&
                                                      conversionTargetType.SpecialType == SpecialType.System_Object,
                    request.SelectorNestingTypeHint,
                    property.GetAttributes()))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0102",
                    $"Could not convert literal '{assignmentSource.Value}' for '{property.Name}' on '{request.OwnerType.ToDisplayString()}'.",
                    request.Document.FilePath,
                    assignmentSource.Line,
                    assignmentSource.Column,
                    request.Options.StrictMode));
                return true;
            }

            valueExpression = convertedValue.Expression;
            valueKind = convertedValue.ValueKind;
            requiresStaticResourceResolver = convertedValue.RequiresStaticResourceResolver;
            valueRequirements = convertedValue.EffectiveRequirements;
        }

        assignment = _createClrPropertyAssignment(
            request,
            valueExpression,
            valueKind,
            requiresStaticResourceResolver,
            valueRequirements,
            preserveBindingValue: false);
        return true;
    }
}
