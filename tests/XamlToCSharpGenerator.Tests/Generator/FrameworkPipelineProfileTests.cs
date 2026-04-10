using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.Generator;
using XamlToCSharpGenerator.LanguageService.Framework.Maui;
using XamlToCSharpGenerator.LanguageService.Framework.WinUI;
using XamlToCSharpGenerator.LanguageService.Framework.Wpf;
using XamlToCSharpGenerator.NoUi.Framework;

namespace XamlToCSharpGenerator.Tests.Generator;

public class FrameworkPipelineProfileTests
{
    [Theory]
    [InlineData("NoUi")]
    [InlineData("WpfPilot")]
    [InlineData("MauiPilot")]
    public void Active_Pilot_Profiles_Map_Shared_Generator_Option_Aliases(string profileId)
    {
        IXamlFrameworkProfile profile = profileId switch
        {
            "WpfPilot" => WpfPilotFrameworkProfile.Instance,
            "MauiPilot" => MauiPilotFrameworkProfile.Instance,
            _ => NoUiFrameworkProfile.Instance
        };

        Assert.Contains(
            "XamlSourceGenUseCompiledBindingsByDefault",
            profile.MsBuildSettings.GetAliases(XamlFrameworkMsBuildSettingKey.UseCompiledBindingsByDefault));
        Assert.Contains(
            "XamlSourceGenCreateSourceInfo",
            profile.MsBuildSettings.GetAliases(XamlFrameworkMsBuildSettingKey.CreateSourceInfo));
        Assert.Contains(
            "XamlSourceGenHotReloadEnabled",
            profile.MsBuildSettings.GetAliases(XamlFrameworkMsBuildSettingKey.HotReloadEnabled));
        Assert.Contains(
            "XamlSourceGenIdeHotReloadEnabled",
            profile.MsBuildSettings.GetAliases(XamlFrameworkMsBuildSettingKey.IdeHotReloadEnabled));
        Assert.Contains(
            "XamlSourceGenHotDesignEnabled",
            profile.MsBuildSettings.GetAliases(XamlFrameworkMsBuildSettingKey.HotDesignEnabled));
    }

    [Theory]
    [InlineData("NoUi")]
    [InlineData("WpfPilot")]
    [InlineData("MauiPilot")]
    public void Active_Pilot_Profiles_Default_Hot_Reload_Features_Are_Disabled(string profileId)
    {
        IXamlFrameworkProfile profile = profileId switch
        {
            "WpfPilot" => WpfPilotFrameworkProfile.Instance,
            "MauiPilot" => MauiPilotFrameworkProfile.Instance,
            _ => NoUiFrameworkProfile.Instance
        };

        Assert.False(profile.BaseConfiguration.Build.HotReloadEnabled);
        Assert.False(profile.BaseConfiguration.Build.IdeHotReloadEnabled);
        Assert.False(profile.BaseConfiguration.Build.HotDesignEnabled);
    }

    [Theory]
    [InlineData("Avalonia")]
    [InlineData("NoUi")]
    [InlineData("WpfPilot")]
    [InlineData("MauiPilot")]
    public void SharedHostPipeline_Generates_For_Each_Profile(string profileId)
    {
        var (generator, code, xamlPath, xamlText, sourceItemGroup, expectedHintPrefix, expectedGeneratedToken) =
            profileId switch
            {
                "Avalonia" => (
                    Generator: (IIncrementalGenerator)new AvaloniaXamlSourceGenerator(),
                    Code: """
                          namespace Avalonia.Controls
                          {
                              public class UserControl
                              {
                                  public object? Content { get; set; }
                              }

                              public class TextBlock
                              {
                                  public string? Text { get; set; }
                              }
                          }

                          namespace Demo
                          {
                              public partial class MainView : global::Avalonia.Controls.UserControl { }
                          }
                          """,
                    XamlPath: "MainView.axaml",
                    XamlText: """
                              <UserControl xmlns="https://github.com/avaloniaui"
                                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                           x:Class="Demo.MainView">
                                  <TextBlock Text="Hello from Avalonia profile" />
                              </UserControl>
                              """,
                    SourceItemGroup: "AvaloniaXaml",
                    ExpectedHintPrefix: "Avalonia.",
                    ExpectedGeneratedToken: "__PopulateGeneratedObjectGraph"),
                "WpfPilot" => (
                    Generator: CreateFrameworkGenerator(WpfPilotFrameworkProfile.Instance),
                    Code: """
                          namespace WpfPilotFramework.Controls
                          {
                              public class Page
                              {
                                  public object? Content { get; set; }
                              }

                              public class StackPanel
                              {
                                  public global::System.Collections.Generic.List<object> Children { get; } = new();
                              }

                              public class TextBlock
                              {
                                  public string? Text { get; set; }
                              }
                          }

                          namespace Demo
                          {
                              public partial class MainWindow : global::WpfPilotFramework.Controls.Page { }
                          }
                          """,
                    XamlPath: "MainWindow.xaml",
                    XamlText: """
                              <controls:Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                             xmlns:controls="clr-namespace:WpfPilotFramework.Controls"
                                             x:Class="Demo.MainWindow">
                                  <controls:StackPanel>
                                      <controls:TextBlock Text="Hello from WPF pilot profile" />
                                  </controls:StackPanel>
                              </controls:Page>
                              """,
                    SourceItemGroup: "Page",
                    ExpectedHintPrefix: "WPFPilot.",
                    ExpectedGeneratedToken: "BuildWpfPilotObjectGraph"),
                "MauiPilot" => (
                    Generator: CreateFrameworkGenerator(MauiPilotFrameworkProfile.Instance),
                    Code: """
                          namespace MauiPilotFramework.Controls
                          {
                              public class ContentPage
                              {
                                  public object? Content { get; set; }
                              }

                              public class VerticalStackLayout
                              {
                                  public global::System.Collections.Generic.List<object> Children { get; } = new();
                              }

                              public class Label
                              {
                                  public string? Text { get; set; }
                              }
                          }

                          namespace Demo
                          {
                              public partial class MainPage : global::MauiPilotFramework.Controls.ContentPage { }
                          }
                          """,
                    XamlPath: "MainPage.xaml",
                    XamlText: """
                              <controls:ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                    xmlns:controls="clr-namespace:MauiPilotFramework.Controls"
                                                    x:Class="Demo.MainPage">
                                  <controls:VerticalStackLayout>
                                      <controls:Label Text="Hello from MAUI pilot profile" />
                                  </controls:VerticalStackLayout>
                              </controls:ContentPage>
                              """,
                    SourceItemGroup: "MauiXaml",
                    ExpectedHintPrefix: "MAUIPilot.",
                    ExpectedGeneratedToken: "BuildMauiPilotVisualTree"),
                _ => (
                    Generator: CreateFrameworkGenerator(NoUiFrameworkProfile.Instance),
                    Code: """
                          namespace NoUiFramework.Controls
                          {
                              public class Page
                              {
                                  public object? Content { get; set; }
                              }

                              public class StackPanel
                              {
                                  public global::System.Collections.Generic.List<object> Children { get; } = new();
                              }

                              public class Label
                              {
                                  public string? Text { get; set; }
                              }
                          }

                          namespace Demo
                          {
                              public partial class MainView : global::NoUiFramework.Controls.Page { }
                          }
                          """,
                    XamlPath: "MainView.xaml",
                    XamlText: """
                              <Page xmlns="clr-namespace:NoUiFramework.Controls"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                    x:Class="Demo.MainView">
                                  <StackPanel>
                                      <Label Text="Hello from NoUi profile" />
                                  </StackPanel>
                              </Page>
                              """,
                    SourceItemGroup: "NoUiXaml",
                    ExpectedHintPrefix: "NoUi.",
                    ExpectedGeneratedToken: "BuildNoUiObjectGraph")
            };

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics, runResult) = FrameworkGeneratorTestHarness.RunGenerator(
            generator,
            compilation,
            [(xamlPath, xamlText, sourceItemGroup, xamlPath)]);

        Assert.Empty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.NotEmpty(runResult.Results);
        var generatedSources = runResult.Results[0].GeneratedSources;
        Assert.NotEmpty(generatedSources);
        Assert.Contains(
            generatedSources,
            generatedSource => generatedSource.HintName.StartsWith(expectedHintPrefix, StringComparison.Ordinal));

        var generatedSyntaxTrees = updatedCompilation.SyntaxTrees.Skip(1).ToArray();
        Assert.NotEmpty(generatedSyntaxTrees);
        Assert.Contains(
            generatedSyntaxTrees,
            syntaxTree => syntaxTree.ToString().Contains(expectedGeneratedToken, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NoUi")]
    [InlineData("WpfPilot")]
    [InlineData("MauiPilot")]
    public void Active_Pilot_Profiles_Report_Unsupported_Binding_Features_Explicitly(string profileId)
    {
        var (generator, code, xamlPath, xamlText, sourceItemGroup) = profileId switch
        {
            "WpfPilot" => (
                Generator: CreateFrameworkGenerator(WpfPilotFrameworkProfile.Instance),
                Code: """
                      namespace WpfPilotFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }

                          public class TextBlock
                          {
                              public string? Text { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainWindow : global::WpfPilotFramework.Controls.Page { }
                      }
                      """,
                XamlPath: "MainWindow.xaml",
                XamlText: """
                          <controls:Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                         xmlns:controls="clr-namespace:WpfPilotFramework.Controls"
                                         x:Class="Demo.MainWindow">
                              <controls:TextBlock Text="{Binding Title}" />
                          </controls:Page>
                          """,
                SourceItemGroup: "Page"),
            "MauiPilot" => (
                Generator: CreateFrameworkGenerator(MauiPilotFrameworkProfile.Instance),
                Code: """
                      namespace MauiPilotFramework.Controls
                      {
                          public class ContentPage
                          {
                              public object? Content { get; set; }
                          }

                          public class Label
                          {
                              public string? Text { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainPage : global::MauiPilotFramework.Controls.ContentPage { }
                      }
                      """,
                XamlPath: "MainPage.xaml",
                XamlText: """
                          <controls:ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                xmlns:controls="clr-namespace:MauiPilotFramework.Controls"
                                                x:Class="Demo.MainPage">
                              <controls:Label Text="{Binding Title}" />
                          </controls:ContentPage>
                          """,
                SourceItemGroup: "MauiXaml"),
            _ => (
                Generator: CreateFrameworkGenerator(NoUiFrameworkProfile.Instance),
                Code: """
                      namespace NoUiFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }

                          public class TextBlock
                          {
                              public string? Text { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainView : global::NoUiFramework.Controls.Page { }
                      }
                      """,
                XamlPath: "MainView.xaml",
                XamlText: """
                          <Page xmlns="clr-namespace:NoUiFramework.Controls"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                x:Class="Demo.MainView">
                              <TextBlock Text="{Binding Title}" />
                          </Page>
                          """,
                SourceItemGroup: "NoUiXaml")
        };

        var compilation = CreateCompilation(code);
        var (_, diagnostics, _) = FrameworkGeneratorTestHarness.RunGenerator(
            generator,
            compilation,
            [(xamlPath, xamlText, sourceItemGroup, xamlPath)]);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0101" &&
                          diagnostic.GetMessage().Contains("markup extension 'Binding'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NoUi")]
    [InlineData("WpfPilot")]
    [InlineData("MauiPilot")]
    public void Active_Pilot_Profiles_Report_Unsupported_CompiledBinding_Scope_Features_Explicitly(string profileId)
    {
        var (generator, code, xamlPath, xamlText, sourceItemGroup) = profileId switch
        {
            "WpfPilot" => (
                Generator: CreateFrameworkGenerator(WpfPilotFrameworkProfile.Instance),
                Code: """
                      namespace WpfPilotFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }

                          public class TextBlock
                          {
                              public string? Text { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainWindow : global::WpfPilotFramework.Controls.Page { }
                          public sealed class MainViewModel { }
                      }
                      """,
                XamlPath: "MainWindow.xaml",
                XamlText: """
                          <controls:Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                         xmlns:controls="clr-namespace:WpfPilotFramework.Controls"
                                         xmlns:demo="clr-namespace:Demo"
                                         x:Class="Demo.MainWindow"
                                         x:DataType="demo:MainViewModel"
                                         x:CompileBindings="True">
                              <controls:TextBlock Text="Hello" />
                          </controls:Page>
                          """,
                SourceItemGroup: "Page"),
            "MauiPilot" => (
                Generator: CreateFrameworkGenerator(MauiPilotFrameworkProfile.Instance),
                Code: """
                      namespace MauiPilotFramework.Controls
                      {
                          public class ContentPage
                          {
                              public object? Content { get; set; }
                          }

                          public class Label
                          {
                              public string? Text { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainPage : global::MauiPilotFramework.Controls.ContentPage { }
                          public sealed class MainViewModel { }
                      }
                      """,
                XamlPath: "MainPage.xaml",
                XamlText: """
                          <controls:ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                xmlns:controls="clr-namespace:MauiPilotFramework.Controls"
                                                xmlns:demo="clr-namespace:Demo"
                                                x:Class="Demo.MainPage"
                                                x:DataType="demo:MainViewModel"
                                                x:CompileBindings="True">
                              <controls:Label Text="Hello" />
                          </controls:ContentPage>
                          """,
                SourceItemGroup: "MauiXaml"),
            _ => (
                Generator: CreateFrameworkGenerator(NoUiFrameworkProfile.Instance),
                Code: """
                      namespace NoUiFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }

                          public class TextBlock
                          {
                              public string? Text { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainView : global::NoUiFramework.Controls.Page { }
                          public sealed class MainViewModel { }
                      }
                      """,
                XamlPath: "MainView.xaml",
                XamlText: """
                          <Page xmlns="clr-namespace:NoUiFramework.Controls"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:demo="clr-namespace:Demo"
                                x:Class="Demo.MainView"
                                x:DataType="demo:MainViewModel"
                                x:CompileBindings="True">
                              <TextBlock Text="Hello" />
                          </Page>
                          """,
                SourceItemGroup: "NoUiXaml")
        };

        var compilation = CreateCompilation(code);
        var (_, diagnostics, _) = FrameworkGeneratorTestHarness.RunGenerator(
            generator,
            compilation,
            [(xamlPath, xamlText, sourceItemGroup, xamlPath)]);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0101" &&
                          diagnostic.GetMessage().Contains("x:DataType scope directives", StringComparison.Ordinal));
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0101" &&
                          diagnostic.GetMessage().Contains("x:CompileBindings directives", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NoUi")]
    [InlineData("WpfPilot")]
    [InlineData("MauiPilot")]
    public void Active_Pilot_Profiles_Report_Unsupported_Generator_Options_Explicitly(string profileId)
    {
        var (generator, code, xamlPath, xamlText, sourceItemGroup) = profileId switch
        {
            "WpfPilot" => (
                Generator: CreateFrameworkGenerator(WpfPilotFrameworkProfile.Instance),
                Code: """
                      namespace WpfPilotFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainWindow : global::WpfPilotFramework.Controls.Page { }
                      }
                      """,
                XamlPath: "MainWindow.xaml",
                XamlText: """
                          <controls:Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                         xmlns:controls="clr-namespace:WpfPilotFramework.Controls"
                                         x:Class="Demo.MainWindow" />
                          """,
                SourceItemGroup: "Page"),
            "MauiPilot" => (
                Generator: CreateFrameworkGenerator(MauiPilotFrameworkProfile.Instance),
                Code: """
                      namespace MauiPilotFramework.Controls
                      {
                          public class ContentPage
                          {
                              public object? Content { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainPage : global::MauiPilotFramework.Controls.ContentPage { }
                      }
                      """,
                XamlPath: "MainPage.xaml",
                XamlText: """
                          <controls:ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                xmlns:controls="clr-namespace:MauiPilotFramework.Controls"
                                                x:Class="Demo.MainPage" />
                          """,
                SourceItemGroup: "MauiXaml"),
            _ => (
                Generator: CreateFrameworkGenerator(NoUiFrameworkProfile.Instance),
                Code: """
                      namespace NoUiFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainView : global::NoUiFramework.Controls.Page { }
                      }
                      """,
                XamlPath: "MainView.xaml",
                XamlText: """
                          <Page xmlns="clr-namespace:NoUiFramework.Controls"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                x:Class="Demo.MainView" />
                          """,
                SourceItemGroup: "NoUiXaml")
        };

        var compilation = CreateCompilation(code);
        var (_, diagnostics, _) = FrameworkGeneratorTestHarness.RunGenerator(
            generator,
            compilation,
            [(xamlPath, xamlText, sourceItemGroup, xamlPath)],
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.XamlSourceGenUseCompiledBindingsByDefault", "true"),
                new KeyValuePair<string, string>("build_property.XamlSourceGenCreateSourceInfo", "true"),
                new KeyValuePair<string, string>("build_property.XamlSourceGenHotReloadEnabled", "true"),
                new KeyValuePair<string, string>("build_property.XamlSourceGenHotDesignEnabled", "true")
            ]);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0101" &&
                          diagnostic.GetMessage().Contains("hot reload", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NoUi")]
    [InlineData("WpfPilot")]
    [InlineData("MauiPilot")]
    public void Active_Pilot_Profiles_Do_Not_Report_Unsupported_Generator_Options_When_Explicitly_Disabled(string profileId)
    {
        var (generator, code, xamlPath, xamlText, sourceItemGroup) = profileId switch
        {
            "WpfPilot" => (
                Generator: CreateFrameworkGenerator(WpfPilotFrameworkProfile.Instance),
                Code: """
                      namespace WpfPilotFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainWindow : global::WpfPilotFramework.Controls.Page { }
                      }
                      """,
                XamlPath: "MainWindow.xaml",
                XamlText: """
                          <controls:Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                         xmlns:controls="clr-namespace:WpfPilotFramework.Controls"
                                         x:Class="Demo.MainWindow" />
                          """,
                SourceItemGroup: "Page"),
            "MauiPilot" => (
                Generator: CreateFrameworkGenerator(MauiPilotFrameworkProfile.Instance),
                Code: """
                      namespace MauiPilotFramework.Controls
                      {
                          public class ContentPage
                          {
                              public object? Content { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainPage : global::MauiPilotFramework.Controls.ContentPage { }
                      }
                      """,
                XamlPath: "MainPage.xaml",
                XamlText: """
                          <controls:ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                xmlns:controls="clr-namespace:MauiPilotFramework.Controls"
                                                x:Class="Demo.MainPage" />
                          """,
                SourceItemGroup: "MauiXaml"),
            _ => (
                Generator: CreateFrameworkGenerator(NoUiFrameworkProfile.Instance),
                Code: """
                      namespace NoUiFramework.Controls
                      {
                          public class Page
                          {
                              public object? Content { get; set; }
                          }
                      }

                      namespace Demo
                      {
                          public partial class MainView : global::NoUiFramework.Controls.Page { }
                      }
                      """,
                XamlPath: "MainView.xaml",
                XamlText: """
                          <Page xmlns="clr-namespace:NoUiFramework.Controls"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                x:Class="Demo.MainView" />
                          """,
                SourceItemGroup: "NoUiXaml")
        };

        var compilation = CreateCompilation(code);
        var (_, diagnostics, _) = FrameworkGeneratorTestHarness.RunGenerator(
            generator,
            compilation,
            [(xamlPath, xamlText, sourceItemGroup, xamlPath)],
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.XamlSourceGenUseCompiledBindingsByDefault", "false"),
                new KeyValuePair<string, string>("build_property.XamlSourceGenCreateSourceInfo", "false"),
                new KeyValuePair<string, string>("build_property.XamlSourceGenHotReloadEnabled", "false"),
                new KeyValuePair<string, string>("build_property.XamlSourceGenIdeHotReloadEnabled", "false"),
                new KeyValuePair<string, string>("build_property.XamlSourceGenHotDesignEnabled", "false")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
    }

    [Theory]
    [InlineData("WPF", "Page")]
    [InlineData("WinUI", "Page")]
    [InlineData("MAUI", "ContentPage")]
    public void SharedHostPipeline_Passive_Framework_Profiles_Skip_Emission_Without_Errors(
        string frameworkId,
        string rootElementName)
    {
        var frameworkInfo = frameworkId switch
        {
            "WPF" => WpfLanguageFrameworkProvider.Instance.Framework,
            "WinUI" => WinUiLanguageFrameworkProvider.Instance.Framework,
            _ => MauiLanguageFrameworkProvider.Instance.Framework
        };
        var generator = CreateFrameworkGenerator(frameworkInfo.Profile);
        var xamlPath = frameworkId == "MAUI" ? "MainView.xaml" : "MainWindow.xaml";
        var xamlText =
            $$"""
              <{{rootElementName}} xmlns="{{frameworkInfo.DefaultXmlNamespace}}"
                                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                   x:Class="Demo.MainView" />
              """;
        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public partial class MainView { }
            }
            """);

        var (updatedCompilation, diagnostics, runResult) = FrameworkGeneratorTestHarness.RunGenerator(
            generator,
            compilation,
            [(xamlPath, xamlText, frameworkInfo.PreferredProjectXamlItemName, xamlPath)]);

        Assert.Empty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.NotEmpty(runResult.Results);
        Assert.Empty(runResult.Results[0].GeneratedSources);
        Assert.Single(updatedCompilation.SyntaxTrees);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = ImmutableArray.Create(
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "FrameworkPipeline.Tests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IIncrementalGenerator CreateFrameworkGenerator(IXamlFrameworkProfile profile)
    {
        var generatorType = typeof(AvaloniaXamlSourceGenerator).Assembly.GetType(
            "XamlToCSharpGenerator.Generator.FrameworkXamlSourceGenerator",
            throwOnError: true)!;
        var constructor = generatorType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(IXamlFrameworkProfile)],
            modifiers: null)!;

        return (IIncrementalGenerator)constructor.Invoke([profile]);
    }
}
