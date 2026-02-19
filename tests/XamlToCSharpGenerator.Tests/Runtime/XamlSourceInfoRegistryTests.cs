using System.Linq;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeRegistry")]
public class XamlSourceInfoRegistryTests
{
    [Fact]
    public void GetAll_Returns_Deterministic_Ordering()
    {
        XamlSourceInfoRegistry.Clear();
        try
        {
            XamlSourceInfoRegistry.Register("avares://Demo/Main.axaml", "Property", "B", "/tmp/Main.axaml", 8, 4);
            XamlSourceInfoRegistry.Register("avares://Demo/Main.axaml", "Object", "A", "/tmp/Main.axaml", 2, 2);
            XamlSourceInfoRegistry.Register("avares://Demo/Main.axaml", "Property", "A", "/tmp/Main.axaml", 7, 2);

            var all = XamlSourceInfoRegistry.GetAll("avares://Demo/Main.axaml").ToArray();

            Assert.Equal(3, all.Length);
            Assert.Equal("Object", all[0].Kind);
            Assert.Equal("A", all[0].Name);
            Assert.Equal("Property", all[1].Kind);
            Assert.Equal("A", all[1].Name);
            Assert.Equal("Property", all[2].Kind);
            Assert.Equal("B", all[2].Name);
        }
        finally
        {
            XamlSourceInfoRegistry.Clear();
        }
    }

    [Fact]
    public void GetByKind_And_TryGet_Return_Expected_Entries()
    {
        XamlSourceInfoRegistry.Clear();
        try
        {
            XamlSourceInfoRegistry.Register("avares://Demo/Main.axaml", "StyleSetter", "Style:0/Setter:0:Text", "/tmp/Main.axaml", 12, 6);
            XamlSourceInfoRegistry.Register("avares://Demo/Main.axaml", "Property", "Object:0/Property:0:Content", "/tmp/Main.axaml", 5, 3);

            var setters = XamlSourceInfoRegistry.GetByKind("avares://Demo/Main.axaml", "StyleSetter");
            var setter = Assert.Single(setters);
            Assert.Equal("Style:0/Setter:0:Text", setter.Name);

            var found = XamlSourceInfoRegistry.TryGet(
                "avares://Demo/Main.axaml",
                "Property",
                "Object:0/Property:0:Content",
                out var descriptor);

            Assert.True(found);
            Assert.NotNull(descriptor);
            Assert.Equal(5, descriptor!.Line);
            Assert.Equal(3, descriptor.Column);
        }
        finally
        {
            XamlSourceInfoRegistry.Clear();
        }
    }
}
