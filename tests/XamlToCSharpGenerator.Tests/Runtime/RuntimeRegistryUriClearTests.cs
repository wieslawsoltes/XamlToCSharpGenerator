using System;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class RuntimeRegistryUriClearTests : IDisposable
{
    public void Dispose()
    {
        GeneratedArtifactTestRestore.RestoreAllLoadedGeneratedArtifacts();
    }

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

    [Fact]
    public void ResetDocumentRegistries_Clears_All_Targeted_Registries_And_Keeps_Other_Documents()
    {
        const string uriA = "avares://Demo/Reset-A.axaml";
        const string uriB = "avares://Demo/Reset-B.axaml";

        XamlSourceGenRegistry.Register(uriA, static _ => "A");
        XamlSourceGenRegistry.Register(uriB, static _ => "B");
        XamlSourceInfoRegistry.Register(uriA, "Object", "RootA", "A.axaml", 1, 1);
        XamlSourceInfoRegistry.Register(uriB, "Object", "RootB", "B.axaml", 1, 1);
        XamlResourceRegistry.Register(uriA, "ResourceA", "Brush", "<SolidColorBrush />");
        XamlResourceRegistry.Register(uriB, "ResourceB", "Brush", "<SolidColorBrush />");
        XamlTemplateRegistry.Register(uriA, "DataTemplate", null, null, null, "<DataTemplate />");
        XamlTemplateRegistry.Register(uriB, "DataTemplate", null, null, null, "<DataTemplate />");
        XamlStyleRegistry.Register(uriA, null, "Button", "Button", "<Style Selector=\"Button\" />");
        XamlStyleRegistry.Register(uriB, null, "TextBlock", "TextBlock", "<Style Selector=\"TextBlock\" />");
        XamlControlThemeRegistry.Register(uriA, "ThemeA", "global::Avalonia.Controls.Button", null, null, "<ControlTheme />");
        XamlControlThemeRegistry.Register(uriB, "ThemeB", "global::Avalonia.Controls.Button", null, null, "<ControlTheme />");
        XamlIncludeRegistry.Register(uriA, "StyleInclude", "avares://Demo/StylesA.axaml", "Styles", true, "<StyleInclude />");
        XamlIncludeRegistry.Register(uriB, "StyleInclude", "avares://Demo/StylesB.axaml", "Styles", true, "<StyleInclude />");
        XamlIncludeGraphRegistry.Register(uriA, "avares://Demo/IncludedA.axaml", "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(uriB, "avares://Demo/IncludedB.axaml", "MergedDictionaries");
        XamlCompiledBindingRegistry.Register(uriA, "ViewA", "Title", "Title", "VmA", static _ => "A");
        XamlCompiledBindingRegistry.Register(uriB, "ViewB", "Title", "Title", "VmB", static _ => "B");

        SourceGenArtifactRegistryRuntime.ResetDocumentRegistries(uriA);

        Assert.False(XamlSourceGenRegistry.TryCreate(serviceProvider: null, uriA, out _));
        Assert.True(XamlSourceGenRegistry.TryCreate(serviceProvider: null, uriB, out var bFactoryResult));
        Assert.Equal("B", bFactoryResult);
        Assert.Empty(XamlSourceInfoRegistry.GetAll(uriA));
        Assert.Single(XamlSourceInfoRegistry.GetAll(uriB));
        Assert.Empty(XamlResourceRegistry.GetAll(uriA));
        Assert.Single(XamlResourceRegistry.GetAll(uriB));
        Assert.Empty(XamlTemplateRegistry.GetAll(uriA));
        Assert.Single(XamlTemplateRegistry.GetAll(uriB));
        Assert.Empty(XamlStyleRegistry.GetAll(uriA));
        Assert.Single(XamlStyleRegistry.GetAll(uriB));
        Assert.Empty(XamlControlThemeRegistry.GetAll(uriA));
        Assert.Single(XamlControlThemeRegistry.GetAll(uriB));
        Assert.Empty(XamlIncludeRegistry.GetAll(uriA));
        Assert.Single(XamlIncludeRegistry.GetAll(uriB));
        Assert.Empty(XamlIncludeGraphRegistry.GetDirect(uriA, "MergedDictionaries"));
        Assert.Single(XamlIncludeGraphRegistry.GetDirect(uriB, "MergedDictionaries"));
        Assert.Empty(XamlCompiledBindingRegistry.GetAll(uriA));
        Assert.Single(XamlCompiledBindingRegistry.GetAll(uriB));

        XamlSourceGenRegistry.Clear();
        XamlSourceInfoRegistry.Clear();
        XamlResourceRegistry.Clear();
        XamlTemplateRegistry.Clear(uriA);
        XamlTemplateRegistry.Clear(uriB);
        XamlStyleRegistry.Clear(uriA);
        XamlStyleRegistry.Clear(uriB);
        XamlControlThemeRegistry.Clear();
        XamlIncludeRegistry.Clear(uriA);
        XamlIncludeRegistry.Clear(uriB);
        XamlIncludeGraphRegistry.Clear(uriA);
        XamlIncludeGraphRegistry.Clear(uriB);
        XamlCompiledBindingRegistry.Clear(uriA);
        XamlCompiledBindingRegistry.Clear(uriB);
    }
}
