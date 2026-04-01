using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Framework.Avalonia;
using XamlToCSharpGenerator.LanguageService.Framework.Maui;
using XamlToCSharpGenerator.LanguageService.Framework.WinUI;
using XamlToCSharpGenerator.LanguageService.Framework.Wpf;

namespace XamlToCSharpGenerator.LanguageService.Framework.All;

public static class XamlBuiltInLanguageFrameworkRegistry
{
    public static XamlLanguageFrameworkRegistry Instance { get; } = Create();

    public static XamlLanguageFrameworkRegistry Create()
    {
        return new XamlLanguageFrameworkRegistryBuilder()
            .Add(AvaloniaLanguageFrameworkProvider.Instance)
            .Add(MauiLanguageFrameworkProvider.Instance)
            .Add(WinUiLanguageFrameworkProvider.Instance)
            .Add(WpfLanguageFrameworkProvider.Instance)
            .Build(FrameworkProfileIds.Avalonia);
    }
}
