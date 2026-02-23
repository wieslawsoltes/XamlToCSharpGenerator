using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeRegistry")]
public class SourceGenStaticResourceResolverTests
{
    [Fact]
    public void Resolve_Returns_Value_From_Transitive_Includes()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/Colors.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Colors.axaml",
            "avares://Demo/Palette.axaml",
            "MergedDictionaries");

        XamlSourceGenRegistry.Register(
            "avares://Demo/Palette.axaml",
            static _ => new Hashtable
            {
                ["AccentBrush"] = "Blue"
            });

        var includes = XamlIncludeGraphRegistry.GetTransitive("avares://Demo/Main.axaml", "MergedDictionaries");
        Assert.Equal(2, includes.Count);
        Assert.Contains(includes, edge => edge.IncludedUri == "avares://Demo/Palette.axaml");
        Assert.True(XamlSourceGenRegistry.TryCreate(null, "avares://Demo/Palette.axaml", out var paletteRoot));
        Assert.IsType<Hashtable>(paletteRoot);

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: null,
            key: "AccentBrush",
            currentUri: "avares://Demo/Main.axaml");

        Assert.Equal("Blue", resolved);
    }

    [Fact]
    public void Resolve_Throws_When_Resource_Is_Missing()
    {
        ResetRuntimeRegistries();

        Assert.Throws<KeyNotFoundException>(() =>
            SourceGenStaticResourceResolver.Resolve(
                anchor: null,
                key: "Missing",
                currentUri: "avares://Demo/Main.axaml"));
    }

    [Fact]
    public void TryResolve_Returns_False_When_Resource_Is_Missing()
    {
        ResetRuntimeRegistries();

        var found = SourceGenStaticResourceResolver.TryResolve(
            anchor: null,
            key: "Missing",
            currentUri: "avares://Demo/Main.axaml",
            value: out var resolved);

        Assert.False(found);
        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_Uses_Last_Merged_Dictionary_Precedence_For_Duplicate_Keys()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/ColorsA.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/ColorsB.axaml",
            "MergedDictionaries");

        XamlSourceGenRegistry.Register(
            "avares://Demo/ColorsA.axaml",
            static _ => new Hashtable
            {
                ["AccentBrush"] = "Blue"
            });

        XamlSourceGenRegistry.Register(
            "avares://Demo/ColorsB.axaml",
            static _ => new Hashtable
            {
                ["AccentBrush"] = "Red"
            });

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: null,
            key: "AccentBrush",
            currentUri: "avares://Demo/Main.axaml");

        Assert.Equal("Red", resolved);
    }

    [Fact]
    public void Resolve_Prefers_Later_CrossAssembly_Include_Order_For_Duplicate_Keys()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://External.Assets/Palette.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/LocalPalette.axaml",
            "MergedDictionaries");

        XamlSourceGenRegistry.Register(
            "avares://External.Assets/Palette.axaml",
            static _ => new Hashtable
            {
                ["AccentBrush"] = "ExternalBlue"
            });
        XamlSourceGenRegistry.Register(
            "avares://Demo/LocalPalette.axaml",
            static _ => new Hashtable
            {
                ["AccentBrush"] = "LocalGreen"
            });

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: null,
            key: "AccentBrush",
            currentUri: "avares://Demo/Main.axaml");

        Assert.Equal("LocalGreen", resolved);
    }

    [Fact]
    public void Resolve_Uses_Explicit_ParentStack_Before_Include_Graph()
    {
        ResetRuntimeRegistries();

        var parentStack = new object[]
        {
            new Hashtable
            {
                ["AccentBrush"] = "Purple"
            }
        };

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: null,
            key: "AccentBrush",
            currentUri: "avares://Demo/Main.axaml",
            parentStack: parentStack);

        Assert.Equal("Purple", resolved);
    }

    [Fact]
    public void Resolve_Uses_Anchor_ThemeVariant_For_Include_Graph_Theme_Dictionaries()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/Themed.axaml",
            "MergedDictionaries");

        var themed = new ResourceDictionary();
        var darkTheme = new ResourceDictionary
        {
            ["AccentBrush"] = "DarkBrush"
        };
        themed.ThemeDictionaries[ThemeVariant.Dark] = darkTheme;

        XamlSourceGenRegistry.Register(
            "avares://Demo/Themed.axaml",
            _ => themed);

        var anchor = CreateThemeScope(ThemeVariant.Dark);
        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: anchor,
            key: "AccentBrush",
            currentUri: "avares://Demo/Main.axaml");

        Assert.Equal("DarkBrush", resolved);
    }

    [Fact]
    public void Resolve_Uses_Last_Merged_Dictionary_Precedence_For_ThemeVariant_Fallback_Collisions()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/ThemedA.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/ThemedB.axaml",
            "MergedDictionaries");

        var themedA = new ResourceDictionary();
        var themedADark = new ResourceDictionary();
        themedADark["AccentBrush"] = "A-Dark";
        themedA.ThemeDictionaries[ThemeVariant.Dark] = themedADark;

        var themedB = new ResourceDictionary();
        var themedBDefault = new ResourceDictionary();
        themedBDefault["AccentBrush"] = "B-Default";
        themedB.ThemeDictionaries[ThemeVariant.Default] = themedBDefault;

        XamlSourceGenRegistry.Register(
            "avares://Demo/ThemedA.axaml",
            _ => themedA);
        XamlSourceGenRegistry.Register(
            "avares://Demo/ThemedB.axaml",
            _ => themedB);

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: CreateThemeScope(ThemeVariant.Dark),
            key: "AccentBrush",
            currentUri: "avares://Demo/Main.axaml");

        Assert.Equal("B-Default", resolved);
    }

    [Fact]
    public void Resolve_Uses_Ancestor_Owner_Merged_Dictionaries_For_Included_File()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/BaseResources.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/FluentControls.axaml",
            "Styles");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/FluentControls.axaml",
            "avares://Demo/RepeatButton.axaml",
            "MergedDictionaries");

        XamlSourceGenRegistry.Register(
            "avares://Demo/BaseResources.axaml",
            static _ => new Hashtable
            {
                ["ButtonPadding"] = "8,5,8,6"
            });
        XamlSourceGenRegistry.Register(
            "avares://Demo/RepeatButton.axaml",
            static _ => new Hashtable());

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: null,
            key: "ButtonPadding",
            currentUri: "avares://Demo/RepeatButton.axaml");

        Assert.Equal("8,5,8,6", resolved);
    }

    [Fact]
    public void Resolve_Does_Not_Materialize_Owner_Uri_When_Resource_Metadata_Proves_Miss()
    {
        ResetRuntimeRegistries();

        var ownerMaterializations = 0;
        var baseResourceMaterializations = 0;

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/BaseResources.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/FluentControls.axaml",
            "Styles");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/FluentControls.axaml",
            "avares://Demo/RepeatButton.axaml",
            "MergedDictionaries");

        XamlResourceRegistry.Register(
            "avares://Demo/BaseResources.axaml",
            "ButtonPadding",
            "Thickness",
            "<Thickness x:Key=\"ButtonPadding\">8,5,8,6</Thickness>");
        XamlResourceRegistry.Register(
            "avares://Demo/RepeatButton.axaml",
            "{x:Type RepeatButton}",
            "ControlTheme",
            "<ControlTheme x:Key=\"{x:Type RepeatButton}\" />");

        XamlSourceGenRegistry.Register(
            "avares://Demo/FluentControls.axaml",
            _ =>
            {
                ownerMaterializations++;
                return new Hashtable();
            });
        XamlSourceGenRegistry.Register(
            "avares://Demo/BaseResources.axaml",
            _ =>
            {
                baseResourceMaterializations++;
                return new Hashtable
                {
                    ["ButtonPadding"] = "8,5,8,6"
                };
            });
        XamlSourceGenRegistry.Register(
            "avares://Demo/RepeatButton.axaml",
            static _ => new Hashtable());

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: null,
            key: "ButtonPadding",
            currentUri: "avares://Demo/RepeatButton.axaml");

        Assert.Equal("8,5,8,6", resolved);
        Assert.Equal(1, baseResourceMaterializations);
        Assert.Equal(0, ownerMaterializations);
    }

    [Fact]
    public void Resolve_Does_Not_StackOverflow_On_Reentrant_Include_Load()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/Owner.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Owner.axaml",
            "avares://Demo/Leaf.axaml",
            "MergedDictionaries");

        XamlSourceGenRegistry.Register(
            "avares://Demo/Main.axaml",
            static _ => new Hashtable());
        XamlSourceGenRegistry.Register(
            "avares://Demo/Owner.axaml",
            static _ => new Hashtable());
        XamlSourceGenRegistry.Register(
            "avares://Demo/Leaf.axaml",
            static __ =>
            {
                try
                {
                    var __resolved = SourceGenStaticResourceResolver.Resolve(
                        anchor: null,
                        key: "Missing",
                        currentUri: "avares://Demo/Main.axaml");
                }
                catch (KeyNotFoundException)
                {
                }

                return new Hashtable();
            });

        Assert.Throws<KeyNotFoundException>(() =>
            SourceGenStaticResourceResolver.Resolve(
                anchor: null,
                key: "Missing",
                currentUri: "avares://Demo/Main.axaml"));
    }

    [Fact]
    public void Resolve_Forwards_ServiceProvider_To_Include_Materialization()
    {
        ResetRuntimeRegistries();

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/Palette.axaml",
            "MergedDictionaries");

        XamlSourceGenRegistry.Register(
            "avares://Demo/Palette.axaml",
            static serviceProvider => new Hashtable
            {
                ["AccentBrush"] = serviceProvider is MarkerServiceProvider
                    ? "FromMarkerProvider"
                    : "WithoutProvider"
            });

        var resolved = SourceGenStaticResourceResolver.Resolve(
            anchor: null,
            key: "AccentBrush",
            currentUri: "avares://Demo/Main.axaml",
            serviceProvider: new MarkerServiceProvider());

        Assert.Equal("FromMarkerProvider", resolved);
    }

    private static ThemeVariantScope CreateThemeScope(ThemeVariant variant)
    {
        var scope = new ThemeVariantScope();
        scope.SetValue(ThemeVariantScope.ActualThemeVariantProperty, variant);
        return scope;
    }

    private static void ResetRuntimeRegistries()
    {
        XamlSourceGenRegistry.Clear();
        XamlIncludeGraphRegistry.Clear();
        XamlResourceRegistry.Clear();
    }

    private sealed class MarkerServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
