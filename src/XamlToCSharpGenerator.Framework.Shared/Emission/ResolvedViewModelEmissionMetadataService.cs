using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ResolvedViewModelEmissionMetadataService
{
    private static readonly MarkupExpressionParser MarkupExpressionParser = new();
    private readonly ImmutableHashSet<string> _knownBindingTypeNames;
    private readonly ImmutableHashSet<string> _knownBindingOptionTypeNames;
    private readonly Func<string, string> _escape;

    public ResolvedViewModelEmissionMetadataService(
        ImmutableArray<string> knownBindingTypeNames,
        ImmutableArray<string> knownBindingOptionTypeNames,
        Func<string, string> escape)
    {
        _knownBindingTypeNames = knownBindingTypeNames.ToImmutableHashSet(StringComparer.Ordinal);
        _knownBindingOptionTypeNames = knownBindingOptionTypeNames.ToImmutableHashSet(StringComparer.Ordinal);
        _escape = escape;
    }

    public IReadOnlyDictionary<string, string> BuildNamedFieldMap(
        ResolvedViewModel viewModel,
        Func<string, string> sanitizeIdentifier)
    {
        if (viewModel.NamedElements.IsDefaultOrEmpty)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var map = new Dictionary<string, string>(viewModel.NamedElements.Length, StringComparer.Ordinal);
        foreach (var namedElement in viewModel.NamedElements)
        {
            map[namedElement.Name] = sanitizeIdentifier(namedElement.Name);
        }

        return map;
    }

    public ImmutableArray<string> CollectKnownTypeNames(ResolvedViewModel viewModel)
    {
        var orderedTypes = new SortedSet<string>(StringComparer.Ordinal);
        CollectKnownTypeNames(viewModel.RootObject, orderedTypes);
        CollectKnownTypeNames(viewModel.Document.RootObject, viewModel.Document, orderedTypes);
        CollectKnownDescriptorTypeNames(viewModel, orderedTypes);
        foreach (var knownBindingTypeName in _knownBindingTypeNames)
        {
            orderedTypes.Add(NormalizeKnownTypeName(knownBindingTypeName));
        }

        foreach (var knownBindingOptionTypeName in _knownBindingOptionTypeNames)
        {
            orderedTypes.Add(NormalizeKnownTypeName(knownBindingOptionTypeName));
        }

        return orderedTypes.ToImmutableArray();
    }

    private static void CollectKnownDescriptorTypeNames(
        ResolvedViewModel viewModel,
        ISet<string> knownTypeNames)
    {
        foreach (var template in viewModel.Templates)
        {
            AddKnownRuntimeTypeName(knownTypeNames, template.TargetTypeName);
            AddKnownRuntimeTypeName(knownTypeNames, template.DataType);
        }

        foreach (var style in viewModel.Styles)
        {
            AddKnownRuntimeTypeName(knownTypeNames, style.TargetTypeName);
        }

        foreach (var controlTheme in viewModel.ControlThemes)
        {
            AddKnownRuntimeTypeName(knownTypeNames, controlTheme.TargetTypeName);
        }
    }

    public string BuildBindingXmlNamespaceMapExpression(ImmutableDictionary<string, string> xmlNamespaces)
    {
        if (xmlNamespaces.IsEmpty)
        {
            return "new global::System.Collections.Generic.Dictionary<string, string>()";
        }

        var orderedNamespaces = xmlNamespaces.OrderBy(static pair => pair.Key, StringComparer.Ordinal);
        var builder = new StringBuilder("new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal) { ");
        var first = true;
        foreach (var xmlNamespace in orderedNamespaces)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append("{ \"");
            builder.Append(_escape(xmlNamespace.Key));
            builder.Append("\", \"");
            builder.Append(_escape(xmlNamespace.Value));
            builder.Append("\" }");
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static void CollectKnownTypeNames(ResolvedObjectNode node, ISet<string> knownTypeNames)
    {
        if (!string.IsNullOrWhiteSpace(node.TypeName))
        {
            knownTypeNames.Add(node.TypeName);
        }

        for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
        {
            CollectKnownTypeNames(node.Children[childIndex], knownTypeNames);
        }

        for (var assignmentIndex = 0; assignmentIndex < node.PropertyElementAssignments.Length; assignmentIndex++)
        {
            var propertyElementAssignment = node.PropertyElementAssignments[assignmentIndex];
            for (var valueIndex = 0; valueIndex < propertyElementAssignment.ObjectValues.Length; valueIndex++)
            {
                CollectKnownTypeNames(propertyElementAssignment.ObjectValues[valueIndex], knownTypeNames);
            }
        }
    }

    private static void CollectKnownTypeNames(
        XamlObjectNode node,
        XamlDocumentModel document,
        ISet<string> knownTypeNames)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            CollectKnownTypeNamesFromValue(assignment.Value, document, knownTypeNames);
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            if (!string.IsNullOrWhiteSpace(propertyElement.TextContent))
            {
                CollectKnownTypeNamesFromValue(propertyElement.TextContent!, document, knownTypeNames);
            }

            foreach (var objectValue in propertyElement.ObjectValues)
            {
                CollectKnownTypeNames(objectValue, document, knownTypeNames);
            }
        }

        foreach (var child in node.ChildObjects)
        {
            CollectKnownTypeNames(child, document, knownTypeNames);
        }
    }

    private static void CollectKnownTypeNamesFromValue(
        string value,
        XamlDocumentModel document,
        ISet<string> knownTypeNames)
    {
        if (!BindingEventMarkupParser.TryParseBindingMarkup(value, MarkupExpressionParser.TryParseMarkupExtension, out var bindingMarkup))
        {
            return;
        }

        foreach (var token in XamlRuntimeBindingPathSemantics.CollectTypeReferenceTokens(bindingMarkup.Path))
        {
            if (TryNormalizeDocumentTypeReferenceToken(document, token, out var typeName))
            {
                knownTypeNames.Add(typeName);
            }
        }
    }

    private static bool TryNormalizeDocumentTypeReferenceToken(
        XamlDocumentModel document,
        string token,
        out string typeName)
    {
        typeName = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = XamlTypeTokenSemantics.TrimGlobalQualifier(token).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex > 0)
        {
            var prefix = trimmed.Substring(0, separatorIndex);
            var localName = trimmed.Substring(separatorIndex + 1);
            if (!document.XmlNamespaces.TryGetValue(prefix, out var xmlNamespace) ||
                !xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal))
            {
                return false;
            }

            var namespaceValue = xmlNamespace.Substring("clr-namespace:".Length);
            var assemblySeparatorIndex = namespaceValue.IndexOf(';');
            if (assemblySeparatorIndex >= 0)
            {
                namespaceValue = namespaceValue.Substring(0, assemblySeparatorIndex);
            }

            if (string.IsNullOrWhiteSpace(namespaceValue) || string.IsNullOrWhiteSpace(localName))
            {
                return false;
            }

            typeName = namespaceValue + "." + localName;
            return true;
        }

        if (trimmed.IndexOf('.') > 0)
        {
            typeName = trimmed;
            return true;
        }

        return false;
    }

    private static string NormalizeKnownTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        return typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName
            : "global::Avalonia.Data." + typeName;
    }

    private static void AddKnownRuntimeTypeName(ISet<string> knownTypeNames, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return;
        }

        var trimmed = typeName.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        knownTypeNames.Add(
            trimmed.StartsWith("global::", StringComparison.Ordinal)
                ? trimmed
                : trimmed.IndexOf('.') >= 0
                    ? "global::" + trimmed
                    : trimmed);
    }
}
