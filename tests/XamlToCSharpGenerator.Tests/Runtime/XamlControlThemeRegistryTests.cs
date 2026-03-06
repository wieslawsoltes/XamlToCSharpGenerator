using Avalonia.Controls;
using Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeRegistry")]
public class XamlControlThemeRegistryTests
{
    [Fact]
    public void TryMaterialize_By_Key_Resolves_BasedOn_Chain()
    {
        XamlControlThemeRegistry.Clear();
        try
        {
            XamlControlThemeRegistry.Register(
                "avares://Demo/Themes.axaml",
                "Theme.Base",
                "global::Avalonia.Controls.Button",
                basedOn: null,
                themeVariant: null,
                rawXaml: "<ControlTheme x:Key=\"Theme.Base\" />",
                factory: static () =>
                {
                    var theme = new ControlTheme(typeof(Button));
                    theme.Setters.Add(new Setter(Button.ContentProperty, "Base"));
                    return theme;
                });

            XamlControlThemeRegistry.Register(
                "avares://Demo/Themes.axaml",
                "Theme.Primary",
                "global::Avalonia.Controls.Button",
                basedOn: "{StaticResource Theme.Base}",
                themeVariant: "Dark",
                rawXaml: "<ControlTheme x:Key=\"Theme.Primary\" />",
                factory: static () =>
                {
                    var theme = new ControlTheme(typeof(Button));
                    theme.Setters.Add(new Setter(Button.ContentProperty, "Primary"));
                    return theme;
                });

            var created = XamlControlThemeRegistry.TryMaterialize(
                "avares://Demo/Themes.axaml",
                "Theme.Primary",
                out var theme);

            Assert.True(created);
            var materialized = Assert.IsType<ControlTheme>(theme);
            Assert.NotNull(materialized.BasedOn);

            var primarySetter = Assert.IsType<Setter>(Assert.Single(materialized.Setters));
            Assert.Equal("Primary", primarySetter.Value);

            var baseTheme = Assert.IsType<ControlTheme>(materialized.BasedOn);
            var baseSetter = Assert.IsType<Setter>(Assert.Single(baseTheme.Setters));
            Assert.Equal("Base", baseSetter.Value);
        }
        finally
        {
            XamlControlThemeRegistry.Clear();
        }
    }

    [Fact]
    public void TryMaterialize_By_Key_Resolves_DynamicResource_BasedOn_Chain()
    {
        XamlControlThemeRegistry.Clear();
        try
        {
            XamlControlThemeRegistry.Register(
                "avares://Demo/Themes.axaml",
                "Theme.Base",
                "global::Avalonia.Controls.Button",
                basedOn: null,
                themeVariant: null,
                rawXaml: "<ControlTheme x:Key=\"Theme.Base\" />",
                factory: static () =>
                {
                    var theme = new ControlTheme(typeof(Button));
                    theme.Setters.Add(new Setter(Button.ContentProperty, "Base"));
                    return theme;
                });

            XamlControlThemeRegistry.Register(
                "avares://Demo/Themes.axaml",
                "Theme.Dynamic",
                "global::Avalonia.Controls.Button",
                basedOn: "{DynamicResource Theme.Base}",
                themeVariant: null,
                rawXaml: "<ControlTheme x:Key=\"Theme.Dynamic\" />",
                factory: static () =>
                {
                    var theme = new ControlTheme(typeof(Button));
                    theme.Setters.Add(new Setter(Button.ContentProperty, "Dynamic"));
                    return theme;
                });

            var created = XamlControlThemeRegistry.TryMaterialize(
                "avares://Demo/Themes.axaml",
                "Theme.Dynamic",
                out var theme);

            Assert.True(created);
            var materialized = Assert.IsType<ControlTheme>(theme);
            Assert.NotNull(materialized.BasedOn);
            Assert.Equal(
                "Base",
                Assert.IsType<Setter>(Assert.Single(Assert.IsType<ControlTheme>(materialized.BasedOn).Setters)).Value);
        }
        finally
        {
            XamlControlThemeRegistry.Clear();
        }
    }

    [Fact]
    public void TryMaterialize_By_TargetType_Uses_ThemeVariant_And_Default_Fallback()
    {
        XamlControlThemeRegistry.Clear();
        try
        {
            XamlControlThemeRegistry.Register(
                "avares://Demo/Themes.axaml",
                "Theme.Button.Default",
                "global::Avalonia.Controls.Button",
                basedOn: null,
                themeVariant: "Default",
                rawXaml: "<ControlTheme x:Key=\"Theme.Button.Default\" />",
                factory: static () =>
                {
                    var theme = new ControlTheme(typeof(Button));
                    theme.Setters.Add(new Setter(Button.ContentProperty, "Default"));
                    return theme;
                });

            XamlControlThemeRegistry.Register(
                "avares://Demo/Themes.axaml",
                "Theme.Button.Dark",
                "global::Avalonia.Controls.Button",
                basedOn: null,
                themeVariant: "Dark",
                rawXaml: "<ControlTheme x:Key=\"Theme.Button.Dark\" />",
                factory: static () =>
                {
                    var theme = new ControlTheme(typeof(Button));
                    theme.Setters.Add(new Setter(Button.ContentProperty, "Dark"));
                    return theme;
                });

            var darkCreated = XamlControlThemeRegistry.TryMaterialize(
                "avares://Demo/Themes.axaml",
                typeof(Button),
                "Dark",
                out var darkTheme);
            Assert.True(darkCreated);
            Assert.Equal(
                "Dark",
                Assert.IsType<Setter>(Assert.Single(Assert.IsType<ControlTheme>(darkTheme).Setters)).Value);

            var fallbackCreated = XamlControlThemeRegistry.TryMaterialize(
                "avares://Demo/Themes.axaml",
                typeof(Button),
                "Light",
                out var fallbackTheme);
            Assert.True(fallbackCreated);
            Assert.Equal(
                "Default",
                Assert.IsType<Setter>(Assert.Single(Assert.IsType<ControlTheme>(fallbackTheme).Setters)).Value);
        }
        finally
        {
            XamlControlThemeRegistry.Clear();
        }
    }

    [Fact]
    public void TryMaterialize_Returns_False_For_Metadata_Only_Registration()
    {
        XamlControlThemeRegistry.Clear();
        try
        {
            XamlControlThemeRegistry.Register(
                "avares://Demo/Themes.axaml",
                "Theme.Button",
                "global::Avalonia.Controls.Button",
                basedOn: null,
                themeVariant: null,
                rawXaml: "<ControlTheme x:Key=\"Theme.Button\" />");

            var created = XamlControlThemeRegistry.TryMaterialize(
                "avares://Demo/Themes.axaml",
                "Theme.Button",
                out var theme);

            Assert.False(created);
            Assert.Null(theme);
        }
        finally
        {
            XamlControlThemeRegistry.Clear();
        }
    }
}
