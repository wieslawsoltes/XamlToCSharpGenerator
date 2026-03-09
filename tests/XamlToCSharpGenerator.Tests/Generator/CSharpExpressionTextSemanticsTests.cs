using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Tests.Generator;

public class CSharpExpressionTextSemanticsTests
{
    [Fact]
    public void NormalizeExpressionCode_Replaces_Alias_Operators()
    {
        var normalized = CSharpExpressionTextSemantics.NormalizeExpressionCode("Name AND IsEnabled OR Value LTE 10");

        Assert.Equal("Name && IsEnabled || Value <= 10", normalized);
    }

    [Fact]
    public void NormalizeExpressionCode_Preserves_Char_Literals_And_Converts_String_Literals()
    {
        var normalized = CSharpExpressionTextSemantics.NormalizeExpressionCode("'x' + 'hello'");

        Assert.Equal("'x' + \"hello\"", normalized);
    }

    [Fact]
    public void NormalizeExpressionCode_Converts_SingleQuoted_Interpolated_Strings_With_Format_Clauses()
    {
        var normalized = CSharpExpressionTextSemantics.NormalizeExpressionCode("$'Total: ${Price * Quantity:F2}'");

        Assert.Equal("$\"Total: {Price * Quantity:F2}\"", normalized);
    }

    [Fact]
    public void IsAsyncLambdaExpression_Detects_Async_Lambda()
    {
        Assert.True(CSharpMarkupExpressionSemantics.IsAsyncLambdaExpression("async (s, e) => SaveAsync()"));
        Assert.False(CSharpMarkupExpressionSemantics.IsAsyncLambdaExpression("(s, e) => Count++"));
    }

    [Theory]
    [InlineData("A + B", true)]
    [InlineData("A AND B", true)]
    [InlineData("SANDY", false)]
    [InlineData("Name", false)]
    public void ContainsImplicitExpressionOperator_Detects_Operators_And_Alias_Boundaries(
        string expression,
        bool expected)
    {
        var actual = CSharpExpressionTextSemantics.ContainsImplicitExpressionOperator(expression);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Call()", true)]
    [InlineData("Call ( )", true)]
    [InlineData("123Call()", false)]
    [InlineData("Call", false)]
    public void IsMethodCallLikeExpression_Returns_Expected_Result(string expression, bool expected)
    {
        var actual = CSharpExpressionTextSemantics.IsMethodCallLikeExpression(expression);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Person.Name", true)]
    [InlineData("Person.Name.Length", false)]
    [InlineData(".Name", false)]
    [InlineData("Name.", false)]
    public void IsMemberAccessLikeExpression_Returns_Expected_Result(string expression, bool expected)
    {
        var actual = CSharpExpressionTextSemantics.IsMemberAccessLikeExpression(expression);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Name", true)]
    [InlineData("_value", true)]
    [InlineData("123", false)]
    [InlineData("A-B", false)]
    public void IsBareIdentifierExpression_Returns_Expected_Result(string token, bool expected)
    {
        var actual = CSharpExpressionTextSemantics.IsBareIdentifierExpression(token);
        Assert.Equal(expected, actual);
    }
}
