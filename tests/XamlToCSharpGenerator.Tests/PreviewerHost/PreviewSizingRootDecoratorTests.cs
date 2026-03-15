using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using XamlToCSharpGenerator.Previewer.DesignerHost;

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
}
