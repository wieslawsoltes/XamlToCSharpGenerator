using System.Globalization;
using System.Xml.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Styling;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewSizingRootDecorator
{
    private const string DesignNamespaceUri = "http://schemas.microsoft.com/expression/blend/2008";
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

    public static object Apply(object loadedRoot, string? authoredXaml = null)
    {
        ArgumentNullException.ThrowIfNull(loadedRoot);

        double? previewWidth;
        double? previewHeight;
        lock (Sync)
        {
            previewWidth = s_previewWidth;
            previewHeight = s_previewHeight;
        }

        var authoredSize = TryExtractDesignSize(authoredXaml);
        previewWidth = authoredSize.Width ?? previewWidth;
        previewHeight = authoredSize.Height ?? previewHeight;

        if (loadedRoot is IStyle style)
        {
            return PrepareStylePreview(style, previewWidth, previewHeight);
        }

        if (loadedRoot is ResourceDictionary resources)
        {
            return PrepareResourceDictionaryPreview(resources, previewWidth, previewHeight);
        }

        if (loadedRoot is Control control)
        {
            return ApplyConfiguredSize(control, previewWidth, previewHeight);
        }

        if (loadedRoot is Application)
        {
            return CreateInfoTextBlock("This file cannot be previewed in design view");
        }

        if (loadedRoot is AvaloniaObject avaloniaObject &&
            loadedRoot is not Window &&
            TryGetPreviewWith(avaloniaObject) is { } previewWith)
        {
            return ApplyConfiguredSize(previewWith, previewWidth, previewHeight);
        }

        return CreateInfoTextBlock("This file cannot be previewed in design view");
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

    private static (double? Width, double? Height) TryExtractDesignSize(string? authoredXaml)
    {
        if (string.IsNullOrWhiteSpace(authoredXaml))
        {
            return default;
        }

        try
        {
            var document = XDocument.Parse(authoredXaml, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
            {
                return default;
            }

            return (
                ParseDesignSize(document.Root.Attribute(XName.Get("DesignWidth", DesignNamespaceUri))?.Value),
                ParseDesignSize(document.Root.Attribute(XName.Get("DesignHeight", DesignNamespaceUri))?.Value));
        }
        catch
        {
            return default;
        }
    }

    private static double? ParseDesignSize(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static object PrepareStylePreview(IStyle style, double? previewWidth, double? previewHeight)
    {
        ArgumentNullException.ThrowIfNull(style);

        var substitute = (style as AvaloniaObject is { } styleObject
            ? TryGetPreviewWith(styleObject)
            : null) ?? CreateDefaultPreviewHost();
        substitute.Styles.Add(style);
        return ApplyConfiguredSize(substitute, previewWidth, previewHeight);
    }

    private static object PrepareResourceDictionaryPreview(
        ResourceDictionary resources,
        double? previewWidth,
        double? previewHeight)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var substitute = TryGetPreviewWith(resources) ?? CreateDefaultPreviewHost();
        EnsureResources(substitute).MergedDictionaries.Add(resources);
        return ApplyConfiguredSize(substitute, previewWidth, previewHeight);
    }

    private static Control ApplyConfiguredSize(Control control, double? previewWidth, double? previewHeight)
    {
        if (previewWidth is null && previewHeight is null)
        {
            return control;
        }

        ApplySize(control, previewWidth, previewHeight);
        return control;
    }

    private static IResourceDictionary EnsureResources(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.Resources ??= new ResourceDictionary();
        return control.Resources;
    }

    private static Control? TryGetPreviewWith(AvaloniaObject avaloniaObject)
    {
        ArgumentNullException.ThrowIfNull(avaloniaObject);

        try
        {
            return Design.GetPreviewWith(avaloniaObject);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static Control? TryGetPreviewWith(ResourceDictionary resourceDictionary)
    {
        ArgumentNullException.ThrowIfNull(resourceDictionary);

        try
        {
            return Design.GetPreviewWith(resourceDictionary);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static Control CreateDefaultPreviewHost()
    {
        var samplePanel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "AXSG preview sample",
                    FontWeight = FontWeight.SemiBold
                },
                new Button
                {
                    Content = "Sample Button",
                    HorizontalAlignment = HorizontalAlignment.Left
                },
                new ToggleButton
                {
                    Content = "Sample Toggle",
                    IsChecked = true,
                    HorizontalAlignment = HorizontalAlignment.Left
                },
                new CheckBox
                {
                    Content = "Sample CheckBox",
                    IsChecked = true,
                    HorizontalAlignment = HorizontalAlignment.Left
                },
                new TextBox
                {
                    Width = 240,
                    Text = "Sample Text",
                    Watermark = "Preview Watermark"
                },
                new ComboBox
                {
                    Width = 240,
                    SelectedIndex = 0,
                    ItemsSource = new[] { "First", "Second", "Third" }
                },
                new Slider
                {
                    Width = 240,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 42
                }
            }
        };

        return new Border
        {
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = samplePanel
            }
        };
    }

    private static Control CreateInfoTextBlock(string message)
    {
        return new TextBlock { Text = message };
    }
}
