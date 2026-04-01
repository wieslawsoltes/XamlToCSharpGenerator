using Avalonia.Markup.Xaml;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class AvaloniaSourceGeneratedXamlLoaderTests : IDisposable
{
    public void Dispose()
    {
        GeneratedArtifactTestRestore.RestoreAllLoadedGeneratedArtifacts();
    }

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

    [Fact]
    public void TryLoad_Resolves_Rooted_Relative_Uri_Using_RootObject_Assembly_When_UriContext_Is_Missing()
    {
        XamlSourceGenRegistry.Clear();
        var assemblyName = typeof(RootAnchor).Assembly.GetName().Name;
        Assert.False(string.IsNullOrWhiteSpace(assemblyName));

        XamlSourceGenRegistry.Register(
            "avares://" + assemblyName + "/Accents/BaseColorsPalette.xaml",
            static _ => ExpectedInstance);

        var loaded = AvaloniaSourceGeneratedXamlLoader.TryLoad(
            new TestRootObjectServiceProvider(new RootAnchor()),
            new Uri("/Accents/BaseColorsPalette.xaml", UriKind.RelativeOrAbsolute),
            out var value);

        Assert.True(loaded);
        Assert.Same(ExpectedInstance, value);
    }

    [Fact]
    public void TryLoad_Resolves_Relative_Uri_Against_Explicit_BaseUri()
    {
        XamlSourceGenRegistry.Clear();
        XamlSourceGenRegistry.Register(
            "avares://Demo/Accents/BaseColorsPalette.xaml",
            static _ => ExpectedInstance);

        var loaded = AvaloniaSourceGeneratedXamlLoader.TryLoad(
            serviceProvider: null,
            new Uri("/Accents/BaseColorsPalette.xaml", UriKind.RelativeOrAbsolute),
            baseUri: new Uri("avares://Demo/FluentTheme.xaml"),
            out var value);

        Assert.True(loaded);
        Assert.Same(ExpectedInstance, value);
    }

    [Fact]
    public void TryLoad_Prefers_Explicit_BaseUri_Over_UriContext_BaseUri()
    {
        XamlSourceGenRegistry.Clear();
        XamlSourceGenRegistry.Register(
            "avares://Demo/Accents/BaseColorsPalette.xaml",
            static _ => ExpectedInstance);
        XamlSourceGenRegistry.Register(
            "avares://Wrong/Accents/BaseColorsPalette.xaml",
            static _ => new object());

        var loaded = AvaloniaSourceGeneratedXamlLoader.TryLoad(
            new TestUriContextServiceProvider(new Uri("avares://Wrong/FluentTheme.xaml")),
            new Uri("/Accents/BaseColorsPalette.xaml", UriKind.RelativeOrAbsolute),
            baseUri: new Uri("avares://Demo/FluentTheme.xaml"),
            out var value);

        Assert.True(loaded);
        Assert.Same(ExpectedInstance, value);
    }

    [Fact]
    public void TryLoad_Missing_Relative_Uri_Raises_One_Missing_Event_For_Preferred_Candidate()
    {
        XamlSourceGenRegistry.Clear();
        var notifications = new List<string>();

        void Handler(string uri) => notifications.Add(uri);

        XamlSourceGenRegistry.MissingUriRequested += Handler;
        try
        {
            var loaded = AvaloniaSourceGeneratedXamlLoader.TryLoad(
                new TestUriContextServiceProvider(new Uri("avares://Wrong/FluentTheme.xaml")),
                new Uri("/Accents/MissingPalette.xaml", UriKind.RelativeOrAbsolute),
                baseUri: new Uri("avares://Demo/FluentTheme.xaml"),
                out var value);

            Assert.False(loaded);
            Assert.Null(value);
            Assert.Single(notifications);
            Assert.True(
                XamlSourceGenUriMapper.Default.UriEquals(
                    "avares://Demo/Accents/MissingPalette.xaml",
                    notifications[0]));
        }
        finally
        {
            XamlSourceGenRegistry.MissingUriRequested -= Handler;
            XamlSourceGenRegistry.Clear();
        }
    }

    [Fact]
    public void Load_With_Explicit_BaseUri_Returns_SourceGenerated_Value()
    {
        XamlSourceGenRegistry.Clear();
        XamlSourceGenRegistry.Register(
            "avares://Demo/Accents/BaseColorsPalette.xaml",
            static _ => ExpectedInstance);

        var value = AvaloniaSourceGeneratedXamlLoader.Load(
            new Uri("/Accents/BaseColorsPalette.xaml", UriKind.RelativeOrAbsolute),
            new Uri("avares://Demo/FluentTheme.xaml"));

        Assert.Same(ExpectedInstance, value);
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

    private sealed class TestRootObjectServiceProvider : IServiceProvider, IRootObjectProvider
    {
        public TestRootObjectServiceProvider(object rootObject)
        {
            RootObject = rootObject;
            IntermediateRootObject = rootObject;
        }

        public object RootObject { get; }

        public object IntermediateRootObject { get; }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IRootObjectProvider) ? this : null;
        }
    }

    private sealed class RootAnchor
    {
    }
}
