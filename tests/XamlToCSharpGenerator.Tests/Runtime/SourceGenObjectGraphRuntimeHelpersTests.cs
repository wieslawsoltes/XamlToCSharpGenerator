using Avalonia.Controls;
using Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class SourceGenObjectGraphRuntimeHelpersTests
{
    [Fact]
    public void TryClearCollection_ForStyles_Clears_StyleItems_But_Preserves_StyleResources()
    {
        var styles = new Styles
        {
            Resources = new ResourceDictionary
            {
                ["Accent"] = "Orange"
            }
        };
        styles.Add(new Style());

        SourceGenObjectGraphRuntimeHelpers.TryClearCollection(styles);

        Assert.Empty(styles);
        Assert.Equal("Orange", styles.Resources["Accent"]);
    }

    [Fact]
    public void TryClearCollection_ForResourceDictionary_Clears_Merged_And_ThemeDictionaries()
    {
        var dictionary = new ResourceDictionary
        {
            ["Foreground"] = "Black"
        };
        dictionary.MergedDictionaries.Add(new ResourceDictionary
        {
            ["Nested"] = "Value"
        });
        dictionary.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["ThemeKey"] = "ThemeValue"
        };

        SourceGenObjectGraphRuntimeHelpers.TryClearCollection(dictionary);

        Assert.Empty(dictionary);
        Assert.Empty(dictionary.MergedDictionaries);
        Assert.Empty(dictionary.ThemeDictionaries);
    }
}
