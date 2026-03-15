using System.Collections.Generic;
using XamlToCSharpGenerator.PreviewerHost.Protocol;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class MinimalBsonTests
{
    [Fact]
    public void SerializeDocument_RoundTrips_Nested_String_And_Int32_Values()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Xaml"] = "<UserControl />",
            ["AssemblyPath"] = "/tmp/Demo.dll",
            ["Exception"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ExceptionType"] = "XmlException",
                ["Message"] = "Bad XAML.",
                ["LineNumber"] = 12,
                ["LinePosition"] = 4
            }
        };

        var bson = MinimalBson.SerializeDocument(payload);
        var roundTripped = MinimalBson.DeserializeDocument(bson);

        Assert.Equal("<UserControl />", roundTripped["Xaml"]);
        Assert.Equal("/tmp/Demo.dll", roundTripped["AssemblyPath"]);

        var exception = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(roundTripped["Exception"]);
        Assert.Equal("XmlException", exception["ExceptionType"]);
        Assert.Equal("Bad XAML.", exception["Message"]);
        Assert.Equal(12, exception["LineNumber"]);
        Assert.Equal(4, exception["LinePosition"]);
    }

    [Fact]
    public void SerializeDocument_Preserves_Null_And_Boolean_Values()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Error"] = null,
            ["IsEnabled"] = true,
            ["Port"] = 45831
        };

        var bson = MinimalBson.SerializeDocument(payload);
        var roundTripped = MinimalBson.DeserializeDocument(bson);

        Assert.Null(roundTripped["Error"]);
        Assert.Equal(true, roundTripped["IsEnabled"]);
        Assert.Equal(45831, roundTripped["Port"]);
    }

    [Fact]
    public void SerializeDocument_RoundTrips_Double_Values()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Width"] = 1280d,
            ["Height"] = 800d,
            ["DpiX"] = 96d,
            ["DpiY"] = 96d
        };

        var bson = MinimalBson.SerializeDocument(payload);
        var roundTripped = MinimalBson.DeserializeDocument(bson);

        Assert.Equal(1280d, Assert.IsType<double>(roundTripped["Width"]));
        Assert.Equal(800d, Assert.IsType<double>(roundTripped["Height"]));
        Assert.Equal(96d, Assert.IsType<double>(roundTripped["DpiX"]));
        Assert.Equal(96d, Assert.IsType<double>(roundTripped["DpiY"]));
    }
}
