using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Utils;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Themes.Fluent;
using XamlToCSharpGenerator.Runtime;

var report = new SortedDictionary<string, string>(StringComparer.Ordinal);

try
{
    AppBuilder
        .Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
        .SetupWithoutStarting();

    AvaloniaSourceGeneratedXamlLoader.Enable();

    var fluentTheme = new FluentTheme();
    if (Application.Current is { } application)
    {
        application.Styles.Clear();
        application.Styles.Add(fluentTheme);
    }

    report["Theme.ItemCount"] = fluentTheme.Count.ToString(CultureInfo.InvariantCulture);
    report["Theme.ResourceCount"] = fluentTheme.Resources.Count.ToString(CultureInfo.InvariantCulture);
    report["Theme.Resources.MergedCount"] = fluentTheme.Resources.MergedDictionaries.Count.ToString(CultureInfo.InvariantCulture);
    for (var i = 0; i < fluentTheme.Resources.MergedDictionaries.Count && i < 8; i++)
    {
        var merged = fluentTheme.Resources.MergedDictionaries[i];
        report[$"Theme.Resources.Merged[{i}].Type"] = merged.GetType().FullName ?? merged.GetType().Name;
    }

    ProbeFirstStyleContainer(fluentTheme, report);
    ProbeControlTheme(fluentTheme, typeof(Button), "Theme.Button", report);
    ProbeControlTheme(fluentTheme, typeof(TextBox), "Theme.TextBox", report);
    ProbeControlTheme(fluentTheme, typeof(Window), "Theme.Window", report);
    ProbeControlTheme(fluentTheme, "FluentTextBoxButton", "Theme.FluentTextBoxButton", report);
    ProbeTemplateMaterialization(
        fluentTheme,
        typeof(Button),
        static () => new Button(),
        "TemplateApply.Button",
        report);
    ProbeTemplateMaterialization(
        fluentTheme,
        typeof(TextBox),
        static () => new TextBox(),
        "TemplateApply.TextBox",
        report);
    ProbeWindowTemplateOverlayMaterialization(fluentTheme, report);
    ProbeTextBoxInteractionStates(fluentTheme, "TextBoxStates", report);
    ProbeSliderInteractionStates(fluentTheme, "SliderStates", report);

    ProbeResource(fluentTheme, "ButtonPadding", "Resource.ButtonPadding.Default", report);
    ProbeResource(fluentTheme, "SystemControlForegroundBaseHighBrush", "Resource.SystemControlForegroundBaseHighBrush.Default", report);
    ProbeResource(fluentTheme, "SystemControlHighlightAccentBrush", "Resource.SystemControlHighlightAccentBrush.Default", report);
    ProbeResource(fluentTheme, "TextControlBorderBrushFocused", "Resource.TextControlBorderBrushFocused.Default", report);
    ProbeResource(fluentTheme, "TextControlSelectionHighlightColor", "Resource.TextControlSelectionHighlightColor.Default", report);
    ProbeResource(fluentTheme, "ToggleButtonBackgroundChecked", "Resource.ToggleButtonBackgroundChecked.Default", report);
    ProbeResource(fluentTheme, "SliderThumbBackgroundPointerOver", "Resource.SliderThumbBackgroundPointerOver.Default", report);
    ProbeResource(fluentTheme, "SliderThumbBackgroundPressed", "Resource.SliderThumbBackgroundPressed.Default", report);
    ProbeResource(fluentTheme, "ToggleButtonBackgroundCheckedPointerOver", "Resource.ToggleButtonBackgroundCheckedPointerOver.Default", report);
    ProbeResource(fluentTheme, "HorizontalMenuFlyoutPresenter", "Resource.HorizontalMenuFlyoutPresenter.Default", report);
    ProbeDirectGeneratedLoad(
        "avares://Avalonia.Themes.Fluent/Controls/Button.xaml",
        "DirectLoad.ButtonXaml",
        report);
    ProbeDirectGeneratedLoad(
        "avares://Avalonia.Themes.Fluent/Accents/BaseResources.xaml",
        "DirectLoad.BaseResourcesXaml",
        report);
    ProbeDirectGeneratedLoad(
        "avares://Avalonia.Themes.Fluent/Accents/FluentControlResources.xaml",
        "DirectLoad.FluentControlResourcesXaml",
        report);

    fluentTheme.DensityStyle = DensityStyle.Compact;
    ProbeResource(fluentTheme, "ButtonPadding", "Resource.ButtonPadding.Compact", report);
}
catch (Exception ex)
{
    report["Probe.Exception.Type"] = ex.GetType().FullName ?? ex.GetType().Name;
    report["Probe.Exception.Message"] = ex.Message;
    report["Probe.Exception.StackTrace"] = ex.StackTrace ?? string.Empty;
    if (ex.InnerException is not null)
    {
        report["Probe.Exception.InnerType"] = ex.InnerException.GetType().FullName ?? ex.InnerException.GetType().Name;
        report["Probe.Exception.InnerMessage"] = ex.InnerException.Message;
        report["Probe.Exception.InnerStackTrace"] = ex.InnerException.StackTrace ?? string.Empty;
    }
}

Console.WriteLine(JsonSerializer.Serialize(report));

static void ProbeFirstStyleContainer(Styles fluentTheme, IDictionary<string, string> report)
{
    if (fluentTheme.Count == 0)
    {
        report["Theme.FirstStyle.Exists"] = "false";
        return;
    }

    var firstItem = fluentTheme[0];
    report["Theme.FirstStyle.Exists"] = "true";
    report["Theme.FirstStyle.Type"] = firstItem.GetType().FullName ?? firstItem.GetType().Name;

    if (firstItem is StyleInclude include)
    {
        report["Theme.FirstStyle.Source"] = include.Source?.ToString() ?? string.Empty;
        try
        {
            var loaded = include.Loaded;
            report["Theme.FirstStyle.LoadedType"] = loaded?.GetType().FullName ?? "<null>";
            if (loaded is Styles loadedStyles)
            {
                report["Theme.FirstStyle.LoadedStylesCount"] = loadedStyles.Count.ToString(CultureInfo.InvariantCulture);
            }

            if (loaded is IResourceNode loadedResourceNode)
            {
                ProbeControlTheme(loadedResourceNode, typeof(Button), "Theme.FirstStyle.Button", report);
            }
        }
        catch (Exception ex)
        {
            report["Theme.FirstStyle.LoadException.Type"] = ex.GetType().FullName ?? ex.GetType().Name;
            report["Theme.FirstStyle.LoadException.Message"] = ex.Message;
        }
    }
    else if (firstItem is IResourceNode firstResourceNode)
    {
        ProbeControlTheme(firstResourceNode, typeof(Button), "Theme.FirstStyle.Button", report);
    }
}

static void ProbeControlTheme(
    IResourceNode node,
    object key,
    string prefix,
    IDictionary<string, string> report)
{
    if (!TryGetResource(node, key, out var value))
    {
        report[$"{prefix}.Found"] = "false";
        return;
    }

    report[$"{prefix}.Found"] = "true";
    report[$"{prefix}.Type"] = value?.GetType().FullName ?? "<null>";

    if (value is not ControlTheme controlTheme)
    {
        return;
    }

    report[$"{prefix}.Setters"] = controlTheme.Setters.Count.ToString(CultureInfo.InvariantCulture);
    report[$"{prefix}.HasTemplateSetter"] = controlTheme.Setters
        .OfType<Setter>()
        .Any(static setter => setter.Property == TemplatedControl.TemplateProperty)
        .ToString();
    report[$"{prefix}.TargetType"] = controlTheme.TargetType?.FullName ?? "<null>";
}

static void ProbeResource(
    IResourceNode node,
    object key,
    string prefix,
    IDictionary<string, string> report)
{
    if (!TryGetResource(node, key, out var value))
    {
        report[$"{prefix}.Found"] = "false";
        return;
    }

    report[$"{prefix}.Found"] = "true";
    report[$"{prefix}.Type"] = value?.GetType().FullName ?? "<null>";
    report[$"{prefix}.Summary"] = SummarizeValue(value);
}

static void ProbeTemplateMaterialization(
    IResourceNode node,
    object themeKey,
    Func<TemplatedControl> controlFactory,
    string prefix,
    IDictionary<string, string> report)
{
    if (!TryGetResource(node, themeKey, out var value) ||
        value is not ControlTheme controlTheme)
    {
        report[$"{prefix}.ThemeFound"] = "false";
        return;
    }

    report[$"{prefix}.ThemeFound"] = "true";

    try
    {
        var templateSetter = controlTheme.Setters
            .OfType<Setter>()
            .FirstOrDefault(static setter => setter.Property == TemplatedControl.TemplateProperty);
        if (templateSetter?.Value is not IControlTemplate template)
        {
            report[$"{prefix}.TemplateFound"] = "false";
            return;
        }

        report[$"{prefix}.TemplateFound"] = "true";

        var control = controlFactory();
        control.Theme = controlTheme;
        control.Template = template;

        var templateApplied = false;
        control.TemplateApplied += (_, _) => templateApplied = true;
        control.ApplyTemplate();

        report[$"{prefix}.Applied"] = templateApplied.ToString().ToLowerInvariant();
        report[$"{prefix}.VisualChildren"] = control
            .GetVisualChildren()
            .Count()
            .ToString(CultureInfo.InvariantCulture);
    }
    catch (Exception ex)
    {
        report[$"{prefix}.Exception.Type"] = ex.GetType().FullName ?? ex.GetType().Name;
        report[$"{prefix}.Exception.Message"] = ex.Message;
    }
}

static void ProbeWindowTemplateOverlayMaterialization(
    IResourceNode node,
    IDictionary<string, string> report)
{
    const string Prefix = "TemplateApply.Window";

    if (!TryGetResource(node, typeof(Window), out var value) ||
        value is not ControlTheme controlTheme)
    {
        report[$"{Prefix}.ThemeFound"] = "false";
        return;
    }

    report[$"{Prefix}.ThemeFound"] = "true";

    try
    {
        var templateSetter = controlTheme.Setters
            .OfType<Setter>()
            .FirstOrDefault(static setter => setter.Property == TemplatedControl.TemplateProperty);
        if (templateSetter?.Value is not IControlTemplate template)
        {
            report[$"{Prefix}.TemplateFound"] = "false";
            return;
        }

        report[$"{Prefix}.TemplateFound"] = "true";

        var window = new Window();
        window.Theme = controlTheme;
        window.Template = template;

        var templateResult = template.Build(window);
        report[$"{Prefix}.RootType"] = templateResult.Result?.GetType().FullName ?? "<null>";

        if (templateResult.Result is not Control root)
        {
            report[$"{Prefix}.OverlayLayerFound"] = "false";
            report[$"{Prefix}.TitleBarFound"] = "false";
            return;
        }

        var manager = root.GetVisualDescendants().OfType<VisualLayerManager>().FirstOrDefault();
        var overlayLayer = manager?.ChromeOverlayLayer;
        var titleBar = overlayLayer?.Children.OfType<TitleBar>().FirstOrDefault();

        report[$"{Prefix}.OverlayLayerFound"] = (overlayLayer is not null).ToString().ToLowerInvariant();
        report[$"{Prefix}.TitleBarFound"] = (titleBar is not null).ToString().ToLowerInvariant();
    }
    catch (Exception ex)
    {
        report[$"{Prefix}.Exception.Type"] = ex.GetType().FullName ?? ex.GetType().Name;
        report[$"{Prefix}.Exception.Message"] = ex.Message;
    }
}

static void ProbeTextBoxInteractionStates(
    IResourceNode node,
    string prefix,
    IDictionary<string, string> report)
{
    if (!TryGetResource(node, typeof(TextBox), out var value) ||
        value is not ControlTheme controlTheme)
    {
        report[$"{prefix}.ThemeFound"] = "false";
        return;
    }

    report[$"{prefix}.ThemeFound"] = "true";

    try
    {
        var templateSetter = controlTheme.Setters
            .OfType<Setter>()
            .FirstOrDefault(static setter => setter.Property == TemplatedControl.TemplateProperty);
        if (templateSetter?.Value is not IControlTemplate template)
        {
            report[$"{prefix}.TemplateFound"] = "false";
            return;
        }

        report[$"{prefix}.TemplateFound"] = "true";

        var textBox = new ProbeTextBox
        {
            Theme = controlTheme,
            Text = "state-probe"
        };
        var hostWindow = new Window
        {
            Width = 320,
            Height = 120,
            Content = textBox
        };

        INameScope? templateNameScope = null;
        textBox.TemplateApplied += (_, e) => templateNameScope = e.NameScope;
        hostWindow.Show();
        textBox.ApplyTemplate();

        var border = templateNameScope?.Find<Border>("PART_BorderElement");
        var watermark = templateNameScope?.Find<TextBlock>("PART_Watermark");

        report[$"{prefix}.BorderFound"] = (border is not null).ToString().ToLowerInvariant();
        report[$"{prefix}.WatermarkFound"] = (watermark is not null).ToString().ToLowerInvariant();
        if (border is null || watermark is null)
        {
            return;
        }

        CaptureState(prefix, "Default", border, watermark, report);

        textBox.SetPseudoClass(":pointerover", true);
        CaptureState(prefix, "PointerOver", border, watermark, report);
        textBox.SetPseudoClass(":pointerover", false);

        textBox.SetPseudoClass(":focus", true);
        CaptureState(prefix, "Focus", border, watermark, report);
        textBox.SetPseudoClass(":focus", false);
        hostWindow.Close();
    }
    catch (Exception ex)
    {
        report[$"{prefix}.Exception.Type"] = ex.GetType().FullName ?? ex.GetType().Name;
        report[$"{prefix}.Exception.Message"] = ex.Message;
    }
}

static void ProbeSliderInteractionStates(
    IResourceNode node,
    string prefix,
    IDictionary<string, string> report)
{
    if (!TryGetResource(node, typeof(Slider), out var value) ||
        value is not ControlTheme controlTheme)
    {
        report[$"{prefix}.ThemeFound"] = "false";
        return;
    }

    report[$"{prefix}.ThemeFound"] = "true";

    try
    {
        var slider = new ProbeSlider
        {
            Theme = controlTheme,
            Value = 50,
            Width = 240
        };
        var hostWindow = new Window
        {
            Width = 320,
            Height = 120,
            Content = slider
        };

        INameScope? templateNameScope = null;
        slider.TemplateApplied += (_, e) => templateNameScope = e.NameScope;
        hostWindow.Show();
        slider.ApplyTemplate();
        report[$"{prefix}.TemplateFound"] = (templateNameScope is not null).ToString().ToLowerInvariant();

        var container = templateNameScope?.Find<Grid>("SliderContainer");
        var track = templateNameScope?.Find<Track>("PART_Track");
        var decreaseButton = templateNameScope?.Find<RepeatButton>("PART_DecreaseButton");
        var increaseButton = templateNameScope?.Find<RepeatButton>("PART_IncreaseButton");
        var thumb = track?.Thumb;

        report[$"{prefix}.ContainerFound"] = (container is not null).ToString().ToLowerInvariant();
        report[$"{prefix}.TrackFound"] = (track is not null).ToString().ToLowerInvariant();
        report[$"{prefix}.DecreaseFound"] = (decreaseButton is not null).ToString().ToLowerInvariant();
        report[$"{prefix}.IncreaseFound"] = (increaseButton is not null).ToString().ToLowerInvariant();
        report[$"{prefix}.ThumbFound"] = (thumb is not null).ToString().ToLowerInvariant();
        if (container is null || decreaseButton is null || increaseButton is null || thumb is null)
        {
            hostWindow.Close();
            return;
        }

        CaptureSliderState(prefix, "Default", container, decreaseButton, increaseButton, thumb, report);

        slider.SetPseudoClass(":pointerover", true);
        CaptureSliderState(prefix, "PointerOver", container, decreaseButton, increaseButton, thumb, report);
        slider.SetPseudoClass(":pointerover", false);

        slider.SetPseudoClass(":pressed", true);
        CaptureSliderState(prefix, "Pressed", container, decreaseButton, increaseButton, thumb, report);
        slider.SetPseudoClass(":pressed", false);

        hostWindow.Close();
    }
    catch (Exception ex)
    {
        report[$"{prefix}.Exception.Type"] = ex.GetType().FullName ?? ex.GetType().Name;
        report[$"{prefix}.Exception.Message"] = ex.Message;
    }
}

static void CaptureState(
    string prefix,
    string stateName,
    Border border,
    TextBlock watermark,
    IDictionary<string, string> report)
{
    report[$"{prefix}.{stateName}.BorderBackground"] = SummarizeValue(border.Background);
    report[$"{prefix}.{stateName}.BorderBrush"] = SummarizeValue(border.BorderBrush);
    report[$"{prefix}.{stateName}.BorderThickness"] = SummarizeValue(border.BorderThickness);
    report[$"{prefix}.{stateName}.WatermarkForeground"] = SummarizeValue(watermark.Foreground);
}

static void CaptureSliderState(
    string prefix,
    string stateName,
    Grid container,
    RepeatButton decreaseButton,
    RepeatButton increaseButton,
    Thumb thumb,
    IDictionary<string, string> report)
{
    report[$"{prefix}.{stateName}.ContainerBackground"] = SummarizeValue(container.Background);
    report[$"{prefix}.{stateName}.DecreaseBackground"] = SummarizeValue(decreaseButton.Background);
    report[$"{prefix}.{stateName}.IncreaseBackground"] = SummarizeValue(increaseButton.Background);
    report[$"{prefix}.{stateName}.ThumbBackground"] = SummarizeValue(thumb.Background);
}

static bool TryGetResource(IResourceNode node, object key, out object? value)
{
    if (node.TryGetResource(key, ThemeVariant.Default, out value))
    {
        return true;
    }

    if (node.TryGetResource(key, ThemeVariant.Light, out value))
    {
        return true;
    }

    return node.TryGetResource(key, null, out value);
}

static void ProbeDirectGeneratedLoad(string uri, string prefix, IDictionary<string, string> report)
{
    if (!AvaloniaSourceGeneratedXamlLoader.TryLoad(serviceProvider: null, new Uri(uri), out var loaded) ||
        loaded is null)
    {
        report[$"{prefix}.Found"] = "false";
        return;
    }

    report[$"{prefix}.Found"] = "true";
    report[$"{prefix}.Type"] = loaded.GetType().FullName ?? loaded.GetType().Name;
    if (loaded is ResourceDictionary dictionary)
    {
        report[$"{prefix}.ResourceCount"] = dictionary.Count.ToString(CultureInfo.InvariantCulture);
        ProbeResource(dictionary, "ButtonPadding", prefix + ".ButtonPadding", report);
        ProbeResource(dictionary, "SystemControlForegroundBaseHighBrush", prefix + ".SystemControlForegroundBaseHighBrush", report);
        ProbeResource(dictionary, "SystemControlHighlightAccentBrush", prefix + ".SystemControlHighlightAccentBrush", report);
        ProbeResource(dictionary, "TextControlBorderBrushFocused", prefix + ".TextControlBorderBrushFocused", report);
        ProbeResource(dictionary, "TextControlSelectionHighlightColor", prefix + ".TextControlSelectionHighlightColor", report);
        ProbeResource(dictionary, "ToggleButtonBackgroundChecked", prefix + ".ToggleButtonBackgroundChecked", report);
        ProbeControlTheme(dictionary, typeof(Button), prefix + ".ButtonTheme", report);
    }
}

static string SummarizeValue(object? value)
{
    return value switch
    {
        null => "<null>",
        ISolidColorBrush solidColorBrush => solidColorBrush.Color.ToString(),
        IBrush => value.GetType().FullName ?? value.GetType().Name,
        Thickness thickness => thickness.ToString(),
        double number => number.ToString(CultureInfo.InvariantCulture),
        float number => number.ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        string text => text,
        _ => value.GetType().FullName ?? value.GetType().Name
    };
}

sealed class ProbeTextBox : TextBox
{
    public void SetPseudoClass(string pseudoClass, bool value)
    {
        PseudoClasses.Set(pseudoClass, value);
    }
}

sealed class ProbeSlider : Slider
{
    public void SetPseudoClass(string pseudoClass, bool value)
    {
        PseudoClasses.Set(pseudoClass, value);
    }
}
