using Avalonia.Markup.Xaml;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class AvaloniaSourceGeneratedXamlLoaderTests
{
    [Fact]
    public void TryLoad_Resolves_Relative_Uri_Against_UriContext_BaseUri()
    {
        XamlSourceGenRegistry.Clear();
        XamlSourceGenRegistry.Register(
            "avares://Demo/Accents/BaseColorsPalette.xaml",
            static _ => ExpectedInstance);

        var serviceProvider = new TestUriContextServiceProvider(new Uri("avares://Demo/FluentTheme.xaml"));
        var loaded = AvaloniaSourceGeneratedXamlLoader.TryLoad(
            serviceProvider,
            new Uri("/Accents/BaseColorsPalette.xaml", UriKind.RelativeOrAbsolute),
            out var value);

        Assert.True(loaded);
        Assert.Same(ExpectedInstance, value);
    }

    [Fact]
    public void TryLoad_Returns_False_For_Unresolvable_Relative_Uri_Without_UriContext()
    {
        XamlSourceGenRegistry.Clear();
        XamlSourceGenRegistry.Register(
            "avares://Demo/Accents/BaseColorsPalette.xaml",
            static _ => new object());

        var loaded = AvaloniaSourceGeneratedXamlLoader.TryLoad(
            serviceProvider: null,
            new Uri("/Accents/BaseColorsPalette.xaml", UriKind.RelativeOrAbsolute),
            out var value);

        Assert.False(loaded);
        Assert.Null(value);
    }

    private static readonly object ExpectedInstance = new();

    private sealed class TestUriContextServiceProvider : IServiceProvider, IUriContext
    {
        public TestUriContextServiceProvider(Uri baseUri)
        {
            BaseUri = baseUri;
        }

        public Uri BaseUri { get; set; }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IUriContext) ? this : null;
        }
    }
}
