using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class TemplateObjectNodeSearchService
{
    private readonly Func<string, string> _normalizePropertyName;
    private readonly ImmutableHashSet<string> _knownTemplateKinds;

    public TemplateObjectNodeSearchService(
        Func<string, string> normalizePropertyName,
        ImmutableHashSet<string> knownTemplateKinds)
    {
        _normalizePropertyName = normalizePropertyName ?? throw new ArgumentNullException(nameof(normalizePropertyName));
        _knownTemplateKinds = knownTemplateKinds;
    }

    public string NormalizePropertyName(string propertyName) => _normalizePropertyName(propertyName);
    public bool IsKnownTemplateKind(string xmlTypeName) => _knownTemplateKinds.Contains(xmlTypeName);

    public XamlObjectNode? FindTemplateNode(
        XamlDocumentModel document,
        XamlTemplateDefinition template)
    {
        return FindTemplateNode(document.RootObject, template);
    }

    public XamlObjectNode? FindTemplateNode(
        XamlObjectNode rootNode,
        XamlTemplateDefinition template)
    {
        if (IsTemplateMatch(rootNode, template))
        {
            return rootNode;
        }

        foreach (var child in EnumerateNestedObjectNodes(rootNode))
        {
            var match = FindTemplateNode(child, template);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public XamlObjectNode? TryGetTemplateContentNode(XamlObjectNode templateNode)
    {
        if (templateNode.ChildObjects.Length > 0)
        {
            return templateNode.ChildObjects[0];
        }

        foreach (var propertyElement in templateNode.PropertyElements)
        {
            if (propertyElement.ObjectValues.Length > 0)
            {
                return propertyElement.ObjectValues[0];
            }
        }

        return null;
    }

    public IEnumerable<XamlObjectNode> EnumerateTemplateDescendants(XamlObjectNode rootNode)
    {
        foreach (var child in EnumerateNestedObjectNodes(rootNode))
        {
            yield return child;

            if (IsKnownTemplateKind(child.XmlTypeName))
            {
                continue;
            }

            foreach (var nested in EnumerateTemplateDescendants(child))
            {
                yield return nested;
            }
        }
    }

    private bool IsTemplateMatch(XamlObjectNode node, XamlTemplateDefinition template)
    {
        return IsKnownTemplateKind(node.XmlTypeName) &&
               string.Equals(node.XmlTypeName, template.Kind, StringComparison.Ordinal) &&
               node.Line == template.Line &&
               node.Column == template.Column;
    }

    private static IEnumerable<XamlObjectNode> EnumerateNestedObjectNodes(XamlObjectNode node)
    {
        foreach (var child in node.ChildObjects)
        {
            yield return child;
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var value in propertyElement.ObjectValues)
            {
                yield return value;
            }
        }
    }
}
