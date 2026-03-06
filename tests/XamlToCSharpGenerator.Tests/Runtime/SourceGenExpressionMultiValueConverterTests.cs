using System.Globalization;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class SourceGenExpressionMultiValueConverterTests
{
    [Fact]
    public void Convert_Coerces_Numeric_Result_To_String_Target()
    {
        var converter = new SourceGenExpressionMultiValueConverter<TestExpressionSource>(static source => (source.Count * 2) + 1);

        var value = converter.Convert(
            [new TestExpressionSource { Count = 4 }],
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Equal("9", value);
    }

    [Fact]
    public void Convert_Evaluates_Null_Coalescing_And_Indexer_Expressions()
    {
        var converter = new SourceGenExpressionMultiValueConverter<TestExpressionSource>(
            static source => source.Nickname ?? ("alias:" + source.FirstName));

        var value = converter.Convert(
            [new TestExpressionSource { FirstName = "Ava", Nickname = null }],
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Equal("alias:Ava", value);
    }

    [Fact]
    public void Convert_Evaluates_Method_And_Format_Expressions()
    {
        var summaryConverter = new SourceGenExpressionMultiValueConverter<TestExpressionSource>(
            static source => source.FormatSummary(source.FirstName, source.LastName, source.Count));
        var summaryValue = summaryConverter.Convert(
            [new TestExpressionSource { FirstName = "Ava", LastName = "SourceGen", Count = 4 }],
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Equal("Ava.SourceGen (4)", summaryValue);

        var tagsConverter = new SourceGenExpressionMultiValueConverter<TestExpressionSource>(
            static source => source.Tags[0] + ", " + source.Tags[1]);
        var tagsValue = tagsConverter.Convert(
            [new TestExpressionSource { Tags = ["xaml", "generator"] }],
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Equal("xaml, generator", tagsValue);

        var converter = new SourceGenExpressionMultiValueConverter<TestExpressionSource>(
            static source => (source.Price * (1 + source.TaxRate)).ToString("0.00", CultureInfo.InvariantCulture));

        var value = converter.Convert(
            [new TestExpressionSource { Price = 12.5m, TaxRate = 0.23m }],
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Equal("15.38", value);
    }

    private sealed class TestExpressionSource
    {
        public int Count { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string? Nickname { get; set; }

        public string LastName { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public decimal TaxRate { get; set; }

        public string[] Tags { get; set; } = [];

        public string FormatSummary(string first, string last, int count)
        {
            return first + "." + last + " (" + count.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
