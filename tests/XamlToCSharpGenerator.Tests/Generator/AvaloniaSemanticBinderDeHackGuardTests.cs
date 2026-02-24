using System;
using System.IO;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaSemanticBinderDeHackGuardTests
{
    [Fact]
    public void Binder_Does_Not_Use_Legacy_Markup_Context_Token_Scanning()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("ContainsMarkupContextTokens(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Does_Not_Use_Binding_Type_Suffix_Heuristics()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("EndsWith(\".Binding\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndsWith(\"Binding\"", source, StringComparison.Ordinal);
        Assert.Contains("IsBindingObjectType(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Centralized_EventBinding_Source_Validation()
    {
        var source = ReadBinderSource();

        Assert.Contains("TryValidateEventBindingBindingSource(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Centralized_Binding_And_Event_Markup_Parser()
    {
        var binderSource = ReadBinderSource();
        var selectorAdapterSource = ReadSelectorSemanticAdapterSource();
        var selectorSemanticsSource = ReadSelectorExpressionSemanticsSource();
        var selectorPredicateResolverSource = ReadSelectorPredicateResolverSource();

        Assert.Contains("BindingEventMarkupParser.TryParseBindingMarkup(", binderSource, StringComparison.Ordinal);
        Assert.Contains("BindingEventMarkupParser.TryParseEventBindingMarkup(", binderSource, StringComparison.Ordinal);
        Assert.Contains("EventBindingPathSemantics.TrySplitMethodPath(", binderSource, StringComparison.Ordinal);
        Assert.Contains("EventBindingPathSemantics.BuildMethodArgumentSets(", binderSource, StringComparison.Ordinal);
        Assert.Contains("DeterministicTypeResolutionSemantics.TryParseGenericTypeToken(", binderSource, StringComparison.Ordinal);
        Assert.Contains("DeterministicTypeResolutionSemantics.TryBuildClrNamespaceMetadataName(", binderSource, StringComparison.Ordinal);
        Assert.Contains("AvaloniaSelectorSemanticAdapter.TryResolveSelectorTargetType(", binderSource, StringComparison.Ordinal);
        Assert.Contains("AvaloniaSelectorSemanticAdapter.TryBuildSelectorExpression(", binderSource, StringComparison.Ordinal);

        Assert.Contains("SelectorTargetTypeResolutionSemantics.ResolveTargetType(", selectorAdapterSource, StringComparison.Ordinal);
        Assert.Contains("SelectorExpressionBuildSemantics.TryBuildSelectorExpression(", selectorAdapterSource, StringComparison.Ordinal);
        Assert.Contains("AvaloniaSelectorPropertyPredicateResolver.TryResolve(", selectorAdapterSource, StringComparison.Ordinal);

        Assert.Contains("SelectorPseudoSyntax.ClassifyPseudoFunction(", selectorSemanticsSource, StringComparison.Ordinal);
        Assert.Contains("SelectorTokenSyntax.TryReadStandaloneTypeToken(", selectorSemanticsSource, StringComparison.Ordinal);
        Assert.Contains("SelectorPropertyPredicateSyntax.TryParse(", selectorPredicateResolverSource, StringComparison.Ordinal);
    }

    private static string ReadBinderSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var binderPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSemanticBinder.cs");
        return File.ReadAllText(binderPath);
    }

    private static string ReadSelectorExpressionSemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var semanticsPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.ExpressionSemantics",
            "SelectorExpressionBuildSemantics.cs");
        return File.ReadAllText(semanticsPath);
    }

    private static string ReadSelectorSemanticAdapterSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var adapterPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSelectorSemanticAdapter.cs");
        return File.ReadAllText(adapterPath);
    }

    private static string ReadSelectorPredicateResolverSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var resolverPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSelectorPropertyPredicateResolver.cs");
        return File.ReadAllText(resolverPath);
    }
}
