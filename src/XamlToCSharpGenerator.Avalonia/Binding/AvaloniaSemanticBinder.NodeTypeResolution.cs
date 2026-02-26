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


    private static string ResolveGeneratedClassModifier(
        XamlDocumentModel document,
        INamedTypeSymbol? classSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var normalizedRequested = NormalizeClassModifier(document.ClassModifier);
        if (!string.IsNullOrWhiteSpace(document.ClassModifier) && normalizedRequested is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0104",
                $"x:ClassModifier value '{document.ClassModifier}' is not supported.",
                document.FilePath,
                1,
                1,
                options.StrictMode));
        }

        if (classSymbol is not null)
        {
            var symbolModifier = ToCSharpClassModifier(classSymbol.DeclaredAccessibility);
            if (normalizedRequested is not null &&
                !normalizedRequested.Equals(symbolModifier, StringComparison.Ordinal))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0105",
                    $"x:ClassModifier '{normalizedRequested}' does not match class declaration accessibility '{symbolModifier}'.",
                    document.FilePath,
                    1,
                    1,
                    options.StrictMode));
            }

            return symbolModifier;
        }

        return normalizedRequested ?? "internal";
    }

    private static INamedTypeSymbol? ResolveCurrentSetterTargetType(
        INamedTypeSymbol? nodeType,
        XamlObjectNode node,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? inheritedSetterTargetType)
    {
        if (nodeType is null)
        {
            return inheritedSetterTargetType;
        }

        if (IsControlTemplateType(nodeType, compilation))
        {
            var targetTypeValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("TargetType", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetTypeValue))
            {
                var targetType = ResolveTypeFromTypeExpression(compilation, document, targetTypeValue, document.ClassNamespace);
                if (targetType is not null)
                {
                    return targetType;
                }
            }

            return inheritedSetterTargetType;
        }

        if (IsControlThemeType(nodeType, compilation))
        {
            var targetTypeValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("TargetType", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetTypeValue))
            {
                var targetType = ResolveTypeFromTypeExpression(compilation, document, targetTypeValue, document.ClassNamespace);
                if (targetType is not null)
                {
                    return targetType;
                }
            }

            return inheritedSetterTargetType;
        }

        if (IsStyleType(nodeType, compilation))
        {
            var selectorValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("Selector", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(selectorValue))
            {
                var selectorValidation = SelectorSyntaxValidator.Validate(selectorValue!);
                if (selectorValidation.IsValid)
                {
                    var selectorTargetType = AvaloniaSelectorSemanticAdapter.TryResolveSelectorTargetType(
                        selectorValidation.Branches,
                        typeToken => ResolveTypeToken(compilation, document, typeToken, document.ClassNamespace),
                        IsTypeAssignableTo,
                        out _,
                        out _);
                    if (selectorTargetType is not null)
                    {
                        return selectorTargetType;
                    }
                }
            }

            return inheritedSetterTargetType;
        }

        return inheritedSetterTargetType;
    }

    private static INamedTypeSymbol? ResolveObjectTypeSymbol(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node)
    {
        if (node.TypeArguments.IsDefaultOrEmpty)
        {
            return ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
        }

        var resolvedTypeArguments = new List<ITypeSymbol>(node.TypeArguments.Length);
        foreach (var argumentToken in node.TypeArguments)
        {
            var resolvedTypeArgument = ResolveTypeToken(compilation, document, argumentToken, document.ClassNamespace);
            if (resolvedTypeArgument is null)
            {
                return ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
            }

            resolvedTypeArguments.Add(resolvedTypeArgument);
        }

        var genericType = ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName, node.TypeArguments.Length) ??
                          ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
        if (genericType is null)
        {
            return null;
        }

        if (genericType.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return genericType.Construct(resolvedTypeArguments.ToArray());
        }

        if (genericType.OriginalDefinition.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return genericType.OriginalDefinition.Construct(resolvedTypeArguments.ToArray());
        }

        return genericType;
    }

    private static bool IsStyleType(INamedTypeSymbol type, Compilation compilation)
    {
        var styleType = compilation.GetTypeByMetadataName("Avalonia.Styling.Style");
        return styleType is not null && IsTypeAssignableTo(type, styleType);
    }

    private static bool IsControlThemeType(INamedTypeSymbol type, Compilation compilation)
    {
        var controlThemeType = compilation.GetTypeByMetadataName("Avalonia.Styling.ControlTheme");
        return controlThemeType is not null && IsTypeAssignableTo(type, controlThemeType);
    }

    private static bool IsControlTemplateType(INamedTypeSymbol type, Compilation compilation)
    {
        var markupControlTemplateType = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.Templates.ControlTemplate");
        if (markupControlTemplateType is not null && IsTypeAssignableTo(type, markupControlTemplateType))
        {
            return true;
        }

        var controlsControlTemplateType = compilation.GetTypeByMetadataName("Avalonia.Controls.Templates.ControlTemplate");
        if (controlsControlTemplateType is not null && IsTypeAssignableTo(type, controlsControlTemplateType))
        {
            return true;
        }

        var iControlTemplate = compilation.GetTypeByMetadataName("Avalonia.Controls.Templates.IControlTemplate");
        return iControlTemplate is not null && IsTypeAssignableTo(type, iControlTemplate);
    }

    private static bool IsTemplateScopeType(INamedTypeSymbol type, Compilation compilation)
    {
        if (IsControlTemplateType(type, compilation))
        {
            return true;
        }

        var itemsPanelTemplateType = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.Templates.ItemsPanelTemplate");
        if (itemsPanelTemplateType is not null && IsTypeAssignableTo(type, itemsPanelTemplateType))
        {
            return true;
        }

        var templateType = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.Templates.Template");
        return templateType is not null && IsTypeAssignableTo(type, templateType);
    }
}
