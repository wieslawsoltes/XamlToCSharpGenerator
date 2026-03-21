using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Generator;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaXBindSourceGeneratorTests
{
    [Fact]
    public void Generates_XBind_Bindings_For_Root_And_DataTemplate_Scopes()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }

                public class ItemsControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ItemsSourceProperty = new();
                    public object? ItemsSource { get; set; }
                    public object? ItemTemplate { get; set; }
                }

                public class DataTemplate
                {
                    public object? DataType { get; set; }
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public static class SourceGenMarkupExtensionRuntime
                {
                    public static T ResolveNamedElement<T>(object target, object root, string name) => default!;
                    public static T CoerceMarkupExtensionValue<T>(object value) => default!;
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class ItemVm
                {
                    public string Name { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public string Title { get; set; } = string.Empty;
                    public global::System.Collections.Generic.List<global::Demo.ViewModels.ItemVm> Items { get; } = new();
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <TextBlock Text="{x:Bind Title}" />
                    <ItemsControl ItemsSource="{x:Bind Items}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:ItemVm">
                                <TextBlock Text="{x:Bind Name}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0117" or "AXSG0118");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("global::Avalonia.Data.BindingPriority.LocalValue", generated);
    }

    [Fact]
    public void Generates_XBind_For_Named_Elements_And_Static_Members()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class TextBox : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public static class SourceGenMarkupExtensionRuntime
                {
                    public static T ResolveNamedElement<T>(object target, object root, string name) => default!;
                    public static T CoerceMarkupExtensionValue<T>(object value) => default!;
                }
            }

            namespace Demo.Helpers
            {
                public static class UiHelpers
                {
                    public static string Prefix { get; } = "prefix";
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
                         xmlns:helpers="clr-namespace:Demo.Helpers"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <TextBox x:Name="Editor" Text="hello" />
                    <TextBlock Text="{x:Bind Editor.Text}" />
                    <TextBlock Text="{x:Bind helpers:UiHelpers.Prefix}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("ResolveNamedElement<global::Avalonia.Controls.TextBox>(target, root, \"Editor\").Text", generated);
        Assert.Contains("global::Demo.Helpers.UiHelpers.Prefix", generated);
    }

    [Fact]
    public void Generates_TwoWay_XBind_With_Explicit_BindBack()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public static class SourceGenMarkupExtensionRuntime
                {
                    public static T ResolveNamedElement<T>(object target, object root, string name) => default!;
                    public static T CoerceMarkupExtensionValue<T>(object value) => default!;
                }
            }

            namespace Demo.Controls
            {
                public class BindingTarget : global::Avalonia.Controls.Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty MyPropertyProperty = new();
                    public string? MyProperty { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public int Count { get; set; }
                    public void ApplyCount(string value) { }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo.Controls"
                         x:Class="Demo.MainView">
                <local:BindingTarget MyProperty="{x:Bind Count, Mode=TwoWay, BindBack=ApplyCount}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("ProvideXBindExpressionBinding<global::Demo.MainView, global::Demo.MainView, global::Demo.Controls.BindingTarget>", generated);
        Assert.Contains("static (source, value) => source.ApplyCount(", generated);
        Assert.Contains("typeof(string)", generated);
    }

    [Fact]
    public void Generates_TwoWay_XBind_With_Delay_And_UpdateSourceTrigger()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Data
            {
                public class Binding
                {
                    public int Delay { get; set; }
                    public UpdateSourceTrigger UpdateSourceTrigger { get; set; }
                    public BindingPriority Priority { get; set; }
                }

                public enum BindingMode
                {
                    OneTime,
                    OneWay,
                    TwoWay
                }

                public enum UpdateSourceTrigger
                {
                    Default,
                    PropertyChanged,
                    LostFocus,
                    Explicit
                }

                public enum BindingPriority
                {
                    LocalValue
                }

                public interface IValueConverter { }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public static class SourceGenMarkupExtensionRuntime
                {
                    public static T ResolveNamedElement<T>(object target, object root, string name) => default!;
                    public static T CoerceMarkupExtensionValue<T>(object value) => default!;
                }
            }

            namespace Demo.Controls
            {
                public class BindingTarget : global::Avalonia.Controls.Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty MyPropertyProperty = new();
                    public string? MyProperty { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public string? SearchText { get; set; }
                    public void ApplySearchText(string value) { }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo.Controls"
                         x:Class="Demo.MainView">
                <local:BindingTarget MyProperty="{x:Bind SearchText, Mode=TwoWay, BindBack=ApplySearchText, Delay=250, UpdateSourceTrigger=PropertyChanged}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("ProvideXBindExpressionBinding<global::Demo.MainView, global::Demo.MainView, global::Demo.Controls.BindingTarget>", generated);
        Assert.Contains(", 250, global::Avalonia.Data.UpdateSourceTrigger.PropertyChanged, global::Avalonia.Data.BindingPriority.LocalValue,", generated);
    }

    [Fact]
    public void Generates_Pathless_XBind_For_Current_Source_Object()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public static readonly global::Avalonia.AvaloniaProperty TagProperty = new();
                    public object? Tag { get; set; }
                }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public static class SourceGenMarkupExtensionRuntime
                {
                    public static T ResolveNamedElement<T>(object target, object root, string name) => default!;
                    public static T CoerceMarkupExtensionValue<T>(object value) => default!;
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
                <TextBlock Tag="{x:Bind}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("ProvideXBindExpressionBinding<global::Demo.MainView, global::Demo.MainView, global::Avalonia.Controls.TextBlock>", generated);
        Assert.Contains("static (source, root, target) => (object?)(source)", generated);
    }

    [Fact]
    public void Generates_XBind_Event_Binding_For_Handler_Methods()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
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
                    public void HandleClick() { }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="{x:Bind HandleClick}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedSources = updatedCompilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .Where(static source => source.StartsWith("// <auto-generated />", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(generatedSources, static source => source.Contains("HandleClick", StringComparison.Ordinal));
        Assert.Contains(generatedSources, static source => source.Contains("__axsgRootTyped", StringComparison.Ordinal));
    }

    [Fact]
    public void Generates_XBind_For_Conditional_Access_Inside_Invocation()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public static class SourceGenMarkupExtensionRuntime
                {
                    public static T ResolveNamedElement<T>(object target, object root, string name) => default!;
                    public static T CoerceMarkupExtensionValue<T>(object value) => default!;
                }
            }

            namespace Demo
            {
                public sealed class ItemVm
                {
                    public string? Name { get; set; }
                }

                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public ItemVm? Selected { get; set; }

                    public string Format(string? value)
                    {
                        return value ?? "<none>";
                    }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="{x:Bind Format(Selected?.Name)}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("source.Format(source.Selected?.Name)", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_XBind_Inside_DataTemplate_Without_DataType()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject
                {
                    public object? SetValue(AvaloniaProperty property, object? value) => value;
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }

                public class DataTemplate
                {
                    public object? DataType { get; set; }
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public static class SourceGenMarkupExtensionRuntime
                {
                    public static T ResolveNamedElement<T>(object target, object root, string name) => default!;
                    public static T CoerceMarkupExtensionValue<T>(object value) => default!;
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
                <UserControl.Content>
                    <DataTemplate>
                        <TextBlock Text="{x:Bind Name}" />
                    </DataTemplate>
                </UserControl.Content>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0110" &&
                          diagnostic.GetMessage().Contains("x:DataType", StringComparison.Ordinal));
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
        string xaml)
    {
        var options = new List<KeyValuePair<string, string>>
        {
            new("build_property.AvaloniaXamlCompilerBackend", "SourceGen"),
            new("build_property.AvaloniaSourceGenCompilerEnabled", "true"),
            new("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "true")
        };

        var generator = new AvaloniaXamlSourceGenerator();
        var additionalTexts = ImmutableArray.Create<AdditionalText>(
            new InMemoryAdditionalText("MainView.axaml", xaml));
        var additionalFileMetadata = ImmutableArray.Create((
            Path: "MainView.axaml",
            Values: (IEnumerable<KeyValuePair<string, string>>)new[]
            {
                new KeyValuePair<string, string>("build_metadata.AdditionalFiles.SourceItemGroup", "AvaloniaXaml"),
                new KeyValuePair<string, string>("build_metadata.AdditionalFiles.TargetPath", "MainView.axaml")
            }));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(options, additionalFileMetadata));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        return (updatedCompilation, diagnostics);
    }

    private static string GetGeneratedPartialClassSource(Compilation compilation, string className)
    {
        var sources = compilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .ToArray();

        return sources.First(source =>
            source.StartsWith("// <auto-generated />", StringComparison.Ordinal) &&
            source.Contains($"partial class {className}", StringComparison.Ordinal));
    }
}
