using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool IsKnownTemplateKind(string kind)
    {
        return SemanticConventions.IsKnownTemplateKind(kind);
    }

    private static bool IsDataTemplateNode(XamlObjectNode node)
    {
        return string.Equals(node.XmlTypeName, "DataTemplate", StringComparison.Ordinal) ||
               string.Equals(node.XmlTypeName, "TreeDataTemplate", StringComparison.Ordinal);
    }

    private static INamedTypeSymbol? ResolveResourceTypeSymbol(
        Compilation compilation,
        XamlDocumentModel document,
        XamlResourceDefinition resource)
    {
        if (resource.TypeArguments.IsDefaultOrEmpty)
        {
            return ResolveTypeSymbol(compilation, resource.XmlNamespace, resource.XmlTypeName);
        }

        var genericType = ResolveTypeSymbol(compilation, resource.XmlNamespace, resource.XmlTypeName, resource.TypeArguments.Length) ??
                          ResolveTypeSymbol(compilation, resource.XmlNamespace, resource.XmlTypeName);
        if (genericType is null)
        {
            return null;
        }

        var typeArguments = new ITypeSymbol[resource.TypeArguments.Length];
        for (var index = 0; index < resource.TypeArguments.Length; index++)
        {
            var resolvedTypeArgument = ResolveTypeToken(compilation, document, resource.TypeArguments[index], document.ClassNamespace);
            if (resolvedTypeArgument is null)
            {
                return genericType;
            }

            typeArguments[index] = resolvedTypeArgument;
        }

        return genericType.TypeParameters.Length == typeArguments.Length
            ? genericType.Construct(typeArguments)
            : genericType;
    }

    private static INamedTypeSymbol? ResolveTemplateNodeType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node)
    {
        return ResolveObjectTypeSymbol(compilation, document, node);
    }

    private static INamedTypeSymbol? ResolveTemplateContentRootExpectedType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlTemplateDefinition template)
    {
        return ResolveTypeFromTypeExpression(compilation, document, template.TargetType, document.ClassNamespace);
    }

    private static void ValidateControlThemeBasedOnChains(
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document)
    {
        ControlThemeBasedOnValidationService.Validate(
            controlThemes,
            diagnostics,
            document);
    }

    private static void ValidateControlTemplateParts(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        TemplateValidationService.ValidateControlTemplateParts(
            template,
            compilation,
            document,
            diagnostics,
            options);
    }

    private static void ValidateTemplateContentRootType(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        TemplateValidationService.ValidateTemplateContentRootType(
            template,
            compilation,
            document,
            diagnostics,
            options);
    }
}
