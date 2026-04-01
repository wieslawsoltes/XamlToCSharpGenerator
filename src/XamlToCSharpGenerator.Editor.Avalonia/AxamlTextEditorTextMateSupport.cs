using System;
using System.IO;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace XamlToCSharpGenerator.Editor.Avalonia;

internal sealed class AxamlTextEditorTextMateSupport
{
    private const string XmlLanguageId = "xml";
    private readonly TextEditor _editor;
    private readonly RegistryOptions _registryOptions;
    private readonly TextMate.Installation _installation;
    private readonly IReadOnlyDictionary<string, string> _vsCodeLightModernGuiColors;
    private string? _currentScopeName;
    private ThemeName _currentThemeName = ThemeName.LightPlus;

    public AxamlTextEditorTextMateSupport(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        _editor = editor;
        _registryOptions = new RegistryOptions(_currentThemeName);
        _vsCodeLightModernGuiColors = AxamlTextEditorThemeResourceLoader.GetVsCodeLightModernGuiColors();
        _installation = editor.InstallTextMate(_registryOptions, initCurrentDocument: true);
        ApplyDocumentUri(documentUri: null);
        ApplyThemeVariant(themeVariant: null);
    }

    public void ApplyDocumentUri(string? documentUri)
    {
        var scopeName = ResolveScopeName(documentUri);
        if (string.Equals(_currentScopeName, scopeName, StringComparison.Ordinal))
        {
            return;
        }

        _installation.SetGrammar(scopeName);
        _currentScopeName = scopeName;
    }

    public void ApplyThemeVariant(ThemeVariant? themeVariant)
    {
        var themeName = MapThemeName(themeVariant);
        if (themeName != _currentThemeName)
        {
            _installation.SetTheme(_registryOptions.LoadTheme(themeName));
            _currentThemeName = themeName;
        }

        ApplyEditorChrome(themeVariant);
    }

    private string ResolveScopeName(string? documentUri)
    {
        if (TryResolveScopeName(documentUri, out var scopeName))
        {
            return scopeName;
        }

        return _registryOptions.GetScopeByLanguageId(XmlLanguageId) ?? "text.xml";
    }

    private bool TryResolveScopeName(string? documentUri, out string scopeName)
    {
        scopeName = string.Empty;

        if (string.IsNullOrWhiteSpace(documentUri) ||
            !Uri.TryCreate(documentUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.IsFile ? uri.LocalPath : uri.AbsolutePath;
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        if (string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase))
        {
            scopeName = _registryOptions.GetScopeByLanguageId(XmlLanguageId) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(scopeName);
        }

        scopeName = _registryOptions.GetScopeByExtension(extension) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(scopeName);
    }

    private static ThemeName MapThemeName(ThemeVariant? themeVariant)
    {
        return themeVariant == ThemeVariant.Dark
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;
    }

    private void ApplyEditorChrome(ThemeVariant? themeVariant)
    {
        var colorOverrides = themeVariant == ThemeVariant.Dark
            ? null
            : _vsCodeLightModernGuiColors;

        if (TryGetBrush("editor.background", colorOverrides, out var background))
        {
            _editor.Background = background;
        }

        if (TryGetBrush("editor.foreground", colorOverrides, out var foreground))
        {
            _editor.Foreground = foreground;
        }

        if (TryGetBrush("editorLineNumber.foreground", colorOverrides, out var lineNumbersForeground))
        {
            _editor.LineNumbersForeground = lineNumbersForeground;
        }

        if (TryGetBrush("editor.selectionBackground", colorOverrides, out var selectionBrush) ||
            TryGetBrush("editor.inactiveSelectionBackground", colorOverrides, out selectionBrush))
        {
            _editor.TextArea.SelectionBrush = selectionBrush;
        }

        if (TryGetBrush("editor.selectionForeground", colorOverrides, out var selectionForeground) ||
            TryGetBrush("list.activeSelectionForeground", colorOverrides, out selectionForeground))
        {
            _editor.TextArea.SelectionForeground = selectionForeground;
        }

        if (TryGetBrush("list.focusAndSelectionOutline", colorOverrides, out var selectionBorderBrush))
        {
            _editor.TextArea.SelectionBorder = new Pen(selectionBorderBrush);
        }

        if (TryGetBrush("editorCursor.foreground", colorOverrides, out var caretBrush) ||
            TryGetBrush("terminalCursor.foreground", colorOverrides, out caretBrush) ||
            TryGetBrush("focusBorder", colorOverrides, out caretBrush))
        {
            _editor.TextArea.CaretBrush = caretBrush;
        }

        if (TryGetBrush("editor.lineHighlightBackground", colorOverrides, out var currentLineBackground))
        {
            _editor.TextArea.TextView.CurrentLineBackground = currentLineBackground;
        }

        if (TryGetBrush("editor.lineHighlightBorder", colorOverrides, out var currentLineBorderBrush))
        {
            _editor.TextArea.TextView.CurrentLineBorder = new Pen(currentLineBorderBrush);
        }

        if (TryGetBrush("textLink.foreground", colorOverrides, out var linkForeground))
        {
            _editor.TextArea.TextView.LinkTextForegroundBrush = linkForeground;
        }

        if (TryGetBrush("editor.findMatchBackground", colorOverrides, out var searchResultsBrush))
        {
            _editor.SearchResultsBrush = searchResultsBrush;
        }
    }

    private bool TryGetBrush(
        string key,
        IReadOnlyDictionary<string, string>? colorOverrides,
        out IBrush brush)
    {
        brush = null!;

        if (!TryGetThemeColorString(key, colorOverrides, out var colorString))
        {
            return false;
        }

        brush = new SolidColorBrush(ParseThemeColor(colorString));
        return true;
    }

    private bool TryGetThemeColorString(
        string key,
        IReadOnlyDictionary<string, string>? colorOverrides,
        out string colorString)
    {
        colorString = string.Empty;

        if (colorOverrides is not null &&
            colorOverrides.TryGetValue(key, out var overrideColorString) &&
            !string.IsNullOrWhiteSpace(overrideColorString))
        {
            colorString = overrideColorString;
            return true;
        }

        return _installation.TryGetThemeColor(key, out colorString) &&
               !string.IsNullOrWhiteSpace(colorString);
    }

    private static Color ParseThemeColor(string colorString)
    {
        return Color.Parse(NormalizeThemeColor(colorString));
    }

    private static string NormalizeThemeColor(string colorString)
    {
        if (colorString.Length == 9)
        {
            return string.Create(9, colorString, static (span, value) =>
            {
                span[0] = '#';
                span[1] = value[7];
                span[2] = value[8];
                span[3] = value[1];
                span[4] = value[2];
                span[5] = value[3];
                span[6] = value[4];
                span[7] = value[5];
                span[8] = value[6];
            });
        }

        return colorString;
    }
}
