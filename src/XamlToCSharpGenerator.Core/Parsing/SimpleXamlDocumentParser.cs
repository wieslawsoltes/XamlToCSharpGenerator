using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Parsing;

public sealed class SimpleXamlDocumentParser : IXamlDocumentParser
{
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly ImmutableHashSet<string> DefaultIgnoredNamespaces = ImmutableHashSet.Create(StringComparer.Ordinal,
        MarkupCompatibility.NamespaceName,
        "http://schemas.microsoft.com/expression/blend/2008");

    public (XamlDocumentModel? Document, ImmutableArray<DiagnosticInfo> Diagnostics) Parse(XamlFileInput input)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        try
        {
            var document = XDocument.Parse(input.Text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0001",
                    "XAML document has no root element.",
                    input.FilePath,
                    1,
                    1,
                    true));
                return (null, diagnostics.ToImmutable());
            }

            var classAttribute = root.Attributes()
                .FirstOrDefault(attribute => attribute.Name == Xaml2006 + "Class");

            string? classFullName = null;
            if (classAttribute is null || string.IsNullOrWhiteSpace(classAttribute.Value) || !classAttribute.Value.Contains('.'))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0002",
                    $"File '{input.FilePath}' is missing x:Class. Emitting classless source-generated artifact.",
                    input.FilePath,
                    1,
                    1,
                    false));
            }
            else
            {
                classFullName = classAttribute.Value;
            }

            var classModifierAttribute = TryGetDirectiveAttribute(root, "ClassModifier");
            var classModifier = classModifierAttribute?.Value;

            var precompileAttribute = TryGetDirectiveAttribute(root, "Precompile");
            bool? precompile = null;
            if (precompileAttribute is null)
            {
                precompile = null;
            }
            else if (bool.TryParse(precompileAttribute.Value, out var parsedPrecompile))
            {
                precompile = parsedPrecompile;
            }
            else
            {
                var lineInfo = (IXmlLineInfo)precompileAttribute;
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0003",
                    "x:Precompile value must be either 'True' or 'False'.",
                    input.FilePath,
                    lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                    lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                    false));
            }

            var xmlNamespaces = CollectNamespaceMappings(root);
            var ignoredNamespaces = CollectIgnoredNamespaces(root, xmlNamespaces);
            var rootObject = ParseObjectNode(root, ignoredNamespaces);
            var namedElements = CollectNamedElements(rootObject);
            var resources = CollectResources(root, ignoredNamespaces);
            var templates = CollectTemplates(root, ignoredNamespaces);
            var styles = CollectStyles(root, ignoredNamespaces);
            var controlThemes = CollectControlThemes(root, ignoredNamespaces);
            var includes = CollectIncludes(root, ignoredNamespaces);

            var model = new XamlDocumentModel(
                FilePath: input.FilePath,
                TargetPath: NormalizeTargetPath(input.TargetPath),
                ClassFullName: classFullName,
                ClassModifier: classModifier,
                Precompile: precompile,
                XmlNamespaces: xmlNamespaces,
                RootObject: rootObject,
                NamedElements: namedElements,
                Resources: resources,
                Templates: templates,
                Styles: styles,
                ControlThemes: controlThemes,
                Includes: includes,
                IsValid: true);

            return (model, diagnostics.ToImmutable());
        }
        catch (XmlException ex)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0001",
                ex.Message,
                input.FilePath,
                Math.Max(1, ex.LineNumber),
                Math.Max(1, ex.LinePosition),
                true));
            return (null, diagnostics.ToImmutable());
        }
    }

    private static XamlObjectNode ParseObjectNode(XElement element, ImmutableHashSet<string> ignoredNamespaces)
    {
        var propertyAssignments = ImmutableArray.CreateBuilder<XamlPropertyAssignment>();
        var childObjects = ImmutableArray.CreateBuilder<XamlObjectNode>();
        var propertyElements = ImmutableArray.CreateBuilder<XamlPropertyElement>();
        var constructorArguments = ImmutableArray.CreateBuilder<XamlObjectNode>();

        var key = TryGetDirectiveValue(element, "Key");
        var name = TryGetName(element);
        var fieldModifier = TryGetFieldModifier(element);
        var dataType = TryGetDirectiveValue(element, "DataType");
        var compileBindings = TryGetBoolDirectiveValue(element, "CompileBindings");
        var factoryMethod = TryGetDirectiveValue(element, "FactoryMethod");
        var typeArguments = ParseTypeArguments(TryGetDirectiveValue(element, "TypeArguments"));
        var arrayItemType = TryGetArrayItemType(element);
        var textContent = TryGetInlineTextContent(element);

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsIgnoredDirective(attribute) || ShouldIgnoreAttribute(attribute, ignoredNamespaces))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)attribute;
            var propertyName = attribute.Name.LocalName;
            propertyAssignments.Add(new XamlPropertyAssignment(
                PropertyName: propertyName,
                XmlNamespace: attribute.Name.NamespaceName,
                Value: attribute.Value,
                IsAttached: propertyName.IndexOf('.') >= 0,
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        foreach (var child in element.Elements())
        {
            if (IsXamlDirectiveElement(child, "Arguments"))
            {
                foreach (var objectValue in child.Elements())
                {
                    if (ShouldIgnoreElement(objectValue, ignoredNamespaces))
                    {
                        continue;
                    }

                    constructorArguments.Add(ParseObjectNode(objectValue, ignoredNamespaces));
                }

                continue;
            }

            if (IsPropertyElement(child))
            {
                var objectValues = ImmutableArray.CreateBuilder<XamlObjectNode>();
                foreach (var objectValue in child.Elements())
                {
                    if (ShouldIgnoreElement(objectValue, ignoredNamespaces))
                    {
                        continue;
                    }

                    objectValues.Add(ParseObjectNode(objectValue, ignoredNamespaces));
                }

                var lineInfo = (IXmlLineInfo)child;
                propertyElements.Add(new XamlPropertyElement(
                    PropertyName: ExtractPropertyElementName(child.Name.LocalName),
                    ObjectValues: objectValues.ToImmutable(),
                    Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                    Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
                continue;
            }

            if (ShouldIgnoreElement(child, ignoredNamespaces))
            {
                continue;
            }

            childObjects.Add(ParseObjectNode(child, ignoredNamespaces));
        }

        var elementLineInfo = (IXmlLineInfo)element;
        return new XamlObjectNode(
            XmlNamespace: element.Name.NamespaceName,
            XmlTypeName: element.Name.LocalName,
            Key: key,
            Name: name,
            FieldModifier: fieldModifier,
            DataType: dataType,
            CompileBindings: compileBindings,
            FactoryMethod: factoryMethod,
            TypeArguments: typeArguments,
            ArrayItemType: arrayItemType,
            ConstructorArguments: constructorArguments.ToImmutable(),
            TextContent: textContent,
            PropertyAssignments: propertyAssignments.ToImmutable(),
            ChildObjects: childObjects.ToImmutable(),
            PropertyElements: propertyElements.ToImmutable(),
            Line: elementLineInfo.HasLineInfo() ? elementLineInfo.LineNumber : 1,
            Column: elementLineInfo.HasLineInfo() ? elementLineInfo.LinePosition : 1);
    }

    private static string? TryGetInlineTextContent(XElement element)
    {
        var inlineTextFragments = element.Nodes()
            .OfType<XText>()
            .Select(static node => node.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        if (inlineTextFragments.Length == 0)
        {
            return null;
        }

        return string.Join(" ", inlineTextFragments);
    }

    private static ImmutableDictionary<string, string> CollectNamespaceMappings(XElement root)
    {
        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (var attribute in root.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var prefix = attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName;
            map[prefix] = attribute.Value;
        }

        return map.ToImmutable();
    }

    private static ImmutableHashSet<string> CollectIgnoredNamespaces(
        XElement root,
        ImmutableDictionary<string, string> xmlNamespaces)
    {
        var ignoredNamespaces = DefaultIgnoredNamespaces.ToBuilder();

        var ignorablePrefixesValue = root.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.Namespace == MarkupCompatibility &&
                attribute.Name.LocalName == "Ignorable")
            ?.Value;

        if (!string.IsNullOrWhiteSpace(ignorablePrefixesValue))
        {
            var prefixes = ignorablePrefixesValue!
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            foreach (var prefix in prefixes)
            {
                if (xmlNamespaces.TryGetValue(prefix, out var namespaceUri) &&
                    !string.IsNullOrWhiteSpace(namespaceUri))
                {
                    ignoredNamespaces.Add(namespaceUri);
                }
            }
        }

        return ignoredNamespaces.ToImmutable();
    }

    private static ImmutableArray<XamlNamedElement> CollectNamedElements(XamlObjectNode root)
    {
        var items = new List<XamlNamedElement>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        Traverse(root);
        return items.ToImmutableArray();

        void Traverse(XamlObjectNode node)
        {
            var name = node.Name;
            if (name is not null && !string.IsNullOrWhiteSpace(name) && seenNames.Add(name))
            {
                items.Add(new XamlNamedElement(
                    Name: name,
                    XmlNamespace: node.XmlNamespace,
                    XmlTypeName: node.XmlTypeName,
                    FieldModifier: node.FieldModifier,
                    Line: node.Line,
                    Column: node.Column));
            }

            foreach (var child in node.ChildObjects)
            {
                Traverse(child);
            }

            foreach (var constructorArgument in node.ConstructorArguments)
            {
                Traverse(constructorArgument);
            }

            foreach (var propertyElement in node.PropertyElements)
            {
                foreach (var objectValue in propertyElement.ObjectValues)
                {
                    Traverse(objectValue);
                }
            }
        }
    }

    private static ImmutableArray<XamlResourceDefinition> CollectResources(XElement root, ImmutableHashSet<string> ignoredNamespaces)
    {
        var resources = ImmutableArray.CreateBuilder<XamlResourceDefinition>();

        foreach (var element in root.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var key = TryGetDirectiveValue(element, "Key");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)element;
            resources.Add(new XamlResourceDefinition(
                Key: key!,
                XmlNamespace: element.Name.NamespaceName,
                XmlTypeName: element.Name.LocalName,
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return resources.ToImmutable();
    }

    private static ImmutableArray<XamlTemplateDefinition> CollectTemplates(XElement root, ImmutableHashSet<string> ignoredNamespaces)
    {
        var templates = ImmutableArray.CreateBuilder<XamlTemplateDefinition>();

        foreach (var element in root.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            if (!IsTemplateElement(element.Name.LocalName))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)element;
            templates.Add(new XamlTemplateDefinition(
                Kind: element.Name.LocalName,
                Key: TryGetDirectiveValue(element, "Key"),
                TargetType: element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "TargetType")?.Value,
                DataType: TryGetDirectiveValue(element, "DataType"),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return templates.ToImmutable();
    }

    private static ImmutableArray<XamlStyleDefinition> CollectStyles(XElement root, ImmutableHashSet<string> ignoredNamespaces)
    {
        var styles = ImmutableArray.CreateBuilder<XamlStyleDefinition>();

        foreach (var element in root.DescendantsAndSelf().Where(x => x.Name.LocalName == "Style"))
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var selectorAttribute = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Selector");
            var selector = selectorAttribute?.Value;

            var lineInfo = (IXmlLineInfo)element;
            var selectorLineInfo = selectorAttribute is null
                ? lineInfo
                : (IXmlLineInfo)selectorAttribute;
            styles.Add(new XamlStyleDefinition(
                Key: TryGetDirectiveValue(element, "Key"),
                Selector: selector ?? string.Empty,
                SelectorLine: selectorLineInfo.HasLineInfo() ? selectorLineInfo.LineNumber : (lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1),
                SelectorColumn: selectorLineInfo.HasLineInfo() ? selectorLineInfo.LinePosition : (lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1),
                DataType: TryGetDirectiveValue(element, "DataType"),
                CompileBindings: TryGetBoolDirectiveValue(element, "CompileBindings"),
                Setters: CollectSetters(element),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return styles.ToImmutable();
    }

    private static ImmutableArray<XamlControlThemeDefinition> CollectControlThemes(XElement root, ImmutableHashSet<string> ignoredNamespaces)
    {
        var themes = ImmutableArray.CreateBuilder<XamlControlThemeDefinition>();

        foreach (var element in root.DescendantsAndSelf().Where(x => x.Name.LocalName == "ControlTheme"))
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var targetType = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "TargetType")?.Value;
            var basedOn = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "BasedOn")?.Value;
            var themeVariant = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "ThemeVariant")?.Value;

            var lineInfo = (IXmlLineInfo)element;
            themes.Add(new XamlControlThemeDefinition(
                Key: TryGetDirectiveValue(element, "Key"),
                TargetType: targetType,
                BasedOn: basedOn,
                ThemeVariant: themeVariant,
                DataType: TryGetDirectiveValue(element, "DataType"),
                CompileBindings: TryGetBoolDirectiveValue(element, "CompileBindings"),
                Setters: CollectSetters(element),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return themes.ToImmutable();
    }

    private static ImmutableArray<XamlSetterDefinition> CollectSetters(XElement scope)
    {
        var setters = ImmutableArray.CreateBuilder<XamlSetterDefinition>();

        foreach (var element in scope.Elements())
        {
            if (element.Name.LocalName == "Setter")
            {
                AddSetterDefinition(element, setters);
                continue;
            }

            if (IsSettersPropertyElement(scope, element))
            {
                foreach (var nestedSetter in element.Elements().Where(x => x.Name.LocalName == "Setter"))
                {
                    AddSetterDefinition(nestedSetter, setters);
                }
            }
        }

        return setters.ToImmutable();
    }

    private static bool IsSettersPropertyElement(XElement scope, XElement element)
    {
        return element.Name.LocalName.Equals(scope.Name.LocalName + ".Setters", StringComparison.Ordinal);
    }

    private static void AddSetterDefinition(XElement setter, ImmutableArray<XamlSetterDefinition>.Builder setters)
    {
        var propertyName = setter.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Property")?.Value;

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        var value = setter.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Value")?.Value;

        if (value is null)
        {
            value = setter.Elements().FirstOrDefault()?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
        }

        var lineInfo = (IXmlLineInfo)setter;
        setters.Add(new XamlSetterDefinition(
            PropertyName: propertyName!,
            Value: value,
            Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
            Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
    }

    private static ImmutableArray<XamlIncludeDefinition> CollectIncludes(XElement root, ImmutableHashSet<string> ignoredNamespaces)
    {
        var includes = ImmutableArray.CreateBuilder<XamlIncludeDefinition>();

        foreach (var element in root.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var kind = element.Name.LocalName;
            if (kind != "ResourceInclude" && kind != "StyleInclude" && kind != "MergeResourceInclude")
            {
                continue;
            }

            var source = element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Source")
                ?.Value;
            var sourceValue = string.IsNullOrWhiteSpace(source) ? string.Empty : source;

            var lineInfo = (IXmlLineInfo)element;
            includes.Add(new XamlIncludeDefinition(
                Kind: kind,
                Source: sourceValue!,
                MergeTarget: ResolveMergeTarget(element),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return includes.ToImmutable();
    }

    private static string ResolveMergeTarget(XElement includeElement)
    {
        XElement? current = includeElement;
        while (current is not null)
        {
            var localName = current.Name.LocalName;
            if (localName.EndsWith(".MergedDictionaries", StringComparison.Ordinal) || localName == "MergedDictionaries")
            {
                return "MergedDictionaries";
            }

            if (localName.EndsWith(".Styles", StringComparison.Ordinal) || localName == "Styles")
            {
                return "Styles";
            }

            current = current.Parent;
        }

        return "Unknown";
    }

    private static bool IsPropertyElement(XElement element)
    {
        return element.Name.LocalName.IndexOf('.') >= 0;
    }

    private static string ExtractPropertyElementName(string localName)
    {
        var separatorIndex = localName.LastIndexOf('.');
        return separatorIndex < 0 ? localName : localName.Substring(separatorIndex + 1);
    }

    private static bool IsTemplateElement(string localName)
    {
        return localName == "DataTemplate"
               || localName == "ControlTemplate"
               || localName == "ItemsPanelTemplate"
               || localName == "TreeDataTemplate";
    }

    private static string NormalizeTargetPath(string targetPath)
    {
        var normalized = targetPath.Replace('\\', '/');
        return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized.Substring(1) : normalized;
    }

    private static string? TryGetName(XElement element)
    {
        var xName = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "Name");
        if (xName is not null)
        {
            return xName.Value;
        }

        var name = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Name");
        return name?.Value;
    }

    private static XAttribute? TryGetDirectiveAttribute(XElement element, string directiveName)
    {
        return element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == directiveName);
    }

    private static string? TryGetDirectiveValue(XElement element, string directiveName)
    {
        return TryGetDirectiveAttribute(element, directiveName)?.Value;
    }

    private static bool? TryGetBoolDirectiveValue(XElement element, string directiveName)
    {
        var value = TryGetDirectiveValue(element, directiveName);
        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value, out var result) ? result : null;
    }

    private static string? TryGetFieldModifier(XElement element)
    {
        var fieldModifier = TryGetDirectiveValue(element, "FieldModifier");
        if (fieldModifier is null)
        {
            return null;
        }

        return fieldModifier.ToLowerInvariant() switch
        {
            "private" => "private",
            "public" => "public",
            "protected" => "protected",
            "internal" => "internal",
            "notpublic" => "internal",
            _ => null,
        };
    }

    private static bool IsIgnoredDirective(XAttribute attribute)
    {
        if (attribute.Name.Namespace != Xaml2006)
        {
            return false;
        }

        return attribute.Name.LocalName == "Class"
               || attribute.Name.LocalName == "Name"
               || attribute.Name.LocalName == "FieldModifier"
               || attribute.Name.LocalName == "ClassModifier"
               || attribute.Name.LocalName == "Key"
               || attribute.Name.LocalName == "DataType"
               || attribute.Name.LocalName == "CompileBindings"
               || attribute.Name.LocalName == "Precompile"
               || attribute.Name.LocalName == "FactoryMethod"
               || attribute.Name.LocalName == "TypeArguments"
               || attribute.Name.LocalName == "Arguments"
               || attribute.Name.LocalName == "Type";
    }

    private static bool ShouldIgnoreAttribute(XAttribute attribute, ImmutableHashSet<string> ignoredNamespaces)
    {
        return ignoredNamespaces.Contains(attribute.Name.NamespaceName);
    }

    private static bool ShouldIgnoreElement(XElement element, ImmutableHashSet<string> ignoredNamespaces)
    {
        return ignoredNamespaces.Contains(element.Name.NamespaceName);
    }

    private static bool IsXamlDirectiveElement(XElement element, string localName)
    {
        return element.Name.Namespace == Xaml2006 &&
               element.Name.LocalName.Equals(localName, StringComparison.Ordinal);
    }

    private static ImmutableArray<string> ParseTypeArguments(string? rawTypeArguments)
    {
        if (string.IsNullOrWhiteSpace(rawTypeArguments))
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        var tokenStart = 0;
        var braceDepth = 0;
        var parenthesisDepth = 0;
        var angleDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';
        var text = rawTypeArguments!;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    break;
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0)
                    {
                        angleDepth--;
                    }

                    break;
                case ',' when braceDepth == 0 && parenthesisDepth == 0 && angleDepth == 0:
                {
                    var token = text.Substring(tokenStart, index - tokenStart).Trim();
                    if (token.Length > 0)
                    {
                        builder.Add(token);
                    }

                    tokenStart = index + 1;
                    break;
                }
            }
        }

        var tail = text.Substring(tokenStart).Trim();
        if (tail.Length > 0)
        {
            builder.Add(tail);
        }

        return builder.ToImmutable();
    }

    private static string? TryGetArrayItemType(XElement element)
    {
        if (!IsXamlDirectiveElement(element, "Array"))
        {
            return null;
        }

        var directiveType = TryGetDirectiveValue(element, "Type");
        if (!string.IsNullOrWhiteSpace(directiveType))
        {
            return directiveType;
        }

        var plainType = element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 &&
                attribute.Name.LocalName.Equals("Type", StringComparison.Ordinal))
            ?.Value;
        if (!string.IsNullOrWhiteSpace(plainType))
        {
            return plainType;
        }

        return null;
    }
}
