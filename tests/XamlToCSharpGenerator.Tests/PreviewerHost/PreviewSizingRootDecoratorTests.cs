using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using XamlToCSharpGenerator.Previewer.DesignerHost;
using global::Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public class PreviewSizingRootDecoratorTests
{
    [AvaloniaFact]
    public void ApplySize_Uses_Preview_Pane_Size_When_Control_Has_No_Explicit_Size()
    {
        var control = new Border();

        PreviewSizingRootDecorator.ApplySize(control, 640, 480);

        Assert.Equal(640, control.Width);
        Assert.Equal(480, control.Height);
    }

    [AvaloniaFact]
    public void ApplySize_Preserves_Explicit_Control_Size()
    {
        var control = new Border
        {
            Width = 320,
            Height = 240
        };

        PreviewSizingRootDecorator.ApplySize(control, 640, 480);

        Assert.Equal(320, control.Width);
        Assert.Equal(240, control.Height);
    }

    [AvaloniaFact]
    public void Apply_Uses_Configured_Fallback_Size()
    {
        var control = new Border();

        PreviewSizingRootDecorator.Configure(800, 600);
        PreviewSizingRootDecorator.Apply(control);

        Assert.Equal(800, control.Width);
        Assert.Equal(600, control.Height);
    }

    [AvaloniaFact]
    public void Apply_Uses_Authored_Design_Size_Before_Configured_Fallback()
    {
        var control = new Border();

        PreviewSizingRootDecorator.Configure(640, 360);
        PreviewSizingRootDecorator.Apply(
            control,
            """
            <Border xmlns="https://github.com/avaloniaui"
                    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                    d:DesignWidth="800"
                    d:DesignHeight="450" />
            """);

        Assert.Equal(800, control.Width);
        Assert.Equal(450, control.Height);
    }

    [AvaloniaFact]
    public void Apply_Returns_Sized_Host_For_ResourceDictionary_Without_PreviewWith()
    {
        var dictionary = new ResourceDictionary();

        PreviewSizingRootDecorator.Configure(720, 480);
        var result = Assert.IsType<Border>(PreviewSizingRootDecorator.Apply(dictionary));

        Assert.Equal(720, result.Width);
        Assert.Equal(480, result.Height);
        Assert.NotNull(result.Resources);
        Assert.Contains(dictionary, result.Resources!.MergedDictionaries);
    }

    [AvaloniaFact]
    public void Apply_Uses_DesignPreviewWith_For_ResourceDictionary_When_Available()
    {
        var dictionary = new ResourceDictionary();
        var previewHost = new Border();
        Design.SetPreviewWith(dictionary, previewHost);

        PreviewSizingRootDecorator.Configure(640, 360);
        var result = PreviewSizingRootDecorator.Apply(dictionary);
        Assert.Same(previewHost, result);

        Assert.Equal(640, previewHost.Width);
        Assert.Equal(360, previewHost.Height);
        Assert.NotNull(previewHost.Resources);
        Assert.Contains(dictionary, previewHost.Resources!.MergedDictionaries);
    }

    [AvaloniaFact]
    public void Apply_Uses_Runtime_Loaded_DesignPreviewWith_For_ResourceDictionary()
    {
        const string xaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Design.PreviewWith>
                <Border>
                  <TextBlock Text="Slider preview host" />
                </Border>
              </Design.PreviewWith>
              <Thickness x:Key="SliderPadding">12</Thickness>
            </ResourceDictionary>
            """;

        var dictionary = Assert.IsType<ResourceDictionary>(AvaloniaRuntimeXamlLoader.Load(
            new RuntimeXamlLoaderDocument(
                new Uri("avares://XamlToCSharpGenerator.Tests/Preview.axaml"),
                rootInstance: null,
                xaml),
            new RuntimeXamlLoaderConfiguration
            {
                LocalAssembly = typeof(PreviewSizingRootDecoratorTests).Assembly,
                DesignMode = true
            }));

        PreviewSizingRootDecorator.Configure(560, 420);
        var result = Assert.IsType<Border>(PreviewSizingRootDecorator.Apply(dictionary));

        Assert.Equal(560, result.Width);
        Assert.Equal(420, result.Height);
        Assert.Equal("Slider preview host", Assert.IsType<TextBlock>(result.Child).Text);
        Assert.NotNull(result.Resources);
        Assert.Contains(dictionary, result.Resources!.MergedDictionaries);
    }

    [AvaloniaFact]
    public void Apply_Returns_Sized_Host_For_Style_Without_PreviewWith()
    {
        var style = new Style(static selector => selector.OfType<Button>());

        PreviewSizingRootDecorator.Configure(700, 420);
        var result = Assert.IsType<Border>(PreviewSizingRootDecorator.Apply(style));

        Assert.Equal(700, result.Width);
        Assert.Equal(420, result.Height);
        Assert.Contains(style, result.Styles);
    }

    [AvaloniaFact]
    public void Apply_Returns_Info_Text_For_Application_Root()
    {
        var application = new Application();

        var result = Assert.IsType<TextBlock>(PreviewSizingRootDecorator.Apply(application));

        Assert.Equal("This file cannot be previewed in design view", result.Text);
    }
}
