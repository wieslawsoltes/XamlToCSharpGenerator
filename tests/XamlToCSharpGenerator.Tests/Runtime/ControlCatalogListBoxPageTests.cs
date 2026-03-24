using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ControlCatalog.Pages;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class ControlCatalogListBoxPageTests
{
    [AvaloniaFact]
    public void ListBox_Page_Realizes_Item_Text()
    {
        AssertListBoxPageRealizesText();
    }

    [AvaloniaFact]
    public void ListBox_Page_Realizes_Item_Text_After_Runtime_State_Reset()
    {
        RuntimeRemoteServiceTestHelper.ResetRuntimeState();
        AssertListBoxPageRealizesText();
    }

    private static void EnsureFluentTheme()
    {
        var application = Application.Current ?? throw new InvalidOperationException("Avalonia application is not initialized.");

        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Insert(0, new FluentTheme());
        }

        application.RequestedThemeVariant = ThemeVariant.Default;
    }

    private static void AssertListBoxPageRealizesText()
    {
        EnsureFluentTheme();

        var window = new Window
        {
            Width = 1280,
            Height = 900,
            Content = new ListBoxPage()
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var listBox = window.GetVisualDescendants().OfType<ListBox>().Single();
            var realizedTexts = listBox
                .GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Take(5)
                .Select(item => item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text)
                .ToArray();

            Assert.Equal(5, realizedTexts.Length);
            Assert.All(realizedTexts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
            Assert.Equal("Item 0", realizedTexts[0]);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
