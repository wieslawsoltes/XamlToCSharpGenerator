using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class TemplateDefinitionBindingService
{
    public delegate bool ShouldSkipBranchDelegate(
        ConditionalXamlExpression? condition,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options);

    public delegate bool IsKnownTemplateKindDelegate(string kind);

    public delegate INamedTypeSymbol? ResolveTypeFromTypeExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawTypeExpression,
        string? fallbackClrNamespace);

    public delegate void ValidateTemplateDelegate(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options);

    private readonly ShouldSkipBranchDelegate _shouldSkipBranch;
    private readonly IsKnownTemplateKindDelegate _isKnownTemplateKind;
    private readonly ResolveTypeFromTypeExpressionDelegate _resolveTypeFromTypeExpression;
    private readonly ValidateTemplateDelegate _validateControlTemplateParts;
    private readonly ValidateTemplateDelegate _validateTemplateContentRootType;

    public TemplateDefinitionBindingService(
        ShouldSkipBranchDelegate shouldSkipBranch,
        IsKnownTemplateKindDelegate isKnownTemplateKind,
        ResolveTypeFromTypeExpressionDelegate resolveTypeFromTypeExpression,
        ValidateTemplateDelegate validateControlTemplateParts,
        ValidateTemplateDelegate validateTemplateContentRootType)
    {
        _shouldSkipBranch = shouldSkipBranch ?? throw new ArgumentNullException(nameof(shouldSkipBranch));
        _isKnownTemplateKind = isKnownTemplateKind ?? throw new ArgumentNullException(nameof(isKnownTemplateKind));
        _resolveTypeFromTypeExpression = resolveTypeFromTypeExpression ?? throw new ArgumentNullException(nameof(resolveTypeFromTypeExpression));
        _validateControlTemplateParts = validateControlTemplateParts ?? throw new ArgumentNullException(nameof(validateControlTemplateParts));
        _validateTemplateContentRootType = validateTemplateContentRootType ?? throw new ArgumentNullException(nameof(validateTemplateContentRootType));
    }

    public ImmutableArray<ResolvedTemplateDefinition> BindTemplates(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var templates = ImmutableArray.CreateBuilder<ResolvedTemplateDefinition>(document.Templates.Length);
        foreach (var template in document.Templates)
        {
            if (_shouldSkipBranch(template.Condition, compilation, document, diagnostics, options))
            {
                continue;
            }

            if (_isKnownTemplateKind(template.Kind))
            {
                if (options.StrictMode &&
                    (string.Equals(template.Kind, "DataTemplate", StringComparison.Ordinal) ||
                     string.Equals(template.Kind, "TreeDataTemplate", StringComparison.Ordinal)) &&
                    string.IsNullOrWhiteSpace(template.DataType))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0500",
                        $"{template.Kind} should declare x:DataType in strict mode.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        options.StrictMode));
                }

                _validateControlTemplateParts(template, compilation, document, diagnostics, options);
                _validateTemplateContentRootType(template, compilation, document, diagnostics, options);
            }

            var targetType = _resolveTypeFromTypeExpression(compilation, document, template.TargetType, document.ClassNamespace);
            var dataType = _resolveTypeFromTypeExpression(compilation, document, template.DataType, document.ClassNamespace);

            templates.Add(new ResolvedTemplateDefinition(
                Kind: template.Kind,
                Key: template.Key,
                TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? template.TargetType,
                DataType: dataType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? template.DataType,
                RawXaml: template.RawXaml,
                Line: template.Line,
                Column: template.Column,
                Condition: template.Condition));
        }

        return templates.ToImmutable();
    }
}
