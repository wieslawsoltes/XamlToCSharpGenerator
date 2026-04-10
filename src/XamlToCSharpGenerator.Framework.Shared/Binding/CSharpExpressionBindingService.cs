using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class CSharpExpressionBindingService
{
    public delegate bool TryParseCSharpExpressionMarkupDelegate(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        bool csharpExpressionsEnabled,
        bool implicitCSharpExpressionsEnabled,
        out string csharpExpressionCode,
        out bool isExplicitExpression);

    public delegate bool TryBuildAccessorExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CompiledBindingAccessorResolutionResult resolution,
        out string errorMessage);

    private readonly TryParseCSharpExpressionMarkupDelegate _tryParseCSharpExpressionMarkup;
    private readonly TryBuildAccessorExpressionDelegate _tryBuildAccessorExpression;
    private readonly MarkupContextTokenSet _markupContextTokens;
    private readonly Func<string, string> _escape;

    public CSharpExpressionBindingService(
        TryParseCSharpExpressionMarkupDelegate tryParseCSharpExpressionMarkup,
        TryBuildAccessorExpressionDelegate tryBuildAccessorExpression,
        MarkupContextTokenSet markupContextTokens,
        Func<string, string> escape)
    {
        _tryParseCSharpExpressionMarkup = tryParseCSharpExpressionMarkup ?? throw new ArgumentNullException(nameof(tryParseCSharpExpressionMarkup));
        _tryBuildAccessorExpression = tryBuildAccessorExpression ?? throw new ArgumentNullException(nameof(tryBuildAccessorExpression));
        _markupContextTokens = markupContextTokens;
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    public bool TryConvertExpressionMarkupToBindingExpression(
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
        out string diagnosticMessage)
    {
        isExpressionMarkup = false;
        expressionBindingValueExpression = string.Empty;
        accessorExpression = string.Empty;
        normalizedExpression = string.Empty;
        resultTypeName = null;
        diagnosticId = string.Empty;
        diagnosticMessage = string.Empty;

        if (!_tryParseCSharpExpressionMarkup(
                value,
                compilation,
                document,
                options.CSharpExpressionsEnabled,
                options.ImplicitCSharpExpressionsEnabled,
                out var expressionCode,
                out _))
        {
            return false;
        }

        isExpressionMarkup = true;
        normalizedExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(expressionCode);
        if (sourceType is null)
        {
            diagnosticId = "AXSG0110";
            diagnosticMessage = "C# expression markup requires a compile-time binding source.";
            return true;
        }

        if (!_tryBuildAccessorExpression(
                compilation,
                document,
                sourceType,
                normalizedExpression,
                targetPropertyType: null,
                unsafeAccessors: null,
                out var resolution,
                out var errorMessage))
        {
            diagnosticId = "AXSG0110";
            diagnosticMessage = errorMessage;
            return true;
        }

        accessorExpression = resolution.AccessorExpression;
        resultTypeName = resolution.ResultTypeName;
        return TryBuildCompiledBindingRuntimeExpression(
            sourceType,
            resolution,
            accessorPlaceholderToken,
            out expressionBindingValueExpression);
    }

    public bool TryResolveImplicitShorthandExpression(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? targetType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out bool isShorthandExpression,
        out CSharpShorthandResolutionResult result)
    {
        result = default;
        isShorthandExpression = false;

        if (!_tryParseCSharpExpressionMarkup(
                value,
                compilation,
                document,
                options.CSharpExpressionsEnabled,
                options.ImplicitCSharpExpressionsEnabled,
                out var expressionCode,
                out _))
        {
            return false;
        }

        if (!CSharpMarkupExpressionSemantics.TryParseSimpleShorthandPath(expressionCode, out var shorthand))
        {
            return false;
        }

        isShorthandExpression = true;
        if (shorthand.Scope == CSharpShorthandExpressionScope.Auto)
        {
            var canResolveBindingContext = TryResolveShorthandForScope(
                compilation,
                document,
                sourceType,
                sourceType,
                rootTypeSymbol,
                shorthand.Path,
                targetType,
                unsafeAccessors,
                out var bindingContextResolution,
                out _);
            var canResolveRoot = TryResolveShorthandForScope(
                compilation,
                document,
                rootTypeSymbol,
                sourceType,
                rootTypeSymbol,
                shorthand.Path,
                targetType,
                unsafeAccessors,
                out var rootResolution,
                out _);

            if (canResolveBindingContext && canResolveRoot)
            {
                result = new CSharpShorthandResolutionResult(
                    Kind: CSharpShorthandResolutionKind.Conflict,
                    Path: shorthand.Path,
                    ValueExpression: null,
                    AccessorExpression: null,
                    SourceTypeName: null,
                    ResultTypeName: null,
                    DiagnosticId: "AXSG0113",
                    DiagnosticMessage: $"Implicit shorthand binding '{shorthand.Path}' is ambiguous between x:DataType and root context.");
                return true;
            }

            if (canResolveBindingContext)
            {
                result = bindingContextResolution;
                return true;
            }

            if (canResolveRoot)
            {
                result = rootResolution;
                return true;
            }
        }

        var sourceScope = shorthand.Scope switch
        {
            CSharpShorthandExpressionScope.Root => rootTypeSymbol,
            CSharpShorthandExpressionScope.BindingContext => sourceType,
            _ => sourceType ?? rootTypeSymbol
        };
        var resolutionKind = shorthand.Scope == CSharpShorthandExpressionScope.Root
            ? CSharpShorthandResolutionKind.RootExpression
            : CSharpShorthandResolutionKind.BindingPath;

        if (sourceScope is null)
        {
            var diagnosticMessage = shorthand.Scope == CSharpShorthandExpressionScope.Root
                ? "Implicit root shorthand binding requires an available root type."
                : "Implicit C# shorthand binding requires x:DataType in scope.";
            result = new CSharpShorthandResolutionResult(
                Kind: resolutionKind,
                Path: shorthand.Path,
                ValueExpression: null,
                AccessorExpression: null,
                SourceTypeName: null,
                ResultTypeName: null,
                DiagnosticId: "AXSG0110",
                DiagnosticMessage: diagnosticMessage);
            return true;
        }

        if (!TryResolveShorthandForScope(
                compilation,
                document,
                sourceScope,
                sourceType,
                rootTypeSymbol,
                shorthand.Path,
                targetType,
                unsafeAccessors,
                out result,
                out var errorMessage))
        {
            result = new CSharpShorthandResolutionResult(
                Kind: CSharpShorthandResolutionKind.Conflict,
                Path: shorthand.Path,
                ValueExpression: null,
                AccessorExpression: null,
                SourceTypeName: sourceScope.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ResultTypeName: null,
                DiagnosticId: "AXSG0110",
                DiagnosticMessage: errorMessage);
            return true;
        }

        if (shorthand.Scope == CSharpShorthandExpressionScope.Root &&
            result.Kind == CSharpShorthandResolutionKind.BindingPath)
        {
            result = result with { Kind = CSharpShorthandResolutionKind.RootExpression };
        }

        return true;
    }

    private bool TryResolveShorthandForScope(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? sourceScope,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        string shorthandPath,
        INamedTypeSymbol? targetType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CSharpShorthandResolutionResult result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        if (sourceScope is null)
        {
            return false;
        }

        if (!_tryBuildAccessorExpression(
                compilation,
                document,
                sourceScope,
                shorthandPath,
                targetType,
                unsafeAccessors,
                out var accessorResolution,
                out errorMessage))
        {
            return false;
        }

        string? bindingExpression;
        var resolutionKind = CSharpShorthandResolutionKind.BindingPath;
        if (rootType is not null &&
            SymbolEqualityComparer.Default.Equals(sourceScope, rootType) &&
            TryBuildInlineCodeBindingExpression(
                compilation,
                sourceType,
                rootType,
                targetType,
                "root." + shorthandPath,
                out var inlineBindingExpression,
                out _,
                out _,
                out _))
        {
            bindingExpression = inlineBindingExpression;
            resolutionKind = CSharpShorthandResolutionKind.RootExpression;
        }
        else
        {
            TryBuildCompiledBindingRuntimeExpression(
                sourceScope,
                accessorResolution,
                accessorPlaceholderToken: null,
                out var compiledBindingExpression);
            bindingExpression = compiledBindingExpression;
        }

        result = new CSharpShorthandResolutionResult(
            Kind: resolutionKind,
            Path: shorthandPath,
            ValueExpression: bindingExpression,
            AccessorExpression: accessorResolution.AccessorExpression,
            SourceTypeName: sourceScope.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ResultTypeName: accessorResolution.ResultTypeName,
            DiagnosticId: null,
            DiagnosticMessage: null);
        return true;
    }

    public bool TryBuildCompiledBindingRuntimeExpression(
        INamedTypeSymbol sourceType,
        CompiledBindingAccessorResolutionResult resolution,
        string? accessorPlaceholderToken,
        out string expression)
    {
        return TryBuildExpressionBindingRuntimeExpression(
            sourceType,
            resolution.AccessorExpression,
            resolution.DependencyNames,
            accessorPlaceholderToken,
            out expression);
    }

    public bool TryBuildInlineCodeBindingExpression(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        string rawCode,
        out string bindingExpression,
        out string normalizedExpression,
        out string? resultTypeName,
        out string errorMessage)
    {
        bindingExpression = string.Empty;
        normalizedExpression = string.Empty;
        resultTypeName = null;
        errorMessage = string.Empty;

        if (!CSharpInlineCodeAnalysisService.TryAnalyzeExpression(
                compilation,
                sourceType,
                rootType,
                targetType,
                rawCode,
                out var analysis,
                out errorMessage))
        {
            return false;
        }

        normalizedExpression = analysis.NormalizedExpression;
        resultTypeName = analysis.ResultTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bindingExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideInlineCodeBinding<" +
            GetTypeNameOrObject(sourceType, compilation) +
            ", " +
            GetTypeNameOrObject(rootType, compilation) +
            ", " +
            GetTypeNameOrObject(targetType, compilation) +
            ">(static (source, root, target) => (object?)(" +
            analysis.NormalizedExpression +
            "), " +
            BuildStringArrayLiteral(analysis.DependencyNames) +
            ", " +
            _markupContextTokens.ServiceProviderToken +
            ", " +
            _markupContextTokens.RootObjectToken +
            ", " +
            _markupContextTokens.IntermediateRootObjectToken +
            ", " +
            _markupContextTokens.TargetObjectToken +
            ", " +
            _markupContextTokens.TargetPropertyToken +
            ", " +
            _markupContextTokens.BaseUriToken +
            ", " +
            _markupContextTokens.ParentStackToken +
            ")";
        return true;
    }

    private bool TryBuildExpressionBindingRuntimeExpression(
        INamedTypeSymbol sourceType,
        string accessorExpression,
        ImmutableArray<string> dependencyNames,
        string? accessorPlaceholderToken,
        out string expression)
    {
        expression = string.Empty;
        if (string.IsNullOrWhiteSpace(accessorExpression))
        {
            return false;
        }

        var dependencyArrayExpression = BuildStringArrayLiteral(dependencyNames);
        var accessorArgument = string.IsNullOrWhiteSpace(accessorPlaceholderToken)
            ? "static source => (object?)(" + accessorExpression + ")"
            : accessorPlaceholderToken!;
        expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            ">(" +
            accessorArgument +
            ", " +
            dependencyArrayExpression +
            ", " +
            _markupContextTokens.ServiceProviderToken +
            ", " +
            _markupContextTokens.RootObjectToken +
            ", " +
            _markupContextTokens.IntermediateRootObjectToken +
            ", " +
            _markupContextTokens.TargetObjectToken +
            ", " +
            _markupContextTokens.TargetPropertyToken +
            ", " +
            _markupContextTokens.BaseUriToken +
            ", " +
            _markupContextTokens.ParentStackToken +
            ")";
        return true;
    }

    private static string GetTypeNameOrObject(INamedTypeSymbol? typeSymbol, Compilation compilation)
    {
        return (typeSymbol ?? compilation.ObjectType).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private string BuildStringArrayLiteral(ImmutableArray<string> values)
    {
        if (values.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<string>()";
        }

        return "new string[] { " +
               string.Join(", ", values.Select(value => "\"" + _escape(value) + "\"")) +
               " }";
    }
}
