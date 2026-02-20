using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Generator;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaXamlSourceGeneratorTests
{
    [Fact]
    public void Generates_InitializeComponent_And_Registry_For_XClass_File()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <SolidColorBrush x:Key="PrimaryBrush" Color="Red" />
                </UserControl.Resources>
                <UserControl.Styles>
                    <DataTemplate x:Key="PersonTemplate">
                        <TextBlock Text="Name" />
                    </DataTemplate>
                </UserControl.Styles>
                <Button x:Name="AcceptButton" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("InitializeComponent", generated);
        Assert.Contains("__RegisterXamlSourceGenArtifacts", generated);
        Assert.Contains("__PopulateGeneratedObjectGraph", generated);
        Assert.Contains("__BuildGeneratedObjectGraph", generated);
        Assert.Contains("__PopulateGeneratedObjectGraph(this, null);", generated);
        Assert.Contains("XamlResourceRegistry.Register", generated);
        Assert.Contains("XamlTemplateRegistry.Register", generated);
        Assert.Contains("AcceptButton =", generated);
        Assert.Contains("if (!__loadedWithSourceGen)", generated);
        Assert.Contains("AcceptButton", generated);
    }

    [Fact]
    public void Generates_Classless_Artifact_For_ResourceDictionary_File()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object> { }
            }

            namespace Avalonia.Media
            {
                public class SolidColorBrush
                {
                    public string? Color { get; set; }
                }
            }
            """;

        const string xaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <SolidColorBrush x:Key="AccentBrush" Color="Blue" />
            </ResourceDictionary>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0002");

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("namespace XamlToCSharpGenerator.Generated", generated);
        Assert.Contains("static class GeneratedXaml_MainView_", generated);
        Assert.Contains("__BuildGeneratedObjectGraph", generated);
        Assert.Contains("XamlResourceRegistry.Register", generated);
        Assert.DoesNotContain("InitializeComponent", generated);
    }

    [Fact]
    public void Generates_Object_Graph_And_Property_Assignments()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class Button : Control
                {
                    public string? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Content="Hello" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__BuildGeneratedObjectGraph", generated);
        Assert.Contains("__PopulateGeneratedObjectGraph", generated);
        Assert.Contains("var __root = __CreateRootInstance(null);", generated);
        Assert.Contains("var __n0 = new global::Avalonia.Controls.Button();", generated);
        Assert.Contains("__n0.Content = \"Hello\";", generated);
        Assert.DoesNotContain("__n0.Content = default!;", generated);
        Assert.Contains("__root.Content = __n0;", generated);
    }

    [Fact]
    public void Does_Not_Clear_Content_On_Nested_Class_Backed_UserControl()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class ContentControl : Control
                {
                    public object? Content { get; set; }
                }

                public class UserControl : ContentControl { }

                public class Button : ContentControl { }
            }

            namespace Demo
            {
                public partial class HostView : global::Avalonia.Controls.UserControl { }
                public partial class ChildView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string hostXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.HostView">
                <local:ChildView />
            </UserControl>
            """;

        const string childXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.ChildView">
                <Button Content="Child Content" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("HostView.axaml", hostXaml), ("ChildView.axaml", childXaml)]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedTrees = updatedCompilation.SyntaxTrees.Select(static tree => tree.ToString()).ToArray();
        var hostGenerated = Assert.Single(
            generatedTrees.Where(static source =>
                source.Contains("partial class HostView") &&
                source.Contains("__PopulateGeneratedObjectGraph")));
        Assert.Contains("new global::Demo.ChildView()", hostGenerated);
        Assert.DoesNotContain(".Content = default!;", hostGenerated);
    }

    [Fact]
    public void Does_Not_Clear_Content_On_TopDown_Attached_Class_Backed_UserControl()
    {
        const string code = """
            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = true)]
                public sealed class UsableDuringInitializationAttribute : global::System.Attribute
                {
                    public UsableDuringInitializationAttribute() { }
                    public UsableDuringInitializationAttribute(bool usable) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class ContentControl : Control
                {
                    public object? Content { get; set; }
                }

                [global::Avalonia.Metadata.UsableDuringInitialization]
                public class UserControl : ContentControl { }

                public class TabControl : Control
                {
                    public global::System.Collections.ArrayList Items { get; } = new();
                }

                public class TabItem : ContentControl
                {
                    public object? Header { get; set; }
                }

                public class Button : ContentControl { }
            }

            namespace Demo
            {
                public partial class HostView : global::Avalonia.Controls.UserControl { }
                public partial class ChildView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string hostXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.HostView">
                <TabControl>
                    <TabItem Header="Child">
                        <local:ChildView />
                    </TabItem>
                </TabControl>
            </UserControl>
            """;

        const string childXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.ChildView">
                <Button Content="Child Content" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("HostView.axaml", hostXaml), ("ChildView.axaml", childXaml)]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedTrees = updatedCompilation.SyntaxTrees.Select(static tree => tree.ToString()).ToArray();
        var hostGenerated = Assert.Single(
            generatedTrees.Where(static source =>
                source.Contains("partial class HostView") &&
                source.Contains("__PopulateGeneratedObjectGraph")));
        Assert.Contains("new global::Demo.ChildView()", hostGenerated);
        Assert.DoesNotContain(".Content = default!;", hostGenerated);
    }

    [Fact]
    public void Emits_Object_Lifecycle_And_NameScope_Completion()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement : global::System.ComponentModel.ISupportInitialize
                {
                    public void BeginInit() { }
                    public void EndInit() { }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope
                {
                    void Register(string name, object value);
                    void Complete();
                    bool IsCompleted { get; }
                }

                public class NameScope : INameScope
                {
                    public bool IsCompleted { get; private set; }
                    public void Register(string name, object value) { }
                    public void Complete() { IsCompleted = true; }
                    public static void SetNameScope(global::Avalonia.StyledElement element, INameScope? scope) { }
                }

                public class Control : global::Avalonia.StyledElement { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control { }
            }

            namespace Avalonia.Markup.Xaml
            {
                public static class AvaloniaXamlLoader
                {
                    public static void Load(object value) { }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Name="AcceptButton" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("private static void __BeginInit(object? value)", generated);
        Assert.Contains("private static void __EndInit(object? value)", generated);
        Assert.Contains("private static void __TryCompleteNameScope(object? scope)", generated);
        Assert.Contains("__BeginInit(__root);", generated);
        Assert.Contains("__EndInit(__root);", generated);
        Assert.Contains("__TryCompleteNameScope(__nameScope);", generated);
    }

    [Fact]
    public void Uses_ServiceProvider_Constructor_When_Parameterless_Is_Unavailable()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class StyledElement { }
                public class Control : StyledElement { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ServiceOnlyControl : Control
                {
                    public ServiceOnlyControl(global::System.IServiceProvider serviceProvider) { }
                }
            }

            namespace Avalonia.Markup.Xaml
            {
                public static class AvaloniaXamlLoader
                {
                    public static void Load(object value) { }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <ServiceOnlyControl />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
    }

    [Fact]
    public void Applies_TopDown_Initialization_For_UsableDuringInitialization_Types()
    {
        const string code = """
            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = true)]
                public sealed class UsableDuringInitializationAttribute : global::System.Attribute
                {
                    public UsableDuringInitializationAttribute(bool value = true) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class StyledElement : global::System.ComponentModel.ISupportInitialize
                {
                    public void BeginInit() { }
                    public void EndInit() { }
                }

                public class Control : StyledElement { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                [global::Avalonia.Metadata.UsableDuringInitialization]
                public class FastInitControl : Control
                {
                    public string? Tag { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml
            {
                public static class AvaloniaXamlLoader
                {
                    public static void Load(object value) { }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <FastInitControl Tag="Ready" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        var attachIndex = generated.IndexOf("__root.Content = __n0;", StringComparison.Ordinal);
        var propertyIndex = generated.IndexOf("__n0.Tag = \"Ready\";", StringComparison.Ordinal);

        Assert.True(attachIndex >= 0, "Expected top-down attachment assignment to be present.");
        Assert.True(propertyIndex >= 0, "Expected FastInitControl property assignment to be present.");
        Assert.True(attachIndex < propertyIndex, "Expected child attachment before property assignment for top-down initialization.");
    }

    [Fact]
    public void Generates_HotReload_Method_And_Registration_By_Default()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button x:Name="AcceptButton" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("internal void __ApplySourceGenHotReload()", generated);
        Assert.Contains("__RegisterXamlSourceGenArtifacts();", generated);
        Assert.Contains("XamlIncludeGraphRegistry.Clear(", generated);
        Assert.Contains("XamlCompiledBindingRegistry.Clear(", generated);
        Assert.Contains("__TrackAndReconcileSourceGenHotReloadState(this);", generated);
        Assert.Contains("XamlSourceGenHotReloadStateTracker.Reconcile", generated);
        Assert.Contains("XamlSourceGenHotReloadManager.Register", generated);
        Assert.Contains("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadRegistrationOptions", generated);
        Assert.Contains("CaptureState = static __instance =>", generated);
        Assert.Contains("RestoreState = static (__instance, __state) =>", generated);
    }

    [Fact]
    public void HotReload_Can_Be_Disabled_Via_Build_Property()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button x:Name="AcceptButton" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "false")
            ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("__ApplySourceGenHotReload", generated);
        Assert.DoesNotContain("XamlSourceGenHotReloadManager.Register", generated);
    }

    [Fact]
    public void HotReload_Emits_Root_State_Tracking_For_Collections_And_Avalonia_Properties()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TitleProperty = new();
                    public object? Content { get; set; }
                    public Styles Styles { get; } = new();
                    public ResourceDictionary Resources { get; } = new();
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                }

                public class TextBlock : Control { }

                public class Styles : global::System.Collections.Generic.List<Control> { }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?> { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Title="Sample">
                <UserControl.Styles>
                    <TextBlock />
                </UserControl.Styles>
                <UserControl.Resources>
                    <TextBlock x:Key="Accent" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__GetSourceGenHotReloadCollectionMembers()", generated);
        Assert.Contains("\"Resources\"", generated);
        Assert.Contains("\"Styles\"", generated);
        Assert.Contains("__GetSourceGenHotReloadAvaloniaPropertyMembers()", generated);
        Assert.Contains("global::Avalonia.Controls.UserControl.TitleProperty", generated);
        Assert.DoesNotContain("private static readonly string[] __SourceGenHotReloadCollectionMembers", generated);
    }

    [Fact]
    public void HotReload_Emits_Named_Field_Members_In_Clr_Reset_Manifest()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button x:Name="AcceptButton" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__GetSourceGenHotReloadClrPropertyMembers()", generated);
        Assert.Contains("return new string[] { \"AcceptButton\" };", generated);
    }

    [Fact]
    public void HotReload_Emits_Clear_Before_Dictionary_Merge_Property_Reapply()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public ResourceDictionary Resources { get; } = new();
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                }

                public class Button : Control { }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?> { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <Button x:Key="PrimaryButton" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__TryClearCollection(__root.Resources);", generated);
        Assert.Contains("__root.Resources.Add(\"PrimaryButton\",", generated);
    }

    [Fact]
    public void HotReload_Emits_Root_Event_Subscription_Manifest_For_Reconciliation()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public event global::System.EventHandler? Loaded;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnLoaded(object? sender, global::System.EventArgs args) { }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Loaded="OnLoaded" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__GetSourceGenHotReloadRootEventSubscriptions()", generated);
        Assert.Contains("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadEventDescriptor(\"Loaded\", \"OnLoaded\", false, null, null, null)", generated);
        Assert.Contains("__GetSourceGenHotReloadRootEventSubscriptions());", generated);
    }

    [Fact]
    public void HotReload_WatchMode_Uses_Last_Good_Source_When_Xaml_Is_Temporarily_Invalid()
    {
        const string code = "namespace Demo; public partial class WatchView {}";
        const string validXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.WatchView">
                <Button Content="Valid" />
            </UserControl>
            """;

        const string invalidXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.WatchView">
                <Button Content="Broken"
            </UserControl>
            """;

        var options = new[]
        {
            new KeyValuePair<string, string>("build_property.DotNetWatchBuild", "true"),
            new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "true"),
            new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadErrorResilienceEnabled", "true"),
        };

        var compilation = CreateCompilation(code);
        var first = RunGeneratorWithResult(
            compilation,
            [("WatchView.axaml", validXaml)],
            options);
        var second = RunGeneratorWithResult(
            compilation,
            [("WatchView.axaml", invalidXaml)],
            options);

        Assert.Empty(first.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(second.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(second.Diagnostics, d => d.Id == "AXSG0001" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(second.Diagnostics, d => d.Id == "AXSG0700" && d.Severity == DiagnosticSeverity.Warning);

        var firstSources = first.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);
        var secondSources = second.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);

        Assert.Equal(firstSources.Count, secondSources.Count);
        foreach (var pair in firstSources)
        {
            Assert.True(secondSources.TryGetValue(pair.Key, out var secondSource));
            Assert.Equal(pair.Value, secondSource);
        }
    }

    [Fact]
    public void HotReload_IdeMode_Uses_Last_Good_Source_When_Xaml_Is_Temporarily_Invalid()
    {
        const string code = "namespace Demo; public partial class IdeView {}";
        const string validXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.IdeView">
                <Button Content="Valid" />
            </UserControl>
            """;

        const string invalidXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.IdeView">
                <Button Content="Broken"
            </UserControl>
            """;

        var options = new[]
        {
            new KeyValuePair<string, string>("build_property.DotNetWatchBuild", "false"),
            new KeyValuePair<string, string>("build_property.BuildingInsideVisualStudio", "true"),
            new KeyValuePair<string, string>("build_property.AvaloniaSourceGenIdeHotReloadEnabled", "true"),
            new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "true"),
            new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadErrorResilienceEnabled", "true"),
        };

        var compilation = CreateCompilation(code);
        var first = RunGeneratorWithResult(
            compilation,
            [("IdeView.axaml", validXaml)],
            options);
        var second = RunGeneratorWithResult(
            compilation,
            [("IdeView.axaml", invalidXaml)],
            options);

        Assert.Empty(first.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(second.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(second.Diagnostics, d => d.Id == "AXSG0001" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(second.Diagnostics, d => d.Id == "AXSG0700" && d.Severity == DiagnosticSeverity.Warning);

        var firstSources = first.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);
        var secondSources = second.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);

        Assert.Equal(firstSources.Count, secondSources.Count);
        foreach (var pair in firstSources)
        {
            Assert.True(secondSources.TryGetValue(pair.Key, out var secondSource));
            Assert.Equal(pair.Value, secondSource);
        }
    }

    [Fact]
    public void HotReload_Resilience_Can_Be_Disabled_To_Keep_Strict_Error_Behavior()
    {
        const string code = "namespace Demo; public partial class StrictView {}";
        const string invalidXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.StrictView">
                <Button>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            [("StrictView.axaml", invalidXaml)],
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.DotNetWatchBuild", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadErrorResilienceEnabled", "false")
            ]);

        Assert.Contains(diagnostics, d => d.Id == "AXSG0001" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0700");
    }

    [Fact]
    public void Emits_Pass_Execution_Trace_When_Enabled()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button x:Name="AcceptButton" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTracePasses", "true")
            ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("// pass: AXSG-P001-BindNamedElements => XNameTransformer", generated);
        Assert.Contains("// pass: AXSG-P900-Finalize", generated);
        Assert.Contains("AddNameScopeRegistration", generated);
    }

    [Fact]
    public void Generates_Children_For_Collection_Properties_With_Inherited_Add_Method()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class Window : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
                public class Controls : global::System.Collections.Generic.List<Control> { }
                public class Panel : Control
                {
                    public Controls Children { get; } = new();
                }
                public class StackPanel : Panel { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.Window { }
            }
            """;

        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Demo.MainView">
                <StackPanel>
                    <TextBlock Text="Hello" />
                    <TextBlock Text="World" />
                </StackPanel>
            </Window>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__root.Content = __n0;", generated);
        Assert.Contains("__n0.Children.Add(__n1);", generated);
        Assert.Contains("__n0.Children.Add(__n2);", generated);
        Assert.Contains("__n1.Text = \"Hello\";", generated);
        Assert.Contains("__n2.Text = \"World\";", generated);
    }

    [Fact]
    public void Emits_Idempotent_Clear_And_Event_Rewire_For_Reapplied_Graphs()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                    public string? Content { get; set; }
                }
                public class Controls : global::System.Collections.Generic.List<Control> { }
                public class Panel : Control
                {
                    public Controls Children { get; } = new();
                }
                public class StackPanel : Panel { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnAccept(object? sender, global::System.EventArgs args) { }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <Button Content="Run" Click="OnAccept" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__TryClearCollection(__n0.Children);", generated);
        Assert.Contains("__TryInvokeClearMethod(value);", generated);
        Assert.Contains("if (list.IsReadOnly || list.IsFixedSize)", generated);
        Assert.Contains("catch (global::System.InvalidOperationException)", generated);
        Assert.Contains("__n1.Click -= __root.OnAccept;", generated);
        Assert.Contains("__n1.Click += __root.OnAccept;", generated);
    }

    [Fact]
    public void Emits_ItemsCollection_Clear_Guards_For_ItemsSource_Backends()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class IndexerDescriptor
                {
                    public AvaloniaProperty? Property { get; set; }
                }
            }

            namespace Avalonia.Data
            {
                public enum BindingMode
                {
                    TwoWay
                }

                public class Binding
                {
                    public Binding(string path) { }
                    public BindingMode Mode { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }

                public class ItemCollection : global::System.Collections.IList
                {
                    public int Count => 0;
                    public object SyncRoot => this;
                    public bool IsSynchronized => false;
                    public bool IsReadOnly => true;
                    public bool IsFixedSize => false;
                    public object? this[int index] { get => null; set { } }
                    public int Add(object? value) => -1;
                    public void Clear() { throw new global::System.InvalidOperationException(); }
                    public bool Contains(object? value) => false;
                    public int IndexOf(object? value) => -1;
                    public void Insert(int index, object? value) { }
                    public void Remove(object? value) { }
                    public void RemoveAt(int index) { }
                    public void CopyTo(global::System.Array array, int index) { }
                    public global::System.Collections.IEnumerator GetEnumerator() => global::System.Array.Empty<object>().GetEnumerator();
                }

                public class ItemsControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ItemsSourceProperty = new();
                    public ItemCollection Items { get; } = new();
                }

                public class ListBox : ItemsControl
                {
                    public object? ItemTemplate { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <ListBox ItemsSource="{Binding People}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Name}" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__TryClearCollection(__n0.Items);", generated);
        Assert.Contains("if (list.IsReadOnly || list.IsFixedSize)", generated);
        Assert.Contains("catch (global::System.InvalidOperationException)", generated);
    }

    [Fact]
    public void Generates_Property_Element_Object_Assignment()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class Window : Control
                {
                    public object? Content { get; set; }
                }
                public class ListBox : Control
                {
                    public object? ItemTemplate { get; set; }
                }
                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.Window { }
            }
            """;

        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:t="clr-namespace:Avalonia.Markup.Xaml.Templates"
                    x:Class="Demo.MainView">
                <ListBox>
                    <ListBox.ItemTemplate>
                        <t:DataTemplate>
                            <TextBlock Text="Row" />
                        </t:DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Window>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(".ItemTemplate =", generated);
        Assert.Contains(".Content =", generated);
        Assert.Contains("__n2.Text = \"Row\";", generated);
    }

    [Fact]
    public void Generates_Keyed_Dictionary_Add_For_XKey_Children()
    {
        const string code = """
            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public global::Avalonia.ResourceDictionary Resources { get; set; } = new();
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:a="clr-namespace:Avalonia"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <a:ResourceDictionary>
                        <TextBlock x:Key="Greeting" Text="Hello" />
                    </a:ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(".Add(\"Greeting\",", generated);
        Assert.Contains("Text = \"Hello\";", generated);
    }

    [Fact]
    public void Generates_Keyed_Dictionary_Add_For_Settable_Dictionary_Property_Entries()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public interface IResourceDictionary
                {
                    void Add(object key, object value);
                }

                public class ResourceDictionary : IResourceDictionary
                {
                    public void Add(object key, object value) { }
                }

                public class Control { }
                public class UserControl : Control
                {
                    public IResourceDictionary Resources { get; set; } = new ResourceDictionary();
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <TextBlock x:Key="Greeting" Text="Hello" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(".Resources.Add(\"Greeting\",", generated);
        Assert.Contains("Text = \"Hello\";", generated);
    }

    [Fact]
    public void Generates_Object_Factory_From_Inline_Text_Content()
    {
        const string code = """
            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public global::Avalonia.ResourceDictionary Resources { get; set; } = new();
                    public object? Content { get; set; }
                }

                public class SolidColorBrush
                {
                    public static SolidColorBrush Parse(string value) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <SolidColorBrush x:Key="AccentBrush">Red</SolidColorBrush>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Controls.SolidColorBrush.Parse(\"Red\")", generated);
        Assert.Contains(".Resources.Add(\"AccentBrush\",", generated);
    }

    [Fact]
    public void Generates_Runtime_Binding_Assignment_For_Avalonia_Properties()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }

                    public void SetValue(AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding
                    : IBinding
                {
                    public Binding(string path) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBox Text="{Binding Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("[new global::Avalonia.Data.IndexerDescriptor { Property = global::Avalonia.Controls.TextBox.TextProperty }] = new global::Avalonia.Data.Binding(\"Name\");", generated);
    }

    [Fact]
    public void Generates_Runtime_Binding_Initializer_Options_For_Supported_Binding_Properties()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }
                public interface IValueConverter { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public enum BindingMode
                {
                    Default,
                    OneWay,
                    TwoWay,
                    OneTime,
                    OneWayToSource
                }

                public enum BindingPriority
                {
                    LocalValue,
                    Style,
                    Template
                }

                public enum UpdateSourceTrigger
                {
                    Default,
                    PropertyChanged,
                    LostFocus,
                    Explicit
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public BindingMode Mode { get; set; }
                    public object? Source { get; set; }
                    public IValueConverter? Converter { get; set; }
                    public global::System.Globalization.CultureInfo? ConverterCulture { get; set; }
                    public object? ConverterParameter { get; set; }
                    public object? StringFormat { get; set; }
                    public object? FallbackValue { get; set; }
                    public object? TargetNullValue { get; set; }
                    public int Delay { get; set; }
                    public BindingPriority Priority { get; set; }
                    public UpdateSourceTrigger UpdateSourceTrigger { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public sealed class DemoConverter : global::Avalonia.Data.IValueConverter { }

                public static class BindingSources
                {
                    public static readonly object Primary = new object();
                }

                public static class Converters
                {
                    public static readonly global::Avalonia.Data.IValueConverter TitleConverter = new DemoConverter();
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <TextBox Text="{Binding Path=Name,
                                        Mode=TwoWay,
                                        Source={x:Static local:BindingSources.Primary},
                                        Converter={x:Static local:Converters.TitleConverter},
                                        ConverterCulture='en-US',
                                        ConverterParameter='arg',
                                        StringFormat='Value: {0}',
                                        FallbackValue='n/a',
                                        TargetNullValue='-',
                                        Delay=250,
                                        Priority=Style,
                                        UpdateSourceTrigger=PropertyChanged}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(\"Name\") {", generated);
        Assert.Contains("Mode = global::Avalonia.Data.BindingMode.TwoWay", generated);
        Assert.Contains("Source = global::Demo.BindingSources.Primary", generated);
        Assert.Contains("Converter = global::Demo.Converters.TitleConverter", generated);
        Assert.Contains("ConverterCulture = global::System.Globalization.CultureInfo.GetCultureInfo(\"en-US\")", generated);
        Assert.Contains("ConverterParameter = \"arg\"", generated);
        Assert.Contains("StringFormat = \"Value: {0}\"", generated);
        Assert.Contains("FallbackValue = \"n/a\"", generated);
        Assert.Contains("TargetNullValue = \"-\"", generated);
        Assert.Contains("Delay = 250", generated);
        Assert.Contains("Priority = global::Avalonia.Data.BindingPriority.Style", generated);
        Assert.Contains("UpdateSourceTrigger = global::Avalonia.Data.UpdateSourceTrigger.PropertyChanged", generated);
    }

    [Fact]
    public void Preserves_Binding_Options_When_Query_Path_Is_Normalized()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public enum BindingMode
                {
                    Default,
                    OneWay
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public BindingMode Mode { get; set; }
                    public string? ElementName { get; set; }
                    public object? FallbackValue { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <TextBox x:Name="SearchBox" />
                    <TextBlock Text="{Binding #SearchBox.Text, Mode=OneWay, FallbackValue='missing'}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(\"Text\")", generated);
        Assert.Contains("Mode = global::Avalonia.Data.BindingMode.OneWay", generated);
        Assert.Contains("ElementName = \"SearchBox\"", generated);
        Assert.Contains("FallbackValue = \"missing\"", generated);
    }

    [Fact]
    public void Supports_ElementName_Query_Binding_Path_With_CompiledBindings_Enabled()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public string? ElementName { get; set; }
                    public object? RelativeSource { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string Name { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <StackPanel>
                    <TextBox x:Name="SearchBox" Text="Hello" />
                    <TextBlock Text="{Binding #SearchBox.Text}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(", generated);
        Assert.Contains("ElementName = \"SearchBox\"", generated);
    }

    [Fact]
    public void Supports_Parent_Query_Binding_Path_With_CompiledBindings_Enabled()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public object? RelativeSource { get; set; }
                }

                public enum RelativeSourceMode
                {
                    Self,
                    FindAncestor
                }

                public class RelativeSource
                {
                    public RelativeSource(RelativeSourceMode mode) { }
                    public int AncestorLevel { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public object? Tag { get; set; }
                }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string Name { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <StackPanel Tag="ParentTag">
                    <TextBlock Text="{Binding $parent.Tag}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(\"Tag\")", generated);
        Assert.Contains("RelativeSource = new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.FindAncestor)", generated);
    }

    [Fact]
    public void Supports_Self_Query_Binding_Path_With_CompiledBindings_Enabled()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public object? RelativeSource { get; set; }
                }

                public enum RelativeSourceMode
                {
                    Self
                }

                public class RelativeSource
                {
                    public RelativeSource(RelativeSourceMode mode) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public object? Tag { get; set; }
                }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string Name { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <StackPanel Tag="SelfTag">
                    <TextBlock Text="{Binding $self.Tag}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(\"Tag\")", generated);
        Assert.Contains("RelativeSource = new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.Self)", generated);
    }

    [Fact]
    public void Supports_Parent_Query_With_Level_Only_Syntax()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public object? RelativeSource { get; set; }
                }

                public enum RelativeSourceMode
                {
                    FindAncestor
                }

                public class RelativeSource
                {
                    public RelativeSource(RelativeSourceMode mode) { }
                    public int AncestorLevel { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public object? Tag { get; set; }
                }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string Name { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <StackPanel Tag="ParentTag">
                    <TextBlock Text="{Binding $parent[2].Tag}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(\"Tag\")", generated);
        Assert.Contains("RelativeSourceMode.FindAncestor", generated);
        Assert.Contains("AncestorLevel = 2", generated);
    }

    [Fact]
    public void Supports_Parent_Query_With_Type_And_Level_Using_Semicolon_Syntax()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public object? RelativeSource { get; set; }
                }

                public enum RelativeSourceMode
                {
                    FindAncestor
                }

                public class RelativeSource
                {
                    public RelativeSource(RelativeSourceMode mode) { }
                    public global::System.Type? AncestorType { get; set; }
                    public int AncestorLevel { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public object? Tag { get; set; }
                }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:ac="clr-namespace:Avalonia.Controls"
                         x:Class="Demo.MainView">
                <StackPanel Tag="ParentTag">
                    <TextBlock Text="{Binding $parent[ac:StackPanel;2].Tag}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(\"Tag\")", generated);
        Assert.Contains("RelativeSourceMode.FindAncestor", generated);
        Assert.Contains("AncestorType = typeof(global::Avalonia.Controls.StackPanel)", generated);
        Assert.Contains("AncestorLevel = 2", generated);
    }

    [Fact]
    public void Converts_Source_XReference_To_ElementName_Binding_Source()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public object? Source { get; set; }
                    public string? ElementName { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <TextBox x:Name="SearchBox" />
                    <TextBlock Text="{Binding Path=Text, Source={x:Reference SearchBox}}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.Binding(\"Text\")", generated);
        Assert.Contains("ElementName = \"SearchBox\"", generated);
        Assert.DoesNotContain("Source =", generated);
    }

    [Fact]
    public void Generates_Name_Reference_Helper_For_XReference_Value()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBox : Control
                {
                    public string? Text { get; set; }
                }
                public class Border : Control
                {
                    public object? Tag { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <TextBox x:Name="SearchBox" />
                    <Border Tag="{x:Reference SearchBox}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideReference(\"SearchBox\"", generated);
        Assert.Contains("SourceGenProvideValueTargetPropertyFactory.CreateWritable<global::Avalonia.Controls.Border", generated);
        Assert.Contains("(\"Tag\", static (__target, __value) => __target.Tag = __value)", generated);
        Assert.DoesNotContain("__AXSG_CTX_", generated);
    }

    [Fact]
    public void Generates_ReflectionBinding_Runtime_Call_For_Markup_Extension()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }

                    public void SetValue(AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public enum BindingMode
                {
                    Default,
                    OneWay
                }

                public enum BindingPriority
                {
                    LocalValue,
                    Style,
                    Template
                }

                public class RelativeSource { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public class ReflectionBindingExtension : global::Avalonia.Data.IBinding
                {
                    public ReflectionBindingExtension(string path) { }
                    public global::Avalonia.Data.BindingMode Mode { get; set; }
                    public global::Avalonia.Data.BindingPriority Priority { get; set; }
                    public object? Source { get; set; }
                    public global::Avalonia.Data.RelativeSource? RelativeSource { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="{ReflectionBinding Message}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"Message\")", generated);
        Assert.Contains("[new global::Avalonia.Data.IndexerDescriptor { Property = global::Avalonia.Controls.TextBlock.TextProperty }] = global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(", generated);
    }

    [Fact]
    public void Generates_OnPlatform_Runtime_Call_For_Markup_Extension()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public class OnPlatformExtension
                {
                }
            }

            namespace Demo
            {
                public class Host : global::Avalonia.Controls.Control
                {
                    public string? Value { get; set; }
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <local:Host Value="{OnPlatform Default='Base', Windows='Win'}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideOnPlatform(", generated);
        Assert.Contains("\"Base\", \"Win\"", generated);
        Assert.DoesNotContain("__AXSG_CTX_", generated);
    }

    [Fact]
    public void Generates_OnFormFactor_Runtime_Call_For_Markup_Extension()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public class OnFormFactorExtension
                {
                }
            }

            namespace Demo
            {
                public class Host : global::Avalonia.Controls.Control
                {
                    public string? Value { get; set; }
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <local:Host Value="{OnFormFactor Default='Base', Desktop='Desktop', Mobile='Mobile'}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(", generated);
        Assert.Contains("\"Base\", \"Desktop\", \"Mobile\", null, __serviceProvider", generated);
        Assert.DoesNotContain("__AXSG_CTX_", generated);
    }

    [Fact]
    public void Generates_RelativeSource_Object_For_Standalone_RelativeSource_Markup()
    {
        const string code = """
            namespace Avalonia.Data
            {
                public enum RelativeSourceMode
                {
                    Self,
                    TemplatedParent,
                    DataContext,
                    FindAncestor
                }

                public enum TreeType
                {
                    Visual,
                    Logical
                }

                public class RelativeSource
                {
                    public RelativeSource(RelativeSourceMode mode) { }
                    public global::System.Type? AncestorType { get; set; }
                    public int AncestorLevel { get; set; }
                    public TreeType Tree { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public class Host : global::Avalonia.Controls.Control
                {
                    public object? Value { get; set; }
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <local:Host Value="{RelativeSource Mode=Self}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.Self)", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Query_Source_Conflict_With_ElementName()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public string? ElementName { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <TextBox x:Name="SearchBox" />
                    <TextBlock Text="{Binding #SearchBox.Text, ElementName=Other}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
    }

    [Fact]
    public void Reports_Diagnostic_For_Query_Source_Conflict_With_Source()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                    public object? Source { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public object? Tag { get; set; }
                }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public static class BindingSources
                {
                    public static object Primary { get; } = new object();
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <TextBlock Text="{Binding $self.Tag, Source={x:Static local:BindingSources.Primary}}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
    }

    [Fact]
    public void Generates_Compiled_Bindings_And_Style_Theme_Registrations()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
                public class Button : Control
                {
                    public object? Content { get; set; }
                }
                public class Style : Control { }
                public class ControlTheme : Control { }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public string Title { get; set; } = string.Empty;
                    public string Caption { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <TextBlock Text="{Binding Title}" />
                <UserControl.Styles>
                    <StyleInclude Source="/Styles/Common.axaml" />
                    <Style Selector="TextBlock" x:DataType="vm:MainViewModel" x:CompileBindings="True">
                        <Setter Property="Text" Value="{CompiledBinding Caption}" />
                    </Style>
                </UserControl.Styles>
                <UserControl.Resources>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude Source="avares://Demo.Assets/Resources/Colors.axaml" />
                    </ResourceDictionary.MergedDictionaries>
                    <ControlTheme x:Key="Theme.Button" TargetType="Button" ThemeVariant="Dark" x:DataType="vm:MainViewModel" x:CompileBindings="True">
                        <Setter Property="Content" Value="{CompiledBinding Title}" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, d => d.Id is "AXSG0110" or "AXSG0111" or "AXSG0300" or "AXSG0301" or "AXSG0302" or "AXSG0303");

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("XamlCompiledBindingRegistry.Register", generated);
        Assert.Contains("__CompiledBindingAccessor0", generated);
        Assert.Contains("XamlStyleRegistry.Register", generated);
        Assert.Contains("XamlControlThemeRegistry.Register", generated);
        Assert.Contains("XamlIncludeRegistry.Register", generated);
        Assert.Contains("XamlIncludeGraphRegistry.Register", generated);
        Assert.Contains("\"Text\"", generated);
        Assert.Contains("\"Content\"", generated);
        Assert.Contains("\"Caption\"", generated);
        Assert.Contains("\"Title\"", generated);
        Assert.Contains("\"Dark\"", generated);
    }

    [Fact]
    public void Emits_ControlTheme_Materializer_Registration_With_Factory_Method()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Resources { get; set; }
                }

                public class Button : Control
                {
                    public static readonly object ContentProperty = new();
                    public string? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Base" TargetType="Button">
                        <Setter Property="Content" Value="Base" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("XamlControlThemeRegistry.Register", generated);
        Assert.Contains("static () => __BuildGeneratedControlTheme0()", generated);
        Assert.Contains("private static global::Avalonia.Styling.ControlTheme __BuildGeneratedControlTheme0()", generated);
        Assert.Contains("__theme.TargetType = typeof(global::Avalonia.Controls.Button);", generated);
        Assert.Contains(
            "__theme.Setters.Add(new global::Avalonia.Styling.Setter(global::Avalonia.Controls.Button.ContentProperty, \"Base\"));",
            generated);
    }

    [Fact]
    public void Resolves_Style_Setter_Target_From_Rightmost_Selector_Type()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
                public class StackPanel : Control { }
                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="StackPanel > TextBlock">
                        <Setter Property="Text" Value="Hello" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0301");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0300");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("\"global::Avalonia.Controls.TextBlock\"", generated);
    }

    [Fact]
    public void Resolves_Style_Selector_Target_With_Namespace_Alias_Syntax()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
            }

            namespace Demo.Controls
            {
                public class FancyControl : global::Avalonia.Controls.Control
                {
                    public string? Title { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo.Controls"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="local|FancyControl:pointerover">
                        <Setter Property="Title" Value="Hover" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0300");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0301");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("\"global::Demo.Controls.FancyControl\"", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Invalid_Style_Selector_Target()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="MissingControl">
                        <Setter Property="Tag" Value="Hello" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0300");
    }

    [Fact]
    public void Reports_Diagnostic_For_Missing_Include_Source()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude />
                    </ResourceDictionary.MergedDictionaries>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0400");
    }

    [Fact]
    public void Reports_Diagnostic_For_Local_Include_Target_Not_Found_In_SourceGen_Set()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude Source="/Missing.axaml" />
                    </ResourceDictionary.MergedDictionaries>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, [("Main.axaml", xaml)]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0403");
    }

    [Fact]
    public void Reports_Diagnostic_For_Include_Cycle_Across_SourceGen_Documents()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
            }

            namespace Demo
            {
                public partial class AView : global::Avalonia.Controls.UserControl { }
                public partial class BView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xamlA = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.AView">
                <UserControl.Resources>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude Source="/B.axaml" />
                    </ResourceDictionary.MergedDictionaries>
                </UserControl.Resources>
            </UserControl>
            """;

        const string xamlB = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.BView">
                <UserControl.Resources>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude Source="/A.axaml" />
                    </ResourceDictionary.MergedDictionaries>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            [("A.axaml", xamlA), ("B.axaml", xamlB)]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0404");
    }

    [Fact]
    public void Reports_Diagnostic_For_Duplicate_Generated_Uri_Registration_Target()
    {
        const string code = """
            namespace Avalonia.Controls { public class UserControl { } }
            namespace Demo
            {
                public partial class FirstView : global::Avalonia.Controls.UserControl { }
                public partial class SecondView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xamlA = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.FirstView" />
            """;

        const string xamlB = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.SecondView" />
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            [("FolderA/Main.axaml", xamlA), ("FolderB/Main.axaml", xamlB)]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0601");
    }

    [Fact]
    public void Deduplicates_AdditionalFiles_With_Same_Path_To_Avoid_HintName_Collisions()
    {
        const string code = """
            namespace Avalonia.Controls { public class UserControl { } }
            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("App.axaml", xaml), ("App.axaml", xaml)]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains("hintName", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(compilation.SyntaxTrees.Count() + 1, updatedCompilation.SyntaxTrees.Count());
    }

    [Fact]
    public void Duplicate_Path_Representations_Are_Deduplicated_To_Avoid_HintName_Collisions()
    {
        const string code = """
            namespace Avalonia.Controls { public class UserControl { } }
            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xamlA = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView" />
            """;

        const string xamlB = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("App.axaml", xamlA), ("./App.axaml", xamlB)]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains("hintName", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(compilation.SyntaxTrees.Count() + 1, updatedCompilation.SyntaxTrees.Count());
    }

    [Fact]
    public void Skips_Generation_When_Precompile_Is_False()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         x:Precompile="False" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(compilation.SyntaxTrees.Count(), updatedCompilation.SyntaxTrees.Count());
    }

    [Fact]
    public void Generates_Class_Modifier_From_Class_Symbol()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("public partial class MainView", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_ClassModifier_Mismatch()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         x:ClassModifier="Internal" />
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0105");
    }

    [Fact]
    public void Reports_Diagnostic_For_Duplicate_Style_Setter()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock">
                        <Setter Property="Text" Value="A" />
                        <Setter Property="Text" Value="B" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0304");
    }

    [Fact]
    public void Reports_Diagnostic_For_ControlTheme_BasedOn_Key_Not_Found()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Resources { get; set; }
                }
                public class Button : Control
                {
                    public string? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Primary"
                                  TargetType="Button"
                                  BasedOn="{StaticResource Theme.Base}">
                        <Setter Property="Content" Value="Primary" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0305");
    }

    [Fact]
    public void Reports_Diagnostic_For_ControlTheme_BasedOn_Cycle()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Resources { get; set; }
                }
                public class Button : Control
                {
                    public string? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.A"
                                  TargetType="Button"
                                  BasedOn="{StaticResource Theme.B}">
                        <Setter Property="Content" Value="A" />
                    </ControlTheme>
                    <ControlTheme x:Key="Theme.B"
                                  TargetType="Button"
                                  BasedOn="{StaticResource Theme.A}">
                        <Setter Property="Content" Value="B" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0306");
    }

    [Fact]
    public void Strict_Mode_Escalates_Warnings_To_Errors()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Tag="UnsupportedProperty" />
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        var strictDiagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0101"));
        Assert.Equal(DiagnosticSeverity.Error, strictDiagnostic.Severity);
    }

    [Fact]
    public void Emits_Source_Info_Registrations_When_Enabled()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button x:Name="ActionButton" Content="Run" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenCreateSourceInfo", "true")
            ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("XamlSourceInfoRegistry.Register", generated);
        Assert.Contains("\"Object\"", generated);
        Assert.Contains("\"NamedElement\"", generated);
        Assert.Contains("NamedElement:0:ActionButton", generated);
    }

    [Fact]
    public void Emits_Style_Setter_Source_Info_Registrations_When_Enabled()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock">
                        <Setter Property="Text" Value="Hello" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenCreateSourceInfo", "true")
            ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("\"StyleSetter\"", generated);
        Assert.Contains("Style:0:TextBlock/Setter:0:Text", generated);
    }

    [Fact]
    public void Emits_Event_Source_Info_Registrations_When_Enabled()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnAccept(object? sender, global::System.EventArgs e)
                    {
                    }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="OnAccept" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenCreateSourceInfo", "true")
            ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("\"Event\"", generated);
        Assert.Contains("/Event:0:Click", generated);
    }

    [Fact]
    public void Ignores_Design_Namespace_Attributes()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                         xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                         mc:Ignorable="d"
                         x:Class="Demo.MainView"
                         d:DesignWidth="800"
                         d:DesignHeight="600" />
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
    }

    [Fact]
    public void Reports_Diagnostic_For_DataTemplate_Without_DataType()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <DataTemplate>
                        <TextBlock Text="Hello" />
                    </DataTemplate>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0500");
    }

    [Fact]
    public void Generates_Routed_Event_Subscription()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                    public void RaiseClick() => Click?.Invoke(this, global::System.EventArgs.Empty);
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnAccept(object? sender, global::System.EventArgs e)
                    {
                    }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="OnAccept" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__n0.Click += __root.OnAccept;", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Incompatible_Clr_Event_Handler_Signature()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnAccept(int value)
                    {
                    }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="OnAccept" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
    }

    [Fact]
    public void Generates_Routed_Event_Field_Subscription_Using_AddHandler()
    {
        const string code = """
            namespace Avalonia.Interactivity
            {
                public class RoutedEventArgs : global::System.EventArgs { }
                public class RoutedEvent { }
                public class RoutedEvent<TEventArgs> : RoutedEvent where TEventArgs : RoutedEventArgs { }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                    public void AddHandler(global::Avalonia.Interactivity.RoutedEvent routedEvent, global::System.Delegate handler) { }
                    public void RemoveHandler(global::Avalonia.Interactivity.RoutedEvent routedEvent, global::System.Delegate handler) { }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.Interactivity.RoutedEvent<global::Avalonia.Interactivity.RoutedEventArgs> ClickEvent = new global::Avalonia.Interactivity.RoutedEvent<global::Avalonia.Interactivity.RoutedEventArgs>();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnAccept(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
                    {
                    }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="OnAccept" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__n0.RemoveHandler(global::Avalonia.Controls.Button.ClickEvent, (global::System.EventHandler<global::Avalonia.Interactivity.RoutedEventArgs>)__root.OnAccept);", generated);
        Assert.Contains("__n0.AddHandler(global::Avalonia.Controls.Button.ClickEvent, (global::System.EventHandler<global::Avalonia.Interactivity.RoutedEventArgs>)__root.OnAccept);", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Incompatible_Routed_Event_Handler_Signature()
    {
        const string code = """
            namespace Avalonia.Interactivity
            {
                public class RoutedEventArgs : global::System.EventArgs { }
                public class RoutedEvent { }
                public class RoutedEvent<TEventArgs> : RoutedEvent where TEventArgs : RoutedEventArgs { }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                    public void AddHandler(global::Avalonia.Interactivity.RoutedEvent routedEvent, global::System.Delegate handler) { }
                    public void RemoveHandler(global::Avalonia.Interactivity.RoutedEvent routedEvent, global::System.Delegate handler) { }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.Interactivity.RoutedEvent<global::Avalonia.Interactivity.RoutedEventArgs> ClickEvent = new global::Avalonia.Interactivity.RoutedEvent<global::Avalonia.Interactivity.RoutedEventArgs>();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnAccept(object? sender, int value)
                    {
                    }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="OnAccept" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain(".AddHandler(global::Avalonia.Controls.Button.ClickEvent", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Invalid_Routed_Event_Field_Definition()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public void AddHandler(object routedEvent, global::System.Delegate handler) { }
                    public void RemoveHandler(object routedEvent, global::System.Delegate handler) { }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly int ClickEvent = 42;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    private void OnAccept(object? sender, global::System.EventArgs e)
                    {
                    }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="OnAccept" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
    }

    [Fact]
    public void Converts_Brush_Literal_For_IBrush_Target()
    {
        const string code = """
            namespace Avalonia.Media
            {
                public interface IBrush { }
                public abstract class Brush : IBrush
                {
                    private sealed class ParsedBrush : IBrush { }
                    public static IBrush Parse(string value) => new ParsedBrush();
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public global::Avalonia.Media.IBrush? Foreground { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Foreground="Red" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Media.Brush.Parse(\"Red\")", generated);
    }

    [Fact]
    public void Resolves_ReadOnly_Avalonia_Property_Assignment_With_SetValue()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public void SetValue(AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new global::Avalonia.AvaloniaProperty();
                    public string? Text { get; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="Hello" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__n0.SetValue(global::Avalonia.Controls.TextBlock.TextProperty, \"Hello\");", generated);
    }

    [Fact]
    public void Resolves_Attached_Avalonia_Property_Assignment_With_SetValue()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public void SetValue(AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class Button : Control { }
                public class Grid : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty RowProperty = new global::Avalonia.AvaloniaProperty();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Grid.Row="1" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__n0.SetValue(global::Avalonia.Controls.Grid.RowProperty, 1);", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_ControlTemplate_Without_TargetType()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
                public class ControlTemplate : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ControlTemplate x:Key="ButtonTemplate" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0501");
    }

    [Fact]
    public void Registers_ControlTemplate_TargetType_Metadata_When_Valid()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control { }
                public class ControlTemplate : Control { }
                public class Button : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ControlTemplate x:Key="ButtonTemplate" TargetType="Button" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0501");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("XamlTemplateRegistry.Register", generated);
        Assert.Contains("\"global::Avalonia.Controls.Button\"", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Indexer_Path()
    {
        const string code = """
            using System.Collections.Generic;

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class Person
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class MainVm
                {
                    public List<Person> People { get; } = new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding People[0].Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.People[0].Name", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Casted_Path_Segment()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class PersonBase
                {
                }

                public class DerivedPerson : PersonBase
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class MainVm
                {
                    public PersonBase SelectedPerson { get; set; } = new DerivedPerson();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding ((vm:DerivedPerson)SelectedPerson).Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("((global::Demo.ViewModels.DerivedPerson)source.SelectedPerson).Name", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Parameterless_Method_Segment()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string ResolveTitle()
                    {
                        return "Title";
                    }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding ResolveTitle}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.ResolveTitle()", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Method_Arguments()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string FormatTitle(int count, string suffix)
                    {
                        return count.ToString() + suffix;
                    }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding FormatTitle(2, 'x')}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.FormatTitle(", generated);
        Assert.Contains("\"x\"", generated);
        Assert.DoesNotContain("source.FormatTitle(2, 'x')", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Compiled_Binding_Method_Argument_Mismatch()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string FormatTitle(int count)
                    {
                        return count.ToString();
                    }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding FormatTitle('bad')}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
    }

    [Fact]
    public void Resolves_Compiled_Binding_Method_Overload_Using_Best_Argument_Match()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MethodResult
                {
                    public string Name { get; set; } = "ok";
                }

                public class MainVm
                {
                    public object ResolveValue(object value)
                    {
                        return value;
                    }

                    public MethodResult ResolveValue(string value)
                    {
                        return new MethodResult();
                    }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding ResolveValue('demo').Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.ResolveValue(", generated);
        Assert.Contains(".Name", generated);
        Assert.DoesNotContain("ResolveValue((global::System.Object)", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Null_Conditional_Segment()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class Person
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class MainVm
                {
                    public Person? SelectedPerson { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding SelectedPerson?.Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.SelectedPerson?.Name", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Null_Conditional_On_NonNullable_Value_Type()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public int Count { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding Count?.ToString}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Attached_Property_Segment()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public static class Grid
                {
                    public static int GetRow(Control control)
                    {
                        return 0;
                    }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public global::Avalonia.Controls.Control Item { get; } = new global::Avalonia.Controls.Control();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         xmlns:ac="clr-namespace:Avalonia.Controls"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding Item.(ac:Grid.Row)}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Controls.Grid.GetRow(source.Item)", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Not_Transform()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public bool IsActive { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding !IsActive}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("!global::System.Convert.ToBoolean(source.IsActive)", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Task_Stream_Operator()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class TitleValue
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class MainVm
                {
                    public global::System.Threading.Tasks.Task<TitleValue> NameTask { get; } =
                        global::System.Threading.Tasks.Task.FromResult(new TitleValue { Name = "demo" });
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding NameTask^.Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenCompiledBindingStreamHelper.UnwrapTask<global::Demo.ViewModels.TitleValue>(source.NameTask)", generated);
        Assert.Contains(".Name", generated);
        Assert.Contains("\"NameTask^.Name\"", generated);
    }

    [Fact]
    public void Generates_Compiled_Binding_Accessor_With_Observable_Stream_Operator()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class ImmediateObservable<T> : global::System.IObservable<T>
                {
                    private readonly T _value;

                    public ImmediateObservable(T value)
                    {
                        _value = value;
                    }

                    public global::System.IDisposable Subscribe(global::System.IObserver<T> observer)
                    {
                        observer.OnNext(_value);
                        observer.OnCompleted();
                        return new Subscription();
                    }

                    private sealed class Subscription : global::System.IDisposable
                    {
                        public void Dispose() { }
                    }
                }

                public class TitleValue
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class MainVm
                {
                    public global::System.IObservable<TitleValue> NameObservable { get; } =
                        new ImmediateObservable<TitleValue>(new TitleValue { Name = "demo" });
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding NameObservable^.Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenCompiledBindingStreamHelper.UnwrapObservable<global::Demo.ViewModels.TitleValue>(source.NameObservable)", generated);
        Assert.Contains(".Name", generated);
        Assert.Contains("\"NameObservable^.Name\"", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Stream_Operator_On_Non_Stream_Type()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string Name { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding Name^}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
    }

    [Fact]
    public void Generates_Static_Resource_Resolver_For_StaticResource_Markup()
    {
        const string code = """
            namespace Avalonia
            {
                public interface IResourceNode
                {
                    bool TryGetResource(object key, object? theme, out object? value);
                }

                public class StyledElement { }

                public class Application
                {
                    public static Application? Current { get; }

                    public bool TryGetResource(object key, object? theme, out object? value)
                    {
                        value = null;
                        return false;
                    }
                }
            }

            namespace Avalonia.LogicalTree
            {
                public interface ILogical
                {
                    ILogical? LogicalParent { get; }
                }
            }

            namespace Avalonia.Controls
            {
                public class NameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, object value) { }
                    public void Register(string name, object element) { }
                }

                public class Control : global::Avalonia.StyledElement, global::Avalonia.IResourceNode, global::Avalonia.LogicalTree.ILogical
                {
                    public bool TryGetResource(object key, object? theme, out object? value)
                    {
                        value = null;
                        return false;
                    }

                    public global::Avalonia.LogicalTree.ILogical? LogicalParent => null;
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public object? Tag { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Tag="{StaticResource AccentBrush}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideStaticResource(\"AccentBrush\"", generated);
        Assert.DoesNotContain("__AXSG_CTX_", generated);
    }

    [Fact]
    public void Resolves_DynamicResource_Expression_As_Binding_For_AvaloniaProperty_Assignment()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public global::Avalonia.Data.IBinding this[global::Avalonia.Data.IndexerDescriptor binding]
                    {
                        get => throw new global::System.NotImplementedException();
                        set { }
                    }

                    public void SetValue(AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public class DynamicResourceExtension : global::Avalonia.Data.IBinding
                {
                    public DynamicResourceExtension(object resourceKey) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ForegroundProperty = new();
                    public object? Foreground { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Foreground="{DynamicResource AccentBrush}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideDynamicResource(\"AccentBrush\"", generated);
        Assert.Contains("[new global::Avalonia.Data.IndexerDescriptor { Property = global::Avalonia.Controls.TextBlock.ForegroundProperty }] = global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideDynamicResource(\"AccentBrush\"", generated);
        Assert.DoesNotContain("__AXSG_CTX_", generated);
    }

    [Fact]
    public void Resolves_Setter_Property_From_Style_Target_Type()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public Styles Styles { get; } = new();
                }

                public class Styles : global::System.Collections.Generic.List<global::Avalonia.Styling.Style> { }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class Style
                {
                    public string Selector { get; set; } = string.Empty;
                    public void Add(Setter setter) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock">
                        <Setter Property="Text" Value="Hello" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Controls.TextBlock.TextProperty", generated);
        Assert.Contains(".Add(__n1);", generated);
    }

    [Fact]
    public void Converts_Setter_Value_Using_Resolved_AvaloniaProperty_Value_Type()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaProperty<T> : AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty<T> { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public Styles Styles { get; } = new();
                }

                public class Styles : global::System.Collections.Generic.List<global::Avalonia.Styling.Style> { }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.StyledProperty<double> FontSizeProperty = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class Style
                {
                    public string Selector { get; set; } = string.Empty;
                    public void Add(Setter setter) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock">
                        <Setter Property="FontSize" Value="17" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__n1.Property = global::Avalonia.Controls.TextBlock.FontSizeProperty;", generated);
        Assert.Contains("Value = 17", generated);
    }

    [Fact]
    public void Converts_Classes_Literal_To_Collection_Adds_On_StyledElement()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.StyledElement { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Classes : global::System.Collections.Generic.List<string> { }

                public class TextBlock : Control
                {
                    public Classes Classes { get; } = new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Classes="highlight warning" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__TryClearCollection(__n0.Classes);", generated);
        Assert.Contains("__n0.Classes.Add(__n1);", generated);
        Assert.Contains("__n0.Classes.Add(__n2);", generated);
        Assert.Contains("var __n1 = \"highlight\";", generated);
        Assert.Contains("var __n2 = \"warning\";", generated);
    }

    [Fact]
    public void Converts_Selector_Value_With_Combinators_And_PseudoClasses()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Styling.Selector? Match { get; set; }
                }

                public class TextBlock : Control { }

                public class Button : Control { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector Class(Selector? previous, string name) => new();
                    public static Selector Name(Selector? previous, string name) => new();
                    public static Selector Descendant(Selector? previous) => new();
                    public static Selector Child(Selector previous) => new();
                    public static Selector Template(Selector previous) => new();
                    public static Selector Or(params Selector[] selectors) => new();
                    public static Selector Nesting(Selector? previous) => new();
                    public static Selector Is(Selector? previous, global::System.Type type) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Match="TextBlock:pointerover > Button#Save /template/ TextBlock.warning" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Styling.Selectors.Child(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Template(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Name(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Class(", generated);
        Assert.Contains("\":pointerover\"", generated);
        Assert.Contains("typeof(global::Avalonia.Controls.TextBlock)", generated);
        Assert.Contains("typeof(global::Avalonia.Controls.Button)", generated);
    }

    [Fact]
    public void Converts_Selector_Value_With_Or_Branches()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Styling.Selector? Match { get; set; }
                }

                public class TextBlock : Control { }

                public class Button : Control { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector Class(Selector? previous, string name) => new();
                    public static Selector Name(Selector? previous, string name) => new();
                    public static Selector Descendant(Selector? previous) => new();
                    public static Selector Child(Selector previous) => new();
                    public static Selector Template(Selector previous) => new();
                    public static Selector Or(params Selector[] selectors) => new();
                    public static Selector Nesting(Selector? previous) => new();
                    public static Selector Is(Selector? previous, global::System.Type type) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Match="TextBlock.primary, Button#Save" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Styling.Selectors.Or(", generated);
        Assert.Contains("typeof(global::Avalonia.Controls.TextBlock)", generated);
        Assert.Contains("typeof(global::Avalonia.Controls.Button)", generated);
    }

    [Fact]
    public void Converts_Selector_Value_With_Pseudo_Functions()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Styling.Selector? Match { get; set; }
                }

                public class TextBlock : Control { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector Class(Selector? previous, string name) => new();
                    public static Selector Is(Selector? previous, global::System.Type type) => new();
                    public static Selector Not(Selector? previous, Selector argument) => new();
                    public static Selector NthChild(Selector? previous, int step, int offset) => new();
                    public static Selector NthLastChild(Selector? previous, int step, int offset) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Match="TextBlock:is(TextBlock):not(.disabled):nth-child(2n+1):nth-last-child(even)" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Styling.Selectors.Is(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Not(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.NthChild(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.NthLastChild(", generated);
    }

    [Fact]
    public void Converts_Selector_Value_With_Property_Equals_Predicate()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Styling.Selector? Match { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty IsVisibleProperty = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector PropertyEquals(Selector? previous, object property, object? value) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Match="TextBlock[IsVisible=true]" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Styling.Selectors.PropertyEquals(", generated);
        Assert.Contains("global::Avalonia.Controls.TextBlock.IsVisibleProperty", generated);
        Assert.Contains(", true)", generated);
    }

    [Fact]
    public void Converts_Selector_Value_With_Attached_Property_Predicate()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Styling.Selector? Match { get; set; }
                }

                public class TextBlock : Control { }

                public static class Grid
                {
                    public static readonly global::Avalonia.AvaloniaProperty RowProperty = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector PropertyEquals(Selector? previous, object property, object? value) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:ac="clr-namespace:Avalonia.Controls"
                         x:Class="Demo.MainView"
                         Match="TextBlock[(ac|Grid.Row)=1]" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Styling.Selectors.PropertyEquals(", generated);
        Assert.Contains("global::Avalonia.Controls.Grid.RowProperty", generated);
        Assert.Contains(", 1)", generated);
    }

    [Fact]
    public void Converts_Nested_Selector_Predicate_Using_Inherited_Target_Type()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty IsReadOnlyProperty = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public class Styles : global::System.Collections.Generic.List<object> { }

                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector Nesting(Selector? previous) => new();
                    public static Selector PropertyEquals(Selector? previous, object property, object? value) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBox">
                        <Style Selector="^[IsReadOnly=true]" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Styling.Selectors.Nesting(", generated);
        Assert.Contains("global::Avalonia.Controls.TextBox.IsReadOnlyProperty", generated);
    }

    [Fact]
    public void Converts_Selector_Not_Function_With_Or_Argument()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Styling.Selector? Match { get; set; }
                }

                public class TextBlock : Control { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector Class(Selector? previous, string name) => new();
                    public static Selector Name(Selector? previous, string name) => new();
                    public static Selector Not(Selector? previous, Selector argument) => new();
                    public static Selector Or(params Selector[] selectors) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         Match="TextBlock:not(.disabled, #Gone)" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Styling.Selectors.Not(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Or(", generated);
    }

    [Fact]
    public void Applies_Default_Style_BindingPriority_For_Style_Setter_Binding()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Data
            {
                public enum BindingPriority
                {
                    LocalValue,
                    Style,
                    Template
                }

                public class Binding
                {
                    public Binding(string path) { }
                    public BindingPriority Priority { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }
                public class Styles : global::System.Collections.Generic.List<object> { }

                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock">
                        <Setter Property="Text" Value="{Binding Name}" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("Priority = global::Avalonia.Data.BindingPriority.Style", generated);
    }

    [Fact]
    public void Preserves_Explicit_BindingPriority_In_Style_Setter_Binding()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Data
            {
                public enum BindingPriority
                {
                    LocalValue,
                    Style,
                    Template
                }

                public class Binding
                {
                    public Binding(string path) { }
                    public BindingPriority Priority { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }
                public class Styles : global::System.Collections.Generic.List<object> { }

                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock">
                        <Setter Property="Text" Value="{Binding Name, Priority=LocalValue}" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("Priority = global::Avalonia.Data.BindingPriority.LocalValue", generated);
        Assert.DoesNotContain("Priority = global::Avalonia.Data.BindingPriority.Style", generated);
    }

    [Fact]
    public void Materializes_ControlTemplate_Content_As_Deferred_Template_Factory()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope { }

                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                }

                public class Control : global::Avalonia.StyledElement { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Border : Control { }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public global::Avalonia.ResourceDictionary Resources { get; set; } = new();
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="MainTemplate" TargetType="{x:Type Border}">
                            <Border x:Name="TemplateBorder" />
                        </ControlTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(".Content = (global::System.Func<global::System.IServiceProvider?, object?>)", generated);
        Assert.Contains("SourceGenDeferredServiceProviderFactory.CreateTemplateNameScope(__templateServiceProvider);", generated);
        Assert.Contains("SourceGenDeferredServiceProviderFactory.CreateDeferredTemplateServiceProvider(__templateServiceProvider, __root, __templateScope", generated);
        Assert.Contains("TemplateResult<global::Avalonia.Controls.Control>", generated);
        Assert.Contains("NameScope.SetNameScope(__templateStyledElement", generated);
        Assert.Contains("Register(\"TemplateBorder\",", generated);
    }

    [Fact]
    public void Attaches_Setter_Content_To_ContentAttributed_Value_Property()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class ContentAttribute : global::System.Attribute { }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope { }
                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                }

                public class StyledElement { }
                public class Control : StyledElement { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class Border : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TagProperty = new();
                    public object? Tag { get; set; }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }
                public class Styles : global::System.Collections.Generic.List<object> { }

                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }

                    [global::Avalonia.Metadata.Content]
                    public object? Value { get; set; }
                }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="Border">
                        <Setter Property="Tag">
                            <ControlTemplate TargetType="{x:Type Border}">
                                <Border />
                            </ControlTemplate>
                        </Setter>
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0103");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Markup.Xaml.Templates.ControlTemplate()", generated);
        Assert.Contains(".Value =", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Invalid_Style_Selector_Grammar_With_Selector_Location()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }
                public class Styles : global::System.Collections.Generic.List<object> { }
                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }

                public static class Selectors
                {
                    public static Selector OfType(Selector? previous, global::System.Type type) => new();
                    public static Selector Child(Selector previous) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock >">
                        <Setter Property="Text" Value="Hello" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0300"));
        Assert.Contains("Unable to parse selector", diagnostic.GetMessage());
        Assert.Contains("Unexpected end of selector", diagnostic.GetMessage());
        Assert.NotEqual(Location.None, diagnostic.Location);
    }

    [Fact]
    public void Reports_Diagnostic_For_Invalid_Style_Selector_Property_Predicate_Without_Type_Context()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }
                public class Styles : global::System.Collections.Generic.List<object> { }
                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector=".warning[Text=true]">
                        <Setter Property="Text" Value="Hello" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0300"));
        Assert.Contains("Property selectors must be applied to a type.", diagnostic.GetMessage());
        Assert.NotEqual(Location.None, diagnostic.Location);
    }

    [Fact]
    public void Reports_Diagnostic_For_Invalid_Style_Selector_NthChild_Syntax()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }
                public class Styles : global::System.Collections.Generic.List<object> { }
                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector="TextBlock:nth-child(2n+)">
                        <Setter Property="Text" Value="Hello" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0300"));
        Assert.Contains("Couldn't parse nth-child arguments.", diagnostic.GetMessage());
        Assert.NotEqual(Location.None, diagnostic.Location);
    }

    [Fact]
    public void Resolves_Common_Target_Type_From_Is_Pseudo_In_Or_Selector()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TagProperty = new();
                    public string? Tag { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TextBlock : Control { }
                public class Button : Control { }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }
                public class Styles : global::System.Collections.Generic.List<object> { }
                public class Style
                {
                    public Selector? Selector { get; set; }
                    public void Add(object value) { }
                }

                public class Setter
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }

                public static class Selectors
                {
                    public static Selector Is(Selector? previous, global::System.Type type) => new();
                    public static Selector Or(params Selector[] selectors) => new();
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Styles>
                    <Style Selector=":is(TextBlock), :is(Button)">
                        <Setter Property="Tag" Value="Shared" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0300");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0301");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("\"global::Avalonia.Controls.Control\"", generated);
    }

    [Fact]
    public void Emits_Template_BindingPriority_For_SetValue_Inside_ControlTemplate()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Data
            {
                public enum BindingPriority
                {
                    LocalValue,
                    Template
                }
            }

            namespace Avalonia.Controls
            {
                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value, global::Avalonia.Data.BindingPriority priority) { }
                }

                public interface INameScope { }

                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                }

                public class StyledElement : AvaloniaObject { }
                public class Control : StyledElement { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class Border : Control
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TagProperty = new();
                    public string? Tag => null;
                }
            }

            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="MainTemplate" TargetType="{x:Type Border}">
                            <Border Tag="Hello" />
                        </ControlTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SetValue(global::Avalonia.Controls.Border.TagProperty, \"Hello\", global::Avalonia.Data.BindingPriority.Template);", generated);
    }

    [Fact]
    public void Falls_Back_To_Two_Argument_SetValue_When_Priority_Overload_Is_Not_Available()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                }

                public interface INameScope { }

                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                }

                public class StyledElement : AvaloniaObject { }
                public class Control : StyledElement { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class Border : Control
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TagProperty = new();
                    public string? Tag => null;
                }
            }

            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="MainTemplate" TargetType="{x:Type Border}">
                            <Border Tag="Hello" />
                        </ControlTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SetValue(global::Avalonia.Controls.Border.TagProperty, \"Hello\");", generated);
        Assert.DoesNotContain("BindingPriority.Template", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Missing_Required_ControlTemplate_Part()
    {
        const string code = """
            namespace Avalonia.Controls.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
                public sealed class TemplatePartAttribute : global::System.Attribute
                {
                    public TemplatePartAttribute() { }
                    public TemplatePartAttribute(string name, global::System.Type type)
                    {
                        Name = name;
                        Type = type;
                    }

                    public string Name { get; set; } = string.Empty;
                    public global::System.Type Type { get; set; } = typeof(object);
                    public bool IsRequired { get; set; }
                }
            }

            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class Border : Control { }
                public class TextBlock : Control { }

                [global::Avalonia.Controls.Metadata.TemplatePart(Name = "PART_Main", Type = typeof(global::Avalonia.Controls.Border), IsRequired = true)]
                public class FancyControl : Control { }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, object scope) { }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="FancyTemplate" TargetType="{x:Type FancyControl}">
                            <TextBlock />
                        </ControlTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0502");
    }

    [Fact]
    public void Reports_Diagnostic_For_ControlTemplate_Part_With_Wrong_Type()
    {
        const string code = """
            namespace Avalonia.Controls.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
                public sealed class TemplatePartAttribute : global::System.Attribute
                {
                    public TemplatePartAttribute() { }
                    public TemplatePartAttribute(string name, global::System.Type type)
                    {
                        Name = name;
                        Type = type;
                    }

                    public string Name { get; set; } = string.Empty;
                    public global::System.Type Type { get; set; } = typeof(object);
                    public bool IsRequired { get; set; }
                }
            }

            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class Border : Control { }
                public class TextBlock : Control { }

                [global::Avalonia.Controls.Metadata.TemplatePart(Name = "PART_Main", Type = typeof(global::Avalonia.Controls.Border), IsRequired = true)]
                public class FancyControl : Control { }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, object scope) { }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="FancyTemplate" TargetType="{x:Type FancyControl}">
                            <TextBlock x:Name="PART_Main" />
                        </ControlTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0503");
    }

    [Fact]
    public void Reports_Diagnostic_For_Missing_Optional_ControlTemplate_Part()
    {
        const string code = """
            namespace Avalonia.Controls.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
                public sealed class TemplatePartAttribute : global::System.Attribute
                {
                    public TemplatePartAttribute() { }
                    public TemplatePartAttribute(string name, global::System.Type type)
                    {
                        Name = name;
                        Type = type;
                    }

                    public string Name { get; set; } = string.Empty;
                    public global::System.Type Type { get; set; } = typeof(object);
                    public bool IsRequired { get; set; }
                }
            }

            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class Border : Control { }

                [global::Avalonia.Controls.Metadata.TemplatePart(Name = "PART_Optional", Type = typeof(global::Avalonia.Controls.Border), IsRequired = false)]
                public class FancyControl : Control { }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, object scope) { }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ControlTemplate x:Key="FancyTemplate" TargetType="{x:Type FancyControl}">
                            <Border />
                        </ControlTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0504");
    }

    [Fact]
    public void Reports_Diagnostic_For_ItemContainer_Inside_DataTemplate_ItemTemplate()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class ContentControl : Control { }
                public class UserControl : ContentControl
                {
                    public object? Content { get; set; }
                }

                public class ItemsControl : Control
                {
                    public object? ItemTemplate { get; set; }
                }

                public class ListBox : ItemsControl { }
                public class ListBoxItem : ContentControl { }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <ListBox>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <ListBoxItem />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0505"));
        Assert.Contains("ListBoxItem", diagnostic.GetMessage());
        Assert.Contains("ListBox.ItemTemplate", diagnostic.GetMessage());
    }

    [Fact]
    public void Reports_Diagnostic_For_ItemContainer_Inside_TreeDataTemplate_ItemTemplate()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class ContentControl : Control { }
                public class UserControl : ContentControl
                {
                    public object? Content { get; set; }
                }

                public class ItemsControl : Control
                {
                    public object? ItemTemplate { get; set; }
                }

                public class TreeView : ItemsControl { }
                public class TreeViewItem : ContentControl { }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class TreeDataTemplate
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TreeView>
                    <TreeView.ItemTemplate>
                        <TreeDataTemplate>
                            <TreeViewItem />
                        </TreeDataTemplate>
                    </TreeView.ItemTemplate>
                </TreeView>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0505"));
        Assert.Contains("TreeViewItem", diagnostic.GetMessage());
        Assert.Contains("TreeView.ItemTemplate", diagnostic.GetMessage());
    }

    [Fact]
    public void Reports_Diagnostic_For_ItemsPanelTemplate_Content_Root_Not_Panel()
    {
        const string code = """
            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class Panel : Control { }
                public class TextBlock : Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ItemsPanelTemplate
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ItemsPanelTemplate x:Key="BadItemsPanel">
                            <TextBlock />
                        </ItemsPanelTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0506"));
        Assert.Contains("ItemsPanelTemplate", diagnostic.GetMessage());
        Assert.Contains("Panel", diagnostic.GetMessage());
    }

    [Fact]
    public void Reports_Diagnostic_For_TreeDataTemplate_Content_Root_Not_Control()
    {
        const string code = """
            namespace Avalonia
            {
                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class PlainObject { }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class TreeDataTemplate
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <TreeDataTemplate x:Key="BadTreeDataTemplate" x:DataType="Control">
                            <PlainObject />
                        </TreeDataTemplate>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0506"));
        Assert.Contains("TreeDataTemplate", diagnostic.GetMessage());
        Assert.Contains("Control", diagnostic.GetMessage());
    }

    [Fact]
    public void Emits_NameScope_Registration_When_Avalonia_NameScope_Is_Available()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Controls
            {
                public class NameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, object value) { }
                    public void Register(string name, object element) { }
                }

                public class Control : global::Avalonia.StyledElement { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button x:Name="ActionButton" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("NameScope.SetNameScope", generated);
        Assert.Contains("__nameScope.Register(\"ActionButton\"", generated);
    }

    [Fact]
    public void Generated_Sources_Are_Deterministic_Across_Repeated_Runs()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var first = RunGeneratorWithResult(compilation, [("MainView.axaml", xaml)]);
        var second = RunGeneratorWithResult(compilation, [("MainView.axaml", xaml)]);

        Assert.Empty(first.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(second.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var firstSources = first.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);
        var secondSources = second.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);

        Assert.Equal(firstSources.Count, secondSources.Count);
        foreach (var pair in firstSources)
        {
            Assert.True(secondSources.TryGetValue(pair.Key, out var otherSource));
            Assert.Equal(pair.Value, otherSource);
        }
    }

    [Fact]
    public void Generated_Sources_Are_Deterministic_When_AdditionalFile_Order_Changes()
    {
        const string code = """
            namespace Demo
            {
                public partial class MainView {}
                public partial class SecondaryView {}
            }
            """;

        const string mainXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button />
            </UserControl>
            """;

        const string secondaryXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.SecondaryView">
                <TextBlock />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var first = RunGeneratorWithResult(
            compilation,
            [("MainView.axaml", mainXaml), ("SecondaryView.axaml", secondaryXaml)]);
        var second = RunGeneratorWithResult(
            compilation,
            [("SecondaryView.axaml", secondaryXaml), ("MainView.axaml", mainXaml)]);

        Assert.Empty(first.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(second.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var firstSources = first.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);
        var secondSources = second.RunResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);

        Assert.Equal(firstSources.Count, secondSources.Count);
        foreach (var pair in firstSources)
        {
            Assert.True(secondSources.TryGetValue(pair.Key, out var otherSource));
            Assert.Equal(pair.Value, otherSource);
        }
    }

    [Fact]
    public void Generates_Construction_From_XArguments()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public class PairControl : global::Avalonia.Controls.Control
                {
                    public PairControl(int left, string right) { }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:demo="clr-namespace:Demo.Controls"
                         x:Class="Demo.MainView">
                <demo:PairControl>
                    <x:Arguments>
                        <x:Int32>7</x:Int32>
                        <x:String>west</x:String>
                    </x:Arguments>
                </demo:PairControl>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0106" or "AXSG0107");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Demo.Controls.PairControl(7, \"west\")", generated);
    }

    [Fact]
    public void Generates_FactoryMethod_And_Constructed_Generic_Type_From_Directives()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public class GenericHolder<T> : global::Avalonia.Controls.Control
                {
                    public static GenericHolder<T> Create(T value)
                    {
                        return new GenericHolder<T>();
                    }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:demo="clr-namespace:Demo.Controls"
                         x:Class="Demo.MainView">
                <demo:GenericHolder x:TypeArguments="x:String" x:FactoryMethod="Create">
                    <x:Arguments>
                        <x:String>Hello</x:String>
                    </x:Arguments>
                </demo:GenericHolder>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0106" or "AXSG0107");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Demo.Controls.GenericHolder<string>", generated);
        Assert.Contains(".Create(\"Hello\")", generated);
    }

    [Fact]
    public void Generates_XArray_Construction_For_Property_Element_Assignment()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public class ArrayHost : global::Avalonia.Controls.Control
                {
                    public string[]? Values { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:demo="clr-namespace:Demo.Controls"
                         x:Class="Demo.MainView">
                    <demo:ArrayHost>
                        <demo:ArrayHost.Values>
                        <x:Array x:TypeArguments="x:String">
                            <x:String>One</x:String>
                            <x:String>Two</x:String>
                        </x:Array>
                    </demo:ArrayHost.Values>
                </demo:ArrayHost>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0108");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new string[]", generated);
        Assert.Contains("\"One\"", generated);
        Assert.Contains("\"Two\"", generated);
    }

    [Fact]
    public void Generates_Extended_Xaml_Primitive_Markup_Extensions()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class PrimitiveProbe : Control
                {
                    public byte ByteValue { get; set; }
                    public decimal DecimalValue { get; set; }
                    public global::System.TimeSpan SpanValue { get; set; }
                    public global::System.Uri? UriValue { get; set; }
                    public char CharValue { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <PrimitiveProbe ByteValue="{x:Byte 5}"
                                DecimalValue="{x:Decimal 12.5}"
                                SpanValue="{x:TimeSpan 00:01:30}"
                                UriValue="{x:Uri https://example.com}"
                                CharValue="{x:Char X}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("((byte)5)", generated);
        Assert.Contains("12.5m", generated);
        Assert.Contains("global::System.TimeSpan.Parse(\"00:01:30\", global::System.Globalization.CultureInfo.InvariantCulture)", generated);
        Assert.Contains("new global::System.Uri(\"https://example.com\", global::System.UriKind.RelativeOrAbsolute)", generated);
        Assert.Contains("'X'", generated);
    }

    [Fact]
    public void Generates_Generic_MarkupExtension_ProvideValue_Call_With_Context()
    {
        const string code = """
            namespace Avalonia.Markup.Xaml
            {
                public abstract class MarkupExtension
                {
                    public abstract object? ProvideValue(global::System.IServiceProvider serviceProvider);
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
                public class TextBlock : Control
                {
                    public string? Text { get; set; }
                }
            }

            namespace Demo.Markup
            {
                public sealed class EchoExtension : global::Avalonia.Markup.Xaml.MarkupExtension
                {
                    public EchoExtension(string value)
                    {
                        Value = value;
                    }

                    public string Value { get; }

                    public string Prefix { get; set; } = string.Empty;

                    public override object? ProvideValue(global::System.IServiceProvider serviceProvider)
                    {
                        return Prefix + Value;
                    }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:m="clr-namespace:Demo.Markup"
                         x:Class="Demo.MainView">
                <TextBlock Text="{m:Echo Hello, Prefix='>>'}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(", generated);
        Assert.Contains("new global::Demo.Markup.EchoExtension(\"Hello\") { Prefix = \">>\" }", generated);
    }

    private static CSharpCompilation CreateCompilation(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = ImmutableArray.Create(
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "Demo.Assembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (Compilation UpdatedCompilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        CSharpCompilation compilation,
        string xaml,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null)
    {
        return RunGenerator(
            compilation,
            [("MainView.axaml", xaml)],
            additionalBuildOptions);
    }

    private static (Compilation UpdatedCompilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Xaml)> xamlFiles,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null)
    {
        var result = RunGeneratorWithResult(compilation, xamlFiles, additionalBuildOptions);
        return (result.UpdatedCompilation, result.Diagnostics);
    }

    private static (
        Compilation UpdatedCompilation,
        ImmutableArray<Diagnostic> Diagnostics,
        GeneratorDriverRunResult RunResult) RunGeneratorWithResult(
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Xaml)> xamlFiles,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null)
    {
        var options = new List<KeyValuePair<string, string>>
        {
            new("build_property.AvaloniaXamlCompilerBackend", "SourceGen"),
            new("build_property.AvaloniaSourceGenCompilerEnabled", "true")
        };

        if (additionalBuildOptions is not null)
        {
            options.AddRange(additionalBuildOptions);
        }

        var generator = new AvaloniaXamlSourceGenerator();
        var additionalTexts = xamlFiles
            .Select(static file => new InMemoryAdditionalText(file.Path, file.Xaml))
            .ToImmutableArray();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(options));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        return (updatedCompilation, diagnostics, driver.GetRunResult());
    }
}
