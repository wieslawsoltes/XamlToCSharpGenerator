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

    [Fact]
    public void Binder_Does_Not_Use_Lexical_Xaml_Fragment_Heuristic()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("IsLikelyXamlFragment(", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeXamlFragmentDetectionService.IsValidFragment(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Does_Not_Use_String_Slicing_For_XType_Resolution()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("ExtractTypeToken(", source, StringComparison.Ordinal);
        Assert.Contains("TypeExpressionResolutionService.ResolveTypeFromExpression(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emitter_Does_Not_Use_Theme_Class_Name_Heuristics()
    {
        var source = ReadEmitterSource();

        Assert.DoesNotContain("IsThemeLikeDocument(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveHotDesignArtifactKindToken(", source, StringComparison.Ordinal);
        Assert.Contains("viewModel.HotDesignArtifactKind", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emitter_MergedDictionary_Include_Resolver_Handles_ResourceInclude()
    {
        var source = ReadEmitterSource();

        Assert.DoesNotContain(
            "includeValue is not global::Avalonia.Markup.Xaml.Styling.MergeResourceInclude",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "includeValue is not global::Avalonia.Markup.Xaml.Styling.ResourceInclude",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"global::Avalonia.Markup.Xaml.Styling.ResourceInclude\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Emitter_StyleInclude_Is_Resolved_Before_Collection_Add()
    {
        var source = ReadEmitterSource();

        Assert.Contains("private static bool __TryApplyStyleInclude(", source, StringComparison.Ordinal);
        Assert.Contains("if (!__TryApplyStyleInclude(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emitter_MergedDictionary_Include_Preserves_MergedDictionary_Semantics()
    {
        var source = ReadEmitterSource();

        Assert.Contains("destinationDictionary.MergedDictionaries.Add(mergedResourceDictionary);", source, StringComparison.Ordinal);
        Assert.Contains("destinationDictionary.MergedDictionaries.Add(mergedResourceProvider);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentFeatureEnricher_Does_Not_Use_PropertyElement_Suffix_Heuristics()
    {
        var source = ReadDocumentFeatureEnricherSource();

        Assert.DoesNotContain("EndsWith(\".Value\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndsWith(\".MergedDictionaries\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndsWith(\".Styles\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("scope.Name.LocalName + \".Setters\"", source, StringComparison.Ordinal);
        Assert.Contains("XamlPropertyTokenSemantics.IsPropertyElementName(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectorPropertyReferences_Use_Centralized_PropertyToken_Semantics()
    {
        var source = ReadSelectorPropertyReferencesSource();

        Assert.DoesNotContain("LastIndexOf('.')", source, StringComparison.Ordinal);
        Assert.Contains("XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkupHelpers_Use_Centralized_StaticMember_Token_Split()
    {
        var source = ReadMarkupHelpersSource();

        Assert.DoesNotContain("memberToken.LastIndexOf('.')", source, StringComparison.Ordinal);
        Assert.Contains("XamlTokenSplitSemantics.TrySplitAtLastSeparator(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Centralized_Property_Suffix_Trimming()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("propertyToken.EndsWith(\"Property\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("trimmed.EndsWith(\"Property\"", source, StringComparison.Ordinal);
        Assert.Contains("XamlTokenSplitSemantics.TrimTerminalSuffix(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Centralized_Colon_Token_Splitting()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("var colonIndex = normalized.IndexOf(':');", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var separatorIndex = trimmed.IndexOf(':');", source, StringComparison.Ordinal);
        Assert.Contains("XamlTokenSplitSemantics.TrySplitAtFirstSeparator(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludeBinding_Uses_Centralized_Include_Path_Semantics()
    {
        var source = ReadIncludesBinderSource();

        Assert.DoesNotContain("targetPath.LastIndexOf('/')", source, StringComparison.Ordinal);
        Assert.Contains("XamlIncludePathSemantics.GetDirectory(", source, StringComparison.Ordinal);
        Assert.Contains("XamlIncludePathSemantics.CombinePath(", source, StringComparison.Ordinal);
        Assert.Contains("XamlIncludePathSemantics.NormalizePath(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Centralized_Runtime_Binding_Path_Semantics()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("var closingParenthesisIndex = normalized.IndexOf(')');", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var typeToken = normalized.Substring(1, closingParenthesisIndex - 1).Trim();", source, StringComparison.Ordinal);
        Assert.Contains("XamlRuntimeBindingPathSemantics.NormalizePath(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Centralized_Type_Token_Semantics()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("token.StartsWith(\"global::\", StringComparison.Ordinal)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("normalized.StartsWith(\"x:\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("XamlTypeTokenSemantics.TrimGlobalQualifier(", source, StringComparison.Ordinal);
        Assert.Contains("XamlTypeTokenSemantics.TrimXamlDirectivePrefix(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoUiBinder_Uses_Centralized_XmlNamespace_Semantics()
    {
        var source = ReadNoUiBinderSource();

        Assert.DoesNotContain("xmlNamespace.StartsWith(ClrNamespacePrefix", source, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlNamespace.StartsWith(UsingNamespacePrefix", source, StringComparison.Ordinal);
        Assert.Contains("XamlXmlNamespaceSemantics.TryExtractClrNamespace(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicTypeResolution_Uses_Centralized_XmlNamespace_Semantics()
    {
        var source = ReadDeterministicTypeResolutionSemanticsSource();

        Assert.DoesNotContain("xmlNamespace.StartsWith(\"clr-namespace:\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlNamespace.StartsWith(\"using:\"", source, StringComparison.Ordinal);
        Assert.Contains("XamlXmlNamespaceSemantics.TryBuildClrNamespaceMetadataName(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Expression_Classification_Service()
    {
        var source = ReadExpressionMarkupSource();

        Assert.Contains("ExpressionClassificationService.TryParseCSharpExpressionMarkup(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsImplicitCSharpExpressionMarkup(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool LooksLikeMarkupExtensionStart(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Type_Resolution_Policy_Service()
    {
        var source = ReadBinderSource();

        Assert.Contains("TypeResolutionPolicyService.TryResolveTokenFallback(", source, StringComparison.Ordinal);
        Assert.Contains("TypeResolutionPolicyService.TryResolveXmlNamespaceFallback(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia default namespace compatibility fallback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia default xml namespace compatibility fallback", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_List_And_Member_Path_Semantics_Services()
    {
        var source = ReadBinderSource();
        var eventBindingPathSource = ReadEventBindingPathSemanticsSource();

        Assert.Contains("XamlListValueSemantics.SplitWhitespaceAndCommaTokens(", source, StringComparison.Ordinal);
        Assert.Contains("XamlListValueSemantics.SplitCommaSeparatedTokens(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ImmutableArray<string> SplitClassTokens(", source, StringComparison.Ordinal);
        Assert.Contains("XamlMemberPathSemantics.SplitPathSegments(", source, StringComparison.Ordinal);
        Assert.Contains("XamlMemberPathSemantics.NormalizeSegmentForMemberLookup(", source, StringComparison.Ordinal);
        Assert.Contains("XamlMemberPathSemantics.SplitPathSegments(", eventBindingPathSource, StringComparison.Ordinal);
    }

    private static string ReadBinderSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var bindingDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding");

        var sourceFiles = Directory.GetFiles(bindingDirectory, "AvaloniaSemanticBinder*.cs")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            sourceFiles.Select(File.ReadAllText));
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

    private static string ReadEmitterSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var emitterPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Emission",
            "AvaloniaCodeEmitter.cs");
        return File.ReadAllText(emitterPath);
    }

    private static string ReadDocumentFeatureEnricherSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Parsing",
            "AvaloniaDocumentFeatureEnricher.cs");
        return File.ReadAllText(path);
    }

    private static string ReadSelectorPropertyReferencesSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSemanticBinder.SelectorPropertyReferences.cs");
        return File.ReadAllText(path);
    }

    private static string ReadExpressionMarkupSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSemanticBinder.ExpressionMarkup.cs");
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

    private static string ReadMarkupHelpersSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSemanticBinder.MarkupHelpers.cs");
        return File.ReadAllText(path);
    }

    private static string ReadIncludesBinderSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSemanticBinder.Includes.cs");
        return File.ReadAllText(path);
    }

    private static string ReadNoUiBinderSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.NoUi",
            "Binding",
            "NoUiSemanticBinder.cs");
        return File.ReadAllText(path);
    }

    private static string ReadDeterministicTypeResolutionSemanticsSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.ExpressionSemantics",
            "DeterministicTypeResolutionSemantics.cs");
        return File.ReadAllText(path);
    }
}
