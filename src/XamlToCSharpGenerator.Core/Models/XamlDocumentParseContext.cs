using System.Collections.Immutable;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlDocumentParseContext(
    XElement RootElement,
    ImmutableHashSet<string> IgnoredNamespaces,
    ImmutableDictionary<string, ConditionalXamlExpression> ConditionalNamespacesByRawUri);
