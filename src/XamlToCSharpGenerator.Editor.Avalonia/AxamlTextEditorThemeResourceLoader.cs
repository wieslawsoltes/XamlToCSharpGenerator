using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Themes;

namespace XamlToCSharpGenerator.Editor.Avalonia;

internal static class AxamlTextEditorThemeResourceLoader
{
    private const string VsCodeLightModernResourceName =
        "XamlToCSharpGenerator.Editor.Avalonia.Resources.vscode-light-modern.json";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> VsCodeLightModernGuiColors = new(
        static () => LoadGuiColors(VsCodeLightModernResourceName));

    public static IReadOnlyDictionary<string, string> GetVsCodeLightModernGuiColors()
    {
        return VsCodeLightModernGuiColors.Value;
    }

    private static IReadOnlyDictionary<string, string> LoadGuiColors(string resourceName)
    {
        using Stream stream = typeof(AxamlTextEditorThemeResourceLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded TextMate theme resource was not found: " + resourceName);
        using var reader = new StreamReader(stream);
        var rawTheme = ThemeReader.ReadThemeSync(reader);
        var guiColors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in rawTheme.GetGuiColors())
        {
            if (entry.Value is string colorString && !string.IsNullOrWhiteSpace(colorString))
            {
                guiColors[entry.Key] = colorString;
            }
        }

        return guiColors;
    }
}
