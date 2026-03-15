using System.Xml.Linq;
using System.Reflection;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class SourceGeneratedPreviewXamlPreprocessor
{
    private const string RuntimeMarkupNamespace = "using:XamlToCSharpGenerator.Runtime.Markup";
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace XmlnsNamespace = XNamespace.Xmlns;
    private static readonly XNamespace XamlNamespace = Xaml2006Namespace;

    public static string Rewrite(string xamlText, Assembly localAssembly)
    {
        ArgumentException.ThrowIfNullOrEmpty(xamlText);
        ArgumentNullException.ThrowIfNull(localAssembly);

        var document = XDocument.Parse(xamlText, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (document.Root is null)
        {
            return xamlText;
        }

        var analysis = PreviewExpressionAnalysisContext.ForAssembly(localAssembly);
        var runtimePrefix = EnsurePreviewMarkupNamespacePrefix(document.Root);
        var changed = RewriteElement(document.Root, inheritedDataType: null, runtimePrefix, analysis);

        return changed ? document.ToString(SaveOptions.DisableFormatting) : xamlText;
    }

    private static bool RewriteElement(
        XElement element,
        Type? inheritedDataType,
        string runtimePrefix,
        PreviewExpressionAnalysisContext analysis)
    {
        var changed = false;
        var currentDataType = ResolveDataType(element, inheritedDataType);

        var attributes = element.Attributes().ToArray();
        for (var index = 0; index < attributes.Length; index++)
        {
            var attribute = attributes[index];
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name == XamlNamespace + "DataType")
            {
                continue;
            }

            if (!TryRewriteExpressionMarkup(attribute.Value, currentDataType, runtimePrefix, analysis, out var rewritten))
            {
                continue;
            }

            attribute.Value = rewritten;
            changed = true;
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XElement childElement:
                    if (RewriteElement(childElement, currentDataType, runtimePrefix, analysis))
                    {
                        changed = true;
                    }

                    break;
                case XText textNode:
                    if (!TryRewriteExpressionMarkup(textNode.Value, currentDataType, runtimePrefix, analysis, out var rewrittenText))
                    {
                        continue;
                    }

                    textNode.Value = rewrittenText;
                    changed = true;
                    break;
            }
        }

        return changed;
    }

    private static Type? ResolveDataType(XElement element, Type? inheritedDataType)
    {
        var attribute = element.Attribute(XamlNamespace + "DataType");
        if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value))
        {
            return inheritedDataType;
        }

        return ResolveTypeReference(element, attribute.Value) ?? inheritedDataType;
    }

    private static Type? ResolveTypeReference(XElement element, string rawValue)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return null;
        }

        var separatorIndex = trimmed.IndexOf(':');
        string? prefix = null;
        var typeName = trimmed;
        if (separatorIndex > 0)
        {
            prefix = trimmed[..separatorIndex];
            typeName = trimmed[(separatorIndex + 1)..];
        }

        if (typeName.Length == 0)
        {
            return null;
        }

        XNamespace xmlNamespace;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            xmlNamespace = element.GetDefaultNamespace();
        }
        else
        {
            xmlNamespace = element.GetNamespaceOfPrefix(prefix!) ?? XNamespace.None;
        }

        return SourceGenKnownTypeRegistry.TryResolve(xmlNamespace.NamespaceName, typeName, out var resolvedType)
            ? resolvedType
            : null;
    }

    private static bool TryRewriteExpressionMarkup(
        string rawValue,
        Type? dataType,
        string runtimePrefix,
        PreviewExpressionAnalysisContext analysis,
        out string rewrittenValue)
    {
        rewrittenValue = string.Empty;
        if (!MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(rawValue, out var innerContent) ||
            !innerContent.StartsWith("=", StringComparison.Ordinal))
        {
            return false;
        }

        var rawExpression = innerContent[1..].Trim();
        if (rawExpression.Length == 0)
        {
            return false;
        }

        var rewrittenExpression = rawExpression;
        IReadOnlyList<string> dependencyNames = Array.Empty<string>();
        if (dataType is not null &&
            analysis.TryRewriteSourceContextExpression(
                dataType,
                rawExpression,
                out var analyzedExpression,
                out var analyzedDependencyNames,
                out _))
        {
            rewrittenExpression = analyzedExpression;
            dependencyNames = analyzedDependencyNames;
        }

        var encodedExpression = PreviewMarkupValueCodec.EncodeBase64Url(rewrittenExpression);
        var dependencyValue = dependencyNames.Count == 0
            ? string.Empty
            : " DependencyNamesBase64Url=" + PreviewMarkupValueCodec.EncodeBase64Url(string.Join("\n", dependencyNames));
        var markupExtensionName = string.IsNullOrWhiteSpace(runtimePrefix)
            ? "CSharp"
            : runtimePrefix + ":CSharp";
        rewrittenValue = "{" +
            markupExtensionName +
            " CodeBase64Url=" +
            encodedExpression +
            dependencyValue +
            "}";
        return true;
    }

    private static string EnsurePreviewMarkupNamespacePrefix(XElement root)
    {
        foreach (var attribute in root.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration ||
                !string.Equals(attribute.Value, RuntimeMarkupNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = attribute.Name.LocalName;
            return prefix == "xmlns" ? string.Empty : prefix;
        }

        var index = 0;
        while (true)
        {
            var prefix = index == 0 ? "axsg" : "axsg" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (root.GetNamespaceOfPrefix(prefix) is not null)
            {
                index++;
                continue;
            }

            root.SetAttributeValue(XmlnsNamespace + prefix, RuntimeMarkupNamespace);
            return prefix;
        }
    }
}
