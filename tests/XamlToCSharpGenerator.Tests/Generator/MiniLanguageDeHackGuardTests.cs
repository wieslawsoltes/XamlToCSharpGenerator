using System;
using System.IO;

namespace XamlToCSharpGenerator.Tests.Generator;

public class MiniLanguageDeHackGuardTests
{
    [Fact]
    public void CompiledBindingPathParser_Uses_Centralized_Segment_Semantics()
    {
        var parserSource = ReadCompiledBindingPathParserSource();

        Assert.Contains("CompiledBindingPathSegmentSemantics.TryParseAttachedPropertySegment(", parserSource, StringComparison.Ordinal);
        Assert.Contains("CompiledBindingPathSegmentSemantics.TryParseCastTypeToken(", parserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("path.IndexOf(')', index + 1)", parserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("path.Substring(index + 1, attachedClosing - index - 1)", parserSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BindingSourceQuerySemantics_Uses_Balanced_Parent_Descriptor_Parsing()
    {
        var source = ReadBindingSourceQuerySemanticsSource();

        Assert.Contains("TopLevelTextParser.TryReadBalancedContent(path, ref descriptorCursor, '[', ']'", source, StringComparison.Ordinal);
        Assert.Contains("TopLevelTextParser.IndexOfTopLevel(descriptor, ','", source, StringComparison.Ordinal);
        Assert.DoesNotContain("path.IndexOf(']', index + 1)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectorPropertyPredicateSyntax_Uses_Centralized_Predicate_Semantics()
    {
        var source = ReadSelectorPropertyPredicateSyntaxSource();

        Assert.Contains("SelectorPropertyPredicateSemantics.TrySplitPredicate(", source, StringComparison.Ordinal);
        Assert.Contains("SelectorPropertyPredicateSemantics.TryParseAttachedPropertyToken(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("predicateText.Substring(0, equalsIndex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("propertyText.Substring(1, propertyText.Length - 2)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectorPropertyPredicateSemantics_Uses_Balanced_Attached_Property_Parsing()
    {
        var source = ReadSelectorPropertyPredicateSemanticsSource();

        Assert.Contains("TopLevelTextParser.TryReadBalancedContent(", source, StringComparison.Ordinal);
        Assert.Contains("FindLastTopLevelSeparator(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("propertyText.LastIndexOf('.')", source, StringComparison.Ordinal);
    }

    private static string ReadCompiledBindingPathParserSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.MiniLanguageParsing",
            "Bindings",
            "CompiledBindingPathParser.cs");
        return File.ReadAllText(path);
    }

    private static string ReadBindingSourceQuerySemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.MiniLanguageParsing",
            "Bindings",
            "BindingSourceQuerySemantics.cs");
        return File.ReadAllText(path);
    }

    private static string ReadSelectorPropertyPredicateSyntaxSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.MiniLanguageParsing",
            "Selectors",
            "SelectorPropertyPredicateSyntax.cs");
        return File.ReadAllText(path);
    }

    private static string ReadSelectorPropertyPredicateSemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.MiniLanguageParsing",
            "Selectors",
            "SelectorPropertyPredicateSemantics.cs");
        return File.ReadAllText(path);
    }
}
