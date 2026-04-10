using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class BindingObjectNodeMarkupParser
{
    private readonly string _xaml2006Namespace;
    private readonly Func<string, string> _normalizePropertyName;
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;

    public BindingObjectNodeMarkupParser(
        string xaml2006Namespace,
        Func<string, string> normalizePropertyName,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension)
    {
        _xaml2006Namespace = xaml2006Namespace ?? throw new ArgumentNullException(nameof(xaml2006Namespace));
        _normalizePropertyName = normalizePropertyName ?? throw new ArgumentNullException(nameof(normalizePropertyName));
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
    }

    public bool TryParseBindingMarkupFromObjectNode(XamlObjectNode node, out BindingMarkup bindingMarkup)
    {
        bindingMarkup = default;
        if (!IsBindingObjectNode(node))
        {
            return false;
        }

        var assignments = BuildAssignmentMap(node);
        var isCompiledBinding = string.Equals(node.XmlTypeName, "CompiledBinding", StringComparison.Ordinal);
        var path = GetBindingPath(node, assignments);

        RelativeSourceMarkup? relativeSource = null;
        if (TryGetNamedValue(assignments, "RelativeSource", out var relativeSourceValue) &&
            BindingEventMarkupParser.TryParseRelativeSourceMarkup(relativeSourceValue, _tryParseMarkupExtension, out var parsedRelativeSource))
        {
            relativeSource = parsedRelativeSource;
        }

        bindingMarkup = new BindingMarkup(
            isCompiledBinding,
            path,
            TryGetOptionalValue(assignments, "Mode"),
            TryGetOptionalValue(assignments, "ElementName"),
            relativeSource,
            TryGetOptionalValue(assignments, "Source"),
            TryGetOptionalValue(assignments, "DataType"),
            TryGetOptionalValue(assignments, "Converter"),
            TryGetOptionalValue(assignments, "ConverterCulture"),
            TryGetOptionalValue(assignments, "ConverterParameter"),
            TryGetOptionalValue(assignments, "StringFormat"),
            TryGetOptionalValue(assignments, "FallbackValue"),
            TryGetOptionalValue(assignments, "TargetNullValue"),
            TryGetOptionalValue(assignments, "Delay"),
            TryGetOptionalValue(assignments, "Priority"),
            TryGetOptionalValue(assignments, "UpdateSourceTrigger"),
            hasSourceConflict: false,
            sourceConflictMessage: null);
        return true;
    }

    public bool TryGetSingleBindingObjectNodeArgumentValue(
        XamlPropertyElement propertyElement,
        bool allowTypeExpressionExtraction,
        out string value)
    {
        value = string.Empty;
        if (propertyElement.ObjectValues.Length == 1)
        {
            var objectValue = propertyElement.ObjectValues[0];
            if (allowTypeExpressionExtraction &&
                TryExtractTypeExpressionFromXamlTypeNode(objectValue, out var typeExpression))
            {
                value = typeExpression;
                return true;
            }

            if (TryGetSingleMarkupExtensionArgumentValue(objectValue, out value))
            {
                return true;
            }
        }

        var rawTextContent = propertyElement.RawTextContent?.Trim();
        if (!string.IsNullOrWhiteSpace(rawTextContent))
        {
            value = rawTextContent!;
            return true;
        }

        var textContent = propertyElement.TextContent?.Trim();
        if (!string.IsNullOrWhiteSpace(textContent))
        {
            value = textContent!;
            return true;
        }

        return false;
    }

    public bool TryExtractTypeExpressionFromXamlTypeNode(XamlObjectNode node, out string typeExpression)
    {
        typeExpression = string.Empty;
        if (!IsXamlTypeNode(node))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            typeExpression = node.TextContent!.Trim();
            return typeExpression.Length > 0;
        }

        foreach (var assignment in node.PropertyAssignments)
        {
            if (string.Equals(_normalizePropertyName(assignment.PropertyName), "TypeName", StringComparison.Ordinal) ||
                string.Equals(_normalizePropertyName(assignment.PropertyName), "Type", StringComparison.Ordinal))
            {
                typeExpression = assignment.Value.Trim();
                return typeExpression.Length > 0;
            }
        }

        return false;
    }

    public string? GetCanonicalBindingObjectNodeArgumentName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var normalizedName = _normalizePropertyName(propertyName);
        return normalizedName switch
        {
            "Path" => "Path",
            "Mode" => "Mode",
            "ElementName" => "ElementName",
            "Source" => "Source",
            "RelativeSource" => "RelativeSource",
            "DataType" => "DataType",
            "Converter" => "Converter",
            "ConverterCulture" => "ConverterCulture",
            "ConverterParameter" => "ConverterParameter",
            "StringFormat" => "StringFormat",
            "FallbackValue" => "FallbackValue",
            "TargetNullValue" => "TargetNullValue",
            "Delay" => "Delay",
            "Priority" => "Priority",
            "UpdateSourceTrigger" => "UpdateSourceTrigger",
            "BindBack" => "BindBack",
            _ => null
        };
    }

    public bool TryGetSingleMarkupExtensionArgumentValue(XamlObjectNode node, out string value)
    {
        value = string.Empty;
        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            value = node.TextContent!.Trim();
            return value.Length > 0;
        }

        if (node.ConstructorArguments.Length == 1 &&
            !string.IsNullOrWhiteSpace(node.ConstructorArguments[0].TextContent))
        {
            value = node.ConstructorArguments[0].TextContent!.Trim();
            return value.Length > 0;
        }

        if (node.PropertyAssignments.Length == 1)
        {
            value = node.PropertyAssignments[0].Value.Trim();
            return value.Length > 0;
        }

        return false;
    }

    public bool IsXamlArrayNode(XamlObjectNode node)
    {
        return string.Equals(node.XmlNamespace, _xaml2006Namespace, StringComparison.Ordinal) &&
               string.Equals(node.XmlTypeName, "Array", StringComparison.Ordinal);
    }

    public bool IsXamlTypeNode(XamlObjectNode node)
    {
        return string.Equals(node.XmlNamespace, _xaml2006Namespace, StringComparison.Ordinal) &&
               string.Equals(node.XmlTypeName, "Type", StringComparison.Ordinal);
    }

    private static bool IsBindingObjectNode(XamlObjectNode node)
    {
        return node.XmlTypeName is "Binding" or "CompiledBinding" or "ReflectionBinding";
    }

    private ImmutableDictionary<string, string> BuildAssignmentMap(XamlObjectNode node)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var assignment in node.PropertyAssignments)
        {
            var canonicalName = GetCanonicalBindingObjectNodeArgumentName(assignment.PropertyName);
            if (canonicalName is not null)
            {
                builder[canonicalName] = assignment.Value;
            }
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            var canonicalName = GetCanonicalBindingObjectNodeArgumentName(propertyElement.PropertyName);
            if (canonicalName is null ||
                !TryGetSingleBindingObjectNodeArgumentValue(
                    propertyElement,
                    allowTypeExpressionExtraction: string.Equals(canonicalName, "DataType", StringComparison.Ordinal),
                    out var value))
            {
                continue;
            }

            builder[canonicalName] = value;
        }

        return builder.ToImmutable();
    }

    private static string GetBindingPath(XamlObjectNode node, ImmutableDictionary<string, string> assignments)
    {
        if (TryGetNamedValue(assignments, "Path", out var path))
        {
            return path;
        }

        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            return node.TextContent!.Trim();
        }

        if (node.ConstructorArguments.Length == 1 &&
            !string.IsNullOrWhiteSpace(node.ConstructorArguments[0].TextContent))
        {
            return node.ConstructorArguments[0].TextContent!.Trim();
        }

        return ".";
    }

    private static bool TryGetNamedValue(
        ImmutableDictionary<string, string> assignments,
        string name,
        out string value)
    {
        if (assignments.TryGetValue(name, out var resolved))
        {
            value = resolved;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? TryGetOptionalValue(
        ImmutableDictionary<string, string> assignments,
        string name)
    {
        return assignments.TryGetValue(name, out var resolved)
            ? resolved
            : null;
    }
}
