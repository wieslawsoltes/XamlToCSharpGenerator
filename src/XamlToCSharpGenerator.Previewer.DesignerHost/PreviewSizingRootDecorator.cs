using global::Avalonia.Controls;
using global::Avalonia.Layout;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewSizingRootDecorator
{
    private static readonly object Sync = new();
    private static double? s_previewWidth;
    private static double? s_previewHeight;

    public static void Configure(double? previewWidth, double? previewHeight)
    {
        lock (Sync)
        {
            s_previewWidth = NormalizeSize(previewWidth);
            s_previewHeight = NormalizeSize(previewHeight);
        }
    }

    public static object Apply(object loadedRoot)
    {
        ArgumentNullException.ThrowIfNull(loadedRoot);

        if (loadedRoot is not Control control)
        {
            return loadedRoot;
        }

        double? previewWidth;
        double? previewHeight;
        lock (Sync)
        {
            previewWidth = s_previewWidth;
            previewHeight = s_previewHeight;
        }

        if (previewWidth is null && previewHeight is null)
        {
            return loadedRoot;
        }

        ApplySize(control, previewWidth, previewHeight);
        return loadedRoot;
    }

    internal static void ApplySize(Control control, double? previewWidth, double? previewHeight)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (previewWidth is > 0 && !control.IsSet(Layoutable.WidthProperty))
        {
            control.Width = previewWidth.Value;
        }

        if (previewHeight is > 0 && !control.IsSet(Layoutable.HeightProperty))
        {
            control.Height = previewHeight.Value;
        }
    }

    private static double? NormalizeSize(double? value)
    {
        return value is > 0 ? value : null;
    }
}
