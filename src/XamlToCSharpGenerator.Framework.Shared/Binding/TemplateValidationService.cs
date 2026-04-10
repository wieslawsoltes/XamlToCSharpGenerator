using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class TemplateValidationService
{
    public delegate bool TryGetNodeNameScopeRegistrationDelegate(XamlObjectNode node, out string registeredName);

    public delegate INamedTypeSymbol? ResolveTemplateNodeTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node);

    public delegate INamedTypeSymbol? ResolveTemplateContentRootExpectedTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        XamlTemplateDefinition template);

    public delegate INamedTypeSymbol? ResolveContractTypeDelegate(
        Compilation compilation,
        TypeContractId contractId);

    private readonly TemplateObjectNodeSearchService _templateObjectNodeSearchService;
    private readonly TryGetNodeNameScopeRegistrationDelegate _tryGetNodeNameScopeRegistration;
    private readonly ResolveTemplateNodeTypeDelegate _resolveTemplateNodeType;
    private readonly ResolveTemplateContentRootExpectedTypeDelegate _resolveTemplateContentRootExpectedType;
    private readonly ResolveContractTypeDelegate _resolveContractType;
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;

    public TemplateValidationService(
        TemplateObjectNodeSearchService templateObjectNodeSearchService,
        TryGetNodeNameScopeRegistrationDelegate tryGetNodeNameScopeRegistration,
        ResolveTemplateNodeTypeDelegate resolveTemplateNodeType,
        ResolveTemplateContentRootExpectedTypeDelegate resolveTemplateContentRootExpectedType,
        ResolveContractTypeDelegate resolveContractType,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo)
    {
        _templateObjectNodeSearchService = templateObjectNodeSearchService ?? throw new ArgumentNullException(nameof(templateObjectNodeSearchService));
        _tryGetNodeNameScopeRegistration = tryGetNodeNameScopeRegistration ?? throw new ArgumentNullException(nameof(tryGetNodeNameScopeRegistration));
        _resolveTemplateNodeType = resolveTemplateNodeType ?? throw new ArgumentNullException(nameof(resolveTemplateNodeType));
        _resolveTemplateContentRootExpectedType = resolveTemplateContentRootExpectedType ?? throw new ArgumentNullException(nameof(resolveTemplateContentRootExpectedType));
        _resolveContractType = resolveContractType ?? throw new ArgumentNullException(nameof(resolveContractType));
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
    }

    public void ValidateControlTemplateParts(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        _ = options;

        if (!string.Equals(template.Kind, "ControlTemplate", StringComparison.Ordinal))
        {
            return;
        }

        var targetType = _resolveTemplateContentRootExpectedType(compilation, document, template);
        var templateNode = _templateObjectNodeSearchService.FindTemplateNode(document, template);
        var contentRoot = templateNode is null
            ? null
            : _templateObjectNodeSearchService.TryGetTemplateContentNode(templateNode);
        if (targetType is null || contentRoot is null)
        {
            return;
        }

        var registeredNames = new Dictionary<string, XamlObjectNode>(StringComparer.Ordinal);
        foreach (var node in EnumerateTemplateNodes(contentRoot))
        {
            if (_tryGetNodeNameScopeRegistration(node, out var registeredName) &&
                !string.IsNullOrWhiteSpace(registeredName))
            {
                registeredNames[registeredName] = node;
            }
        }

        foreach (var attribute in targetType.GetAttributes())
        {
            if (!IsTemplatePartAttribute(attribute))
            {
                continue;
            }

            var partName = ReadTemplatePartName(attribute);
            var partType = ReadTemplatePartType(attribute);
            var isRequired = ReadTemplatePartRequired(attribute);
            if (string.IsNullOrWhiteSpace(partName) || partType is not INamedTypeSymbol expectedPartType)
            {
                continue;
            }

            if (!registeredNames.TryGetValue(partName!, out var partNode))
            {
                diagnostics.Add(new DiagnosticInfo(
                    isRequired ? "AXSG0502" : "AXSG0504",
                    isRequired
                        ? $"ControlTemplate for '{targetType.Name}' is missing required part '{partName}'."
                        : $"ControlTemplate for '{targetType.Name}' does not define optional part '{partName}'.",
                    document.FilePath,
                    template.Line,
                    template.Column,
                    options.StrictMode && isRequired));

                continue;
            }

            var actualPartType = _resolveTemplateNodeType(compilation, document, partNode);
            if (actualPartType is not null &&
                !_isTypeAssignableTo(actualPartType, expectedPartType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0503",
                    $"ControlTemplate part '{partName}' must be assignable to '{expectedPartType.Name}', but resolved to '{actualPartType.Name}'.",
                    document.FilePath,
                    partNode.Line,
                    partNode.Column,
                    options.StrictMode));
            }
        }
    }

    public void ValidateTemplateContentRootType(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        if (string.Equals(template.Kind, "ControlTemplate", StringComparison.Ordinal) &&
            options.StrictMode &&
            string.IsNullOrWhiteSpace(template.TargetType))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0501",
                "ControlTemplate requires TargetType in strict mode.",
                document.FilePath,
                template.Line,
                template.Column,
                options.StrictMode));
        }

        var templateNode = _templateObjectNodeSearchService.FindTemplateNode(document, template);
        var contentRoot = templateNode is null
            ? null
            : _templateObjectNodeSearchService.TryGetTemplateContentNode(templateNode);
        if (contentRoot is null)
        {
            return;
        }

        var actualRootType = _resolveTemplateNodeType(compilation, document, contentRoot);
        if (actualRootType is null)
        {
            return;
        }

        INamedTypeSymbol? expectedContractType = template.Kind switch
        {
            "ControlTemplate" => _resolveContractType(compilation, TypeContractId.AvaloniaControl),
            "ItemsPanelTemplate" => _resolveContractType(compilation, TypeContractId.AvaloniaPanel),
            "DataTemplate" => _resolveContractType(compilation, TypeContractId.AvaloniaControl),
            "TreeDataTemplate" => _resolveContractType(compilation, TypeContractId.AvaloniaControl),
            _ => null
        };

        if (expectedContractType is null ||
            _isTypeAssignableTo(actualRootType, expectedContractType))
        {
            return;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0506",
            $"{template.Kind} content root must be assignable to '{expectedContractType.Name}', but resolved to '{actualRootType.Name}'.",
            document.FilePath,
            contentRoot.Line,
            contentRoot.Column,
            options.StrictMode));
    }

    private IEnumerable<XamlObjectNode> EnumerateTemplateNodes(XamlObjectNode contentRoot)
    {
        yield return contentRoot;

        foreach (var descendant in _templateObjectNodeSearchService.EnumerateTemplateDescendants(contentRoot))
        {
            yield return descendant;
        }
    }

    private static bool IsTemplatePartAttribute(AttributeData attribute)
    {
        var attributeName = attribute.AttributeClass?.Name;
        return string.Equals(attributeName, "TemplatePartAttribute", StringComparison.Ordinal) ||
               string.Equals(attributeName, "TemplatePart", StringComparison.Ordinal);
    }

    private static string? ReadTemplatePartName(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (string.Equals(namedArgument.Key, "Name", StringComparison.Ordinal))
            {
                return namedArgument.Value.Value as string;
            }
        }

        return attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;
    }

    private static ITypeSymbol? ReadTemplatePartType(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (string.Equals(namedArgument.Key, "Type", StringComparison.Ordinal))
            {
                return namedArgument.Value.Value as ITypeSymbol;
            }
        }

        return attribute.ConstructorArguments.Length > 1
            ? attribute.ConstructorArguments[1].Value as ITypeSymbol
            : null;
    }

    private static bool ReadTemplatePartRequired(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (string.Equals(namedArgument.Key, "IsRequired", StringComparison.Ordinal) &&
                namedArgument.Value.Value is bool isRequired)
            {
                return isRequired;
            }
        }

        return false;
    }
}
