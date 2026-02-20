using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeRegistry")]
public class XamlSourceGenRegistryTests
{
    [Fact]
    public void Register_Duplicate_Uri_Raises_Conflict_Event_And_Replaces_Entry()
    {
        XamlSourceGenRegistry.Clear();
        var duplicateUri = string.Empty;

        void Handler(string uri) => duplicateUri = uri;

        XamlSourceGenRegistry.DuplicateUriRegistration += Handler;
        try
        {
            XamlSourceGenRegistry.Register("avares://Demo/Main.axaml", static _ => "first");
            XamlSourceGenRegistry.Register("avares://Demo/Main.axaml", static _ => "second");

            var created = XamlSourceGenRegistry.TryCreate(null, "avares://Demo/Main.axaml", out var value);

            Assert.True(created);
            Assert.Equal("second", value);
            Assert.Equal("avares://Demo/Main.axaml", duplicateUri);
        }
        finally
        {
            XamlSourceGenRegistry.DuplicateUriRegistration -= Handler;
            XamlSourceGenRegistry.Clear();
        }
    }

    [Fact]
    public void TryCreate_Missing_Uri_Raises_Missing_Event()
    {
        XamlSourceGenRegistry.Clear();
        var missingUri = string.Empty;

        void Handler(string uri) => missingUri = uri;

        XamlSourceGenRegistry.MissingUriRequested += Handler;
        try
        {
            var created = XamlSourceGenRegistry.TryCreate(null, "avares://Demo/Missing.axaml", out var value);

            Assert.False(created);
            Assert.Null(value);
            Assert.Equal("avares://Demo/Missing.axaml", missingUri);
        }
        finally
        {
            XamlSourceGenRegistry.MissingUriRequested -= Handler;
            XamlSourceGenRegistry.Clear();
        }
    }

    [Fact]
    public void Unregister_Removes_Registered_Uri()
    {
        XamlSourceGenRegistry.Clear();
        const string uri = "avares://Demo/Main.axaml";

        XamlSourceGenRegistry.Register(uri, static _ => "value");
        XamlSourceGenRegistry.Unregister(uri);

        var created = XamlSourceGenRegistry.TryCreate(null, uri, out var value);

        Assert.False(created);
        Assert.Null(value);
    }
}
