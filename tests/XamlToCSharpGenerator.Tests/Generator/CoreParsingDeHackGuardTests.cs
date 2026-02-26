using System;
using System.IO;

namespace XamlToCSharpGenerator.Tests.Generator;

public class CoreParsingDeHackGuardTests
{
    [Fact]
    public void SimpleXamlDocumentParser_Uses_Centralized_Conditional_Expression_Semantics()
    {
        var source = ReadSimpleXamlDocumentParserSource();

        Assert.Contains("XamlConditionalExpressionSemantics.TryParse(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseConditionalArguments(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryNormalizeConditionalArgument(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateConditionalMethodArity(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SupportedConditionalMethodNames", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SimpleXamlDocumentParser_Uses_Centralized_Type_Argument_List_Semantics()
    {
        var source = ReadSimpleXamlDocumentParserSource();

        Assert.Contains("XamlTypeArgumentListSemantics.Parse(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ImmutableArray<string> ParseTypeArguments(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_Binding_Path_Uses_Centralized_Cast_Segment_Semantics()
    {
        var source = ReadRuntimeBindingPathSemanticsSource();

        Assert.Contains("CompiledBindingPathSegmentSemantics.TryParseCastTypeToken(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("normalized.IndexOf(')')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("normalized.Substring(1, closingParenthesisIndex - 1)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkupExpressionParser_Uses_Centralized_Markup_Argument_Semantics()
    {
        var source = ReadMarkupExpressionParserSource();

        Assert.Contains("XamlMarkupArgumentSemantics.TryParseHead(", source, StringComparison.Ordinal);
        Assert.Contains("XamlMarkupArgumentSemantics.SplitArguments(", source, StringComparison.Ordinal);
        Assert.Contains("XamlMarkupArgumentSemantics.TryParseNamedArgument(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("inner.Substring(0, headLength)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("argument.Substring(0, equalsIndex)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalNamespaceUtilities_Use_Canonical_Conditional_Expression_Semantics()
    {
        var source = ReadConditionalNamespaceUtilitiesSource();

        Assert.Contains("XamlConditionalExpressionSemantics.TryParseMethodCallShape(", source, StringComparison.Ordinal);
        Assert.Contains("XamlConditionalNamespaceUriSemantics.TrySplit(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TopLevelTextParser.IndexOfTopLevel(rawNamespace, '?')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("rawNamespace.Substring(separatorIndex + 1)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("candidateCondition.EndsWith(\")\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("candidateCondition.IndexOf('(') <= 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalExpressionSemantics_Use_Centralized_Method_Call_Semantics()
    {
        var source = ReadConditionalExpressionSemanticsSource();

        Assert.Contains("XamlConditionalMethodCallSemantics.TryParseMethodCall(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("trimmed.IndexOf('(')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("trimmed.Substring(0, openParenIndex)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SimpleXamlDocumentParser_Uses_Property_Element_Semantics_Service()
    {
        var source = ReadSimpleXamlDocumentParserSource();

        Assert.Contains("XamlPropertyElementSemantics.IsAttachedPropertyToken(", source, StringComparison.Ordinal);
        Assert.Contains("XamlPropertyElementSemantics.IsPropertyElementName(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("propertyName.IndexOf('.') >= 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("element.Name.LocalName.IndexOf('.') >= 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SimpleXamlDocumentParser_Uses_Whitespace_Token_Semantics_For_Ignorable_Prefixes()
    {
        var source = ReadSimpleXamlDocumentParserSource();

        Assert.Contains("XamlWhitespaceTokenSemantics.SplitTokens(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BindingEventMarkupParser_Uses_Centralized_Reference_Name_Semantics()
    {
        var source = ReadBindingEventMarkupParserSource();

        Assert.Contains("XamlReferenceNameSemantics.TryNormalizeReferenceName(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("unquoted.IndexOfAny([' ', '\\t', '\\r', '\\n']) >= 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EventBindingPath_And_EventHandler_Use_Centralized_Identifier_Semantics()
    {
        var eventBindingPathSource = ReadEventBindingPathSemanticsSource();
        var eventHandlerSource = ReadEventHandlerNameSemanticsSource();

        Assert.Contains("XamlIdentifierSemantics.IsIdentifier(", eventBindingPathSource, StringComparison.Ordinal);
        Assert.DoesNotContain("char.IsLetter(ch) || ch == '_'", eventBindingPathSource, StringComparison.Ordinal);

        Assert.Contains("XamlIdentifierSemantics.TryNormalizeIdentifier(", eventHandlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MiniLanguageSyntaxFacts.IsIdentifierStart(", eventHandlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MiniLanguageSyntaxFacts.IsIdentifierPart(", eventHandlerSource, StringComparison.Ordinal);
    }

    private static string ReadSimpleXamlDocumentParserSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "SimpleXamlDocumentParser.cs");
        return File.ReadAllText(path);
    }

    private static string ReadRuntimeBindingPathSemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "XamlRuntimeBindingPathSemantics.cs");
        return File.ReadAllText(path);
    }

    private static string ReadMarkupExpressionParserSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "MarkupExpressionParser.cs");
        return File.ReadAllText(path);
    }

    private static string ReadConditionalNamespaceUtilitiesSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "XamlConditionalNamespaceUtilities.cs");
        return File.ReadAllText(path);
    }

    private static string ReadConditionalExpressionSemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "XamlConditionalExpressionSemantics.cs");
        return File.ReadAllText(path);
    }

    private static string ReadBindingEventMarkupParserSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "BindingEventMarkupParser.cs");
        return File.ReadAllText(path);
    }

    private static string ReadEventBindingPathSemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "EventBindingPathSemantics.cs");
        return File.ReadAllText(path);
    }

    private static string ReadEventHandlerNameSemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Core",
            "Parsing",
            "XamlEventHandlerNameSemantics.cs");
        return File.ReadAllText(path);
    }
}
