using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignCanvasState
{
    public double Zoom { get; set; } = 1.0;

    public string FormFactor { get; set; } = "Desktop";

    public double Width { get; set; } = 1366;

    public double Height { get; set; } = 768;

    public bool AutoSyncProjectFile { get; set; } = true;

    public bool DarkTheme { get; set; }

    public SourceGenHotDesignCanvasState Clone()
    {
        return new SourceGenHotDesignCanvasState
        {
            Zoom = Zoom,
            FormFactor = FormFactor,
            Width = Width,
            Height = Height,
            AutoSyncProjectFile = AutoSyncProjectFile,
            DarkTheme = DarkTheme
        };
    }

    public void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, 0.1, 5.0);
    }
}
