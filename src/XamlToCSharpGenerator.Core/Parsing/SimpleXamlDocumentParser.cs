using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
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

    private readonly ImmutableDictionary<string, string> _globalXmlNamespaces;
    private readonly bool _allowImplicitDefaultXmlns;
    private readonly string? _implicitDefaultXmlns;
    private readonly ImmutableArray<IXamlDocumentEnricher> _documentEnrichers;

    public SimpleXamlDocumentParser(
        ImmutableDictionary<string, string>? globalXmlNamespaces = null,
        bool allowImplicitDefaultXmlns = false,
        string? implicitDefaultXmlns = null,
        ImmutableArray<IXamlDocumentEnricher>? documentEnrichers = null)
    {
        _globalXmlNamespaces = globalXmlNamespaces ?? ImmutableDictionary<string, string>.Empty;
        _allowImplicitDefaultXmlns = allowImplicitDefaultXmlns;
        _implicitDefaultXmlns = implicitDefaultXmlns;
        _documentEnrichers = documentEnrichers ?? ImmutableArray<IXamlDocumentEnricher>.Empty;
    }

    public (XamlDocumentModel? Document, ImmutableArray<DiagnosticInfo> Diagnostics) Parse(XamlFileInput input)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        try
        {
            var document = LoadDocument(input.Text);
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
                if (ShouldReportMissingClassDirective(root))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0002",
                        $"File '{input.FilePath}' is missing x:Class. Emitting classless source-generated artifact.",
                        input.FilePath,
                        1,
                        1,
                        false));
                }
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

            var xmlNamespaces = CollectNamespaceMappings(
                root,
                diagnostics,
                input.FilePath,
                out var conditionalNamespacesByRawUri);
            var ignoredNamespaces = CollectIgnoredNamespaces(root, xmlNamespaces);
            var rootObject = ParseObjectNode(
                root,
                ignoredNamespaces,
                conditionalNamespacesByRawUri,
                diagnostics,
                input.FilePath);
            var namedElements = CollectNamedElements(rootObject);
            var model = new XamlDocumentModel(
                FilePath: input.FilePath,
                TargetPath: NormalizeTargetPath(input.TargetPath),
                ClassFullName: classFullName,
                ClassModifier: classModifier,
                Precompile: precompile,
                XmlNamespaces: xmlNamespaces,
                RootObject: rootObject,
                NamedElements: namedElements,
                Resources: ImmutableArray<XamlResourceDefinition>.Empty,
                Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
                Styles: ImmutableArray<XamlStyleDefinition>.Empty,
                ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
                Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
                IsValid: true);

            if (_documentEnrichers.Length > 0)
            {
                var parseContext = new XamlDocumentParseContext(
                    root,
                    ignoredNamespaces,
                    conditionalNamespacesByRawUri);
                foreach (var documentEnricher in _documentEnrichers)
                {
                    try
                    {
                        var (enrichedModel, enrichmentDiagnostics) = documentEnricher.Enrich(model, parseContext);
                        model = enrichedModel;
                        diagnostics.AddRange(enrichmentDiagnostics);
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0001",
                            $"XAML document enricher '{documentEnricher.GetType().FullName}' failed: {ex.Message}",
                            input.FilePath,
                            1,
                            1,
                            true));
                    }
                }
            }

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

    private static bool ShouldReportMissingClassDirective(XElement root)
    {
        var localName = root.Name.LocalName;
        return localName is not (
            "ResourceDictionary" or
            "Styles" or
            "Style" or
            "ControlTheme" or
            "DataTemplate" or
            "TreeDataTemplate");
    }

    private XamlObjectNode ParseObjectNode(
        XElement element,
        ImmutableHashSet<string> ignoredNamespaces,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string filePath)
    {
        var propertyAssignments = ImmutableArray.CreateBuilder<XamlPropertyAssignment>();
        var childObjects = ImmutableArray.CreateBuilder<XamlObjectNode>();
        var propertyElements = ImmutableArray.CreateBuilder<XamlPropertyElement>();
        var constructorArguments = ImmutableArray.CreateBuilder<XamlObjectNode>();
        var elementXmlNamespace = XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(element.Name.NamespaceName);
        var elementCondition = XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
            element.Name.NamespaceName,
            conditionalNamespacesByRawUri);
        var isArrayDirective = IsXamlDirectiveElement(element, "Array");
        string? key = null;
        bool? isShared = null;
        string? xName = null;
        string? plainName = null;
        string? fieldModifier = null;
        string? dataType = null;
        bool? compileBindings = null;
        string? factoryMethod = null;
        string? typeArgumentsValue = null;
        string? xamlArrayItemType = null;
        string? plainArrayItemType = null;
        var textContent = TryGetInlineTextContent(element);
        var rawTextContent = TryGetRawInlineTextContent(element);

        foreach (var attribute in element.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                if (attribute.Name.Namespace == Xaml2006)
                {
                    if (attribute.Name.LocalName == "Key")
                    {
                        key = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "Shared")
                    {
                        if (bool.TryParse(attribute.Value, out var parsedShared))
                        {
                            isShared = parsedShared;
                        }
                        else
                        {
                            ReportInvalidSharedDirective(attribute, diagnostics, filePath);
                        }
                    }
                    else if (attribute.Name.LocalName == "Name")
                    {
                        xName = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "FieldModifier")
                    {
                        fieldModifier = XamlAccessibilityModifierSemantics.NormalizeFieldModifier(attribute.Value);
                    }
                    else if (attribute.Name.LocalName == "DataType")
                    {
                        dataType = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "CompileBindings")
                    {
                        compileBindings = bool.TryParse(attribute.Value, out var parsedCompileBindings)
                            ? parsedCompileBindings
                            : (bool?)null;
                    }
                    else if (attribute.Name.LocalName == "FactoryMethod")
                    {
                        factoryMethod = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "TypeArguments")
                    {
                        typeArgumentsValue = attribute.Value;
                    }
                    else if (isArrayDirective && attribute.Name.LocalName == "Type")
                    {
                        xamlArrayItemType = attribute.Value;
                    }
                }
                else if (attribute.Name.NamespaceName.Length == 0)
                {
                    if (attribute.Name.LocalName == "Name")
                    {
                        plainName = attribute.Value;
                    }
                    else if (isArrayDirective &&
                             attribute.Name.LocalName.Equals("Type", StringComparison.Ordinal))
                    {
                        plainArrayItemType = attribute.Value;
                    }
                }
            }

            if (attribute.IsNamespaceDeclaration || IsIgnoredDirective(attribute) || ShouldIgnoreAttribute(attribute, ignoredNamespaces))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)attribute;
            var propertyName = attribute.Name.LocalName;
            var normalizedAttributeNamespace = XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(
                attribute.Name.NamespaceName);
            propertyAssignments.Add(new XamlPropertyAssignment(
                PropertyName: propertyName,
                XmlNamespace: normalizedAttributeNamespace,
                Value: attribute.Value,
                IsAttached: XamlPropertyElementSemantics.IsAttachedPropertyToken(propertyName),
                Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                    attribute.Name.NamespaceName,
                    conditionalNamespacesByRawUri),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        var name = xName ?? plainName;
        var typeArguments = XamlTypeArgumentListSemantics.Parse(typeArgumentsValue);
        var arrayItemType = !string.IsNullOrWhiteSpace(xamlArrayItemType)
            ? xamlArrayItemType
            : !string.IsNullOrWhiteSpace(plainArrayItemType)
                ? plainArrayItemType
                : null;

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

                    constructorArguments.Add(ParseObjectNode(
                        objectValue,
                        ignoredNamespaces,
                        conditionalNamespacesByRawUri,
                        diagnostics,
                        filePath));
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

                    objectValues.Add(ParseObjectNode(
                        objectValue,
                        ignoredNamespaces,
                        conditionalNamespacesByRawUri,
                        diagnostics,
                        filePath));
                }

                var lineInfo = (IXmlLineInfo)child;
                var propertyElementTextContent = TryGetInlineTextContent(child);
                var propertyElementRawTextContent = TryGetRawInlineTextContent(child);
                propertyElements.Add(new XamlPropertyElement(
                    PropertyName: ExtractPropertyElementName(child.Name.LocalName),
                    XmlNamespace: child.Name.NamespaceName,
                    ObjectValues: objectValues.ToImmutable(),
                    Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                        child.Name.NamespaceName,
                        conditionalNamespacesByRawUri),
                    Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                    Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                    TextContent: propertyElementTextContent,
                    RawTextContent: propertyElementRawTextContent));
                continue;
            }

            if (ShouldIgnoreElement(child, ignoredNamespaces))
            {
                continue;
            }

            childObjects.Add(ParseObjectNode(
                child,
                ignoredNamespaces,
                conditionalNamespacesByRawUri,
                diagnostics,
                filePath));
        }

        var elementLineInfo = (IXmlLineInfo)element;
        return new XamlObjectNode(
            XmlNamespace: elementXmlNamespace,
            XmlTypeName: element.Name.LocalName,
            Condition: elementCondition,
            Key: key,
            IsShared: isShared,
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
            Column: elementLineInfo.HasLineInfo() ? elementLineInfo.LinePosition : 1,
            RawTextContent: rawTextContent);
    }

    private static void ReportInvalidSharedDirective(
        XAttribute attribute,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string filePath)
    {
        var lineInfo = (IXmlLineInfo)attribute;
        diagnostics.Add(new DiagnosticInfo(
            "AXSG0004",
            "x:Shared value must be either 'True' or 'False'.",
            filePath,
            lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
            lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
            false));
    }

    private XDocument LoadDocument(string text)
    {
        if (_globalXmlNamespaces.IsEmpty &&
            (!_allowImplicitDefaultXmlns || string.IsNullOrWhiteSpace(_implicitDefaultXmlns)))
        {
            return XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }

        var namespaceManager = new XmlNamespaceManager(new NameTable());
        foreach (var mapping in _globalXmlNamespaces)
        {
            if (string.IsNullOrWhiteSpace(mapping.Value))
            {
                continue;
            }

            namespaceManager.AddNamespace(mapping.Key, mapping.Value);
        }

        if (_allowImplicitDefaultXmlns &&
            !string.IsNullOrWhiteSpace(_implicitDefaultXmlns) &&
            string.IsNullOrWhiteSpace(namespaceManager.DefaultNamespace))
        {
            namespaceManager.AddNamespace(string.Empty, _implicitDefaultXmlns!);
        }

        using var reader = XmlReader.Create(
            new StringReader(text),
            new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Document
            },
            new XmlParserContext(
                namespaceManager.NameTable,
                namespaceManager,
                null,
                XmlSpace.None));

        return XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
    }

    private string? TryGetInlineTextContent(XElement element)
    {
        StringBuilder? builder = null;

        foreach (var node in element.Nodes())
        {
            var textNode = node as XText;
            if (textNode is null)
            {
                continue;
            }

            var trimmedValue = textNode.Value.Trim();
            if (trimmedValue.Length == 0)
            {
                continue;
            }

            if (builder is null)
            {
                builder = new StringBuilder(trimmedValue.Length);
            }
            else if (builder!.Length > 0)
            {
                builder.Append(' ');
            }

            builder!.Append(trimmedValue);
        }

        return builder?.ToString();
    }

    private static string? TryGetRawInlineTextContent(XElement element)
    {
        StringBuilder? builder = null;

        foreach (var node in element.Nodes())
        {
            if (node is not XText textNode)
            {
                continue;
            }

            if (textNode.Value.Length == 0)
            {
                continue;
            }

            builder ??= new StringBuilder(textNode.Value.Length);
            builder.Append(textNode.Value);
        }

        return builder?.ToString();
    }

    private ImmutableDictionary<string, string> CollectNamespaceMappings(
        XElement root,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string filePath,
        out ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var conditionMap = ImmutableDictionary.CreateBuilder<string, ConditionalXamlExpression>(StringComparer.Ordinal);

        foreach (var globalNamespace in _globalXmlNamespaces)
        {
            if (string.IsNullOrWhiteSpace(globalNamespace.Value))
            {
                continue;
            }

            var line = 1;
            var column = 1;
            if (XamlConditionalNamespaceUtilities.TrySplitConditionalNamespaceUri(
                    globalNamespace.Value,
                    out var normalizedNamespace,
                    out var rawCondition))
            {
                if (TryParseConditionalExpression(rawCondition!, line, column, out var condition, out var errorMessage))
                {
                    conditionMap[globalNamespace.Value] = condition;
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0120",
                        $"Invalid conditional XAML namespace expression '{rawCondition}' for prefix '{globalNamespace.Key}': {errorMessage}",
                        filePath,
                        line,
                        column,
                        false));
                }

                map[globalNamespace.Key] = normalizedNamespace;
            }
            else
            {
                map[globalNamespace.Key] = globalNamespace.Value;
            }
        }

        if (_allowImplicitDefaultXmlns &&
            !string.IsNullOrWhiteSpace(_implicitDefaultXmlns) &&
            !map.ContainsKey(string.Empty))
        {
            if (XamlConditionalNamespaceUtilities.TrySplitConditionalNamespaceUri(
                    _implicitDefaultXmlns!,
                    out var normalizedDefaultXmlNamespace,
                    out var rawDefaultCondition))
            {
                if (TryParseConditionalExpression(rawDefaultCondition!, 1, 1, out var defaultCondition, out var errorMessage))
                {
                    conditionMap[_implicitDefaultXmlns!] = defaultCondition;
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0120",
                        $"Invalid conditional XAML namespace expression '{rawDefaultCondition}' for implicit default namespace: {errorMessage}",
                        filePath,
                        1,
                        1,
                        false));
                }

                map[string.Empty] = normalizedDefaultXmlNamespace;
            }
            else
            {
                map[string.Empty] = _implicitDefaultXmlns!;
            }
        }

        foreach (var attribute in root.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var prefix = attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName;
            if (XamlConditionalNamespaceUtilities.TrySplitConditionalNamespaceUri(
                    attribute.Value,
                    out var normalizedAttributeNamespace,
                    out var rawCondition))
            {
                var lineInfo = (IXmlLineInfo)attribute;
                var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
                var column = lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1;

                if (TryParseConditionalExpression(rawCondition!, line, column, out var condition, out var errorMessage))
                {
                    conditionMap[attribute.Value] = condition;
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0120",
                        $"Invalid conditional XAML namespace expression '{rawCondition}' for prefix '{prefix}': {errorMessage}",
                        filePath,
                        line,
                        column,
                        false));
                }

                map[prefix] = normalizedAttributeNamespace;
            }
            else
            {
                map[prefix] = attribute.Value;
            }
        }

        conditionalNamespacesByRawUri = conditionMap.ToImmutable();
        return map.ToImmutable();
    }

    private static ImmutableHashSet<string> CollectIgnoredNamespaces(
        XElement root,
        ImmutableDictionary<string, string> xmlNamespaces)
    {
        var ignoredNamespaces = DefaultIgnoredNamespaces.ToBuilder();

        foreach (var attribute in root.Attributes())
        {
            if (attribute.Name.Namespace != MarkupCompatibility ||
                !attribute.Name.LocalName.Equals("Ignorable", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(attribute.Value))
            {
                continue;
            }

            var prefixes = XamlWhitespaceTokenSemantics.SplitTokens(attribute.Value);

            foreach (var prefix in prefixes)
            {
                if (xmlNamespaces.TryGetValue(prefix, out var namespaceUri) &&
                    !string.IsNullOrWhiteSpace(namespaceUri))
                {
                    ignoredNamespaces.Add(namespaceUri);
                }
            }

            break;
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

    

    private static bool TryParseConditionalExpression(
        string rawExpression,
        int line,
        int column,
        out ConditionalXamlExpression expression,
        out string errorMessage)
    {
        expression = null!;
        if (!XamlConditionalExpressionSemantics.TryParse(
                rawExpression,
                out var parsedExpression,
                out errorMessage))
        {
            return false;
        }

        expression = new ConditionalXamlExpression(
            RawExpression: parsedExpression.RawExpression,
            MethodName: parsedExpression.MethodName,
            Arguments: parsedExpression.Arguments,
            Line: line,
            Column: column);
        return true;
    }

    private static bool IsPropertyElement(XElement element)
    {
        return XamlPropertyElementSemantics.IsPropertyElementName(element.Name.LocalName);
    }

    private static string ExtractPropertyElementName(string localName)
    {
        // Preserve owner-qualified tokens (for example ToolTip.Tip or Grid.RowDefinitions)
        // so semantic binding can distinguish attached property elements from normal members.
        return localName;
    }

    private static string NormalizeTargetPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        var normalized = targetPath.Replace('\\', '/').Trim();
        if (normalized.StartsWith("!/", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }
        else if (normalized.StartsWith("!", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        while (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        return normalized;
    }

    private static XAttribute? TryGetDirectiveAttribute(XElement element, string directiveName)
    {
        return element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == directiveName);
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
               || attribute.Name.LocalName == "Shared"
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
        return ignoredNamespaces.Contains(XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(attribute.Name.NamespaceName));
    }

    private static bool ShouldIgnoreElement(XElement element, ImmutableHashSet<string> ignoredNamespaces)
    {
        return ignoredNamespaces.Contains(XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(element.Name.NamespaceName));
    }

    private static bool IsXamlDirectiveElement(XElement element, string localName)
    {
        return element.Name.Namespace == Xaml2006 &&
               element.Name.LocalName.Equals(localName, StringComparison.Ordinal);
    }

}
