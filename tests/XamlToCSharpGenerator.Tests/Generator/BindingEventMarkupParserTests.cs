using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class BindingEventMarkupParserTests
{
    private static readonly MarkupExpressionParser Parser =
        new(new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));

    [Fact]
    public void TryParseBindingMarkup_Parses_ElementName_Query_Path()
    {
        var success = BindingEventMarkupParser.TryParseBindingMarkup(
            "{Binding #InputBox.Text}",
            TryParseMarkupExtension,
            out var bindingMarkup);

        Assert.True(success);
        Assert.Equal("InputBox", bindingMarkup.ElementName);
        Assert.Equal("Text", bindingMarkup.Path);
        Assert.False(bindingMarkup.HasSourceConflict);
    }

    [Fact]
    public void TryParseBindingMarkup_Parses_Self_Query_Path()
    {
        var success = BindingEventMarkupParser.TryParseBindingMarkup(
            "{Binding $self.IsEnabled}",
            TryParseMarkupExtension,
            out var bindingMarkup);

        Assert.True(success);
        Assert.NotNull(bindingMarkup.RelativeSource);
        Assert.Equal("Self", bindingMarkup.RelativeSource?.Mode);
        Assert.Equal("IsEnabled", bindingMarkup.Path);
    }

    [Fact]
    public void TryParseBindingMarkup_Detects_Source_Query_Conflict()
    {
        var success = BindingEventMarkupParser.TryParseBindingMarkup(
            "{Binding #InputBox.Text, ElementName=Other}",
            TryParseMarkupExtension,
            out var bindingMarkup);

        Assert.True(success);
        Assert.True(bindingMarkup.HasSourceConflict);
        Assert.Contains("#name", bindingMarkup.SourceConflictMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseEventBindingMarkup_Parses_Command_And_Binding_Parameter()
    {
        Assert.True(
            TryParseMarkupExtension(
                "{EventBinding Command=SaveCommand, Parameter={Binding SelectedItem}, Source=DataContext, PassEventArgs=True}",
                out var markupExtension));

        var success = BindingEventMarkupParser.TryParseEventBindingMarkup(
            markupExtension,
            TryParseMarkupExtension,
            TryConvertLiteralValueExpression,
            out var eventBindingMarkup,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(ResolvedEventBindingTargetKind.Command, eventBindingMarkup.TargetKind);
        Assert.Equal(ResolvedEventBindingSourceMode.DataContext, eventBindingMarkup.SourceMode);
        Assert.Equal("SaveCommand", eventBindingMarkup.TargetPath);
        Assert.Equal("SelectedItem", eventBindingMarkup.ParameterPath);
        Assert.False(eventBindingMarkup.HasParameterValueExpression);
        Assert.True(eventBindingMarkup.PassEventArgs);
    }

    [Fact]
    public void TryParseEventBindingMarkup_Uses_Literal_Converter_Callback()
    {
        Assert.True(
            TryParseMarkupExtension(
                "{EventBinding Command=SaveCommand, Parameter='42'}",
                out var markupExtension));

        var success = BindingEventMarkupParser.TryParseEventBindingMarkup(
            markupExtension,
            TryParseMarkupExtension,
            TryConvertLiteralValueExpression,
            out var eventBindingMarkup,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.True(eventBindingMarkup.HasParameterValueExpression);
        Assert.Equal("lit(42)", eventBindingMarkup.ParameterValueExpression);
        Assert.Null(eventBindingMarkup.ParameterPath);
    }

    [Fact]
    public void TryParseResolveByNameReferenceToken_Parses_Markup_And_Literal_Forms()
    {
        Assert.True(
            BindingEventMarkupParser.TryParseResolveByNameReferenceToken(
                "{x:Reference Name=RootPanel}",
                TryParseMarkupExtension,
                out var markupReferenceToken));
        Assert.Equal("RootPanel", markupReferenceToken.Name);
        Assert.True(markupReferenceToken.FromMarkupExtension);

        Assert.True(
            BindingEventMarkupParser.TryParseResolveByNameReferenceToken(
                "RootPanel",
                TryParseMarkupExtension,
                out var literalReferenceToken));
        Assert.Equal("RootPanel", literalReferenceToken.Name);
        Assert.False(literalReferenceToken.FromMarkupExtension);
    }

    [Fact]
    public void TryParseEventBindingMarkup_Parses_Null_Parameter_Markup()
    {
        Assert.True(
            TryParseMarkupExtension(
                "{EventBinding Command=SaveCommand, Parameter={x:Null}}",
                out var markupExtension));

        var success = BindingEventMarkupParser.TryParseEventBindingMarkup(
            markupExtension,
            TryParseMarkupExtension,
            TryConvertLiteralValueExpression,
            out var eventBindingMarkup,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.True(eventBindingMarkup.HasParameterValueExpression);
        Assert.Equal("null", eventBindingMarkup.ParameterValueExpression);
    }

    [Fact]
    public void TryParseResolveByNameReferenceToken_Parses_ResolveByName_Alias()
    {
        Assert.True(
            BindingEventMarkupParser.TryParseResolveByNameReferenceToken(
                "{ResolveByName Name=RootPanel}",
                TryParseMarkupExtension,
                out var token));
        Assert.Equal("RootPanel", token.Name);
        Assert.True(token.FromMarkupExtension);
    }

    [Theory]
    [InlineData("Root Panel")]
    [InlineData("{x:Reference Name='Root Panel'}")]
    public void TryParseResolveByNameReferenceToken_Rejects_Whitespace_Name(string value)
    {
        Assert.False(
            BindingEventMarkupParser.TryParseResolveByNameReferenceToken(
                value,
                TryParseMarkupExtension,
                out _));
    }

    [Fact]
    public void IsEventBindingMarkupExtension_Recognizes_Xaml_Directive_Form()
    {
        Assert.True(
            TryParseMarkupExtension(
                "{x:EventBinding Command=SaveCommand}",
                out var markupExtension));

        Assert.True(BindingEventMarkupParser.IsEventBindingMarkupExtension(markupExtension));
    }

    [Fact]
    public void TryParseEventBindingMarkup_Maps_Default_Source_Alias_To_DataContextThenRoot()
    {
        Assert.True(
            TryParseMarkupExtension(
                "{EventBinding Command=SaveCommand, Source=Default}",
                out var markupExtension));

        var success = BindingEventMarkupParser.TryParseEventBindingMarkup(
            markupExtension,
            TryParseMarkupExtension,
            TryConvertLiteralValueExpression,
            out var eventBindingMarkup,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(ResolvedEventBindingSourceMode.DataContextThenRoot, eventBindingMarkup.SourceMode);
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return Parser.TryParseMarkupExtension(value, out markupExtension);
    }

    private static bool TryConvertLiteralValueExpression(string literalValue, out string expression)
    {
        expression = "lit(" + literalValue + ")";
        return true;
    }
}
