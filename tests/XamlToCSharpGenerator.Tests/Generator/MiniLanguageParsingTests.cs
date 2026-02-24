using System.Linq;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Tests.Generator;

public class MiniLanguageParsingTests
{
    [Fact]
    public void MinimalTextDiff_Replaces_Middle_Span_And_RoundTrips()
    {
        var oldText = "<TextBlock Text=\"Old\"/>";
        var newText = "<TextBlock Text=\"New\"/>";

        var patch = MinimalTextDiff.CreatePatch(oldText, newText);
        var applied = MinimalTextDiff.ApplyPatch(oldText, patch);

        Assert.Equal(newText, applied);
        Assert.False(patch.IsNoOp);
        Assert.Equal(3, patch.RemovedLength);
        Assert.Equal(3, patch.InsertedLength);
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abXYc", false)]
    [InlineData("abXYc", "abc", false)]
    public void MinimalTextDiff_Handles_NoOp_Insert_And_Delete(
        string oldText,
        string newText,
        bool expectedNoOp)
    {
        var patch = MinimalTextDiff.CreatePatch(oldText, newText);
        var applied = MinimalTextDiff.ApplyPatch(oldText, patch);

        Assert.Equal(newText, applied);
        Assert.Equal(expectedNoOp, patch.IsNoOp);
    }

    [Fact]
    public void SplitTopLevel_Respects_Nested_Markup_Content()
    {
        var tokens = TopLevelTextParser.SplitTopLevel(
            "ValueA, {Binding Path=Name, Converter={x:Static local:Converters.Name}}, ValueB",
            ',',
            trimTokens: true,
            removeEmpty: true);

        Assert.Equal(3, tokens.Length);
        Assert.Equal("ValueA", tokens[0]);
        Assert.Equal("{Binding Path=Name, Converter={x:Static local:Converters.Name}}", tokens[1]);
        Assert.Equal("ValueB", tokens[2]);
    }

    [Fact]
    public void IndexOfTopLevel_Ignores_Tokens_Inside_Nested_Content()
    {
        var index = TopLevelTextParser.IndexOfTopLevel(
            "Path={Binding A=B, Converter={x:Static local:C.D}}",
            '=');

        Assert.Equal(4, index);
    }

    [Fact]
    public void SplitTopLevelSegments_Returns_Trimmed_Text_And_Stable_Offsets()
    {
        var segments = TopLevelTextParser.SplitTopLevelSegments(
            "  A  , {Binding Path=Name, Converter={x:Static local:C.D}} ,  B ",
            ',',
            trimTokens: true,
            removeEmpty: true);

        Assert.Equal(3, segments.Length);
        Assert.Equal("A", segments[0].Text);
        Assert.Equal("{Binding Path=Name, Converter={x:Static local:C.D}}", segments[1].Text);
        Assert.Equal("B", segments[2].Text);
        Assert.True(segments[1].Start > segments[0].Start);
        Assert.True(segments[2].Start > segments[1].Start);
    }

    [Fact]
    public void SelectorBranchTokenizer_Parses_Combinators_Deterministically()
    {
        var ok = SelectorBranchTokenizer.TryTokenize(
            "Button .warning > TextBlock /template/ Border#Chrome",
            out var segments);

        Assert.True(ok);
        Assert.Equal(4, segments.Length);
        Assert.Equal("Button", segments[0].Text);
        Assert.Equal(SelectorCombinatorKind.None, segments[0].Combinator);
        Assert.Equal(".warning", segments[1].Text);
        Assert.Equal(SelectorCombinatorKind.Descendant, segments[1].Combinator);
        Assert.Equal("TextBlock", segments[2].Text);
        Assert.Equal(SelectorCombinatorKind.Child, segments[2].Combinator);
        Assert.Equal("Border#Chrome", segments[3].Text);
        Assert.Equal(SelectorCombinatorKind.Template, segments[3].Combinator);
    }

    [Fact]
    public void SelectorBranchTokenizer_Rejects_Consecutive_Combinators()
    {
        var ok = SelectorBranchTokenizer.TryTokenize(
            "TextBlock > > Border",
            out _,
            out var errorMessage,
            out var errorOffset);

        Assert.False(ok);
        Assert.Contains("combinator", errorMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.True(errorOffset > 0);
    }

    [Fact]
    public void SelectorBranchTokenizer_Reads_Alias_Qualified_Type_Token()
    {
        var index = 0;
        var ok = SelectorBranchTokenizer.TryReadTypeToken("local|FancyButton.warning", ref index, out var typeToken);

        Assert.True(ok);
        Assert.Equal("local:FancyButton", typeToken);
        Assert.Equal("local|FancyButton".Length, index);
    }

    [Fact]
    public void SelectorSyntaxValidator_Validates_Selector_And_Extracts_Branch_Type_Tokens()
    {
        var validation = SelectorSyntaxValidator.Validate("Button:pointerover, TextBlock.warning");

        Assert.True(validation.IsValid);
        Assert.Equal(2, validation.Branches.Length);
        Assert.Equal("Button", validation.Branches[0].LastTypeToken);
        Assert.Equal("TextBlock", validation.Branches[1].LastTypeToken);
        Assert.True(validation.Branches[1].LastTypeOffset > validation.Branches[0].LastTypeOffset);
    }

    [Fact]
    public void SelectorSyntaxValidator_Reports_Property_Selector_Without_Type_Context()
    {
        var validation = SelectorSyntaxValidator.Validate(".warning[Text=true]");

        Assert.False(validation.IsValid);
        Assert.Equal("Property selectors must be applied to a type.", validation.ErrorMessage);
        Assert.True(validation.ErrorOffset > 0);
    }

    [Fact]
    public void SelectorSyntaxValidator_Reports_Invalid_NthChild_Arguments()
    {
        var validation = SelectorSyntaxValidator.Validate("TextBlock:nth-child(2n+)");

        Assert.False(validation.IsValid);
        Assert.Equal("Couldn't parse nth-child arguments.", validation.ErrorMessage);
    }

    [Fact]
    public void SelectorTokenSyntax_Reads_Standalone_Type_Token()
    {
        var ok = SelectorTokenSyntax.TryReadStandaloneTypeToken(" local|FancyButton ", out var typeToken);

        Assert.True(ok);
        Assert.Equal("local:FancyButton", typeToken);
    }

    [Fact]
    public void SelectorPropertyPredicateSyntax_Parses_Regular_And_Attached_Predicates()
    {
        var regularOk = SelectorPropertyPredicateSyntax.TryParse("Tag='Probe'", out var regularPredicate);
        var attachedOk = SelectorPropertyPredicateSyntax.TryParse("(controls|Grid.Row)=1", out var attachedPredicate);

        Assert.True(regularOk);
        Assert.Equal("Tag", regularPredicate.PropertyToken);
        Assert.Equal("'Probe'", regularPredicate.RawValue);

        Assert.True(attachedOk);
        Assert.Equal("controls:Grid.Row", attachedPredicate.PropertyToken);
        Assert.Equal("1", attachedPredicate.RawValue);
    }

    [Fact]
    public void SelectorPropertyPredicateSyntax_Rejects_Invalid_Attached_Property_Syntax()
    {
        var ok = SelectorPropertyPredicateSyntax.TryParse("(Grid|)=1", out _);

        Assert.False(ok);
    }

    [Fact]
    public void CompiledBindingPathParser_Parses_Complex_Path_Shape()
    {
        var ok = CompiledBindingPathParser.TryParse(
            "!((vm:Customer)Orders)[0]?.GetName(\"x\", {Binding Path=Value}).(controls:Grid.Row)^",
            out var segments,
            out var leadingNotCount,
            out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal(1, leadingNotCount);
        Assert.Equal(3, segments.Length);

        Assert.Equal("Orders", segments[0].MemberName);
        Assert.Equal("vm:Customer", segments[0].CastTypeToken);
        Assert.Equal("0", Assert.Single(segments[0].Indexers));
        Assert.False(segments[0].AcceptsNull);

        Assert.Equal("GetName", segments[1].MemberName);
        Assert.True(segments[1].AcceptsNull);
        Assert.True(segments[1].IsMethodCall);
        Assert.Equal(2, segments[1].MethodArguments.Length);

        Assert.True(segments[2].IsAttachedProperty);
        Assert.Equal("controls:Grid", segments[2].AttachedOwnerTypeToken);
        Assert.Equal("Row", segments[2].MemberName);
        Assert.Equal(1, segments[2].StreamCount);
    }

    [Fact]
    public void CompiledBindingPathParser_Rejects_Trailing_Not_Operator()
    {
        var ok = CompiledBindingPathParser.TryParse(
            "!",
            out _,
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("cannot end after '!'", errorMessage);
    }

    [Fact]
    public void CompiledBindingPathParser_Rejects_Empty_Method_Arguments()
    {
        var ok = CompiledBindingPathParser.TryParse(
            "Foo(,)",
            out _,
            out _,
            out var errorMessage);

        Assert.False(ok);
        Assert.Contains("empty argument", errorMessage);
    }

    [Fact]
    public void SelectorNestingComposer_Composes_Parent_And_Nested_Selectors()
    {
        var composed = SelectorNestingComposer.ComposeNestedStyleSelector(
            "Button, TextBox",
            "^:pointerover, > Border");

        var parts = TopLevelTextParser.SplitTopLevel(composed, ',', trimTokens: true, removeEmpty: true);
        Assert.Equal(4, parts.Length);
        Assert.Contains("Button:pointerover", parts);
        Assert.Contains("TextBox:pointerover", parts);
        Assert.Contains("Button> Border", parts);
        Assert.Contains("TextBox> Border", parts);
    }

    [Theory]
    [InlineData("odd", true, 2, 1)]
    [InlineData("2n+1", true, 2, 1)]
    [InlineData("2n+", false, 0, 0)]
    public void SelectorPseudoSyntax_Parses_NthChild_Expressions(
        string input,
        bool expected,
        int expectedStep,
        int expectedOffset)
    {
        var ok = SelectorPseudoSyntax.TryParseNthChildExpression(input, out var step, out var offset);

        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.Equal(expectedStep, step);
            Assert.Equal(expectedOffset, offset);
        }
    }

    [Theory]
    [InlineData("is", SelectorPseudoFunctionKind.Is)]
    [InlineData(" not ", SelectorPseudoFunctionKind.Not)]
    [InlineData("nth-child", SelectorPseudoFunctionKind.NthChild)]
    [InlineData("nth-last-child", SelectorPseudoFunctionKind.NthLastChild)]
    [InlineData("pointerover", SelectorPseudoFunctionKind.Unknown)]
    public void SelectorPseudoSyntax_Classifies_Pseudo_Functions(string pseudoName, SelectorPseudoFunctionKind expectedKind)
    {
        var kind = SelectorPseudoSyntax.ClassifyPseudoFunction(pseudoName);

        Assert.Equal(expectedKind, kind);
    }
}
