using System;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeRegistry")]
public class RuntimeRegistryUriClearTests
{
    [Fact]
    public void Clear_By_Uri_Removes_Only_Targeted_Resource_Metadata()
    {
        const string uriA = "avares://Demo/A.axaml";
        const string uriB = "avares://Demo/B.axaml";

        XamlResourceRegistry.Register(uriA, "ColorA", "Brush", "<SolidColorBrush />");
        XamlResourceRegistry.Register(uriB, "ColorB", "Brush", "<SolidColorBrush />");

        XamlResourceRegistry.Clear(uriA);

        Assert.Empty(XamlResourceRegistry.GetAll(uriA));
        Assert.Single(XamlResourceRegistry.GetAll(uriB));
    }

    [Fact]
    public void Clear_By_Uri_Removes_Only_Targeted_Style_Template_And_Include_Metadata()
    {
        const string uriA = "avares://Demo/A.axaml";
        const string uriB = "avares://Demo/B.axaml";

        XamlStyleRegistry.Register(uriA, null, "Button", "Button", "<Style Selector=\"Button\" />");
        XamlStyleRegistry.Register(uriB, null, "TextBlock", "TextBlock", "<Style Selector=\"TextBlock\" />");
        XamlTemplateRegistry.Register(uriA, "DataTemplate", null, null, null, "<DataTemplate />");
        XamlTemplateRegistry.Register(uriB, "DataTemplate", null, null, null, "<DataTemplate />");
        XamlIncludeRegistry.Register(uriA, "StyleInclude", "avares://Demo/StylesA.axaml", "Styles", true, "<StyleInclude />");
        XamlIncludeRegistry.Register(uriB, "StyleInclude", "avares://Demo/StylesB.axaml", "Styles", true, "<StyleInclude />");

        XamlStyleRegistry.Clear(uriA);
        XamlTemplateRegistry.Clear(uriA);
        XamlIncludeRegistry.Clear(uriA);

        Assert.Empty(XamlStyleRegistry.GetAll(uriA));
        Assert.Single(XamlStyleRegistry.GetAll(uriB));
        Assert.Empty(XamlTemplateRegistry.GetAll(uriA));
        Assert.Single(XamlTemplateRegistry.GetAll(uriB));
        Assert.Empty(XamlIncludeRegistry.GetAll(uriA));
        Assert.Single(XamlIncludeRegistry.GetAll(uriB));
    }

    [Fact]
    public void Clear_By_Uri_Removes_Only_Targeted_CompiledBindings_Themes_SourceInfo_And_IncludeGraph()
    {
        const string uriA = "avares://Demo/A.axaml";
        const string uriB = "avares://Demo/B.axaml";

        XamlCompiledBindingRegistry.Register(uriA, "ViewA", "Title", "Title", "VmA", static _ => "A");
        XamlCompiledBindingRegistry.Register(uriB, "ViewB", "Title", "Title", "VmB", static _ => "B");
        XamlControlThemeRegistry.Register(uriA, "ThemeA", "global::Avalonia.Controls.Button", null, null, "<ControlTheme />");
        XamlControlThemeRegistry.Register(uriB, "ThemeB", "global::Avalonia.Controls.Button", null, null, "<ControlTheme />");
        XamlSourceInfoRegistry.Register(uriA, "Object", "RootA", "A.axaml", 1, 1);
        XamlSourceInfoRegistry.Register(uriB, "Object", "RootB", "B.axaml", 1, 1);
        XamlIncludeGraphRegistry.Register(uriA, "avares://Demo/IncludedA.axaml", "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(uriB, "avares://Demo/IncludedB.axaml", "MergedDictionaries");

        XamlCompiledBindingRegistry.Clear(uriA);
        XamlControlThemeRegistry.Clear(uriA);
        XamlSourceInfoRegistry.Clear(uriA);
        XamlIncludeGraphRegistry.Clear(uriA);

        Assert.Empty(XamlCompiledBindingRegistry.GetAll(uriA));
        Assert.Single(XamlCompiledBindingRegistry.GetAll(uriB));
        Assert.Empty(XamlControlThemeRegistry.GetAll(uriA));
        Assert.Single(XamlControlThemeRegistry.GetAll(uriB));
        Assert.Empty(XamlSourceInfoRegistry.GetAll(uriA));
        Assert.Single(XamlSourceInfoRegistry.GetAll(uriB));
        Assert.Empty(XamlIncludeGraphRegistry.GetDirect(uriA, "MergedDictionaries"));
        Assert.Single(XamlIncludeGraphRegistry.GetDirect(uriB, "MergedDictionaries"));
    }
}
