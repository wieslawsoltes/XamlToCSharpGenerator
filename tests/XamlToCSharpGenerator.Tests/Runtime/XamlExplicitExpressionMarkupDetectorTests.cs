using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class XamlExplicitExpressionMarkupDetectorTests
{
    [Fact]
    public void ContainsExplicitExpressionMarkup_Returns_True_For_Attribute_Value()
    {
        const string xaml = """
                            <TextBlock xmlns="https://github.com/avaloniaui"
                                       Text="{= Count * Count}" />
                            """;

        Assert.True(XamlExplicitExpressionMarkupDetector.ContainsExplicitExpressionMarkup(xaml));
    }

    [Fact]
    public void ContainsExplicitExpressionMarkup_Returns_True_For_Element_Text()
    {
        const string xaml = """
                            <Setter xmlns="https://github.com/avaloniaui">
                              <Setter.Value>{= FirstName + '!' }</Setter.Value>
                            </Setter>
                            """;

        Assert.True(XamlExplicitExpressionMarkupDetector.ContainsExplicitExpressionMarkup(xaml));
    }

    [Fact]
    public void ContainsExplicitExpressionMarkup_Returns_False_For_Regular_Binding()
    {
        const string xaml = """
                            <TextBlock xmlns="https://github.com/avaloniaui"
                                       Text="{Binding Count}" />
                            """;

        Assert.False(XamlExplicitExpressionMarkupDetector.ContainsExplicitExpressionMarkup(xaml));
    }

    [Fact]
    public void ContainsExplicitExpressionMarkup_Uses_Heuristic_For_Malformed_Xaml()
    {
        const string xaml = "<TextBlock Text=\"{= Count * Count}\"";

        Assert.True(XamlExplicitExpressionMarkupDetector.ContainsExplicitExpressionMarkup(xaml));
    }
}
