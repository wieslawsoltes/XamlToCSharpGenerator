using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XamlTypeNodeBindingService
{
    private readonly BindingObjectNodeMarkupParser _bindingObjectNodeMarkupParser;
    private readonly ObjectNodeKeyExpressionService _objectNodeKeyExpressionService;
    private readonly Func<string?, string?> _normalizeObjectNodeName;
    private readonly Func<Compilation, XamlDocumentModel, string, string, INamedTypeSymbol?> _resolveTypeToken;
    private readonly Func<Compilation, XamlDocumentModel, string, string, INamedTypeSymbol?> _resolveTypeSymbol;

    public XamlTypeNodeBindingService(
        BindingObjectNodeMarkupParser bindingObjectNodeMarkupParser,
        ObjectNodeKeyExpressionService objectNodeKeyExpressionService,
        Func<string?, string?> normalizeObjectNodeName,
        Func<Compilation, XamlDocumentModel, string, string, INamedTypeSymbol?> resolveTypeToken,
        Func<Compilation, XamlDocumentModel, string, string, INamedTypeSymbol?> resolveTypeSymbol)
    {
        _bindingObjectNodeMarkupParser = bindingObjectNodeMarkupParser ?? throw new ArgumentNullException(nameof(bindingObjectNodeMarkupParser));
        _objectNodeKeyExpressionService = objectNodeKeyExpressionService ?? throw new ArgumentNullException(nameof(objectNodeKeyExpressionService));
        _normalizeObjectNodeName = normalizeObjectNodeName ?? throw new ArgumentNullException(nameof(normalizeObjectNodeName));
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _resolveTypeSymbol = resolveTypeSymbol ?? throw new ArgumentNullException(nameof(resolveTypeSymbol));
    }

    public ResolvedObjectNode BindXamlTypeNode(
        XamlObjectNode node,
        Compilation compilation,
        XamlDocumentModel document,
        bool strictMode,
        Action<DiagnosticInfo> reportDiagnostic)
    {
        _ = strictMode;
        _ = reportDiagnostic;

        string? typeExpression = null;
        if (_bindingObjectNodeMarkupParser.TryExtractTypeExpressionFromXamlTypeNode(node, out var extractedTypeExpression))
        {
            typeExpression = extractedTypeExpression;
        }

        INamedTypeSymbol? resolvedType = null;
        if (!string.IsNullOrWhiteSpace(typeExpression))
        {
            resolvedType = _resolveTypeToken(compilation, document, typeExpression!, document.ClassNamespace) ??
                           _resolveTypeSymbol(compilation, document, document.RootObject.XmlNamespace, typeExpression!);
            if (resolvedType is null &&
                !node.TypeArguments.IsDefaultOrEmpty &&
                TryBuildGenericMetadataToken(typeExpression!, node.TypeArguments.Length, out var genericMetadataToken))
            {
                resolvedType = _resolveTypeToken(compilation, document, genericMetadataToken, document.ClassNamespace);
            }
        }

        if (resolvedType is not null &&
            !node.TypeArguments.IsDefaultOrEmpty)
        {
            var resolvedTypeArguments = ImmutableArray.CreateBuilder<ITypeSymbol>(node.TypeArguments.Length);
            foreach (var typeArgument in node.TypeArguments)
            {
                var resolvedTypeArgument = _resolveTypeToken(compilation, document, typeArgument, document.ClassNamespace);
                if (resolvedTypeArgument is null)
                {
                    resolvedTypeArguments.Clear();
                    break;
                }

                resolvedTypeArguments.Add(resolvedTypeArgument);
            }

            if (resolvedTypeArguments.Count == node.TypeArguments.Length)
            {
                if (resolvedType.TypeParameters.Length == resolvedTypeArguments.Count)
                {
                    resolvedType = resolvedType.Construct(resolvedTypeArguments.ToArray());
                }
                else if (resolvedType.OriginalDefinition.TypeParameters.Length == resolvedTypeArguments.Count)
                {
                    resolvedType = resolvedType.OriginalDefinition.Construct(resolvedTypeArguments.ToArray());
                }
            }
        }

        var factoryExpression = resolvedType is null
            ? "typeof(global::System.Object)"
            : "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";

        return new ResolvedObjectNode(
            KeyExpression: _objectNodeKeyExpressionService.BuildObjectNodeKeyExpression(node.Key, compilation, document),
            Name: _normalizeObjectNodeName(node.Name),
            TypeName: "global::System.Type",
            IsBindingObjectNode: false,
            FactoryExpression: factoryExpression,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition);
    }

    private static bool TryBuildGenericMetadataToken(
        string typeExpression,
        int typeArgumentCount,
        out string genericMetadataToken)
    {
        genericMetadataToken = string.Empty;
        if (string.IsNullOrWhiteSpace(typeExpression) || typeArgumentCount <= 0)
        {
            return false;
        }

        var trimmed = XamlTypeTokenSemantics.TrimGlobalQualifier(typeExpression).Trim();
        if (trimmed.Length == 0 || trimmed.IndexOf('`') >= 0)
        {
            return false;
        }

        genericMetadataToken = trimmed + "`" + typeArgumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}
