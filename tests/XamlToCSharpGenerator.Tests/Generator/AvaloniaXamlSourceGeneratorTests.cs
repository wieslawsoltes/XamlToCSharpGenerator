using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Compiler;
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
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
    public void Resolves_Keyed_ResourceInclude_Before_Dictionary_Insertion()
    {
        const string code = "namespace Demo; public partial class ThemeHost {}";
        const string xaml = """
            <Styles xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Demo.ThemeHost">
                <Styles.Resources>
                    <ResourceDictionary>
                        <ResourceInclude x:Key="CompactStyles" Source="/DensityStyles/Compact.xaml" />
                    </ResourceDictionary>
                </Styles.Resources>
            </Styles>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "ThemeHost");
        Assert.DoesNotContain("__TryResolveDictionaryEntryValue(", generated);
        Assert.DoesNotContain("map[key] = dictionaryValue;", generated);
        Assert.DoesNotContain("__NormalizeDictionaryValue", generated);
        Assert.Contains("CompactStyles", generated);
    }

    [Fact]
    public void Resolves_Default_Avalonia_Namespace_From_XmlnsDefinition_Assembly_Attributes()
    {
        const string code = """
            using Avalonia.Metadata;

            [assembly: XmlnsDefinition("https://github.com/avaloniaui", "Demo.Extras")]

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class XmlnsDefinitionAttribute : global::System.Attribute
                {
                    public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Extras
            {
                public class FancyGlyph
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
                <FancyGlyph Text="Hello" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("new global::Demo.Extras.FancyGlyph()", generated);
    }

    [Fact]
    public void Resolves_Custom_Xmlns_Through_XmlnsDefinition_Assembly_Attributes()
    {
        const string code = """
            using Avalonia.Metadata;

            [assembly: XmlnsDefinition("urn:demo", "Demo.Controls")]

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class XmlnsDefinitionAttribute : global::System.Attribute
                {
                    public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public class CustomControl { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:demo="urn:demo"
                         x:Class="Demo.MainView">
                <demo:CustomControl />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("new global::Demo.Controls.CustomControl()", generated);
    }

    [Fact]
    public void Reports_Ambiguous_Type_Resolution_With_Deterministic_Choice()
    {
        const string code = """
            using Avalonia.Metadata;

            [assembly: XmlnsDefinition("https://github.com/avaloniaui", "Demo.ControlsA")]
            [assembly: XmlnsDefinition("https://github.com/avaloniaui", "Demo.ControlsB")]

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class XmlnsDefinitionAttribute : global::System.Attribute
                {
                    public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.ControlsA
            {
                public class FancyGlyph { }
            }

            namespace Demo.ControlsB
            {
                public class FancyGlyph { }
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
                <FancyGlyph />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        var ambiguityDiagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0112"));
        Assert.Contains("FancyGlyph", ambiguityDiagnostic.GetMessage());
        Assert.Contains("Using '", ambiguityDiagnostic.GetMessage());
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.True(
            generated.Contains("new global::Demo.ControlsA.FancyGlyph()", StringComparison.Ordinal) ||
            generated.Contains("new global::Demo.ControlsB.FancyGlyph()", StringComparison.Ordinal));
    }

    [Fact]
    public void Disables_Default_Namespace_Compatibility_Type_Fallback_When_Option_Off()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public class ThemeDoodad { }
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
                <ThemeDoodad />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            [
                new KeyValuePair<string, string>(
                    "build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled",
                    "false")
            ]);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("new global::Avalonia.Markup.Xaml.Styling.ThemeDoodad()", generated);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Resolves_Default_Namespace_Compatibility_Type_Fallback_When_Option_Explicitly_On()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public class ThemeDoodad { }
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
                <ThemeDoodad />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>(
                    "build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled",
                    "true")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0113" &&
                          diagnostic.GetMessage().Contains("Avalonia default xml namespace compatibility fallback", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Markup.Xaml.Styling.ThemeDoodad()", generated);
    }

    [Fact]
    public void Disables_Default_Namespace_Compatibility_Type_Fallback_When_Option_Explicitly_Off_In_NonStrict_Mode()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public class ThemeDoodad { }
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
                <ThemeDoodad />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "false")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0113");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.DoesNotContain("new global::Avalonia.Markup.Xaml.Styling.ThemeDoodad()", generated);
    }

    [Fact]
    public void Disables_Default_Namespace_Compatibility_Type_Fallback_When_Option_Explicitly_Off_In_Strict_Mode()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public class ThemeDoodad { }
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
                <ThemeDoodad />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "false")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0113");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.DoesNotContain("new global::Avalonia.Markup.Xaml.Styling.ThemeDoodad()", generated);
    }

    [Fact]
    public void Disables_Default_Namespace_Extension_Compatibility_Fallback_In_Strict_Mode()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public class ThemeDoodadExtension { }
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
                <ThemeDoodad />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "true")
            ]);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("new global::Avalonia.Markup.Xaml.Styling.ThemeDoodadExtension()", generated);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0113");
    }

    [Fact]
    public void Resolves_Global_Prefix_Without_Local_Xmlns_Declaration_From_Assembly_Attribute()
    {
        const string code = """
            using Avalonia.Metadata;

            [assembly: XmlnsPrefix("using:Demo.Controls", "local")]

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class XmlnsPrefixAttribute : global::System.Attribute
                {
                    public XmlnsPrefixAttribute(string xmlNamespace, string prefix) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public class CustomControl { }
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
                <local:CustomControl />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0001");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Demo.Controls.CustomControl()", generated);
    }

    [Fact]
    public void Supports_Implicit_Default_Xmlns_And_Global_Prefixes_From_Build_Properties()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }

                public class StackPanel
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class Button
                {
                    public string? Content { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public class CustomControl { }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl x:Class="Demo.MainView">
                <StackPanel>
                    <Button Content="Hello" />
                    <local:CustomControl />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenAllowImplicitXmlnsDeclaration", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenGlobalXmlnsPrefixes", "x=http://schemas.microsoft.com/winfx/2006/xaml;local=using:Demo.Controls")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0001");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Controls.Button()", generated);
        Assert.Contains("new global::Demo.Controls.CustomControl()", generated);
    }

    [Fact]
    public void Supports_Implicit_Standard_Xaml_Prefixes_When_Implicit_Xmlns_Is_Enabled()
    {
        const string code = """
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
            """;

        const string xaml = """
            <UserControl x:Class="Demo.MainView" mc:Ignorable="d">
                <TextBlock d:DataContext="{x:Null}" Text="Sample" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenAllowImplicitXmlnsDeclaration", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled", "true")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0001");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0002");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("partial class MainView", generated);
        Assert.Contains("__PopulateGeneratedObjectGraph(this, null);", generated);
    }

    [Fact]
    public void Infers_XClass_From_TargetPath_When_Enabled_And_Class_Exists()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Views
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("Views/MainView.axaml", xaml)],
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenInferClassFromPath", "true"),
                new KeyValuePair<string, string>("build_property.RootNamespace", "Demo")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0002");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("namespace Demo.Views", generated);
        Assert.Contains("partial class MainView", generated);
        Assert.Contains("__PopulateGeneratedObjectGraph(this, null);", generated);
    }

    [Fact]
    public void Keeps_MissingClass_Diagnostic_When_Inferred_Class_Does_Not_Exist()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui">
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            [("Views/MissingView.axaml", xaml)],
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenInferClassFromPath", "true"),
                new KeyValuePair<string, string>("build_property.RootNamespace", "Demo")
            ]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0002");
    }

    [Fact]
    public void Resolves_Unprefixed_Project_Control_From_RootNamespace_When_Enabled()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public sealed class CustomPanel { }
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
                <CustomPanel />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenImplicitProjectNamespacesEnabled", "true"),
                new KeyValuePair<string, string>("build_property.RootNamespace", "Demo")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Demo.Controls.CustomPanel()", generated);
    }

    [Fact]
    public void Resolves_XDataType_Prefix_From_Global_Prefix_Build_Property()
    {
        const string code = """
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
                public sealed class MainViewModel
                {
                    public string Name { get; } = "Demo";
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel">
                <TextBlock Text="{Binding Name}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenGlobalXmlnsPrefixes", "vm=using:Demo"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenUseCompiledBindingsByDefault", "true")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0110");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("XamlCompiledBindingRegistry.Register", generated);
        Assert.Contains("\"Name\"", generated);
    }

    [Fact]
    public void Resolves_XStatic_Owner_Type_From_Global_Prefix_Build_Property()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }

                public class TextBlock
                {
                    public static readonly object TextProperty = new();
                    public string? Text { get; set; }
                    public void SetValue(object property, object? value) { }
                }
            }

            namespace Demo.Catalog
            {
                public static class SampleStaticValues
                {
                    public static string VersionLabel { get; } = "1.0";
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
                <TextBlock Text="{x:Static catalog:SampleStaticValues.VersionLabel}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>(
                    "build_property.AvaloniaSourceGenGlobalXmlnsPrefixes",
                    "x=http://schemas.microsoft.com/winfx/2006/xaml;vm=using:Demo;catalog=using:Demo.Catalog;pages=using:Demo.Pages;resources=using:Demo.Resources")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Demo.Catalog.SampleStaticValues.VersionLabel", generated);
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
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0002");

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("namespace XamlToCSharpGenerator.Generated", generated);
        Assert.Contains("static class GeneratedXaml_MainView_", generated);
        Assert.Contains("__BuildGeneratedObjectGraph", generated);
        Assert.Contains("XamlResourceRegistry.Register", generated);
        Assert.DoesNotContain("InitializeComponent", generated);
    }

    [Fact]
    public void Classless_Artifact_BuildUri_Strips_AvaloniaResource_TargetPath_Prefix()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object> { }
            }
            """;

        const string xaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """;

        var compilation = CreateCompilation(code);
        var result = RunGeneratorWithResult(
            compilation,
            [("HamburgerMenu.xaml", xaml, "!/HamburgerMenu/HamburgerMenu.xaml")]);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = string.Join(
            Environment.NewLine,
            result.RunResult.Results
                .SelectMany(static runResult => runResult.GeneratedSources)
                .Select(static source => source.SourceText.ToString()));

        Assert.Contains("avares://Demo.Assembly/HamburgerMenu/HamburgerMenu.xaml", generated);
        Assert.DoesNotContain("avares://Demo.Assembly/!/HamburgerMenu/HamburgerMenu.xaml", generated);
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
        Assert.Contains("__n0.Content =", generated);
        Assert.DoesNotContain("__n0.Content = default!;", generated);
        Assert.True(
            generated.Contains("__root.Content =", StringComparison.Ordinal) ||
            generated.Contains("__TrySetClrProperty(__root, \"Content\",", StringComparison.Ordinal));
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
        Assert.DoesNotContain("private static void __BeginInit(object? value)", generated);
        Assert.DoesNotContain("private static void __EndInit(object? value)", generated);
        Assert.DoesNotContain("private static void __TryCompleteNameScope(object? scope)", generated);
        Assert.Contains("__AXSGObjectGraph.BeginInit(__root);", generated);
        Assert.Contains("__AXSGObjectGraph.EndInit(__root);", generated);
        Assert.Contains("__AXSGObjectGraph.TryCompleteNameScope(__nameScope);", generated);
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
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            "new global::Avalonia.Controls.ServiceOnlyControl(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CreateObjectConstructionServiceProvider(",
            generated);
        Assert.DoesNotContain(
            "new global::Avalonia.Controls.ServiceOnlyControl(global::XamlToCSharpGenerator.Runtime.SourceGenServiceProviderUtilities.EnsureNotNull(",
            generated);
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
        var attachIndex = generated.IndexOf("__root.Content =", StringComparison.Ordinal);
        if (attachIndex < 0)
        {
            attachIndex = generated.IndexOf("__TrySetClrProperty(__root, \"Content\",", StringComparison.Ordinal);
        }

        var propertyIndex = generated.IndexOf("__n0.Tag =", StringComparison.Ordinal);
        if (propertyIndex < 0)
        {
            propertyIndex = generated.IndexOf("__TrySetClrProperty(__n0, \"Tag\",", StringComparison.Ordinal);
        }

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
        Assert.Contains("internal static void __InitializeXamlSourceGenArtifacts()", generated);
        Assert.Contains("__RegisterXamlSourceGenArtifacts();", generated);
        Assert.Contains("SourceGenArtifactRegistryRuntime.ResetDocumentRegistries(", generated);
        Assert.Contains("__TrackAndReconcileSourceGenHotReloadState(this);", generated);
        Assert.Contains("XamlSourceGenHotReloadStateTracker.Reconcile", generated);
        Assert.Contains("XamlSourceGenHotReloadManager.Register", generated);
        Assert.Contains("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadRegistrationOptions", generated);
        Assert.Contains("BuildUri = \"avares://", generated);
        Assert.Contains("XamlSourceGenTypeUriRegistry.Register(typeof(", generated);
        Assert.Contains("XamlSourceGenArtifactRefreshRegistry.Register(typeof(", generated);
        Assert.DoesNotContain("XamlSourceGenHotDesignManager.Register", generated);
        Assert.Contains("CaptureState = static __instance =>", generated);
        Assert.Contains("RestoreState = static (__instance, __state) =>", generated);
    }

    [Fact]
    public void Generates_Classless_HotReload_Registration_With_Tracking_Type()
    {
        const string code = "namespace Demo;";
        const string xaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Thickness x:Key="Inset">1</Thickness>
            </ResourceDictionary>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("private static void __RegisterSourceGenHotReload(object __instance)", generated);
        Assert.Contains("__RegisterSourceGenHotReload(__instance);", generated);
        Assert.Contains("TrackingType = typeof(", generated);
        Assert.Contains("BuildUri = \"avares://", generated);
        Assert.Contains("SourcePath = ", generated);
        Assert.Contains("private static void __ApplySourceGenHotReload(object __instance)", generated);
        Assert.DoesNotContain("__TrackAndReconcileSourceGenHotReloadState", generated);
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
    public void HotDesign_Can_Be_Enabled_Via_Build_Property()
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
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "false"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotDesignEnabled", "true")
            ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("internal void __ApplySourceGenHotReload()", generated);
        Assert.DoesNotContain("XamlSourceGenHotReloadManager.Register", generated);
        Assert.Contains("XamlSourceGenHotDesignManager.Register", generated);
        Assert.Contains("new global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignRegistrationOptions", generated);
        Assert.Contains("BuildUri = \"avares://", generated);
        Assert.Contains("DocumentRole = global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignDocumentRole.Root", generated);
        Assert.Contains("ArtifactKind = global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignArtifactKind.View", generated);
        Assert.Contains("ScopeHints = new string[] { \"control\", \"UserControl\" }", generated);
    }

    [Fact]
    public void HotReload_Emits_Root_State_Tracking_For_Collections_And_Avalonia_Properties()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class StyledElement
                {
                    public static readonly AvaloniaProperty NameProperty = new();
                    public void SetValue(AvaloniaProperty property, object? value) { }
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
        var generated = updatedCompilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .FirstOrDefault(source =>
                source.Contains("__RegisterXamlSourceGenArtifacts", StringComparison.Ordinal) &&
                source.Contains("partial class MainView", StringComparison.Ordinal));
        Assert.True(
            generated is not null,
            "No generated source found. Diagnostics:\n" +
            string.Join("\n", diagnostics.Select(static d => d.ToString())) +
            "\nTrees:\n" +
            string.Join("\n---\n", updatedCompilation.SyntaxTrees.Select(static tree => tree.FilePath ?? "<no-path>")));
        Assert.Contains("__GetSourceGenHotReloadCollectionCleanupDescriptors()", generated);
        Assert.Contains("\"Resources\"", generated);
        Assert.Contains("\"Styles\"", generated);
        Assert.Contains("__GetSourceGenHotReloadAvaloniaPropertyCleanupDescriptors()", generated);
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
        Assert.Contains("__GetSourceGenHotReloadClrPropertyCleanupDescriptors()", generated);
        Assert.Contains("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"AcceptButton\",", generated);
    }

    [Fact]
    public void HotReload_Does_Not_Treat_Attached_Static_Setters_As_Clr_Reset_Members()
    {
        const string code = """
            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.Window { }
            }
            """;
        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Demo.MainView"
                    RenderOptions.BitmapInterpolationMode="HighQuality" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join(
            "\n---\n",
            updatedCompilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.DoesNotContain(
            "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"SetBitmapInterpolationMode\",",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__typed.SetBitmapInterpolationMode = default!;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void HotReload_Emits_Clear_Before_Dictionary_Merge_Property_Reapply()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
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
        Assert.Contains("__AXSGObjectGraph.TryClearDictionaryEntries(__root.Resources);", generated);
        Assert.True(
            generated.Contains("__root.Resources.Add(\"PrimaryButton\",", StringComparison.Ordinal) ||
            (generated.Contains("__AXSGObjectGraph.TryAddToDictionary(", StringComparison.Ordinal) &&
             generated.Contains("\"PrimaryButton\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Supports_XShared_False_For_Deferred_Resource_Dictionary_Entries()
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
                    <Button x:Key="PrimaryButton" x:Shared="False" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__AXSGObjectGraph.TryAddToDictionary(__root.Resources, \"PrimaryButton\",", generated);
        Assert.Contains("__SourceGenDocumentUri, false);", generated);
    }

    [Fact]
    public void Supports_XShared_False_For_Named_Deferred_Resource_Dictionary_Entries()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaObject { }
                public class StyledElement : AvaloniaObject
                {
                    public string? Name { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope
                {
                    void Register(string name, object value);
                    object? Find(string name);
                    void Complete();
                    bool IsCompleted { get; }
                }

                public class NameScope : INameScope
                {
                    public void Register(string name, object value) { }
                    public object? Find(string name) => null;
                    public void Complete() { }
                    public bool IsCompleted => false;
                }

                public class Control : global::Avalonia.StyledElement { }

                public class UserControl : Control
                {
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
                    <Button x:Key="PrimaryButton" x:Shared="False" x:Name="NamedButton" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("CreateDeferredResourceNameScope(__deferredServiceProvider);", generated);
        Assert.Contains("CreateDeferredResourceServiceProvider(__deferredServiceProvider,", generated);
        Assert.Contains("__deferredResourceNameScope", generated);
        Assert.Contains("__AXSGObjectGraph.TryAddToDictionary(__root.Resources, \"PrimaryButton\",", generated);
        Assert.Contains("__SourceGenDocumentUri, false);", generated);
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
        Assert.Contains("__GetSourceGenHotReloadRootEventCleanupDescriptors()", generated);
        Assert.Contains("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"C|Loaded|OnLoaded|||\",", generated);
        Assert.Contains("__GetSourceGenHotReloadRootEventCleanupDescriptors());", generated);
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
    public void HotReload_Emits_Assembly_MetadataUpdateHandler_Hook_For_SourceGen_Avalonia_Assemblies()
    {
        const string code = "namespace Demo; public partial class HookedView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.HookedView">
                <Button Content="Hook" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var result = RunGeneratorWithResult(
            compilation,
            [("HookedView.axaml", xaml)],
            [
                new KeyValuePair<string, string>("build_property.DotNetWatchBuild", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "true"),
            ]);

        var generatedSources = result.RunResult.Results
            .SelectMany(static output => output.GeneratedSources)
            .Select(static source => source.SourceText.ToString())
            .ToArray();

        var metadataHandlerSources = generatedSources
            .Where(static source => source.Contains(
                "MetadataUpdateHandler(typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Single(metadataHandlerSources);
        Assert.Equal(
            1,
            CountOccurrences(
                metadataHandlerSources[0],
                "MetadataUpdateHandler(typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))"));

        static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }

            return count;
        }
    }

    [Fact]
    public void HotReload_IosDebug_Emits_Linker_Preservation_Hints_For_MetadataUpdate_Entrypoints()
    {
        const string code = "namespace Demo; public partial class HookedView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.HookedView">
                <Button Content="Hook" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var result = RunGeneratorWithResult(
            compilation,
            [("HookedView.axaml", xaml)],
            [
                new KeyValuePair<string, string>("build_property.DotNetWatchBuild", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenIosHotReloadEnabled", "true"),
            ]);

        var generatedSources = result.RunResult.Results
            .SelectMany(static output => output.GeneratedSources)
            .Select(static source => source.SourceText.ToString())
            .ToArray();
        var metadataHandlerSource = Assert.Single(
            generatedSources.Where(static source => source.Contains(
                "MetadataUpdateHandler(typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))",
                StringComparison.Ordinal)));

        Assert.Contains(
            "DynamicDependency(nameof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.ClearCache)",
            metadataHandlerSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicDependency(nameof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.UpdateApplication)",
            metadataHandlerSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "ModuleInitializer",
            metadataHandlerSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HotReload_WatchMode_Uses_Persistent_Last_Good_Source_After_InMemory_Cache_Reset()
    {
        const string code = "namespace Demo; public partial class PersistedWatchView {}";
        const string validXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.PersistedWatchView">
                <Button Content="Valid" />
            </UserControl>
            """;

        const string invalidXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.PersistedWatchView">
                <Button Content="Broken"
            </UserControl>
            """;

        var cacheDirectory = Path.Combine(Path.GetTempPath(), "AXSG-HotReloadCache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDirectory);

        try
        {
            XamlSourceGeneratorCompilerHost.ClearHotReloadFallbackCacheForTesting();

            var options = new[]
            {
                new KeyValuePair<string, string>("build_property.DotNetWatchBuild", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadEnabled", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenHotReloadErrorResilienceEnabled", "true"),
                new KeyValuePair<string, string>("build_property.IntermediateOutputPath", cacheDirectory),
                new KeyValuePair<string, string>("build_property.MSBuildProjectDirectory", cacheDirectory),
            };

            var compilation = CreateCompilation(code);
            var first = RunGeneratorWithResult(
                compilation,
                [("PersistedWatchView.axaml", validXaml)],
                options);

            Assert.Empty(first.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.True(Directory.EnumerateFiles(cacheDirectory, "*.cache", SearchOption.AllDirectories).Any());

            XamlSourceGeneratorCompilerHost.ClearHotReloadFallbackCacheForTesting();

            var second = RunGeneratorWithResult(
                compilation,
                [("PersistedWatchView.axaml", invalidXaml)],
                options);

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
        finally
        {
            try
            {
                if (Directory.Exists(cacheDirectory))
                {
                    Directory.Delete(cacheDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort test cleanup only.
            }

            XamlSourceGeneratorCompilerHost.ClearHotReloadFallbackCacheForTesting();
        }
    }

    [Fact]
    public void HotReload_WatchMode_Uses_Last_Good_Source_When_TargetPath_Shape_Changes()
    {
        const string code = "namespace Demo; public partial class TargetPathShapeView {}";
        const string validXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.TargetPathShapeView">
                <Button Content="Valid" />
            </UserControl>
            """;

        const string invalidXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.TargetPathShapeView">
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
            [("Pages/TargetPathShapeView.axaml", validXaml)],
            options);
        var second = RunGeneratorWithResult(
            compilation,
            [("./Pages/TargetPathShapeView.axaml", invalidXaml)],
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
        Assert.Contains("// pass: AXSG-P001-BindNamedElements => AXSG-P010-BindRootObject, XNameTransformer", generated);
        Assert.Contains("// pass: AXSG-P900-Finalize", generated);
        Assert.Contains("AddNameScopeRegistration", generated);
    }

    [Fact]
    public void Does_Not_Emit_Compile_Metrics_By_Default()
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
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        Assert.DoesNotContain(diagnostics, d => d.Id is "AXSG0800" or "AXSG0801");
    }

    [Fact]
    public void Emits_Compile_Metrics_Diagnostics_When_Enabled()
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
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenMetricsEnabled", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenMetricsDetailed", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "true")
            ]);

        var summaryDiagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0800"));
        Assert.Equal(DiagnosticSeverity.Info, summaryDiagnostic.Severity);
        Assert.Contains("Global XAML graph analysis", summaryDiagnostic.GetMessage(), StringComparison.Ordinal);

        var fileDiagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0801"));
        Assert.Equal(DiagnosticSeverity.Info, fileDiagnostic.Severity);
        var message = fileDiagnostic.GetMessage();
        Assert.Contains("XAML compile metrics", message, StringComparison.Ordinal);
        Assert.Contains("parse=", message, StringComparison.Ordinal);
        Assert.Contains("bind=", message, StringComparison.Ordinal);
        Assert.Contains("emit=", message, StringComparison.Ordinal);
        Assert.Contains("typeResolutionFallbacks=", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_Compile_Metrics_Type_Resolution_Fallback_Count()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public class ThemeDoodad { }
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
                <ThemeDoodad />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenMetricsEnabled", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenMetricsDetailed", "true"),
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled", "true")
            ]);

        var fileDiagnostic = Assert.Single(diagnostics.Where(d => d.Id == "AXSG0801"));
        Assert.Contains("typeResolutionFallbacks=1", fileDiagnostic.GetMessage(), StringComparison.Ordinal);
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
        Assert.True(
            generated.Contains("__root.Content =", StringComparison.Ordinal) ||
            generated.Contains("__TrySetClrProperty(__root, \"Content\",", StringComparison.Ordinal));
        Assert.Contains("Children", generated);
        Assert.True(
            generated.Contains("Add(", StringComparison.Ordinal) ||
            generated.Contains("__TryAddToCollection(", StringComparison.Ordinal));
        Assert.Contains("TextBlock", generated);
        Assert.True(
            generated.Contains("__n1.Text =", StringComparison.Ordinal) ||
            generated.Contains("__TrySetClrProperty(__n1, \"Text\",", StringComparison.Ordinal));
        Assert.True(
            generated.Contains("__n2.Text =", StringComparison.Ordinal) ||
            generated.Contains("__TrySetClrProperty(__n2, \"Text\",", StringComparison.Ordinal));
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
        Assert.Contains("__AXSGObjectGraph.TryClearCollection(__n0.Children);", generated);
        Assert.DoesNotContain("__TryInvokeClearMethod(", generated);
        Assert.DoesNotContain("if (list.IsReadOnly || list.IsFixedSize)", generated);
        Assert.DoesNotContain("catch (global::System.InvalidOperationException)", generated);
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
        Assert.Contains("__AXSGObjectGraph.TryClearCollection(__n0.Items);", generated);
        Assert.DoesNotContain("if (list.IsReadOnly || list.IsFixedSize)", generated);
        Assert.DoesNotContain("catch (global::System.InvalidOperationException)", generated);
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
        Assert.Contains("__n2.Text =", generated);
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
        Assert.True(
            generated.Contains(".Add(\"Greeting\",", StringComparison.Ordinal) ||
            (generated.Contains("__AXSGObjectGraph.TryAddToDictionary(", StringComparison.Ordinal) &&
             generated.Contains("\"Greeting\"", StringComparison.Ordinal)));
        Assert.Contains("Text =", generated);
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
        Assert.True(
            generated.Contains(".Resources.Add(\"Greeting\",", StringComparison.Ordinal) ||
            (generated.Contains("__AXSGObjectGraph.TryAddToDictionary(", StringComparison.Ordinal) &&
             generated.Contains("\"Greeting\"", StringComparison.Ordinal)));
        Assert.Contains("Text =", generated);
    }

    [Fact]
    public void Does_Not_Clear_Newly_Created_Merged_Resource_Dictionary_Instance()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?>
                {
                    public global::System.Collections.Generic.List<ResourceDictionary> MergedDictionaries { get; } = new();
                }

                public class UserControl
                {
                    public ResourceDictionary Resources { get; } = new();
                    public object? Content { get; set; }
                }

                public class TextBlock
                {
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public class ThemeDictionary : global::Avalonia.Controls.ResourceDictionary
                {
                    public ThemeDictionary()
                    {
                        Add("Marker", "FromTheme");
                    }
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:demo="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <demo:ThemeDictionary />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </UserControl.Resources>
                <TextBlock Text="{StaticResource Marker}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .First(source =>
                source.Contains("__RegisterXamlSourceGenArtifacts", StringComparison.Ordinal) &&
                source.Contains("partial class MainView", StringComparison.Ordinal));
        Assert.Matches(
            new global::System.Text.RegularExpressions.Regex(
                @"__AXSGObjectGraph\.TryClearDictionaryEntries\([^)]*\.Resources\);",
                global::System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            generated);
        Assert.DoesNotMatch(
            new global::System.Text.RegularExpressions.Regex(
                @"__AXSGObjectGraph\.TryClearCollection\([^)]*\.Resources\);",
                global::System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            generated);
    }

    [Fact]
    public void Treats_MultiValue_Implicit_Content_Collection_As_Collection_Add()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo
            {
                public class ChildNode { }

                public class ChildCollection : global::System.Collections.Generic.List<ChildNode>
                {
                }

                public class HostControl : global::Avalonia.Controls.UserControl
                {
                    public ChildCollection Content { get; set; } = new();
                }

                public partial class MainView : HostControl { }
            }
            """;

        const string xaml = """
            <HostControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:demo="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <demo:ChildNode />
                <demo:ChildNode />
            </HostControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0103");
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.True(
            generated.Contains("__AXSGObjectGraph.TryClearCollection(__root.Content);", StringComparison.Ordinal) ||
            generated.Contains("__TrySetClrProperty(__root, \"Content\",", StringComparison.Ordinal) ||
            generated.Contains("__root.Content =", StringComparison.Ordinal));
        Assert.True(
            generated.Contains("__TryAddToCollection(", StringComparison.Ordinal) ||
            generated.Contains(".Content.Add(", StringComparison.Ordinal) ||
            generated.Contains(".Content).Add(", StringComparison.Ordinal) ||
            generated.Contains("Add((global::Demo.ChildNode)", StringComparison.Ordinal));
        Assert.Contains("ChildNode", generated);
    }

    [Fact]
    public void Keyed_ResourceInclude_Uses_DictionaryEntry_Resolver()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?>
                {
                }

                public class UserControl
                {
                    public ResourceDictionary Resources { get; set; } = new();
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Styling
            {
                public class ResourceInclude
                {
                    public object? Loaded => null;
                    public global::System.Uri? Source { get; set; }
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
                        <ResourceInclude x:Key="CompactStyles" Source="/DensityStyles/Compact.xaml" />
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("__NormalizeDictionaryValue(", generated);
        Assert.DoesNotContain("__TryResolveDictionaryEntryValue(", generated);
        Assert.DoesNotContain("map[key] = dictionaryValue;", generated);
        Assert.Contains("__AXSGObjectGraph.TryAddToDictionary(", generated);
    }

    [Fact]
    public void StaticResource_Object_Node_Uses_ProvideValue_When_Added_To_Dictionary()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?>
                {
                }
            }

            namespace Avalonia.Markup.Xaml
            {
                public abstract class MarkupExtension
                {
                    public abstract object? ProvideValue(global::System.IServiceProvider serviceProvider);
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public sealed class StaticResourceExtension : global::Avalonia.Markup.Xaml.MarkupExtension
                {
                    public object? ResourceKey { get; set; }

                    public override object? ProvideValue(global::System.IServiceProvider serviceProvider) => null;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.ResourceDictionary { }
            }
            """;

        const string xaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                x:Class="Demo.MainView">
                <StaticResource x:Key="Alias" ResourceKey="BaseKey" />
            </ResourceDictionary>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            new[]
            {
                new KeyValuePair<string, string>(
                    "build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled",
                    "false")
            });

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            "SourceGenMarkupExtensionRuntime.ProvideMarkupExtension((global::Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension)",
            generated);
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
        Assert.True(
            generated.Contains(".Resources.Add(\"AccentBrush\",", StringComparison.Ordinal) ||
            (generated.Contains("__AXSGObjectGraph.TryAddToDictionary(", StringComparison.Ordinal) &&
             generated.Contains("\"AccentBrush\"", StringComparison.Ordinal)));
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
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public object? Tag { get; set; }
                }
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
        Assert.Contains("SourceGenMarkupExtensionRuntime.ApplyBinding(", generated);
        Assert.Contains("global::Avalonia.Controls.TextBox.TextProperty", generated);
        Assert.Contains("global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.AttachBindingNameScope(new global::Avalonia.Data.Binding(\"Name\")", generated);
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

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
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

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
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
    public void Supports_Negated_Typed_Parent_Query_Compiled_Binding_Without_Ambient_XDataType()
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
                    public global::System.Type? AncestorType { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject
                {
                    public bool IsEnabled { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class DataGrid : Control
                {
                    public object? Content { get; set; }
                }

                public class Border : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty IsVisibleProperty = new();
                    public bool IsVisible { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
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
                         x:CompileBindings="True">
                <DataGrid>
                    <Border IsVisible="{Binding !$parent[DataGrid].IsEnabled}" />
                </DataGrid>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0110");
        Assert.DoesNotContain(diagnostics, d => d.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("RelativeSourceMode.FindAncestor", generated);
        Assert.Contains("AncestorType = typeof(global::Avalonia.Controls.DataGrid)", generated);
        Assert.Contains("\"!IsEnabled\"", generated);
        Assert.Contains("source.IsEnabled", generated);
        Assert.Contains("!global::System.Convert.ToBoolean(source.IsEnabled)", generated);
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
        Assert.DoesNotContain("new global::Avalonia.Data.Binding(\"Text\") { Source =", generated);
    }

    [Fact]
    public void Applies_ResolveByName_Semantics_For_Avalonia_Property_Literals()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Controls
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property | global::System.AttributeTargets.Method)]
                public sealed class ResolveByNameAttribute : global::System.Attribute { }

                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }

                public class Border : Control { }
            }

            namespace Avalonia.Controls.Primitives
            {
                public class Popup : global::Avalonia.Controls.Control
                {
                    public static readonly global::Avalonia.StyledProperty<global::Avalonia.Controls.Control?> PlacementTargetProperty = new();

                    [global::Avalonia.Controls.ResolveByName]
                    public global::Avalonia.Controls.Control? PlacementTarget
                    {
                        get => null;
                        set { }
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
                         xmlns:p="clr-namespace:Avalonia.Controls.Primitives"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <Border x:Name="Background" />
                    <p:Popup PlacementTarget="Background" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("ProvideReference(\"Background\"", generated);
        Assert.DoesNotContain("PlacementTargetProperty, \"Background\"", generated);
    }

    [Fact]
    public void Applies_ResolveByName_Semantics_For_Attached_Avalonia_Property_Literals()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AttachedProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Controls
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property | global::System.AttributeTargets.Method)]
                public sealed class ResolveByNameAttribute : global::System.Attribute { }

                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }

                public class Border : Control { }

                public class RelativePanel
                {
                    public static readonly global::Avalonia.AttachedProperty<object> LeftOfProperty = new();

                    [ResolveByName]
                    public static object GetLeftOf(global::Avalonia.AvaloniaObject obj) => new object();

                    [ResolveByName]
                    public static void SetLeftOf(global::Avalonia.AvaloniaObject obj, object value) { }
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
                         xmlns:local="clr-namespace:Avalonia.Controls"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <Border x:Name="Target" />
                    <Border local:RelativePanel.LeftOf="Target" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("ProvideReference(\"Target\"", generated);
        Assert.DoesNotContain("LeftOfProperty, \"Target\"", generated);
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
        Assert.Contains("SourceGenMarkupExtensionRuntime.ApplyBinding(", generated);
        Assert.Contains("global::Avalonia.Controls.TextBlock.TextProperty", generated);
        Assert.Contains("global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.AttachBindingNameScope(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(", generated);
    }

    [Fact]
    public void Generates_Runtime_Call_For_Explicit_CSharp_Expression_Binding()
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

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class PersonVm
                {
                    public string FirstName { get; set; } = string.Empty;
                    public string LastName { get; set; } = string.Empty;
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
                         x:DataType="vm:PersonVm"
                         x:CompileBindings="True">
                <TextBlock Text="{= FirstName + ' - ' + LastName}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<global::Demo.ViewModels.PersonVm>", generated);
        Assert.Contains("__AXSG_CompiledBinding_", generated);
        Assert.DoesNotContain("static source => (object?)(source.FirstName + \" - \" + source.LastName)", generated);
        Assert.Contains(
            "SourceGenMarkupExtensionRuntime.ApplyBinding(",
            generated);
        Assert.Contains("global::Avalonia.Controls.TextBlock.TextProperty", generated);
        Assert.DoesNotContain(
            ".SetValue(global::Avalonia.Controls.TextBlock.TextProperty, global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<global::Demo.ViewModels.PersonVm>(",
            generated);
    }

    [Fact]
    public void Generates_Runtime_Call_For_Implicit_CSharp_Expression_Binding()
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

            namespace Demo.ViewModels
            {
                public class PersonVm
                {
                    public string FirstName { get; set; } = string.Empty;
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
                         x:DataType="vm:PersonVm"
                         x:CompileBindings="True">
                <TextBlock Text="{FirstName + '!'}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<global::Demo.ViewModels.PersonVm>", generated);
        Assert.Contains("__AXSG_CompiledBinding_", generated);
        Assert.DoesNotContain("static source => (object?)(source.FirstName + '!')", generated);
        Assert.Contains(
            "SourceGenMarkupExtensionRuntime.ApplyBinding(",
            generated);
        Assert.Contains("global::Avalonia.Controls.TextBlock.TextProperty", generated);
        Assert.DoesNotContain(
            ".SetValue(global::Avalonia.Controls.TextBlock.TextProperty, global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<global::Demo.ViewModels.PersonVm>(",
            generated);
    }

    [Fact]
    public void Generates_Runtime_Call_For_Interpolated_Implicit_CSharp_Expression_Binding()
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

            namespace Demo.ViewModels
            {
                public class PersonVm
                {
                    public decimal Price { get; set; }
                    public int Quantity { get; set; }
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
                         x:DataType="vm:PersonVm"
                         x:CompileBindings="True">
                <TextBlock Text="{$'Total: ${Price * Quantity:F2}'}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("ProvideExpressionBinding<global::Demo.ViewModels.PersonVm>", generated);
        Assert.Contains("$\"Total: {source.Price * source.Quantity:F2}\"", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Expression_Binding_Without_XDataType()
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
                <TextBlock Text="{= FirstName + LastName}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0110");
    }

    [Fact]
    public void Generates_Expression_Bindings_For_Sealed_XDataType_ViewModel()
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
                public interface IBinding { }
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
                    public static readonly global::Avalonia.AvaloniaProperty IsVisibleProperty = new();
                    public bool IsVisible { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class MarkupExtensionsPageViewModel
                {
                    public string FirstName { get; } = "Ava";
                    public string LastName { get; } = "SourceGen";
                    public int Count { get; } = 3;
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
                         x:DataType="vm:MarkupExtensionsPageViewModel"
                         x:CompileBindings="True">
                <TextBlock Text="{= FirstName + ' - ' + LastName}" />
                <TextBlock Text="{= FirstName + '!'}" />
                <TextBlock Text="{= Count + 7}" />
                <TextBlock IsVisible="{= Count > 0}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.FirstName", generated);
        Assert.Contains("source.LastName", generated);
        Assert.Contains("source.Count", generated);
        Assert.Contains("ApplyBinding(", generated);
        Assert.DoesNotContain(".Text = (string)(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideExpressionBinding", generated);
        Assert.DoesNotContain(".IsVisible = (bool)(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideExpressionBinding", generated);
    }

    [Fact]
    public void Generates_Expression_Bindings_For_Style_And_ControlTheme_Setters()
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
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <UserControl.Styles>
                    <Style Selector="TextBlock" x:DataType="vm:MainViewModel">
                        <Setter Property="Text" Value="{= Caption + '!'}" />
                    </Style>
                </UserControl.Styles>
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Button" TargetType="Button" x:DataType="vm:MainViewModel">
                        <Setter Property="Content" Value="{= Title + ' (' + Count + ')'}" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("XamlCompiledBindingRegistry.Register", generated);
        Assert.Contains("\"{= Caption + '!'", generated);
        Assert.Contains("source.Title", generated);
        Assert.Contains("source.Count", generated);
    }

    [Fact]
    public void Lowers_Shorthand_Expressions_To_Bindings_And_Root_Expressions()
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
                public interface IBinding { }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
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
                    public object? Text { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class Style : Control
                {
                    public string? Selector { get; set; }
                }

                public class ControlTheme : Control
                {
                    public object? TargetType { get; set; }
                }
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
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public string Title { get; set; } = string.Empty;
                    public string RootOnly { get; set; } = string.Empty;
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <StackPanel>
                    <TextBlock Text="{.Title}" />
                    <TextBlock Text="{this.RootOnly}" />
                </StackPanel>
                <UserControl.Styles>
                    <Style Selector="TextBlock" x:DataType="vm:MainViewModel">
                        <Setter Property="Text" Value="{Caption}" />
                    </Style>
                </UserControl.Styles>
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Button" TargetType="Button" x:DataType="vm:MainViewModel">
                        <Setter Property="Content" Value="{.Title}" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0113" &&
                          diagnostic.GetMessage().Contains("ambiguous", StringComparison.OrdinalIgnoreCase));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("new global::Avalonia.Data.Binding(\"Title\")", generated);
        Assert.True(
            generated.Split("XamlCompiledBindingRegistry.Register", StringSplitOptions.None).Length - 1 >= 3,
            "Expected compiled binding registrations for direct, style, and control theme shorthand bindings.");
        Assert.Contains("\"Caption\"", generated);
        Assert.Contains("ProvideInlineCodeBinding<global::Demo.ViewModels.MainViewModel, global::Demo.MainView", generated);
        Assert.Contains("root.RootOnly", generated);
    }

    [Fact]
    public void Allows_NonPublic_Shorthand_Bindings_In_Styles_And_ControlThemes()
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
                public interface IBinding { }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
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
                    public object? Text { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class Style : Control
                {
                    public string? Selector { get; set; }
                }

                public class ControlTheme : Control
                {
                    public object? TargetType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class MainViewModel
                {
                    private string HiddenTitle { get; } = "hidden";
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
                <UserControl.Styles>
                    <Style Selector="TextBlock" x:DataType="vm:MainViewModel">
                        <Setter Property="Text" Value="{HiddenTitle}" />
                    </Style>
                </UserControl.Styles>
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Button" TargetType="Button" x:DataType="vm:MainViewModel">
                        <Setter Property="Content" Value="{HiddenTitle}" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("new global::Avalonia.Data.Binding(\"HiddenTitle\")", generated);
        Assert.True(
            generated.Split("XamlCompiledBindingRegistry.Register", StringSplitOptions.None).Length - 1 >= 2,
            "Expected compiled binding registrations for both style and control theme shorthand bindings.");
        Assert.Contains("Name = \"get_HiddenTitle\"", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Ambiguous_Shorthand_Between_DataType_And_Root()
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
                public interface IBinding { }
                public class Binding : IBinding
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
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public object? Text { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public string Title { get; set; } = string.Empty;
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public string Title { get; set; } = string.Empty;
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel">
                <TextBlock Text="{Title}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0113" &&
                          diagnostic.GetMessage().Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reports_Diagnostic_When_BindingContext_Shorthand_Has_No_DataType()
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
                public interface IBinding { }
                public class Binding : IBinding
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
                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                    public object? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl
                {
                    public string Title { get; set; } = string.Empty;
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <TextBlock Text="{.Title}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0110" &&
                          diagnostic.GetMessage().Contains("x:DataType", StringComparison.Ordinal));
    }

    [Fact]
    public void Generates_InlineCSharp_Bindings_For_Compact_And_ObjectElement_Values()
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
                public interface IBinding { }
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

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
            }

            [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "XamlToCSharpGenerator.Runtime.Markup")]

            namespace XamlToCSharpGenerator.Runtime.Markup
            {
                public class CSharp
                {
                    public string? Code { get; set; }
                }

                public class CSharpExtension
                {
                    public string? Code { get; set; }
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public class CSharp : Markup.CSharp
                {
                }

                public class CSharpExtension : Markup.CSharpExtension
                {
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public string ProductName { get; set; } = string.Empty;
                    public int Quantity { get; set; }
                    public string FormatSummary(string productName, int quantity) => productName + quantity;
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
                <StackPanel>
                    <TextBlock Text="{CSharp Code=source.ProductName}" />
                    <TextBlock>
                        <TextBlock.Text>
                            <CSharp>source.FormatSummary(source.ProductName, source.Quantity)</CSharp>
                        </TextBlock.Text>
                    </TextBlock>
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0112");
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("ProvideInlineCodeBinding<global::Demo.ViewModels.MainViewModel", generated);
        Assert.Contains("source.ProductName", generated);
        Assert.True(
            generated.Split("ProvideInlineCodeBinding<global::Demo.ViewModels.MainViewModel", StringSplitOptions.None).Length - 1 >= 2,
            "Expected both compact and object-element inline code bindings to be emitted.");
    }

    [Fact]
    public void Generates_InlineCSharp_Event_Code_For_Compact_And_ObjectElement_Handlers()
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
                    public object? Content { get; set; }
                    public event global::System.EventHandler? Click;
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<Control> Children { get; } = new();
                }
            }

            namespace XamlToCSharpGenerator.Runtime
            {
                public class CSharp
                {
                    public string? Code { get; set; }
                }

                public class CSharpExtension
                {
                    public string? Code { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public int ClickCount { get; set; }
                    public void RecordSender(object? sender) { }
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
                         xmlns:axsg="using:XamlToCSharpGenerator.Runtime"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <StackPanel>
                    <Button Click="{axsg:CSharp Code=(sender, e) => source.ClickCount++}" />
                    <Button>
                        <Button.Click>
                            <axsg:CSharp>source.RecordSender(sender);</axsg:CSharp>
                        </Button.Click>
                    </Button>
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(".Click += __root.__AXSG_EventBinding_", generated);
        Assert.Contains("source.ClickCount++", generated);
        Assert.Contains("source.RecordSender(sender);", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_Style_Setter_Expression_Without_XDataType()
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
                public class Style : Control { }
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
                        <Setter Property="Text" Value="{= Caption + '!'}" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0110");
    }

    [Fact]
    public void Supports_Disabling_Implicit_CSharp_Expressions_While_Keeping_Explicit_Expressions()
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

            namespace Demo.ViewModels
            {
                public class PersonVm
                {
                    public string FirstName { get; set; } = string.Empty;
                    public string LastName { get; set; } = string.Empty;
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
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:PersonVm"
                         x:CompileBindings="True">
                <ac:TextBlock Tag="{FirstName + '!'}"
                              Text="{= FirstName + ' - ' + LastName}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenImplicitCSharpExpressionsEnabled", "false")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.LastName", generated);
    }

    [Fact]
    public void Treats_Implicit_Expression_As_Literal_When_Implicit_Mode_Disabled()
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

            namespace Demo.ViewModels
            {
                public class PersonVm
                {
                    public string FirstName { get; set; } = string.Empty;
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
                         x:DataType="vm:PersonVm"
                         x:CompileBindings="True">
                <TextBlock Text="{FirstName + '!'}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenImplicitCSharpExpressionsEnabled", "false")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("ProvideExpressionBinding<global::Demo.ViewModels.PersonVm>", generated);
        Assert.Contains("\"{FirstName + '!'}\"", generated);
    }

    [Fact]
    public void Tracks_Dependency_Names_For_Explicit_Source_Expression_Members()
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

            namespace Demo.ViewModels
            {
                public class PersonVm
                {
                    public string FirstName { get; set; } = string.Empty;
                    public string LastName { get; set; } = string.Empty;
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
                         x:DataType="vm:PersonVm"
                         x:CompileBindings="True">
                <TextBlock Text="{= source.FirstName + ' ' + source.LastName}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new string[] { \"FirstName\", \"LastName\" }", generated);
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
    public void Resolves_OnPlatform_Object_Element_To_Markup_Extension_Type()
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
                    public object? Default { get; set; }
                }

                public class On
                {
                    public string[]? Options { get; set; }
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
                <local:Host>
                    <local:Host.Value>
                        <OnPlatform Default="Base">
                            <On Options="Windows" Content="Win" />
                        </OnPlatform>
                    </local:Host.Value>
                </local:Host>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Markup.Xaml.MarkupExtensions.OnPlatformExtension()", generated);
        Assert.DoesNotContain("new global::System.Object();", generated);
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
    public void Generates_OnFormFactor_Runtime_Call_With_NullSafe_Coercion_For_Value_Types()
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
                public enum DisplayMode
                {
                    A,
                    B
                }

                public class Host : global::Avalonia.Controls.Control
                {
                    public DisplayMode Value { get; set; }
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <local:Host Value="{OnFormFactor Desktop={x:Static local:DisplayMode.A}, Mobile={x:Static local:DisplayMode.B}}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            "SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<global::Demo.DisplayMode>(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(",
            generated);
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
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

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
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

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
        Assert.Contains("__CompiledBindingAccessor(0, source)", generated);
        Assert.Contains("private static object? __CompiledBindingAccessor(int __index, object __source)", generated);
        Assert.DoesNotContain("__CompiledBindingAccessor0(", generated);
        Assert.Contains("XamlStyleRegistry.Register", generated);
        Assert.Contains("XamlControlThemeRegistry.Register", generated);
        Assert.Contains("XamlIncludeRegistry.Register", generated);
        Assert.Contains("XamlIncludeGraphRegistry.Register", generated);
        Assert.DoesNotContain("__TryResolveStyleInclude(global::Avalonia.Markup.Xaml.Styling.StyleInclude styleInclude, object? ownerContext, out global::Avalonia.Styling.IStyle resolvedStyle)", generated);
        Assert.DoesNotContain("private sealed class __SourceGenIncludeLoadServiceProvider", generated);
        Assert.DoesNotContain("destinationDictionary.MergedDictionaries.Add(mergedResourceDictionary);", generated);
        Assert.Contains("\"Text\"", generated);
        Assert.Contains("\"Content\"", generated);
        Assert.Contains("\"Caption\"", generated);
        Assert.Contains("\"Title\"", generated);
        Assert.Contains("\"Dark\"", generated);
    }

    [Fact]
    public void Resolves_Compiled_Binding_Paths_For_Inherited_Interface_Members_In_Views_Styles_And_ControlThemes()
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
                public interface IItemBase
                {
                    string Id { get; }

                    string ResolveTitle();
                }

                public interface IItem : IItemBase
                {
                }

                public interface IMainViewModel
                {
                    IItem Item { get; }
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
                         x:DataType="vm:IMainViewModel"
                         x:CompileBindings="True">
                <TextBlock Text="{CompiledBinding Item.Id}" />
                <UserControl.Styles>
                    <Style Selector="TextBlock" x:DataType="vm:IItem" x:CompileBindings="True">
                        <Setter Property="Text" Value="{CompiledBinding Id}" />
                    </Style>
                </UserControl.Styles>
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Button" TargetType="Button" x:DataType="vm:IItem" x:CompileBindings="True">
                        <Setter Property="Content" Value="{CompiledBinding ResolveTitle}" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("source.Item.Id", generated);
        Assert.Contains("source.Id", generated);
        Assert.Contains("source.ResolveTitle()", generated);
    }

    [Fact]
    public void Resolves_Compiled_Method_To_Command_Bindings_For_Style_And_ControlTheme_Targets()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                public sealed class DependsOnAttribute : global::System.Attribute
                {
                    public DependsOnAttribute(string name)
                    {
                        Name = name;
                    }

                    public string Name { get; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class Styles : global::System.Collections.Generic.List<object> { }
                public class ResourceDictionary : global::System.Collections.Generic.List<object> { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public Styles Styles { get; } = new();
                    public ResourceDictionary Resources { get; } = new();
                }
                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
                public class Style : Control
                {
                    public string? Selector { get; set; }
                }
                public class ControlTheme : Control
                {
                    public object? TargetType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public interface IFactoryCapabilities
                {
                    [global::Avalonia.Metadata.DependsOn(nameof(IsEnabled))]
                    bool CanFloatDockable(object? parameter);

                    bool IsEnabled { get; }
                }

                public interface IFactory : IFactoryCapabilities
                {
                    void FloatDockable(int index);
                }

                public interface IOwner
                {
                    IFactory Factory { get; }
                }

                public class MainVm
                {
                    public IOwner Owner { get; set; } = null!;
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
                <UserControl.Styles>
                    <Style Selector="Button" x:DataType="vm:MainVm" x:CompileBindings="True">
                        <Setter Property="Command" Value="{CompiledBinding Owner.Factory.FloatDockable}" />
                    </Style>
                </UserControl.Styles>
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Button" TargetType="Button" x:DataType="vm:MainVm" x:CompileBindings="True">
                        <Setter Property="Command" Value="{CompiledBinding Owner.Factory.FloatDockable}" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMethodCommandRuntime.Create((object?)(source.Owner.Factory)", generated);
        Assert.Contains("SourceGenMethodCommandRuntime.ConvertParameter<int>(parameter)", generated);
        Assert.Contains("CanFloatDockable(parameter)", generated);
        Assert.Contains("new string[] { \"IsEnabled\" }", generated);
        Assert.Contains("\"Owner.Factory.FloatDockable()\"", generated);
    }

    [Fact]
    public void Generates_Expression_Backed_Method_Command_For_Object_Node_Command_Property()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }

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
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();
                    public static readonly global::Avalonia.AvaloniaProperty<object?> CommandParameterProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                    public object? CommandParameter { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public interface IFactory
                {
                    void FloatDockable(int index);
                }

                public interface IOwner
                {
                    IFactory Factory { get; }
                }

                public class MainVm
                {
                    public IOwner Owner { get; set; } = null!;
                    public int Index { get; set; }
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
                <Button Command="{Binding Owner.Factory.FloatDockable}"
                        CommandParameter="{Binding Index}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideExpressionBinding<global::Demo.ViewModels.MainVm>", generated);
        Assert.Contains("SourceGenMethodCommandRuntime.Create((object?)(source.Owner.Factory)", generated);
        Assert.Contains("new string[] { \"Owner\", \"Owner.Factory\" }", generated);
        Assert.Contains("global::Avalonia.Controls.Button.CommandProperty", generated);
        Assert.DoesNotContain("new global::Avalonia.Data.Binding(\"Owner.Factory.FloatDockable\")", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generates_Expression_Backed_Method_Command_For_Context_Menu_Object_Node()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }

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
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class ResourceDictionary : global::System.Collections.Generic.List<object> { }

                public class ContextMenu : Control { }

                public class MenuItem : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();
                    public static readonly global::Avalonia.AvaloniaProperty<object?> CommandParameterProperty = new();

                    public object? Header { get; set; }
                    public global::System.Windows.Input.ICommand? Command { get; set; }
                    public object? CommandParameter { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public interface IFactory
                {
                    void FloatDockable(int index);
                }

                public interface IOwner
                {
                    IFactory Factory { get; }
                }

                public class MainVm
                {
                    public IOwner Owner { get; set; } = null!;
                    public int Index { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MenuResources : global::Avalonia.Controls.ResourceDictionary { }
            }
            """;

        const string xaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:vm="clr-namespace:Demo.ViewModels"
                                x:Class="Demo.MenuResources"
                                x:DataType="vm:MainVm"
                                x:CompileBindings="True">
                <ContextMenu x:Key="Menu">
                    <MenuItem Header="Float"
                              Command="{Binding Owner.Factory.FloatDockable}"
                              CommandParameter="{Binding Index}" />
                </ContextMenu>
            </ResourceDictionary>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = string.Join(
            Environment.NewLine,
            updatedCompilation.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.DoesNotContain("new global::Avalonia.Data.Binding(\"Owner.Factory.FloatDockable\")", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolves_Method_Command_For_Overridden_Base_Method_Without_Ambiguity()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class BaseActions
                {
                    public virtual void Execute()
                    {
                    }
                }

                public class DerivedActions : BaseActions
                {
                    public override void Execute()
                    {
                    }
                }

                public class MainVm
                {
                    public DerivedActions Actions { get; set; } = new();
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
                <Button Command="{CompiledBinding Actions.Execute}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMethodCommandRuntime.Create((object?)(source.Actions)", generated);
        Assert.Contains("\"Actions.Execute()\"", generated);
    }

    [Fact]
    public void Resolves_Derived_CanExecute_For_Inherited_Method_Command()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                public sealed class DependsOnAttribute : global::System.Attribute
                {
                    public DependsOnAttribute(string name)
                    {
                        Name = name;
                    }

                    public string Name { get; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class BaseActions
                {
                    public void Execute()
                    {
                    }
                }

                public class DerivedActions : BaseActions
                {
                    [global::Avalonia.Metadata.DependsOn(nameof(IsEnabled))]
                    public bool CanExecute(object? parameter) => parameter is not null;

                    public bool IsEnabled { get; set; }
                }

                public class MainVm
                {
                    public DerivedActions Actions { get; set; } = new();
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
                <Button Command="{CompiledBinding Actions.Execute}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("CanExecute(parameter)", generated);
        Assert.Contains("new string[] { \"IsEnabled\" }", generated);
    }

    [Fact]
    public void Prefers_ICommand_Property_Over_Method_Command_Fallback()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class CommandBase
                {
                    public global::System.Windows.Input.ICommand Save { get; } = null!;
                }

                public class MainVm : CommandBase
                {
                    public new void Save()
                    {
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
                <Button Command="{CompiledBinding Save}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("SourceGenMethodCommandRuntime.Create(", generated, StringComparison.Ordinal);
        Assert.Contains("source.Save", generated);
        Assert.DoesNotContain("source.Save()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Deduplicates_Equivalent_Interface_Method_Command_Candidates()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public interface IA
                {
                    void Execute();
                }

                public interface IB
                {
                    void Execute();
                }

                public interface ICombined : IA, IB
                {
                }

                public class MainVm
                {
                    public ICombined Actions { get; set; } = null!;
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
                <Button Command="{CompiledBinding Actions.Execute}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMethodCommandRuntime.Create((object?)(source.Actions)", generated);
        Assert.Contains("\"Actions.Execute()\"", generated);
    }

    [Fact]
    public void Prefers_ICommand_Returning_Method_Over_Method_Command_Wrapping()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public global::System.Windows.Input.ICommand BuildCommand() => null!;
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
                <Button Command="{CompiledBinding BuildCommand}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("SourceGenMethodCommandRuntime.Create(", generated, StringComparison.Ordinal);
        Assert.Contains("source.BuildCommand()", generated);
    }

    [Fact]
    public void Does_Not_Treat_NonICommand_Command_Property_As_Method_Command_Target()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class Styles : global::System.Collections.Generic.List<object> { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public Styles Styles { get; } = new();
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<string?> CommandProperty = new();

                    public string? Command { get; set; }
                }

                public class Style : Control
                {
                    public string? Selector { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class MainVm
                {
                    public string BuildLabel() => "ready";
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
                <Button Command="{CompiledBinding BuildLabel}" />
                <UserControl.Styles>
                    <Style Selector="Button" x:DataType="vm:MainVm" x:CompileBindings="True">
                        <Setter Property="Command" Value="{CompiledBinding BuildLabel}" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("SourceGenMethodCommandRuntime.Create(", generated, StringComparison.Ordinal);
        Assert.Contains("source.BuildLabel()", generated);
    }

    [Fact]
    public void Uses_Explicit_DataContext_Binding_Assignment_As_Compiled_Binding_Scope()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Panel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public interface IRootDock
                {
                    global::System.Windows.Input.ICommand ExitWindows { get; }

                    global::System.Windows.Input.ICommand ShowWindows { get; }
                }

                public class MainVm
                {
                    public IRootDock Layout { get; set; } = null!;
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
                <Panel DataContext="{Binding Layout}">
                    <Button Command="{Binding ExitWindows}" />
                    <Button Command="{Binding ShowWindows}" />
                </Panel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = string.Join("\n", updatedCompilation.SyntaxTrees.Select(static syntaxTree => syntaxTree.ToString()));
        Assert.Contains("source.Layout", generated);
        Assert.Contains("source.ExitWindows", generated);
        Assert.Contains("source.ShowWindows", generated);
    }

    [Fact]
    public void Uses_Parent_Scope_For_DataContext_Assignment_And_Node_Scope_For_Sibling_Bindings()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Windows.Input.ICommand?> CommandProperty = new();

                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public interface IRootDock
                {
                    global::System.Windows.Input.ICommand Navigate { get; }
                }

                public class MainVm
                {
                    public IRootDock Layout { get; set; } = null!;
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
                <Button DataContext="{Binding Layout}"
                        x:DataType="vm:IRootDock"
                        Command="{Binding Navigate}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = string.Join("\n", updatedCompilation.SyntaxTrees.Select(static syntaxTree => syntaxTree.ToString()));
        Assert.Contains("source.Layout", generated);
        Assert.Contains("source.Navigate", generated);
    }

    [Fact]
    public void Uses_ElementName_DataContext_Assignment_As_Compiled_Binding_Scope()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class DockPanel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class TextBox : Control
                {
                    public string? Text { get; set; }
                }

                public class ListBox : Control
                {
                    public object? ItemsSource { get; set; }
                }
            }

            namespace Demo
            {
                public partial class RootView : global::Avalonia.Controls.UserControl
                {
                    public string? Filter { get; set; }

                    public global::System.Collections.Generic.IReadOnlyList<string>? FilteredEvents { get; set; }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.RootView"
                         x:Name="Root"
                         x:CompileBindings="True">
                <DockPanel DataContext="{Binding #Root}">
                    <TextBox Text="{Binding Filter}" />
                    <ListBox ItemsSource="{Binding FilteredEvents}" />
                </DockPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = string.Join("\n", updatedCompilation.SyntaxTrees.Select(static syntaxTree => syntaxTree.ToString()));
        Assert.Contains("source.Filter", generated);
        Assert.Contains("source.FilteredEvents", generated);
    }

    [Fact]
    public void Does_Not_Infer_DataContext_Scope_From_NonRoot_ElementName_Without_NameScope_Analysis()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Panel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class TextBox : Control
                {
                    public string? Text { get; set; }
                }
            }

            namespace Demo
            {
                public partial class RootView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.RootView"
                         x:CompileBindings="True">
                <Panel>
                    <TextBox x:Name="Visible" Text="hello" />
                    <Panel DataContext="{Binding #Visible}">
                        <TextBox Text="{Binding Text}" />
                    </Panel>
                </Panel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0110");
    }

    [Fact]
    public void Materializes_DataTemplate_XDataType_To_Runtime_DataType_Assignment()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
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
                public class TextBlock : Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Collections.AvaloniaList<global::Avalonia.Controls.Templates.IDataTemplate> DataTemplates { get; } = new();
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class ItemViewModel { }
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
                         x:Class="Demo.MainView">
                <UserControl.DataTemplates>
                    <DataTemplate x:DataType="vm:ItemViewModel">
                        <TextBlock />
                    </DataTemplate>
                </UserControl.DataTemplates>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            ".DataType = (global::System.Type)(typeof(global::Demo.ViewModels.ItemViewModel));",
            generated);
    }

    [Fact]
    public void Uses_DataTemplate_DataType_Property_As_Compiled_Binding_Source_Scope()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
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
                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public class UserControl : Control
                {
                    public global::Avalonia.Collections.AvaloniaList<global::Avalonia.Controls.Templates.IDataTemplate> DataTemplates { get; } = new();
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class ItemViewModel
                {
                    public string Title { get; set; } = string.Empty;
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
                         x:CompileBindings="True">
                <UserControl.DataTemplates>
                    <DataTemplate DataType="vm:ItemViewModel">
                        <TextBlock Text="{CompiledBinding Title}" />
                    </DataTemplate>
                </UserControl.DataTemplates>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("source.Title", generated);
    }

    [Fact]
    public void Infers_ItemTemplate_DataType_From_ItemsSource_Attribute()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
            }

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class InheritDataTypeFromItemsAttribute : global::System.Attribute
                {
                    public InheritDataTypeFromItemsAttribute(string ancestorItemsProperty)
                    {
                        AncestorItemsProperty = ancestorItemsProperty;
                    }

                    public string AncestorItemsProperty { get; }

                    public global::System.Type? AncestorType { get; set; }
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

                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ItemsControl : Control
                {
                    public object? ItemsSource { get; set; }

                    [global::Avalonia.Metadata.InheritDataTypeFromItems(nameof(ItemsSource))]
                    public object? ItemTemplate { get; set; }
                }

                public class ListBox : ItemsControl { }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class RowVm
                {
                    public string Name { get; set; } = string.Empty;
                }

                public sealed class MainVm
                {
                    public global::System.Collections.Generic.IReadOnlyList<RowVm> Rows { get; } =
                        global::System.Array.Empty<RowVm>();
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
                <ListBox ItemsSource="{CompiledBinding Rows}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{CompiledBinding Name}" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(
            "SourceGenCompiledBindingDescriptor(\"avares://Demo.Assembly/MainView.axaml\", \"global::Avalonia.Controls.TextBlock\", \"Text\", \"Name\", \"global::Demo.ViewModels.RowVm\"",
            generated);
        Assert.Contains("var source = (global::Demo.ViewModels.RowVm)__source;", generated);
        Assert.Contains("return source.Name;", generated);
    }

    [Fact]
    public void Infers_ItemTemplate_DataType_From_ObjectElement_ItemsSource_Binding()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
            }

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class InheritDataTypeFromItemsAttribute : global::System.Attribute
                {
                    public InheritDataTypeFromItemsAttribute(string ancestorItemsProperty)
                    {
                        AncestorItemsProperty = ancestorItemsProperty;
                    }

                    public string AncestorItemsProperty { get; }

                    public global::System.Type? AncestorType { get; set; }
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

                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ItemsControl : Control
                {
                    public object? ItemsSource { get; set; }

                    [global::Avalonia.Metadata.InheritDataTypeFromItems(nameof(ItemsSource))]
                    public object? ItemTemplate { get; set; }
                }

                public class ListBox : ItemsControl { }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.MarkupExtensions
            {
                public class CompiledBindingExtension
                {
                    public string? Path { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class RowVm
                {
                    public string Name { get; set; } = string.Empty;
                }

                public sealed class MainVm
                {
                    public global::System.Collections.Generic.IReadOnlyList<RowVm> Rows { get; } =
                        global::System.Array.Empty<RowVm>();
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
                <ListBox>
                    <ListBox.ItemsSource>
                        <CompiledBinding Path="Rows" />
                    </ListBox.ItemsSource>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{CompiledBinding Name}" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(
            "SourceGenCompiledBindingDescriptor(\"avares://Demo.Assembly/MainView.axaml\", \"global::Avalonia.Controls.TextBlock\", \"Text\", \"Name\", \"global::Demo.ViewModels.RowVm\"",
            generated);
        Assert.Contains("var source = (global::Demo.ViewModels.RowVm)__source;", generated);
        Assert.Contains("return source.Name;", generated);
    }

    [Fact]
    public void Infers_ItemTemplate_DataType_From_Shorthand_ItemsSource_Binding()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
            }

            namespace Avalonia.Data
            {
                public interface IBinding { }

                public class Binding : IBinding
                {
                    public Binding(string path) { }
                }
            }

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class InheritDataTypeFromItemsAttribute : global::System.Attribute
                {
                    public InheritDataTypeFromItemsAttribute(string ancestorItemsProperty)
                    {
                        AncestorItemsProperty = ancestorItemsProperty;
                    }

                    public string AncestorItemsProperty { get; }

                    public global::System.Type? AncestorType { get; set; }
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

                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ItemsControl : Control
                {
                    public object? ItemsSource { get; set; }

                    [global::Avalonia.Metadata.InheritDataTypeFromItems(nameof(ItemsSource))]
                    public object? ItemTemplate { get; set; }
                }

                public class ListBox : ItemsControl { }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class RowVm
                {
                    public string Name { get; set; } = string.Empty;
                }

                public sealed class MainVm
                {
                    public global::System.Collections.Generic.IReadOnlyList<RowVm> Rows { get; } =
                        global::System.Array.Empty<RowVm>();
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
                <ListBox ItemsSource="{Rows}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{CompiledBinding Name}" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(
            "SourceGenCompiledBindingDescriptor(\"avares://Demo.Assembly/MainView.axaml\", \"global::Avalonia.Controls.TextBlock\", \"Text\", \"Name\", \"global::Demo.ViewModels.RowVm\"",
            generated);
        Assert.Contains("var source = (global::Demo.ViewModels.RowVm)__source;", generated);
        Assert.Contains("return source.Name;", generated);
    }

    [Fact]
    public void Infers_Annotated_Binding_And_Template_DataTypes_From_Ancestor_ItemsSource()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
            }

            namespace Avalonia.Data
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class AssignBindingAttribute : global::System.Attribute { }

                public interface IBinding { }
            }

            namespace Avalonia.Metadata
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class InheritDataTypeFromItemsAttribute : global::System.Attribute
                {
                    public InheritDataTypeFromItemsAttribute(string ancestorItemsProperty)
                    {
                        AncestorItemsProperty = ancestorItemsProperty;
                    }

                    public string AncestorItemsProperty { get; }

                    public global::System.Type? AncestorType { get; set; }
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

                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.Controls
            {
                public sealed class DataGridLikeControl : global::Avalonia.Controls.Control
                {
                    public object? ItemsSource { get; set; }

                    public global::Avalonia.Collections.AvaloniaList<DataGridLikeColumn> Columns { get; } = new();
                }

                public sealed class DataGridLikeColumn
                {
                    [global::Avalonia.Data.AssignBinding]
                    [global::Avalonia.Metadata.InheritDataTypeFromItems(
                        nameof(DataGridLikeControl.ItemsSource),
                        AncestorType = typeof(DataGridLikeControl))]
                    public global::Avalonia.Data.IBinding? ValueBinding { get; set; }

                    [global::Avalonia.Metadata.InheritDataTypeFromItems(
                        nameof(DataGridLikeControl.ItemsSource),
                        AncestorType = typeof(DataGridLikeControl))]
                    public object? CellTemplate { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class RowVm
                {
                    public string Name { get; set; } = string.Empty;
                }

                public sealed class MainVm
                {
                    public global::System.Collections.Generic.IReadOnlyList<RowVm> Rows { get; } =
                        global::System.Array.Empty<RowVm>();
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
                         xmlns:local="clr-namespace:Demo.Controls"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainVm"
                         x:CompileBindings="True">
                <local:DataGridLikeControl ItemsSource="{CompiledBinding Rows}">
                    <local:DataGridLikeControl.Columns>
                        <local:DataGridLikeColumn ValueBinding="{CompiledBinding Name}" />
                        <local:DataGridLikeColumn>
                            <local:DataGridLikeColumn.CellTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{CompiledBinding Name}" />
                                </DataTemplate>
                            </local:DataGridLikeColumn.CellTemplate>
                        </local:DataGridLikeColumn>
                    </local:DataGridLikeControl.Columns>
                </local:DataGridLikeControl>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(
            "SourceGenCompiledBindingDescriptor(\"avares://Demo.Assembly/MainView.axaml\", \"global::Demo.Controls.DataGridLikeColumn\", \"ValueBinding\", \"Name\", \"global::Demo.ViewModels.RowVm\"",
            generated);
        Assert.Contains(
            "SourceGenCompiledBindingDescriptor(\"avares://Demo.Assembly/MainView.axaml\", \"global::Avalonia.Controls.TextBlock\", \"Text\", \"Name\", \"global::Demo.ViewModels.RowVm\"",
            generated);
        Assert.True(
            Regex.Matches(
                generated,
                @"var source = \(global::Demo\.ViewModels\.RowVm\)__source;",
                RegexOptions.CultureInvariant).Count >= 2,
            "Expected ancestor-items inference to produce RowVm-typed compiled binding accessors for both binding and template scopes.");
        Assert.True(
            Regex.Matches(
                generated,
                @"return source\.Name;",
                RegexOptions.CultureInvariant).Count >= 2,
            "Expected ancestor-items inference to bind Name against RowVm in both binding and template scopes.");
    }

    [Fact]
    public void Uses_Distinct_Compiled_Binding_Accessors_For_Identical_Command_Paths_Across_Source_Types()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledElement { }
                public class AvaloniaObject : StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope { }

                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                }

                public class Control : global::Avalonia.AvaloniaObject { }

                public class Button : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty CommandProperty = new();
                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Collections.AvaloniaList<global::Avalonia.Controls.Templates.IDataTemplate> DataTemplates { get; } = new();
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class RootVm
                {
                    public global::System.Windows.Input.ICommand CancelCommand { get; } = null!;
                }

                public class ItemVm
                {
                    public global::System.Windows.Input.ICommand CancelCommand { get; } = null!;
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
                         x:DataType="vm:RootVm"
                         x:CompileBindings="True">
                <Button Command="{CompiledBinding CancelCommand}" />
                <UserControl.DataTemplates>
                    <DataTemplate DataType="vm:ItemVm">
                        <Button Command="{CompiledBinding CancelCommand}" />
                    </DataTemplate>
                </UserControl.DataTemplates>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "AXSG0110" or "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        var rootMatch = Regex.Match(
            generated,
            @"ProvideExpressionBinding<global::Demo\.ViewModels\.RootVm>\((__AXSG_CompiledBinding_[A-Za-z0-9_]+),",
            RegexOptions.CultureInvariant);
        var itemMatch = Regex.Match(
            generated,
            @"ProvideExpressionBinding<global::Demo\.ViewModels\.ItemVm>\((__AXSG_CompiledBinding_[A-Za-z0-9_]+),",
            RegexOptions.CultureInvariant);

        Assert.True(rootMatch.Success, "Expected root compiled command binding accessor.");
        Assert.True(itemMatch.Success, "Expected template compiled command binding accessor.");

        var rootMethodName = rootMatch.Groups[1].Value;
        var itemMethodName = itemMatch.Groups[1].Value;

        Assert.NotEqual(rootMethodName, itemMethodName);
        Assert.Contains(
            $"private static object? {rootMethodName}(global::Demo.ViewModels.RootVm source)",
            generated);
        Assert.Contains(
            $"private static object? {itemMethodName}(global::Demo.ViewModels.ItemVm source)",
            generated);
    }

    [Fact]
    public void Allows_NonPublic_Compiled_Binding_Properties_With_XamlX_Parity()
    {
        const string code = """
            namespace Avalonia
            {
                public class StyledElement { }
            }

            namespace Avalonia.Collections
            {
                public class AvaloniaList<T> : global::System.Collections.Generic.List<T> { }
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

                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public class UserControl : Control
                {
                    public global::Avalonia.Collections.AvaloniaList<global::Avalonia.Controls.Templates.IDataTemplate> DataTemplates { get; } = new();
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }

                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class DataTemplate : global::Avalonia.Controls.Templates.IDataTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? DataType { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class WizardContext
                {
                    public string Title { get; set; } = string.Empty;
                }

                public abstract class WizardStepViewModelBase
                {
                    protected WizardContext Context { get; } = new();
                    internal string InternalTitle { get; } = "internal";
                }

                public sealed class StepViewModel : WizardStepViewModelBase
                {
                    private string HiddenTitle { get; } = "hidden";
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
                         x:CompileBindings="True">
                <UserControl.DataTemplates>
                    <DataTemplate DataType="vm:StepViewModel">
                        <TextBlock Text="{CompiledBinding Context.Title}" />
                        <TextBlock Text="{CompiledBinding HiddenTitle}" />
                        <TextBlock Text="{CompiledBinding InternalTitle}" />
                    </DataTemplate>
                </UserControl.DataTemplates>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0122");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("Name = \"get_Context\"", generated);
        Assert.Contains("Name = \"get_HiddenTitle\"", generated);
        Assert.DoesNotContain("Name = \"get_InternalTitle\"", generated, StringComparison.Ordinal);
        Assert.Contains("source.InternalTitle", generated);
    }

    [Fact]
    public void Emits_NullConditional_NonPublic_Compiled_Binding_Access_With_Single_Target_Evaluation()
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
                public sealed class ChildVm
                {
                    private string HiddenTitle { get; } = "hidden";
                }

                public sealed class MainVm
                {
                    public ChildVm? SelectedChild { get; } = new ChildVm();
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
                <TextBlock Text="{CompiledBinding SelectedChild?.HiddenTitle}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Matches(
            @"source\.SelectedChild is \{ \} (__axsg_target_[0-9a-f]+) \? __AXSG_UnsafeAccessor_[0-9a-f]+\(\1\) : null",
            generated);
        Assert.DoesNotContain("source.SelectedChild is null ? default", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_Lifted_Null_Semantics_For_NonPublic_ValueType_NullConditional_Access()
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
                public sealed class ChildVm
                {
                    private int HiddenCount { get; } = 42;
                }

                public sealed class MainVm
                {
                    public ChildVm? SelectedChild { get; } = new ChildVm();
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
                <TextBlock Text="{CompiledBinding SelectedChild?.HiddenCount}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("(global::System.Nullable<int>)__AXSG_UnsafeAccessor_", generated);
        Assert.Contains("default(global::System.Nullable<int>)", generated);
    }

    [Fact]
    public void Preserves_Authored_NullConditional_Boundary_After_NonPublic_Helper_Access()
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
                public sealed class HiddenContext
                {
                    public string Title { get; set; } = string.Empty;
                }

                public sealed class ChildVm
                {
                    private HiddenContext HiddenData { get; } = new();
                }

                public sealed class MainVm
                {
                    public ChildVm? SelectedChild { get; } = new ChildVm();
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
                <TextBlock Text="{CompiledBinding SelectedChild?.HiddenData.Title}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Matches(
            @"source\.SelectedChild is \{ \} (__axsg_target_[0-9a-f]+) \? __AXSG_UnsafeAccessor_[0-9a-f]+\(\1\)\.Title : null",
            generated);
    }

    [Fact]
    public void Preserves_Explicit_NullConditional_After_NonPublic_Helper_Access()
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
                public sealed class HiddenContext
                {
                    public string Title { get; set; } = string.Empty;
                }

                public sealed class ChildVm
                {
                    private HiddenContext? HiddenData { get; } = new();
                }

                public sealed class MainVm
                {
                    public ChildVm? SelectedChild { get; } = new ChildVm();
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
                <TextBlock Text="{CompiledBinding SelectedChild?.HiddenData?.Title}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Matches(
            @"source\.SelectedChild is \{ \} (__axsg_target_[0-9a-f]+) \? __AXSG_UnsafeAccessor_[0-9a-f]+\(\1\)\?\.Title : null",
            generated);
    }

    [Fact]
    public void Allows_NonPublic_Compiled_Binding_Methods_With_XamlX_Parity()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

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

                public class TextBlock : Control
                {
                    public object? Text { get; set; }
                }

                public class Button : Control
                {
                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public abstract class StepViewModelBase
                {
                    protected string BuildTitle() => "ready";

                    protected void Save()
                    {
                    }

                    protected bool CanSave(object? parameter) => parameter is not null;
                }

                public sealed class StepViewModel : StepViewModelBase
                {
                    private string FormatTitle(int count, string suffix) => count.ToString() + suffix;

                    internal string InternalLabel() => "internal";
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
                         x:DataType="vm:StepViewModel"
                         x:CompileBindings="True">
                <StackPanel>
                    <TextBlock Text="{CompiledBinding BuildTitle}" />
                    <TextBlock Text="{CompiledBinding FormatTitle(2, 'x')}" />
                    <TextBlock Text="{CompiledBinding InternalLabel}" />
                    <Button Command="{CompiledBinding Save}" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0122");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("Name = \"BuildTitle\"", generated);
        Assert.Contains("Name = \"FormatTitle\"", generated);
        Assert.Contains("Name = \"Save\"", generated);
        Assert.Contains("Name = \"CanSave\"", generated);
        Assert.DoesNotContain("Name = \"InternalLabel\"", generated, StringComparison.Ordinal);
        Assert.Contains("source.InternalLabel()", generated);
        Assert.Contains("SourceGenMethodCommandRuntime.Create((object?)(source)", generated);
    }

    [Fact]
    public void Prefers_Accessible_Base_Method_Command_Over_Derived_NonPublic_Candidates()
    {
        const string code = """
            namespace System.Windows.Input
            {
                public interface ICommand { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public global::System.Windows.Input.ICommand? Command { get; set; }
                }
            }

            namespace Demo.ViewModels
            {
                public class CommandBase
                {
                    public void Save()
                    {
                    }

                    public bool CanSave(object? parameter) => parameter is not null;
                }

                public sealed class MainVm : CommandBase
                {
                    private new void Save()
                    {
                    }

                    private void Save(object? parameter)
                    {
                    }

                    private new bool CanSave(object? parameter) => false;
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
                <Button Command="{CompiledBinding Save}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0122");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("SourceGenMethodCommandRuntime.Create((object?)(source)", generated);
        Assert.Contains("((global::Demo.ViewModels.CommandBase)target).Save()", generated);
        Assert.Contains("((global::Demo.ViewModels.CommandBase)target).CanSave(parameter)", generated);
        Assert.DoesNotContain("Name = \"Save\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Name = \"CanSave\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_ControlTheme_Materializer_Registration_With_Factory_Method()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? Tag { get; set; }
                }
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
        Assert.Contains("static () => __BuildGeneratedControlTheme(0)", generated);
        Assert.Contains("private static global::Avalonia.Styling.ControlTheme __BuildGeneratedControlTheme(int __index)", generated);
        Assert.DoesNotContain("__BuildGeneratedControlTheme0()", generated);
        Assert.Contains("__theme.TargetType = typeof(global::Avalonia.Controls.Button);", generated);
        Assert.Contains(
            "__theme.Setters.Add(new global::Avalonia.Styling.Setter(global::Avalonia.Controls.Button.ContentProperty, \"Base\"));",
            generated);
    }

    [Fact]
    public void Emits_RuntimeXamlValue_For_ControlTheme_Setter_Fragment_Content()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty
                {
                    public static readonly object UnsetValue = new();
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object> { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public ResourceDictionary Resources { get; } = new();
                }

                public class ContentControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class Border : Control { }

                public class FancyControl : ContentControl { }
            }

            namespace Avalonia.Styling
            {
                public class ControlTheme
                {
                    public global::System.Type? TargetType { get; set; }
                    public global::System.Collections.Generic.List<Setter> Setters { get; } = new();
                }

                public class Setter
                {
                    public Setter() { }
                    public Setter(global::Avalonia.AvaloniaProperty property, object? value)
                    {
                        Property = property;
                        Value = value;
                    }

                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class Template
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
                    <ControlTheme x:Key="Theme.Fancy" TargetType="FancyControl">
                        <Setter Property="Content">
                            <Template>
                                <Border />
                            </Template>
                        </Setter>
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("ProvideRuntimeXamlValue(\"<Template", generated);
        Assert.DoesNotContain(
            "new global::Avalonia.Styling.Setter(global::Avalonia.Controls.ContentControl.ContentProperty, \"<Template",
            generated);
    }

    [Fact]
    public void Does_Not_Use_RuntimeXaml_Fallback_For_Malformed_Fragment_Literal()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty
                {
                    public static readonly object UnsetValue = new();
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object> { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public ResourceDictionary Resources { get; } = new();
                }

                public class ContentControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class Border : Control { }

                public class FancyControl : ContentControl { }
            }

            namespace Avalonia.Styling
            {
                public class ControlTheme
                {
                    public global::System.Type? TargetType { get; set; }
                    public global::System.Collections.Generic.List<Setter> Setters { get; } = new();
                }

                public class Setter
                {
                    public Setter() { }
                    public Setter(global::Avalonia.AvaloniaProperty property, object? value)
                    {
                        Property = property;
                        Value = value;
                    }

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
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Fancy" TargetType="FancyControl">
                        <Setter Property="Content" Value="&lt;Template&gt;&lt;Border /&gt;&lt;/Templte&gt;" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("ProvideRuntimeXamlValue(\"<Template", generated);
    }

    [Fact]
    public void Resolves_XType_TypeName_Named_Argument_For_ControlTheme_TargetType()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object> { }

                public class UserControl : Control
                {
                    public ResourceDictionary Resources { get; } = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class ControlTheme
                {
                    public global::System.Type? TargetType { get; set; }
                    public global::System.Collections.Generic.List<Setter> Setters { get; } = new();
                }

                public class Setter
                {
                    public Setter() { }
                    public object? Property { get; set; }
                    public object? Value { get; set; }
                }
            }

            namespace Demo
            {
                public class FancyControl : global::Avalonia.Controls.Control { }
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="using:Demo"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Fancy" TargetType="{x:Type TypeName=local:FancyControl}" />
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("__theme.TargetType = typeof(global::Demo.FancyControl);", generated);
    }

    [Fact]
    public void Emits_UnsetValue_Fallback_For_Unresolved_ControlTheme_Setter_Literal()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty
                {
                    public static readonly object UnsetValue = new();
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object> { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public ResourceDictionary Resources { get; } = new();
                }

                public class FancyControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty TransformProperty = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class ControlTheme
                {
                    public global::System.Type? TargetType { get; set; }
                    public global::System.Collections.Generic.List<Setter> Setters { get; } = new();
                }

                public class Setter
                {
                    public Setter() { }
                    public Setter(global::Avalonia.AvaloniaProperty property, object? value)
                    {
                        Property = property;
                        Value = value;
                    }

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
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Fancy" TargetType="FancyControl">
                        <Setter Property="Transform" Value="scale(1.2)" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "AXSG0102" &&
            diagnostic.GetMessage().Contains("Strategy=AvaloniaProperty.UnsetValueFallback.", StringComparison.Ordinal));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            "new global::Avalonia.Styling.Setter(global::Avalonia.Controls.FancyControl.TransformProperty, global::Avalonia.AvaloniaProperty.UnsetValue));",
            generated);
    }

    [Fact]
    public void Converts_ControlTheme_Setter_WindowTransparencyLevel_List_Value()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object> { }

                public class UserControl : Control
                {
                    public ResourceDictionary Resources { get; } = new();
                }

                public class WindowTransparencyLevel
                {
                    private WindowTransparencyLevel() { }
                    public static WindowTransparencyLevel None { get; } = new();
                    public static WindowTransparencyLevel Transparent { get; } = new();
                    public static WindowTransparencyLevel Blur { get; } = new();
                }

                public class TopLevel : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<global::System.Collections.Generic.IReadOnlyList<WindowTransparencyLevel>> TransparencyLevelHintProperty = new();
                }

                public class PopupRoot : TopLevel { }
            }

            namespace Avalonia.Styling
            {
                public class ControlTheme
                {
                    public global::System.Type? TargetType { get; set; }
                    public global::System.Collections.Generic.List<Setter> Setters { get; } = new();
                }

                public class Setter
                {
                    public Setter() { }
                    public Setter(global::Avalonia.AvaloniaProperty property, object? value)
                    {
                        Property = property;
                        Value = value;
                    }

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
                <UserControl.Resources>
                    <ControlTheme x:Key="PopupRootTheme" TargetType="PopupRoot">
                        <Setter Property="TransparencyLevelHint" Value="Transparent, Blur" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Id == "AXSG0102" &&
            diagnostic.GetMessage().Contains("TransparencyLevelHint", StringComparison.Ordinal));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            "new global::System.Collections.Generic.List<global::Avalonia.Controls.WindowTransparencyLevel> { global::Avalonia.Controls.WindowTransparencyLevel.Transparent, global::Avalonia.Controls.WindowTransparencyLevel.Blur }",
            generated);
        Assert.DoesNotContain("AvaloniaProperty.UnsetValue", generated);
    }

    [Fact]
    public void Uses_Compatibility_String_Fallback_For_Unresolved_Style_Setter_Value_Assignment_Without_Diagnostic()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty
                {
                    public static readonly object UnsetValue = new();
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class StyleBase
                {
                    public void Add(object value) { }
                }

                public class Styles : global::System.Collections.Generic.List<Style> { }

                public class Style : StyleBase
                {
                    public string? Selector { get; set; }
                }

                public class Setter
                {
                    public object? Property { get; set; }
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
                    <Style Selector="MissingControl">
                        <Setter Property="Transform" Value="scale(1.2)" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("Value = \"scale(1.2)\";", generated);
    }

    [Fact]
    public void Emits_Strict_Strategy_Diagnostic_For_Unresolved_Style_Setter_Value_Assignment()
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
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class StyleBase
                {
                    public void Add(object value) { }
                }

                public class Styles : global::System.Collections.Generic.List<Style> { }

                public class Style : StyleBase
                {
                    public string? Selector { get; set; }
                }

                public class Setter
                {
                    public object? Property { get; set; }
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
                    <Style Selector="MissingControl">
                        <Setter Property="Transform" Value="scale(1.2)" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "AXSG0102" &&
            diagnostic.GetMessage().Contains("Strategy=StrictError", StringComparison.Ordinal));
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
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

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
            [("FolderA/Main.axaml", xamlA, "Main.axaml"), ("FolderB/Main.axaml", xamlB, "Main.axaml")]);

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
        Assert.Equal(compilation.SyntaxTrees.Count() + 2, updatedCompilation.SyntaxTrees.Count());
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
        Assert.Equal(compilation.SyntaxTrees.Count() + 2, updatedCompilation.SyntaxTrees.Count());
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
        Assert.Equal(compilation.SyntaxTrees.Count() + 1, updatedCompilation.SyntaxTrees.Count());
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
    public void Does_Not_Report_Cycle_For_ControlTheme_Self_Key_BasedOn_Override_Pattern()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Resources { get; set; }
                }
                public class DataGrid : Control { }
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
                    <ControlTheme x:Key="{x:Type DataGrid}"
                                  TargetType="DataGrid"
                                  BasedOn="{StaticResource {x:Type DataGrid}}">
                        <Setter Property="Tag" Value="Override" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0306");
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
        Assert.Contains("// AXSG:XAML", generated);
        Assert.Contains("#line ", generated);
        Assert.Contains("\"MainView.axaml\"", generated);
    }

    [Fact]
    public void Does_Not_Emit_Debug_Line_Directives_When_Source_Info_Disabled()
    {
        const string code = "namespace Demo; public partial class MainView {}";
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Content="Run" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("// AXSG:XAML", generated);
        Assert.DoesNotContain("#line ", generated);
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

                public class TextBlock : Control
                {
                    public string? Text { get; set; }
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
    public void Reports_Diagnostic_For_DataTemplate_Without_DataType_In_Strict_Mode()
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
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0500");
    }

    [Fact]
    public void Does_Not_Report_DataTemplate_DataType_Diagnostic_By_Default()
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

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0500");
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
    public void Generates_EventBinding_Command_Wrapper_And_Subscription()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public global::System.Windows.Input.ICommand SaveCommand { get; set; } = null!;
                    public object? SelectedItem { get; set; }
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
                <Button Click="{EventBinding Command=SaveCommand, Parameter={Binding SelectedItem}}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(".Click += __root.__AXSG_EventBinding_", generated);
        Assert.Contains("private void __AXSG_EventBinding_", generated);
        Assert.Contains("__TryGetEventBindingDataContext(__arg0)", generated);
        Assert.Contains("__axsgDataContext is global::Demo.ViewModels.MainViewModel __axsgDataContextTyped", generated);
        Assert.DoesNotContain("SourceGenEventBindingRuntime.InvokeCommand(", generated);
        Assert.Contains("__axsgDataContextTyped.SaveCommand", generated);
        Assert.Contains("__axsgDataContextTyped.SelectedItem", generated);
    }

    [Fact]
    public void Emits_Reflection_Free_Command_EventBinding_Handler()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public global::System.Windows.Input.ICommand SaveCommand { get; set; } = null!;
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
                <Button Click="{EventBinding Command=SaveCommand}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("SourceGenEventBindingRuntime.InvokeCommand(", generated);
        Assert.DoesNotContain("System.Reflection", generated);
        Assert.DoesNotContain("BindingFlags", generated);
        Assert.DoesNotContain("GetProperty(", generated);
        Assert.DoesNotContain("GetField(", generated);
        Assert.DoesNotContain("GetMethod(", generated);
        Assert.DoesNotContain("MethodInfo", generated);
    }

    [Fact]
    public void Generates_EventBinding_Method_Wrapper_And_Subscription()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public void SaveWithArgs(object? sender, object? args) { }
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
                <Button Click="{EventBinding Method=SaveWithArgs, PassEventArgs=True}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(".Click += __root.__AXSG_EventBinding_", generated);
        Assert.Contains("private void __AXSG_EventBinding_", generated);
        Assert.DoesNotContain("SourceGenEventBindingRuntime.InvokeMethod(", generated);
        Assert.Contains("__axsgDataContextTyped.SaveWithArgs(", generated);
        Assert.Contains("__arg0", generated);
        Assert.Contains("__arg1", generated);
    }

    [Fact]
    public void Generates_Inline_Event_Lambda_Wrapper_And_Subscription()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
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
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <Button Click="{(s, e) => Count++}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(".Click += __root.__AXSG_EventBinding_", generated);
        Assert.Contains("private void __AXSG_EventBinding_", generated);
        Assert.Contains("global::Demo.ViewModels.MainViewModel source = __axsgDataContextTyped;", generated);
        Assert.Contains(" s = ", generated, StringComparison.Ordinal);
        Assert.Contains("source.Count++;", generated);
        Assert.DoesNotContain("__axsgHandler =", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_Event_Code_Does_Not_Report_XClass_Diagnostic_When_Class_Is_Declared_But_Not_Resolved()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public int Count { get; set; }
                }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <Button>
                    <Button.Click>
                        <axsg:CSharp xmlns:axsg="using:XamlToCSharpGenerator.Runtime"><![CDATA[
                            source.Count++;
                        ]]></axsg:CSharp>
                    </Button.Click>
                </Button>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Id == "AXSG0600" &&
            diagnostic.GetMessage().Contains("requires x:Class-backed root type", StringComparison.Ordinal));

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("partial class MainView", generated, StringComparison.Ordinal);
        Assert.Contains("private void __AXSG_EventBinding_", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_Diagnostic_For_Async_Inline_Event_Lambda()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control
                {
                    public object? DataContext { get; set; }
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public class MainViewModel
                {
                    public global::System.Threading.Tasks.Task SaveAsync() => global::System.Threading.Tasks.Task.CompletedTask;
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
                <Button Click="{async (s, e) => SaveAsync()}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "AXSG0600" &&
            diagnostic.GetMessage().Contains("does not support async lambdas", StringComparison.Ordinal));

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain(".Click += __root.__AXSG_EventBinding_", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_Inline_Event_Lambda_Handler_Name_Stable_When_Line_Numbers_Shift()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public object? DataContext { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class MainViewModel
                {
                    public int Count { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string originalXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <Button Click="{(s, e) => Count++}" />
            </UserControl>
            """;

        const string shiftedXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <TextBlock Text="Spacer" />
                <Button Click="{(s, e) => Count++}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (originalCompilation, originalDiagnostics) = RunGenerator(compilation, originalXaml);
        var (shiftedCompilation, shiftedDiagnostics) = RunGenerator(compilation, shiftedXaml);

        Assert.DoesNotContain(originalDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(shiftedDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var originalGenerated = GetGeneratedPartialClassSource(originalCompilation, "MainView");
        var shiftedGenerated = GetGeneratedPartialClassSource(shiftedCompilation, "MainView");

        var originalMethodName = ExtractGeneratedEventBindingMethodName(originalGenerated);
        var shiftedMethodName = ExtractGeneratedEventBindingMethodName(shiftedGenerated);
        Assert.False(string.IsNullOrWhiteSpace(originalMethodName));
        Assert.Equal(originalMethodName, shiftedMethodName);
        Assert.DoesNotContain("__AXSG_EventBinding_Click_", shiftedGenerated, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_Inline_Event_Lambda_Handler_Name_Stable_When_New_Event_Binding_Is_Inserted_Before_It()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public object? DataContext { get; set; }
                }

                public class Button : Control
                {
                    public event global::System.EventHandler? Click;
                }
            }

            namespace Demo.ViewModels
            {
                public sealed class MainViewModel
                {
                    public int Count { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string originalXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <Button Click="{(s, e) => Count++}" />
            </UserControl>
            """;

        const string shiftedXaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="clr-namespace:Demo.ViewModels"
                         x:Class="Demo.MainView"
                         x:DataType="vm:MainViewModel"
                         x:CompileBindings="True">
                <Button Click="{(s, e) => Count = 0}" />
                <Button Click="{(s, e) => Count++}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (originalCompilation, originalDiagnostics) = RunGenerator(compilation, originalXaml);
        var (shiftedCompilation, shiftedDiagnostics) = RunGenerator(compilation, shiftedXaml);

        Assert.DoesNotContain(originalDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(shiftedDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var originalGenerated = GetGeneratedPartialClassSource(originalCompilation, "MainView");
        var shiftedGenerated = GetGeneratedPartialClassSource(shiftedCompilation, "MainView");

        var originalMethodName = ExtractGeneratedEventBindingMethodName(originalGenerated);
        Assert.Contains($"private void {originalMethodName}(", shiftedGenerated);
        Assert.DoesNotContain("__AXSG_EventBinding_Click_", shiftedGenerated, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_Diagnostic_For_EventBinding_With_Both_Command_And_Method()
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
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.MainView">
                <Button Click="{EventBinding Command=SaveCommand, Method=Save}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0600");
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("__n0.SetValue(global::Avalonia.Controls.TextBlock.TextProperty", generated);
        Assert.Contains("\"Hello\"", generated);
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("__n0.SetValue(global::Avalonia.Controls.Grid.RowProperty", generated);
        Assert.Contains("1", generated);
    }

    [Fact]
    public void Reports_Diagnostic_For_ControlTemplate_Without_TargetType_In_Strict_Mode()
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
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalBuildOptions:
            [
                new KeyValuePair<string, string>("build_property.AvaloniaSourceGenStrictMode", "true")
            ]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0501");
    }

    [Fact]
    public void Does_Not_Report_ControlTemplate_TargetType_Diagnostic_By_Default()
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

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0501");
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
    public void Reports_Diagnostic_For_Parameterless_Void_Method_Segment()
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
                    public void ResolveTitle()
                    {
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
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0111" &&
                          diagnostic.GetMessage().Contains(
                              "not a supported parameterless method with a return value",
                              StringComparison.Ordinal));
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
    public void Prefers_Accessible_Method_Overload_Over_NonPublic_UnsafeAccessor_Candidate()
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
                    private string FormatTitle(string value)
                    {
                        return "private:" + value;
                    }

                    public string FormatTitle(object value)
                    {
                        return "public:" + value;
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
                <TextBlock Text="{CompiledBinding FormatTitle('demo')}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("FormatTitle(", generated);
        Assert.DoesNotContain("Name = \"FormatTitle\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Falls_Back_To_NonPublic_Method_Overload_When_Accessible_Overloads_Are_Incompatible()
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
                    public string FormatTitle(int value)
                    {
                        return "public:" + value.ToString();
                    }

                    private string FormatTitle(string value)
                    {
                        return "private:" + value;
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
                <TextBlock Text="{CompiledBinding FormatTitle('demo')}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("Name = \"FormatTitle\"", generated, StringComparison.Ordinal);
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
        Assert.Contains("private static object? __ResolveStaticResource(object? anchor, object key)", generated);
        Assert.DoesNotContain("__AXSG_CTX_", generated);
    }

    [Fact]
    public void Emits_Uncast_StaticResource_Call_For_AvaloniaProperty_Assignment()
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
                public class UserControl : global::Avalonia.AvaloniaObject
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : global::Avalonia.AvaloniaObject
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
                <TextBlock Text="{StaticResource Marker}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(
            ".SetValue(global::Avalonia.Controls.TextBlock.TextProperty",
            generated);
        Assert.Contains(
            "ProvideStaticResource(\"Marker\"",
            generated);
        Assert.DoesNotContain(
            ".SetValue(global::Avalonia.Controls.TextBlock.TextProperty, (string)global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideStaticResource(\"Marker\"",
            generated);
    }

    [Fact]
    public void Casts_StaticResource_In_Binding_Initializer_For_Typed_Properties()
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

            namespace Avalonia.Data.Converters
            {
                public interface IValueConverter { }
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
                    public global::Avalonia.Data.Converters.IValueConverter? Converter { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class UserControl : global::Avalonia.AvaloniaObject
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : global::Avalonia.AvaloniaObject
                {
                    public static readonly global::Avalonia.AvaloniaProperty TextProperty = new();
                }
            }

            namespace Demo
            {
                public sealed class DemoConverter : global::Avalonia.Data.Converters.IValueConverter { }
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView">
                <UserControl.Resources>
                    <local:DemoConverter x:Key="Conv" />
                </UserControl.Resources>
                <TextBlock Text="{Binding Path=Name, Converter={StaticResource Conv}}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            "Converter = global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceStaticResourceValue<global::Avalonia.Data.Converters.IValueConverter>(",
            generated);
        Assert.Contains("ProvideStaticResource(\"Conv\"", generated);
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
        Assert.Contains("SourceGenMarkupExtensionRuntime.ApplyBinding(", generated);
        Assert.Contains("global::Avalonia.Controls.TextBlock.ForegroundProperty", generated);
        Assert.Contains("global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.AttachBindingNameScope(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideDynamicResource(\"AccentBrush\"", generated);
        Assert.DoesNotContain("__AXSG_CTX_", generated);
    }

    [Fact]
    public void Resolves_Setter_Property_From_Style_Target_Type()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaProperty<T> : AvaloniaProperty { }
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
                    public static readonly global::Avalonia.AvaloniaProperty<string> TextProperty = new();
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("global::Avalonia.Controls.TextBlock.TextProperty", generated);
        Assert.True(
            generated.Contains(".Add(__n1);", StringComparison.Ordinal) ||
            generated.Contains("__TryAddToCollection(", StringComparison.Ordinal) ||
            generated.Contains("new global::Avalonia.Styling.Setter(", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolves_Transition_Property_Token_Using_Transition_Owner_Context()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public object? this[global::Avalonia.Data.IndexerDescriptor descriptor] { set { } }
                }
            }

            namespace Avalonia.Data
            {
                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class Panel : Control
                {
                    public static readonly global::Avalonia.StyledProperty<object?> RenderTransformProperty = new();
                    public global::Avalonia.Animation.Transitions? Transitions { get; set; }
                }
            }

            namespace Avalonia.Animation
            {
                public class TransitionBase { }

                public class Transitions : global::System.Collections.Generic.List<global::Avalonia.Animation.TransitionBase> { }

                public class TransformOperationsTransition : TransitionBase
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public string? Duration { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.Panel { }
            }
            """;

        const string xaml = """
            <Panel xmlns="https://github.com/avaloniaui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   x:Class="Demo.MainView">
                <Panel.Transitions>
                    <Transitions>
                        <TransformOperationsTransition Property="RenderTransform"
                                                       Duration="0:0:0.1" />
                    </Transitions>
                </Panel.Transitions>
            </Panel>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("global::Avalonia.Controls.Panel.RenderTransformProperty", generated);
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
        Assert.Contains("__n1.Property =", generated);
        Assert.Contains("global::Avalonia.Controls.TextBlock.FontSizeProperty", generated);
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.True(
            generated.Contains("__AXSGObjectGraph.TryClearCollection(__n0.Classes);", StringComparison.Ordinal) ||
            generated.Contains("ApplyClassValue(", StringComparison.Ordinal));
        Assert.True(
            generated.Contains("__n0.Classes.Add(", StringComparison.Ordinal) ||
            generated.Contains("Classes)).Add(", StringComparison.Ordinal) ||
            generated.Contains("__TryAddToCollection(__n0.Classes", StringComparison.Ordinal) ||
            (generated.Contains("__TryAddToCollection(", StringComparison.Ordinal) &&
             generated.Contains("Classes", StringComparison.Ordinal)) ||
            generated.Contains("ApplyClassValue(", StringComparison.Ordinal));
        Assert.True(
            generated.Contains("var __n1 = \"highlight\";", StringComparison.Ordinal) ||
            generated.Contains("\"highlight\"", StringComparison.Ordinal));
        Assert.True(
            generated.Contains("var __n2 = \"warning\";", StringComparison.Ordinal) ||
            generated.Contains("\"warning\"", StringComparison.Ordinal));
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
    public void Reports_Diagnostic_For_Selector_Attached_Property_Predicate_With_Invalid_Typed_Value()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaProperty<T> : AvaloniaProperty { }
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
                    public static readonly global::Avalonia.AvaloniaProperty<int> RowProperty = new();
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
                         Match="TextBlock[(ac|Grid.Row)=notAnInt]" />
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
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
    public void Converts_Nested_Selector_With_Template_Axis_Using_Parent_Target_Type()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaProperty<T> : AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public enum Dock
                {
                    Left,
                    Top,
                    Right,
                    Bottom
                }

                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }

                public class TabControl : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<Dock> TabStripPlacementProperty = new();
                }

                public class ItemsPresenter : Control { }
                public class WrapPanel : Control { }
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
                    public static Selector Name(Selector? previous, string name) => new();
                    public static Selector Template(Selector previous) => new();
                    public static Selector Child(Selector previous) => new();
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
                    <Style Selector="TabControl">
                        <Style Selector="^[TabStripPlacement=Left] /template/ ItemsPresenter#PART_ItemsPresenter > WrapPanel" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Controls.TabControl.TabStripPlacementProperty", generated);
        Assert.Contains("global::Avalonia.Controls.Dock.Left", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Nesting(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Template(", generated);
        Assert.Contains("global::Avalonia.Styling.Selectors.Child(", generated);
    }

    [Fact]
    public void Suppresses_ControlTheme_BasedOn_Diagnostic_When_Key_Exists_In_Another_Xaml_File()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Resources { get; set; }
                }

                public class MenuItem : Control
                {
                    public string? Header { get; set; }
                }
            }

            namespace Demo
            {
                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string baseThemeXaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ControlTheme x:Key="FluentTopLevelMenuItem" TargetType="MenuItem">
                    <Setter Property="Header" Value="Base" />
                </ControlTheme>
            </ResourceDictionary>
            """;

        const string derivedThemeXaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ControlTheme x:Key="HorizontalMenuItem"
                              TargetType="MenuItem"
                              BasedOn="{StaticResource FluentTopLevelMenuItem}">
                    <Setter Property="Header" Value="Horizontal" />
                </ControlTheme>
            </ResourceDictionary>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            [
                ("Themes/Menu.xaml", baseThemeXaml),
                ("Themes/MenuItem.xaml", derivedThemeXaml)
            ]);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0305" &&
                          diagnostic.GetMessage().Contains("FluentTopLevelMenuItem", StringComparison.Ordinal));
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(".Content = (global::System.Func<global::System.IServiceProvider?, object?>)", generated);
        Assert.Contains("SourceGenDeferredServiceProviderFactory.CreateTemplateNameScope(__templateServiceProvider);", generated);
        Assert.Contains("SourceGenDeferredServiceProviderFactory.CreateDeferredTemplateServiceProvider(__templateServiceProvider, __root, __templateScope", generated);
        Assert.Contains("TemplateResult<global::Avalonia.Controls.Control>", generated);
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
        Assert.True(
            generated.Contains(".Value =", StringComparison.Ordinal) ||
            (generated.Contains("__TrySetClrProperty(", StringComparison.Ordinal) &&
             generated.Contains("\"Value\"", StringComparison.Ordinal)));
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("SetValue(global::Avalonia.Controls.Border.TagProperty", generated);
        Assert.Contains("BindingPriority.Template", generated);
        Assert.Contains("\"Hello\"", generated);
    }

    [Fact]
    public void Resolves_TemplateBinding_Inside_Standalone_ControlTemplate_With_TargetType()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value, global::Avalonia.Data.BindingPriority priority) { }
                    public object? this[global::Avalonia.Data.IndexerDescriptor descriptor] { set { } }
                }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?>
                {
                }
            }

            namespace Avalonia.Data
            {
                public enum BindingPriority
                {
                    LocalValue,
                    Template
                }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class TemplateBinding
                {
                    public TemplateBinding(global::Avalonia.AvaloniaProperty property) { }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope
                {
                    bool IsCompleted { get; }
                    void Complete();
                }

                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                    public bool IsCompleted => false;
                    public void Complete() { }
                }

                public class StyledElement : global::Avalonia.AvaloniaObject { }

                public class Control : StyledElement { }

                public class ContentControl : Control
                {
                    public static readonly global::Avalonia.StyledProperty<object?> ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class UserControl : ContentControl
                {
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TextProperty = new();
                    public string? Text { get; set; }
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
                public class TemplatedInfoControl : global::Avalonia.Controls.ContentControl
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TitleProperty = new();
                    public string? Title { get; set; }
                }

                public partial class TemplatesView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.TemplatesView">
                <UserControl.Resources>
                    <ControlTemplate x:Key="InfoTemplate" TargetType="{x:Type local:TemplatedInfoControl}">
                        <StackPanel>
                            <TextBlock Text="{TemplateBinding Title}" />
                            <TextBlock Text="{TemplateBinding Content}" />
                        </StackPanel>
                    </ControlTemplate>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.TemplateBinding(global::Demo.TemplatedInfoControl.TitleProperty)", generated);
        Assert.Contains("new global::Avalonia.Data.TemplateBinding(global::Avalonia.Controls.ContentControl.ContentProperty)", generated);
        Assert.DoesNotContain("SetValue(global::Avalonia.Controls.TextBlock.TextProperty, \"{TemplateBinding Title}\"", generated);
        Assert.DoesNotContain("SetValue(global::Avalonia.Controls.TextBlock.TextProperty, \"{TemplateBinding Content}\"", generated);
    }

    [Fact]
    public void Resolves_TemplateBinding_Named_Arguments_Mode_And_Converter()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value, global::Avalonia.Data.BindingPriority priority) { }
                    public object? this[global::Avalonia.Data.IndexerDescriptor descriptor] { set { } }
                }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?>
                {
                }
            }

            namespace Avalonia.Data
            {
                public enum BindingPriority
                {
                    LocalValue,
                    Template
                }

                public enum BindingMode
                {
                    Default,
                    OneWay,
                    TwoWay
                }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class TemplateBinding
                {
                    public TemplateBinding(global::Avalonia.AvaloniaProperty property) { }
                    public BindingMode Mode { get; set; }
                    public object? Converter { get; set; }
                    public object? ConverterParameter { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope
                {
                    bool IsCompleted { get; }
                    void Complete();
                }

                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                    public bool IsCompleted => false;
                    public void Complete() { }
                }

                public class StyledElement : global::Avalonia.AvaloniaObject { }

                public class Control : StyledElement { }

                public class ContentControl : Control
                {
                    public static readonly global::Avalonia.StyledProperty<object?> ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class UserControl : ContentControl
                {
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TextProperty = new();
                    public string? Text { get; set; }
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
                public static class TemplateConverters
                {
                    public static object Identity => new object();
                }

                public class TemplatedInfoControl : global::Avalonia.Controls.ContentControl
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TitleProperty = new();
                    public string? Title { get; set; }
                }

                public partial class TemplatesView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.TemplatesView">
                <UserControl.Resources>
                    <ControlTemplate x:Key="InfoTemplate" TargetType="{x:Type local:TemplatedInfoControl}">
                        <StackPanel>
                            <TextBlock Text="{TemplateBinding Title, Mode=TwoWay, Converter={x:Static local:TemplateConverters.Identity}, ConverterParameter='marker'}" />
                        </StackPanel>
                    </ControlTemplate>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(
            "new global::Avalonia.Data.TemplateBinding(global::Demo.TemplatedInfoControl.TitleProperty) { Mode = global::Avalonia.Data.BindingMode.TwoWay, Converter = global::Demo.TemplateConverters.Identity, ConverterParameter = \"marker\" }",
            generated);
    }

    [Fact]
    public void Resolves_TemplateBinding_Inside_Derived_ControlTemplate_Type()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value, global::Avalonia.Data.BindingPriority priority) { }
                    public object? this[global::Avalonia.Data.IndexerDescriptor descriptor] { set { } }
                }

                public class ResourceDictionary : global::System.Collections.Generic.Dictionary<object, object?>
                {
                }
            }

            namespace Avalonia.Data
            {
                public enum BindingPriority
                {
                    LocalValue,
                    Template
                }

                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }

                public class TemplateBinding
                {
                    public TemplateBinding(global::Avalonia.AvaloniaProperty property) { }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope
                {
                    bool IsCompleted { get; }
                    void Complete();
                }

                public class NameScope : INameScope
                {
                    public static void SetNameScope(global::Avalonia.StyledElement styled, INameScope scope) { }
                    public void Register(string name, object element) { }
                    public bool IsCompleted => false;
                    public void Complete() { }
                }

                public class StyledElement : global::Avalonia.AvaloniaObject { }

                public class Control : StyledElement { }

                public class ContentControl : Control
                {
                    public static readonly global::Avalonia.StyledProperty<object?> ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class UserControl : ContentControl
                {
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class TextBlock : Control
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TextProperty = new();
                    public string? Text { get; set; }
                }
            }

            namespace Avalonia.Controls.Templates
            {
                public class TemplateResult<T>
                {
                    public TemplateResult(T result, global::Avalonia.Controls.INameScope scope) { }
                }

                public interface IControlTemplate { }
            }

            namespace Avalonia.Markup.Xaml.Templates
            {
                public class ControlTemplate : global::Avalonia.Controls.Templates.IControlTemplate
                {
                    public object? Content { get; set; }
                    public global::System.Type? TargetType { get; set; }
                }
            }

            namespace Demo
            {
                public sealed class FancyTemplateRoot : global::Avalonia.Controls.ContentControl
                {
                    public static readonly global::Avalonia.StyledProperty<string?> TitleProperty = new();
                    public string? Title { get; set; }
                }

                public sealed class FancyDerivedTemplate : global::Avalonia.Markup.Xaml.Templates.ControlTemplate
                {
                }

                public partial class TemplatesView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.TemplatesView">
                <UserControl.Resources>
                    <local:FancyDerivedTemplate x:Key="FancyTemplate" TargetType="{x:Type local:FancyTemplateRoot}">
                        <StackPanel>
                            <TextBlock Text="{TemplateBinding Title}" />
                            <TextBlock Text="{TemplateBinding Content}" />
                        </StackPanel>
                    </local:FancyDerivedTemplate>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Data.TemplateBinding(global::Demo.FancyTemplateRoot.TitleProperty)", generated);
        Assert.Contains("new global::Avalonia.Data.TemplateBinding(global::Avalonia.Controls.ContentControl.ContentProperty)", generated);
        Assert.DoesNotContain("SetValue(global::Avalonia.Controls.TextBlock.TextProperty, \"{TemplateBinding Title}\"", generated);
        Assert.DoesNotContain("SetValue(global::Avalonia.Controls.TextBlock.TextProperty, \"{TemplateBinding Content}\"", generated);
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
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("SetValue(global::Avalonia.Controls.Border.TagProperty", generated);
        Assert.Contains("\"Hello\"", generated);
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
        Assert.Contains("__AXSGObjectGraph.TrySetNameScope", generated);
        Assert.Contains("__nameScope.Register(\"ActionButton\"", generated);
    }

    [Fact]
    public void Does_Not_Register_NameScope_For_Markup_Extension_Name_Value()
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
                <Button x:Name="{Binding BadName}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("__nameScope.Register(\"{Binding BadName}\"", generated);
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
                    public global::System.DateTime DateValue { get; set; }
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
                                DateValue="{x:DateTime 2026-03-01T10:15:30Z}"
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
        Assert.Contains("global::System.DateTime.FromBinary(", generated);
        Assert.DoesNotContain("global::System.DateTime.Parse(", generated);
        Assert.Contains("global::System.TimeSpan.FromTicks(900000000L)", generated);
        Assert.Contains("new global::System.Uri(\"https://example.com\", global::System.UriKind.RelativeOrAbsolute)", generated);
        Assert.Contains("'X'", generated);
    }

    [Fact]
    public void Emits_Font_Literals_Without_Runtime_Font_Parse_Helpers()
    {
        const string code = """
            namespace Avalonia.Media
            {
                public class FontFeature
                {
                    public static FontFeature Parse(string value) => new FontFeature();
                }

                public class FontFeatureCollection : global::System.Collections.Generic.List<FontFeature>
                {
                }

                public class FontFamily
                {
                    public static FontFamily Parse(string value) => new FontFamily();
                    public static FontFamily Parse(string value, global::System.Uri baseUri) => new FontFamily();
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
                    public global::Avalonia.Media.FontFeatureCollection? FontFeatures { get; set; }
                    public global::Avalonia.Media.FontFamily? FontFamily { get; set; }
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
                <TextBlock FontFeatures="liga 1, ,  kern 0 ,   "
                           FontFamily="Inter, Segoe UI" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Avalonia.Media.FontFeatureCollection {", generated);
        Assert.Contains("global::Avalonia.Media.FontFeature.Parse(\"liga 1\")", generated);
        Assert.Contains("global::Avalonia.Media.FontFeature.Parse(\"kern 0\")", generated);
        Assert.DoesNotContain("global::Avalonia.Media.FontFeature.Parse(\"\")", generated);
        Assert.DoesNotContain("SourceGenMarkupExtensionRuntime.ParseFontFeatureCollection(", generated);
        Assert.DoesNotContain("SourceGenMarkupExtensionRuntime.ParseFontFamily(", generated);
    }

    [Fact]
    public void Emits_Deterministic_SolidColorBrush_For_Color_Literals_With_Parse_Fallback_Preserved()
    {
        const string code = """
            namespace Avalonia.Media
            {
                public interface IBrush
                {
                }

                public class Brush : IBrush
                {
                    public static Brush Parse(string value) => new Brush();
                }

                public struct Color
                {
                    public static Color FromUInt32(uint value) => default;
                }

                public static class Colors
                {
                    public static Color Red => default;
                }

                public sealed class SolidColorBrush : Brush
                {
                    public SolidColorBrush(Color color)
                    {
                    }

                    public static SolidColorBrush Parse(string value) => new SolidColorBrush(default);
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ProbeControl : Control
                {
                    public global::Avalonia.Media.IBrush? HexBrush { get; set; }
                    public global::Avalonia.Media.IBrush? NamedBrush { get; set; }
                    public global::Avalonia.Media.IBrush? FallbackBrush { get; set; }
                    public global::Avalonia.Media.Brush? BrushHex { get; set; }
                    public global::Avalonia.Media.SolidColorBrush? SolidHex { get; set; }
                    public global::Avalonia.Media.SolidColorBrush? SolidFallback { get; set; }
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
                <ProbeControl HexBrush="#FF010203"
                              NamedBrush="Red"
                              FallbackBrush="rgba(1,2,3,1)"
                              BrushHex="#FF102030"
                              SolidHex="#FF506070"
                              SolidFallback="rgba(2,3,4,1)" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains(
            "new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromUInt32(0xFF010203u))",
            generated);
        Assert.Contains(
            "new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Colors.Red)",
            generated);
        Assert.Contains("global::Avalonia.Media.Brush.Parse(\"rgba(1,2,3,1)\")", generated);
        Assert.Contains(
            "new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromUInt32(0xFF102030u))",
            generated);
        Assert.Contains(
            "new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromUInt32(0xFF506070u))",
            generated);
        Assert.Contains("global::Avalonia.Media.SolidColorBrush.Parse(\"rgba(2,3,4,1)\")", generated);
    }

    [Fact]
    public void Emits_Deterministic_TransformOperations_For_Canonical_Function_Forms_With_Parse_Fallback_Preserved()
    {
        const string code = """
            namespace Avalonia
            {
                public struct Matrix
                {
                    public Matrix(
                        double m11,
                        double m12,
                        double m21,
                        double m22,
                        double m31,
                        double m32)
                    {
                    }
                }
            }

            namespace Avalonia.Media.Transformation
            {
                public sealed class TransformOperations
                {
                    public static TransformOperations Identity => new TransformOperations();
                    public static TransformOperations Parse(string value) => new TransformOperations();
                    public static Builder CreateBuilder(int capacity) => new Builder();

                    public struct Builder
                    {
                        public void AppendTranslate(double x, double y)
                        {
                        }

                        public void AppendScale(double x, double y)
                        {
                        }

                        public void AppendSkew(double x, double y)
                        {
                        }

                        public void AppendRotate(double angle)
                        {
                        }

                        public void AppendMatrix(global::Avalonia.Matrix matrix)
                        {
                        }

                        public TransformOperations Build() => new TransformOperations();
                    }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ProbeControl : Control
                {
                    public global::Avalonia.Media.Transformation.TransformOperations? IdentityTransform { get; set; }
                    public global::Avalonia.Media.Transformation.TransformOperations? CompositeTransform { get; set; }
                    public global::Avalonia.Media.Transformation.TransformOperations? MatrixTransform { get; set; }
                    public global::Avalonia.Media.Transformation.TransformOperations? ScaleXTransform { get; set; }
                    public global::Avalonia.Media.Transformation.TransformOperations? ScaleYTransform { get; set; }
                    public global::Avalonia.Media.Transformation.TransformOperations? FallbackTransform { get; set; }
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
                <ProbeControl IdentityTransform="none"
                              CompositeTransform="translate(10px,20px) rotate(90deg) scale(2)"
                              MatrixTransform="matrix(1,0,0,1,10,20)"
                              ScaleXTransform="scaleX(2)"
                              ScaleYTransform="scaleY(3)"
                              FallbackTransform="translate(10,20,30)" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains(
            "global::Avalonia.Media.Transformation.TransformOperations.Identity",
            generated);
        Assert.Contains(
            "global::Avalonia.Media.Transformation.TransformOperations.CreateBuilder(3)",
            generated);
        Assert.Contains("__builder.AppendTranslate(10d, 20d);", generated);
        Assert.Contains("__builder.AppendRotate(", generated);
        Assert.Contains("__builder.AppendScale(2d, 2d);", generated);
        Assert.Contains(
            "global::Avalonia.Media.Transformation.TransformOperations.CreateBuilder(1)",
            generated);
        Assert.Contains(
            "__builder.AppendMatrix(new global::Avalonia.Matrix(1d, 0d, 0d, 1d, 10d, 20d));",
            generated);
        Assert.Contains("__builder.AppendScale(2d, 1d);", generated);
        Assert.Contains("__builder.AppendScale(1d, 3d);", generated);
        Assert.Contains(
            "global::Avalonia.Media.Transformation.TransformOperations.Parse(\"translate(10,20,30)\")",
            generated);
    }

    [Fact]
    public void Emits_Deterministic_Cursor_For_StandardCursorType_Literals_With_Parse_Fallback_Preserved()
    {
        const string code = """
            namespace Avalonia.Input
            {
                public enum StandardCursorType
                {
                    Arrow,
                    Hand
                }

                public class Cursor
                {
                    public Cursor(StandardCursorType type)
                    {
                    }

                    public static Cursor Parse(string value) => new Cursor(StandardCursorType.Arrow);
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ProbeControl : Control
                {
                    public global::Avalonia.Input.Cursor? DeterministicCursor { get; set; }
                    public global::Avalonia.Input.Cursor? FallbackCursor { get; set; }
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
                <ProbeControl DeterministicCursor="StandardCursorType.Hand"
                              FallbackCursor="custom://cursor" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains(
            "new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand)",
            generated);
        Assert.Contains(
            "global::Avalonia.Input.Cursor.Parse(\"custom://cursor\")",
            generated);
    }

    [Fact]
    public void Emits_Deterministic_KeyGesture_Literals_With_Parse_Fallback_Preserved()
    {
        const string code = """
            namespace Avalonia.Input
            {
                public enum Key
                {
                    None = 0,
                    A = 1,
                    F10 = 2,
                    OemPlus = 3
                }

                [global::System.Flags]
                public enum KeyModifiers
                {
                    None = 0,
                    Shift = 1,
                    Control = 2,
                    Alt = 4,
                    Meta = 8
                }

                public class KeyGesture
                {
                    public KeyGesture(Key key, KeyModifiers modifiers)
                    {
                    }

                    public static KeyGesture Parse(string value) => new KeyGesture(Key.None, KeyModifiers.None);
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }
                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ProbeControl : Control
                {
                    public global::Avalonia.Input.KeyGesture? DeterministicGesture { get; set; }
                    public global::Avalonia.Input.KeyGesture? PlusGesture { get; set; }
                    public global::Avalonia.Input.KeyGesture? FallbackGesture { get; set; }
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
                <ProbeControl DeterministicGesture="Ctrl+Shift+A"
                              PlusGesture="Ctrl++"
                              FallbackGesture="Ctrl+UnknownToken" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains(
            "new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.A, global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift)",
            generated);
        Assert.Contains(
            "new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.OemPlus, global::Avalonia.Input.KeyModifiers.Control)",
            generated);
        Assert.Contains(
            "global::Avalonia.Input.KeyGesture.Parse(\"Ctrl+UnknownToken\")",
            generated);
    }

    [Fact]
    public void Emits_Avalonia_Intrinsic_Literals_Without_Static_Parse_Fallback()
    {
        const string code = """
            namespace Avalonia
            {
                public enum RelativeUnit
                {
                    Absolute,
                    Relative
                }

                public struct Thickness
                {
                    public Thickness(double uniform) { }
                    public Thickness(double left, double top, double right, double bottom) { }
                    public static Thickness Parse(string value) => default;
                }

                public struct CornerRadius
                {
                    public CornerRadius(double uniform) { }
                    public CornerRadius(double topLeft, double topRight, double bottomRight, double bottomLeft) { }
                    public static CornerRadius Parse(string value) => default;
                }

                public struct Point
                {
                    public Point(double x, double y) { }
                    public static Point Parse(string value) => default;
                }

                public struct Vector
                {
                    public Vector(double x, double y) { }
                    public static Vector Parse(string value) => default;
                }

                public struct Size
                {
                    public Size(double width, double height) { }
                    public static Size Parse(string value) => default;
                }

                public struct Rect
                {
                    public Rect(double x, double y, double width, double height) { }
                    public static Rect Parse(string value) => default;
                }

                public struct Matrix
                {
                    public Matrix(double m11, double m12, double m21, double m22, double m31, double m32) { }
                    public Matrix(double m11, double m12, double m13, double m21, double m22, double m23, double m31, double m32, double m33) { }
                    public static Matrix Parse(string value) => default;
                }

                public struct Vector3D
                {
                    public Vector3D(double x, double y, double z) { }
                    public static Vector3D Parse(string value) => default;
                }

                public struct PixelPoint
                {
                    public PixelPoint(int x, int y) { }
                    public static PixelPoint Parse(string value) => default;
                }

                public struct PixelSize
                {
                    public PixelSize(int width, int height) { }
                    public static PixelSize Parse(string value) => default;
                }

                public struct PixelRect
                {
                    public PixelRect(int x, int y, int width, int height) { }
                    public static PixelRect Parse(string value) => default;
                }

                public struct RelativePoint
                {
                    public RelativePoint(double x, double y, RelativeUnit unit) { }
                    public static RelativePoint Parse(string value) => default;
                }

                public struct RelativeScalar
                {
                    public RelativeScalar(double scalar, RelativeUnit unit) { }
                    public static RelativeScalar Parse(string value) => default;
                }

                public struct RelativeRect
                {
                    public RelativeRect(double x, double y, double width, double height, RelativeUnit unit) { }
                    public static RelativeRect Parse(string value) => default;
                }
            }

            namespace Avalonia.Media
            {
                public struct Color
                {
                    public static Color FromUInt32(uint value) => default;
                    public static Color Parse(string value) => default;
                }

                public static class Colors
                {
                    public static Color Red => default;
                }
            }

            namespace Avalonia.Controls
            {
                public enum GridUnitType
                {
                    Auto,
                    Pixel,
                    Star
                }

                public struct GridLength
                {
                    public GridLength(double value, GridUnitType unit) { }
                    public static GridLength Auto => default;
                    public static GridLength Parse(string value) => default;
                }

                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class ProbeControl : Control
                {
                    public global::Avalonia.Thickness ThicknessValue { get; set; }
                    public global::Avalonia.CornerRadius CornerRadiusValue { get; set; }
                    public global::Avalonia.Point PointValue { get; set; }
                    public global::Avalonia.Vector VectorValue { get; set; }
                    public global::Avalonia.Size SizeValue { get; set; }
                    public global::Avalonia.Rect RectValue { get; set; }
                    public global::Avalonia.Matrix MatrixValue { get; set; }
                    public global::Avalonia.Vector3D Vector3DValue { get; set; }
                    public global::Avalonia.PixelPoint PixelPointValue { get; set; }
                    public global::Avalonia.PixelSize PixelSizeValue { get; set; }
                    public global::Avalonia.PixelRect PixelRectValue { get; set; }
                    public global::Avalonia.Controls.GridLength GridLengthValue { get; set; }
                    public global::Avalonia.RelativePoint RelativePointValue { get; set; }
                    public global::Avalonia.RelativeScalar RelativeScalarValue { get; set; }
                    public global::Avalonia.RelativeRect RelativeRectValue { get; set; }
                    public global::Avalonia.Media.Color ColorValue { get; set; }
                    public global::Avalonia.Media.Color NamedColorValue { get; set; }
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
                <ProbeControl ThicknessValue="1,2,3,4"
                              CornerRadiusValue="2"
                              PointValue="10,20"
                              VectorValue="30,40"
                              SizeValue="50,60"
                              RectValue="1,2,3,4"
                              MatrixValue="1,0,0,1,10,20"
                              Vector3DValue="1,2,3"
                              PixelPointValue="4,5"
                              PixelSizeValue="6,7"
                              PixelRectValue="8,9,10,11"
                              GridLengthValue="2*"
                              RelativePointValue="10%,20%"
                              RelativeScalarValue="75%"
                              RelativeRectValue="1%,2%,3%,4%"
                              ColorValue="#FF112233"
                              NamedColorValue="Red" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains("ThicknessValue", generated);
        Assert.Contains("CornerRadiusValue", generated);
        Assert.Contains("PointValue", generated);
        Assert.Contains("VectorValue", generated);
        Assert.Contains("SizeValue", generated);
        Assert.Contains("RectValue", generated);
        Assert.Contains("MatrixValue", generated);
        Assert.Contains("Vector3DValue", generated);
        Assert.Contains("PixelPointValue", generated);
        Assert.Contains("PixelSizeValue", generated);
        Assert.Contains("PixelRectValue", generated);
        Assert.Contains("GridLengthValue", generated);
        Assert.Contains("RelativePointValue", generated);
        Assert.Contains("RelativeScalarValue", generated);
        Assert.Contains("RelativeRectValue", generated);
        Assert.Contains("ColorValue", generated);
        Assert.Contains("NamedColorValue", generated);

        Assert.DoesNotContain("global::Avalonia.Thickness.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.CornerRadius.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Point.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Vector.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Size.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Rect.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Matrix.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Vector3D.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.PixelPoint.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.PixelSize.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.PixelRect.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Controls.GridLength.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.RelativePoint.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.RelativeScalar.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.RelativeRect.Parse(", generated);
        Assert.DoesNotContain("global::Avalonia.Media.Color.Parse(", generated);
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

    [Fact]
    public void Generates_Generic_MarkupExtension_For_KeyGesture_Target_Without_Extension_Suffix()
    {
        const string code = """
            namespace Avalonia.Markup.Xaml
            {
                public abstract class MarkupExtension
                {
                    public abstract object? ProvideValue(global::System.IServiceProvider serviceProvider);
                }
            }

            namespace Avalonia.Input
            {
                public enum Key
                {
                    None = 0,
                    N = 1
                }

                [global::System.Flags]
                public enum KeyModifiers
                {
                    None = 0,
                    Control = 1,
                    Meta = 2
                }

                public sealed class KeyGesture
                {
                    public KeyGesture(Key key, KeyModifiers modifiers)
                    {
                    }

                    public static KeyGesture Parse(string value) => new KeyGesture(Key.None, KeyModifiers.None);
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public sealed class ProbeControl : Control
                {
                    public global::Avalonia.Input.KeyGesture? Gesture { get; set; }
                }
            }

            namespace Demo.Markup
            {
                public sealed class PlatformGesture : global::Avalonia.Markup.Xaml.MarkupExtension
                {
                    public string? Text { get; set; }

                    public override object? ProvideValue(global::System.IServiceProvider serviceProvider)
                    {
                        return global::Avalonia.Input.KeyGesture.Parse(Text ?? string.Empty);
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
                <ProbeControl Gesture="{m:PlatformGesture Text=Primary+N}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(", generated);
        Assert.Contains("new global::Demo.Markup.PlatformGesture() { Text = \"Primary+N\" }", generated);
        Assert.DoesNotContain(
            "global::Avalonia.Input.KeyGesture.Parse(\"{m:PlatformGesture Text=Primary+N}\")",
            generated);
    }

    [Fact]
    public void Generates_Generic_MarkupExtension_Without_Extension_Suffix_When_Suffixed_Type_Is_Not_A_MarkupExtension()
    {
        const string code = """
            namespace Avalonia.Markup.Xaml
            {
                public abstract class MarkupExtension
                {
                    public abstract object? ProvideValue(global::System.IServiceProvider serviceProvider);
                }
            }

            namespace Avalonia.Input
            {
                public enum Key
                {
                    None = 0,
                    N = 1
                }

                [global::System.Flags]
                public enum KeyModifiers
                {
                    None = 0,
                    Control = 1,
                    Meta = 2
                }

                public sealed class KeyGesture
                {
                    public KeyGesture(Key key, KeyModifiers modifiers)
                    {
                    }

                    public static KeyGesture Parse(string value) => new KeyGesture(Key.None, KeyModifiers.None);
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public sealed class ProbeControl : Control
                {
                    public global::Avalonia.Input.KeyGesture? Gesture { get; set; }
                }
            }

            namespace Demo.Markup
            {
                public sealed class PlatformGestureExtension
                {
                    public string? Text { get; set; }
                }

                public sealed class PlatformGesture : global::Avalonia.Markup.Xaml.MarkupExtension
                {
                    public string? Text { get; set; }

                    public override object? ProvideValue(global::System.IServiceProvider serviceProvider)
                    {
                        return global::Avalonia.Input.KeyGesture.Parse(Text ?? string.Empty);
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
                <ProbeControl Gesture="{m:PlatformGesture Text=Primary+N}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(", generated);
        Assert.Contains("new global::Demo.Markup.PlatformGesture() { Text = \"Primary+N\" }", generated);
        Assert.DoesNotContain("new global::Demo.Markup.PlatformGestureExtension()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "global::Avalonia.Input.KeyGesture.Parse(\"{m:PlatformGesture Text=Primary+N}\")",
            generated);
    }

    [Fact]
    public void Emits_Generic_Markup_Extension_For_Avalonia_Property_From_Using_Namespace()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public object? this[global::Avalonia.Data.IndexerDescriptor descriptor] { set { } }
                }
            }

            namespace Avalonia.Data
            {
                public class Binding
                {
                    public Binding(string path) { }
                }

                public sealed class IndexerDescriptor
                {
                    public IndexerDescriptor(global::System.Type type, string name, object? argument) { }
                }
            }

            namespace Avalonia.Markup.Xaml
            {
                public abstract class MarkupExtension
                {
                    public abstract object? ProvideValue(global::System.IServiceProvider serviceProvider);
                }
            }

            namespace Avalonia.Input
            {
                public enum Key
                {
                    None = 0
                }

                public enum KeyModifiers
                {
                    None = 0,
                    Control = 1,
                    Meta = 2
                }

                public sealed class KeyGesture
                {
                    public KeyGesture(Key key, KeyModifiers modifiers)
                    {
                    }

                    public static KeyGesture Parse(string value) => new KeyGesture(Key.None, KeyModifiers.None);
                }

                public class InputElement : global::Avalonia.AvaloniaObject
                {
                    public global::System.Collections.Generic.IList<global::Avalonia.Input.KeyBinding> KeyBindings { get; } =
                        new global::System.Collections.Generic.List<global::Avalonia.Input.KeyBinding>();
                }

                public sealed class KeyBinding : InputElement
                {
                    public static global::Avalonia.AvaloniaProperty GestureProperty { get; } = new global::Avalonia.AvaloniaProperty();
                    public global::Avalonia.Input.KeyGesture? Gesture { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class UserControl : global::Avalonia.Input.InputElement
                {
                    public object? Content { get; set; }
                }
            }

            namespace Demo.Markup
            {
                public sealed class PlatformGesture : global::Avalonia.Markup.Xaml.MarkupExtension
                {
                    public string? Text { get; set; }

                    public override object? ProvideValue(global::System.IServiceProvider serviceProvider)
                    {
                        return global::Avalonia.Input.KeyGesture.Parse(Text ?? string.Empty);
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
                         xmlns:m="using:Demo.Markup"
                         x:Class="Demo.MainView">
                <UserControl.KeyBindings>
                    <KeyBinding Gesture="{m:PlatformGesture Text=Primary+N}" x:CompileBindings="False" />
                </UserControl.KeyBindings>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0102");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();

        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(", generated);
        Assert.Contains("new global::Demo.Markup.PlatformGesture() { Text = \"Primary+N\" }", generated);
        Assert.DoesNotContain(
            "global::Avalonia.Input.KeyGesture.Parse(\"{m:PlatformGesture Text=Primary+N}\")",
            generated);
    }

    [Fact]
    public void Emits_Indexer_Assignment_For_Binding_PropertyElement_On_Avalonia_Property()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(AvaloniaProperty property, object? value) { }
                    public object? this[global::Avalonia.Data.IndexerDescriptor descriptor] { set { } }
                }

                public class Binding { }

                public class Visual : AvaloniaObject
                {
                    public static readonly AvaloniaProperty IsVisibleProperty = new();
                    public bool IsVisible { get; set; }
                }
            }

            namespace Avalonia.Data
            {
                public class IndexerDescriptor
                {
                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.Visual { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class TextBlock : Control { }
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
                <TextBlock>
                    <TextBlock.IsVisible>
                        <Binding />
                    </TextBlock.IsVisible>
                </TextBlock>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("SourceGenMarkupExtensionRuntime.ApplyBinding(", generated);
        Assert.Contains("global::Avalonia.Visual.IsVisibleProperty", generated);
        Assert.DoesNotContain(".IsVisible = (bool)(", generated);
    }

    [Fact]
    public void Treats_MultiValue_Collection_PropertyElement_As_Collection_Add()
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

                public class Grid : Control
                {
                    public RowDefinitionsCollection RowDefinitions { get; set; } = new();
                }

                public class RowDefinition : Control { }

                public class RowDefinitionsCollection : global::System.Collections.Generic.List<RowDefinition>
                {
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
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition />
                        <RowDefinition />
                    </Grid.RowDefinitions>
                </Grid>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0103");
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains(".RowDefinitions", generated);
        Assert.True(
            generated.Contains("__TryAddToCollection(", StringComparison.Ordinal) ||
            generated.Contains(".Add(", StringComparison.Ordinal));
    }

    [Fact]
    public void Materializes_NonAssignable_OwnerQualified_AvaloniaPropertyElement_Value_Container()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AvaloniaProperty<TValue> : AvaloniaProperty { }

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

                public class Panel : Control
                {
                    public ControlCollection Children { get; } = new();
                }

                public class ControlCollection : global::System.Collections.Generic.List<Control>
                {
                }
            }

            namespace Avalonia.Controls.Chrome
            {
                public class TitleBar : global::Avalonia.Controls.Control
                {
                }
            }

            namespace Avalonia.Controls.Primitives
            {
                public class ChromeOverlayLayer : global::Avalonia.Controls.Panel
                {
                }

                public class VisualLayerManager : global::Avalonia.Controls.Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty<ChromeOverlayLayer> ChromeOverlayLayerProperty = new();

                    public ChromeOverlayLayer ChromeOverlayLayer { get; } = new();
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
                <VisualLayerManager>
                    <VisualLayerManager.ChromeOverlayLayer>
                        <TitleBar />
                    </VisualLayerManager.ChromeOverlayLayer>
                </VisualLayerManager>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0103");
        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("new global::Avalonia.Controls.Primitives.ChromeOverlayLayer()", generated);
        Assert.Contains("TitleBar", generated);
        Assert.Contains(".ChromeOverlayLayerProperty", generated);
    }

    [Fact]
    public void Resolves_Attached_Style_Setter_Property_Token()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class Panel : Control { }

                public class Grid : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty RowProperty = new();
                }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
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
                    public Setter() { }
                    public Setter(global::Avalonia.AvaloniaProperty property, object? value)
                    {
                        Property = property;
                        Value = value;
                    }

                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }

                public static class Selectors
                {
                    public static Selector? Nesting(Selector? previous) => new();
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
                    <Style Selector="Panel">
                        <Setter Property="Grid.Row" Value="1" />
                    </Style>
                </UserControl.Styles>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0301");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Controls.Grid.RowProperty", generated);
    }

    [Fact]
    public void Resolves_Parenthesized_Attached_Setter_Property_Tokens_In_Style_And_ControlTheme()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(AvaloniaProperty property, object? value) { }
                }

                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class Panel : Control { }

                public class Button : Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
                }

                public class Grid : Control
                {
                    public static readonly global::Avalonia.AvaloniaProperty RowProperty = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class Selector { }

                public class Style
                {
                    public Selector? Selector { get; set; }
                    public global::System.Collections.Generic.List<Setter> Setters { get; } = new();
                }

                public class ControlTheme
                {
                    public global::System.Type? TargetType { get; set; }
                    public global::System.Collections.Generic.List<Setter> Setters { get; } = new();
                }

                public class Setter
                {
                    public Setter() { }

                    public Setter(global::Avalonia.AvaloniaProperty property, object? value)
                    {
                        Property = property;
                        Value = value;
                    }

                    public global::Avalonia.AvaloniaProperty? Property { get; set; }
                    public object? Value { get; set; }
                }

                public static class Selectors
                {
                    public static Selector? Nesting(Selector? previous) => new();
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
                    <Style Selector="Panel">
                        <Setter Property="(Grid.Row)" Value="1" />
                    </Style>
                </UserControl.Styles>
                <UserControl.Resources>
                    <ControlTheme x:Key="Theme.Button" TargetType="Button">
                        <Setter Property="(Grid.Row)" Value="2" />
                    </ControlTheme>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0301");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0303");
        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("global::Avalonia.Controls.Grid.RowProperty", generated);
    }

    [Fact]
    public void Ignores_Design_PreviewWith_PropertyElement_During_Runtime_Binding()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(AvaloniaProperty property, object? value) { }
                }

                public class ResourceDictionary
                {
                    public void Add(object key, object value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class Border : Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public global::Avalonia.ResourceDictionary Resources { get; } = new();
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
                        <Design.PreviewWith>
                            <Border />
                        </Design.PreviewWith>
                    </ResourceDictionary>
                </UserControl.Resources>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0101" && diagnostic.GetMessage().Contains("PreviewWith", StringComparison.Ordinal));
    }

    [Fact]
    public void Applies_Type_And_Property_Aliases_From_Transform_Rule_File()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public string? Foreground { get; set; }
                }
            }

            namespace Demo.Custom
            {
                public class FancyAliasControl : global::Avalonia.Controls.Control { }
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
                         AccentText="Blue">
                <FancyAlias />
            </UserControl>
            """;

        const string transformRules = """
            {
              "typeAliases": [
                {
                  "xmlNamespace": "https://github.com/avaloniaui",
                  "xamlType": "FancyAlias",
                  "clrType": "Demo.Custom.FancyAliasControl"
                }
              ],
              "propertyAliases": [
                {
                  "targetType": "Avalonia.Controls.UserControl",
                  "xamlProperty": "AccentText",
                  "clrProperty": "Foreground"
                }
              ]
            }
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalFiles:
            [
                ("transform-rules.json", transformRules, "AvaloniaSourceGenTransformRule")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Demo.Custom.FancyAliasControl()", generated);
        Assert.Contains("Foreground", generated);
    }

    [Fact]
    public void Applies_Type_And_Property_Aliases_From_Unified_Configuration_Transform_Documents()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public string? Foreground { get; set; }
                }
            }

            namespace Demo.Custom
            {
                public class FancyAliasControl : global::Avalonia.Controls.Control { }
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
                         AccentText="Blue">
                <FancyAlias />
            </UserControl>
            """;

        const string configurationJson = """
            {
              "schemaVersion": 1,
              "build": {
                "backend": "SourceGen",
                "isEnabled": true
              },
              "transform": {
                "rawTransformDocuments": {
                  "inline-rules.json": "{ \"typeAliases\": [ { \"xmlNamespace\": \"https://github.com/avaloniaui\", \"xamlType\": \"FancyAlias\", \"clrType\": \"Demo.Custom.FancyAliasControl\" } ], \"propertyAliases\": [ { \"targetType\": \"Avalonia.Controls.UserControl\", \"xamlProperty\": \"AccentText\", \"clrProperty\": \"Foreground\" } ] }"
                }
              }
            }
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalFiles:
            [
                ("xaml-sourcegen.config.json", configurationJson, "None")
            ]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Demo.Custom.FancyAliasControl()", generated);
        Assert.Contains("Foreground", generated);
    }

    [Fact]
    public void Unified_Configuration_Transform_Documents_Override_Legacy_Rule_Files_With_Deterministic_Diagnostic()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public string? Foreground { get; set; }
                    public string? LegacyForeground { get; set; }
                }
            }

            namespace Demo.Custom
            {
                public class LegacyAliasControl : global::Avalonia.Controls.Control { }

                public class ConfigAliasControl : global::Avalonia.Controls.Control { }
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
                         AccentText="Blue">
                <FancyAlias />
            </UserControl>
            """;

        const string legacyTransformRules = """
            {
              "typeAliases": [
                {
                  "xmlNamespace": "https://github.com/avaloniaui",
                  "xamlType": "FancyAlias",
                  "clrType": "Demo.Custom.LegacyAliasControl"
                }
              ],
              "propertyAliases": [
                {
                  "targetType": "Avalonia.Controls.UserControl",
                  "xamlProperty": "AccentText",
                  "clrProperty": "LegacyForeground"
                }
              ]
            }
            """;

        const string configurationJson = """
            {
              "schemaVersion": 1,
              "build": {
                "backend": "SourceGen",
                "isEnabled": true
              },
              "transform": {
                "rawTransformDocuments": {
                  "inline-rules.json": "{ \"typeAliases\": [ { \"xmlNamespace\": \"https://github.com/avaloniaui\", \"xamlType\": \"FancyAlias\", \"clrType\": \"Demo.Custom.ConfigAliasControl\" } ], \"propertyAliases\": [ { \"targetType\": \"Avalonia.Controls.UserControl\", \"xamlProperty\": \"AccentText\", \"clrProperty\": \"Foreground\" } ] }"
                }
              }
            }
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalFiles:
            [
                ("transform-rules.json", legacyTransformRules, "AvaloniaSourceGenTransformRule"),
                ("xaml-sourcegen.config.json", configurationJson, "None")
            ]);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "AXSG0903" &&
                          diagnostic.GetMessage().Contains("legacy transform rule files and unified configuration", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Demo.Custom.ConfigAliasControl()", generated);
        Assert.DoesNotContain("new global::Demo.Custom.LegacyAliasControl()", generated);
        Assert.Contains("Foreground", generated);
    }

    [Fact]
    public void Resolves_Type_And_Property_Aliases_From_Assembly_Attributes()
    {
        const string code = """
            using XamlToCSharpGenerator.Runtime;

            [assembly: SourceGenXamlTypeAliasAttribute("https://github.com/avaloniaui", "FancyAlias", "Demo.Custom.FancyAliasControl")]
            [assembly: SourceGenXamlPropertyAliasAttribute("Avalonia.Controls.UserControl", "AccentText", "Foreground")]

            namespace XamlToCSharpGenerator.Runtime
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class SourceGenXamlTypeAliasAttribute : global::System.Attribute
                {
                    public SourceGenXamlTypeAliasAttribute(string xmlNamespace, string xamlTypeName, string clrTypeName) { }
                }

                [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
                public sealed class SourceGenXamlPropertyAliasAttribute : global::System.Attribute
                {
                    public SourceGenXamlPropertyAliasAttribute(string targetTypeName, string xamlPropertyName, string clrPropertyName) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                    public string? Foreground { get; set; }
                }
            }

            namespace Demo.Custom
            {
                public class FancyAliasControl : global::Avalonia.Controls.Control { }
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
                         AccentText="Green">
                <FancyAlias />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0101");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("new global::Demo.Custom.FancyAliasControl()", generated);
        Assert.Contains("Foreground", generated);
    }

    [Fact]
    public void Reports_Invalid_Transform_Rule_Diagnostics()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
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
                         x:Class="Demo.MainView" />
            """;

        const string invalidTransformRules = """
            {
              "propertyAliases": [
                {
                  "targetType": "Avalonia.Controls.UserControl"
                }
              ]
            }
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(
            compilation,
            xaml,
            additionalFiles:
            [
                ("transform-rules.json", invalidTransformRules, "AvaloniaSourceGenTransformRule")
            ]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0901");
    }

    [Fact]
    public void ConditionalXaml_Skips_False_Branches_Before_Semantic_Type_Resolution()
    {
        const string code = """
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
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:present="https://github.com/avaloniaui?ApiInformation.IsTypePresent('Avalonia.Controls.TextBlock')"
                         xmlns:missing="https://github.com/avaloniaui?ApiInformation.IsTypePresent('Demo.DoesNotExistControl')"
                         x:Class="Demo.MainView">
                <present:TextBlock x:Name="VisibleLabel" Text="Visible" />
                <missing:DoesNotExistControl x:Name="HiddenLabel" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("VisibleLabel", generated);
        Assert.DoesNotContain("HiddenLabel", generated);
        Assert.DoesNotContain("DoesNotExistControl", generated);
    }

    [Fact]
    public void ConditionalXaml_Reports_Invalid_Conditional_Expression_Diagnostic()
    {
        const string code = """
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
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:cx="https://github.com/avaloniaui?ApiInformation.IsThingPresent('Avalonia.Controls.TextBlock')"
                         x:Class="Demo.MainView">
                <cx:TextBlock Text="Sample" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (_, diagnostics) = RunGenerator(compilation, xaml);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0120");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Emits_AppRoot_ApplicationStyles_PropertyElement_Assignments()
    {
        const string code = """
            namespace Avalonia
            {
                public class Application
                {
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }
            }

            namespace Avalonia.Styling
            {
                public class Styles : global::System.Collections.Generic.List<object> { }
            }

            namespace Avalonia.Themes.Fluent
            {
                public class FluentTheme
                {
                    public string? DensityStyle { get; set; }
                }
            }

            namespace Demo
            {
                public partial class App : global::Avalonia.Application { }
            }
            """;

        const string xaml = """
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:fluent="clr-namespace:Avalonia.Themes.Fluent"
                         x:Class="Demo.App">
                <Application.Styles>
                    <fluent:FluentTheme DensityStyle="Compact" />
                </Application.Styles>
            </Application>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("App.axaml", xaml)]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "App");
        Assert.Contains("partial class App", generated);
        Assert.Contains("__AXSGObjectGraph.TryClearCollection(__root.Styles);", generated);
        Assert.Contains("__root.Styles", generated);
        Assert.Contains("new global::Avalonia.Themes.Fluent.FluentTheme(", generated);
        Assert.Contains("DensityStyle", generated);
    }

    [Fact]
    public void Emits_Generic_Collection_Clear_Paths_For_Style_And_Resource_HotReload_Reconciliation()
    {
        const string code = """
            namespace Avalonia
            {
                public class Application
                {
                    public global::Avalonia.Styling.Styles Styles { get; } = new();
                }
            }

            namespace Avalonia.Styling
            {
                public interface IStyle { }
                public class SetterBase { }
                public class Styles : global::System.Collections.Generic.ICollection<IStyle>, IStyle
                {
                    private readonly global::System.Collections.Generic.List<IStyle> _items = new();
                    public int Count => _items.Count;
                    public bool IsReadOnly => false;
                    public void Add(IStyle item) => _items.Add(item);
                    public void Clear() => _items.Clear();
                    public bool Contains(IStyle item) => _items.Contains(item);
                    public void CopyTo(IStyle[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
                    public bool Remove(IStyle item) => _items.Remove(item);
                    public global::System.Collections.Generic.IEnumerator<IStyle> GetEnumerator() => _items.GetEnumerator();
                    global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
                }
            }

            namespace Avalonia.Controls
            {
                public interface IResourceProvider { }
            }

            namespace Avalonia.Controls.Templates
            {
                public interface IDataTemplate { }
            }

            namespace Demo
            {
                public partial class App : global::Avalonia.Application { }
            }
            """;

        const string xaml = """
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.App" />
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("App.axaml", xaml)]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.DoesNotContain("case global::System.Collections.Generic.ICollection<global::Avalonia.Styling.IStyle> styleCollection:", generated);
        Assert.DoesNotContain("case global::System.Collections.Generic.ICollection<global::Avalonia.Controls.IResourceProvider> resourceProviderCollection:", generated);
        Assert.DoesNotContain("case global::System.Collections.Generic.ICollection<global::Avalonia.Styling.SetterBase> setterCollection:", generated);

        var trackerSource = ReadRuntimeHotReloadStateTrackerSource();
        Assert.Contains("case Styles styles:", trackerSource);
        Assert.Contains("case DataTemplates dataTemplates:", trackerSource);
        Assert.Contains("case Classes classes:", trackerSource);
    }

    [Fact]
    public void Emits_AppRoot_ApplicationResources_PropertyElement_Assignment()
    {
        const string code = """
            namespace Avalonia
            {
                public class Application
                {
                    public global::Avalonia.Controls.ResourceDictionary? Resources { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public class ResourceDictionary
                {
                    public global::System.Collections.Generic.List<object> MergedDictionaries { get; } = new();
                }
            }

            namespace Demo
            {
                public partial class App : global::Avalonia.Application { }
            }
            """;

        const string xaml = """
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.App">
                <Application.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceDictionary />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </Application.Resources>
            </Application>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(
            compilation,
            [("App.axaml", xaml)]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0100");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains("partial class App", generated);
        Assert.Contains("Resources", generated);
        Assert.Contains("new global::Avalonia.Controls.ResourceDictionary(", generated);
        Assert.Contains("MergedDictionaries", generated);
    }

    [Fact]
    public void Emitted_ObjectGraph_Code_Does_Not_Use_Reflection_Fallbacks()
    {
        const string code = """
            namespace Avalonia.Controls
            {
                public class UserControl
                {
                    public object? Content { get; set; }
                }

                public class StackPanel
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class Button
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
                <StackPanel>
                    <Button Content="Hello" />
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = updatedCompilation.SyntaxTrees.Last().ToString();
        var forbiddenTokens = new[]
        {
            "System.Reflection",
            "BindingFlags",
            "GetMethods(",
            "GetProperty(",
            "Invoke(",
            "TypeDescriptor",
            "Convert.ChangeType",
            "__TrySetClrProperty",
            "__TryConvertValue",
            "__TryInvokeClearMethod"
        };

        foreach (var token in forbiddenTokens)
        {
            Assert.DoesNotContain(token, generated);
        }
    }

    [Fact]
    public void Binder_Source_Does_Not_Reparse_Template_RawXaml_For_Validation()
    {
        var source = GetBinderSourceText();
        Assert.DoesNotContain("XDocument.Parse(rawXaml", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetTemplateContentRootElement(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCollectControlTemplateNamedParts(template.RawXaml", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Source_Uses_Parser_First_Markup_Extension_Gate_For_Implicit_Expressions()
    {
        var source = GetBinderSourceText();
        Assert.DoesNotContain("ExtractMarkupHeadToken(", source, StringComparison.Ordinal);
        Assert.Contains("ExpressionClassificationService.TryParseCSharpExpressionMarkup(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Source_Does_Not_Use_Legacy_Template_Or_StaticResource_String_Heuristics()
    {
        var source = GetBinderSourceText();
        Assert.DoesNotContain("EndsWith(\"Template\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Contains(\"__ResolveStaticResource(\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_AssignBinding_Avalonia_Property_As_Binding_Object()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value, global::Avalonia.Data.BindingPriority priority) { }
                }
            }

            namespace Avalonia.Data
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class AssignBindingAttribute : global::System.Attribute { }

                public enum BindingPriority
                {
                    LocalValue
                }

                public enum BindingMode
                {
                    Default,
                    TwoWay
                }

                public interface IBinding { }

                public class Binding : IBinding
                {
                    public Binding() { }
                    public Binding(string path) { Path = path; }
                    public string? Path { get; set; }
                    public BindingMode Mode { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope
                {
                    bool IsCompleted { get; }
                    void Complete();
                    void Register(string name, object element);
                }

                public class NameScope : INameScope
                {
                    public bool IsCompleted => false;
                    public void Complete() { }
                    public void Register(string name, object element) { }
                }

                public class StyledElement : global::Avalonia.AvaloniaObject
                {
                    public string? Name { get; set; }
                }

                public class Control : StyledElement { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.StyledProperty<object?> ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }
            }

            namespace Demo
            {
                public class EditableItem : global::Avalonia.Controls.Control
                {
                    public static readonly global::Avalonia.StyledProperty<global::Avalonia.Data.IBinding?> TextBindingProperty = new();

                    [global::Avalonia.Data.AssignBinding]
                    public global::Avalonia.Data.IBinding? TextBinding { get; set; }
                }

                public class ItemVm
                {
                    public string Name { get; set; } = string.Empty;
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         xmlns:data="clr-namespace:Avalonia.Data"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <local:EditableItem TextBinding="{Binding Name, Mode=TwoWay}" />
                    <local:EditableItem>
                        <local:EditableItem.TextBinding>
                            <data:Binding Path="Name" Mode="TwoWay" />
                        </local:EditableItem.TextBinding>
                    </local:EditableItem>
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.DoesNotMatch(
            @"ApplyBinding\([^;]*EditableItem\.TextBindingProperty",
            generated);
        Assert.Equal(2, Regex.Matches(generated, @"SetValue\(global::Demo\.EditableItem\.TextBindingProperty,").Count);
        Assert.True(Regex.Matches(generated, @"AttachBindingNameScope\(").Count >= 2);
    }

    [Fact]
    public void Preserves_AssignBinding_For_Attached_Avalonia_Property_As_Binding_Object()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class StyledProperty<T> : AvaloniaProperty { }
                public class AttachedProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value, global::Avalonia.Data.BindingPriority priority) { }
                }
            }

            namespace Avalonia.Data
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
                public sealed class AssignBindingAttribute : global::System.Attribute { }

                public enum BindingPriority
                {
                    LocalValue
                }

                public enum BindingMode
                {
                    Default,
                    TwoWay
                }

                public interface IBinding { }

                public class Binding : IBinding
                {
                    public Binding() { }
                    public Binding(string path) { Path = path; }
                    public string? Path { get; set; }
                    public BindingMode Mode { get; set; }
                }
            }

            namespace Avalonia.Controls
            {
                public interface INameScope
                {
                    bool IsCompleted { get; }
                    void Complete();
                    void Register(string name, object element);
                }

                public class NameScope : INameScope
                {
                    public bool IsCompleted => false;
                    public void Complete() { }
                    public void Register(string name, object element) { }
                }

                public class StyledElement : global::Avalonia.AvaloniaObject
                {
                    public string? Name { get; set; }
                }

                public class Control : StyledElement { }

                public class UserControl : Control
                {
                    public static readonly global::Avalonia.StyledProperty<object?> ContentProperty = new();
                    public object? Content { get; set; }
                }

                public class StackPanel : Control
                {
                    public global::System.Collections.Generic.List<object> Children { get; } = new();
                }

                public class Border : Control { }
            }

            namespace Demo
            {
                public class AttachedBindingHost
                {
                    public static readonly global::Avalonia.AttachedProperty<global::Avalonia.Data.IBinding?> TextBindingProperty = new();

                    [global::Avalonia.Data.AssignBinding]
                    public global::Avalonia.Data.IBinding? TextBinding { get; set; }
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         xmlns:data="clr-namespace:Avalonia.Data"
                         x:Class="Demo.MainView">
                <StackPanel>
                    <Border local:AttachedBindingHost.TextBinding="{Binding Name, Mode=TwoWay}" />
                    <Border>
                        <local:AttachedBindingHost.TextBinding>
                            <data:Binding Path="Name" Mode="TwoWay" />
                        </local:AttachedBindingHost.TextBinding>
                    </Border>
                </StackPanel>
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.DoesNotMatch(
            @"ApplyBinding\([^;]*AttachedBindingHost\.TextBindingProperty",
            generated);
        Assert.Equal(2, Regex.Matches(generated, @"SetValue\(global::Demo\.AttachedBindingHost\.TextBindingProperty,").Count);
        Assert.True(Regex.Matches(generated, @"AttachBindingNameScope\(").Count >= 2);
    }

    [Fact]
    public void Allows_NonPublic_Compiled_Binding_For_Attached_Avalonia_Property_Assignment()
    {
        const string code = """
            namespace Avalonia
            {
                public class AvaloniaProperty { }
                public class AttachedProperty<T> : AvaloniaProperty { }

                public class AvaloniaObject
                {
                    public void SetValue(global::Avalonia.AvaloniaProperty property, object? value) { }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control : global::Avalonia.AvaloniaObject { }

                public class UserControl : Control
                {
                    public object? Content { get; set; }
                }

                public class Border : Control { }
            }

            namespace Demo
            {
                public sealed class AttachedBindingHost
                {
                    public static readonly global::Avalonia.AttachedProperty<object?> TextBindingProperty = new();

                    public object? TextBinding { get; set; }
                }

                public sealed class MainVm
                {
                    private string HiddenTitle { get; } = "secret";
                }

                public partial class MainView : global::Avalonia.Controls.UserControl { }
            }
            """;

        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:local="clr-namespace:Demo"
                         x:Class="Demo.MainView"
                         x:DataType="local:MainVm"
                         x:CompileBindings="True">
                <Border local:AttachedBindingHost.TextBinding="{CompiledBinding HiddenTitle}" />
            </UserControl>
            """;

        var compilation = CreateCompilation(code);
        var (updatedCompilation, diagnostics) = RunGenerator(compilation, xaml);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0111");

        var generated = GetGeneratedPartialClassSource(updatedCompilation, "MainView");
        Assert.Contains("Name = \"get_HiddenTitle\"", generated);
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
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null,
        IReadOnlyList<(string Path, string Text, string SourceItemGroup)>? additionalFiles = null)
    {
        return RunGenerator(
            compilation,
            [("MainView.axaml", xaml)],
            additionalBuildOptions,
            additionalFiles);
    }

    private static (Compilation UpdatedCompilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Xaml)> xamlFiles,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null,
        IReadOnlyList<(string Path, string Text, string SourceItemGroup)>? additionalFiles = null)
    {
        var xamlInputsWithTargetPaths = xamlFiles
            .Select(static file => (file.Path, file.Xaml, TargetPath: file.Path))
            .ToArray();

        var result = RunGeneratorWithResult(
            compilation,
            xamlInputsWithTargetPaths,
            additionalBuildOptions,
            additionalFiles);
        return (result.UpdatedCompilation, result.Diagnostics);
    }

    private static (Compilation UpdatedCompilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Xaml, string TargetPath)> xamlFiles,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null,
        IReadOnlyList<(string Path, string Text, string SourceItemGroup)>? additionalFiles = null)
    {
        var result = RunGeneratorWithResult(compilation, xamlFiles, additionalBuildOptions, additionalFiles);
        return (result.UpdatedCompilation, result.Diagnostics);
    }

    private static (
        Compilation UpdatedCompilation,
        ImmutableArray<Diagnostic> Diagnostics,
        GeneratorDriverRunResult RunResult) RunGeneratorWithResult(
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Xaml)> xamlFiles,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null,
        IReadOnlyList<(string Path, string Text, string SourceItemGroup)>? additionalFiles = null)
    {
        var xamlInputsWithTargetPaths = xamlFiles
            .Select(static file => (file.Path, file.Xaml, TargetPath: file.Path))
            .ToArray();

        return RunGeneratorWithResult(compilation, xamlInputsWithTargetPaths, additionalBuildOptions, additionalFiles);
    }

    private static (
        Compilation UpdatedCompilation,
        ImmutableArray<Diagnostic> Diagnostics,
        GeneratorDriverRunResult RunResult) RunGeneratorWithResult(
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Xaml, string TargetPath)> xamlFiles,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null,
        IReadOnlyList<(string Path, string Text, string SourceItemGroup)>? additionalFiles = null)
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

        if (!options.Any(static pair =>
                string.Equals(
                    pair.Key,
                    "build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled",
                    StringComparison.Ordinal)))
        {
            // Legacy generator-shape tests in this suite assume compatibility fallback behavior.
            // Explicitly pin it here so default-option changes are covered by focused fallback tests instead.
            options.Add(new KeyValuePair<string, string>(
                "build_property.AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled",
                "true"));
        }

        var generator = new AvaloniaXamlSourceGenerator();
        var additionalTextInputs = xamlFiles
            .Select(static file => (
                Path: file.Path,
                Text: file.Xaml,
                SourceItemGroup: "AvaloniaXaml",
                TargetPath: file.TargetPath))
            .ToList();
        if (additionalFiles is not null)
        {
            additionalTextInputs.AddRange(additionalFiles.Select(static file => (
                file.Path,
                file.Text,
                file.SourceItemGroup,
                TargetPath: file.Path)));
        }

        var additionalTexts = additionalTextInputs
            .Select(static file => new InMemoryAdditionalText(file.Path, file.Text))
            .ToImmutableArray();
        var additionalFileMetadata = additionalTextInputs
            .Select(static file =>
            {
                var values = new List<KeyValuePair<string, string>>
                {
                    new("build_metadata.AdditionalFiles.SourceItemGroup", file.SourceItemGroup),
                    new("build_metadata.AdditionalFiles.TargetPath", file.TargetPath)
                };

                return (file.Path, Values: (IEnumerable<KeyValuePair<string, string>>)values);
            })
            .ToImmutableArray();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(options, additionalFileMetadata));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        return (updatedCompilation, diagnostics, driver.GetRunResult());
    }

    private static string GetGeneratedPartialClassSource(Compilation compilation, string className)
    {
        var sources = compilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .ToArray();

        var generated = sources.FirstOrDefault(source =>
            source.StartsWith("// <auto-generated />", StringComparison.Ordinal) &&
            source.Contains($"partial class {className}", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(generated))
        {
            return generated;
        }

        generated = sources.FirstOrDefault(source =>
            source.Contains($"partial class {className}", StringComparison.Ordinal) &&
            (source.Contains("__PopulateGeneratedObjectGraph(", StringComparison.Ordinal) ||
             source.Contains("__RegisterXamlSourceGenArtifacts(", StringComparison.Ordinal) ||
             source.Contains("InitializeComponent(", StringComparison.Ordinal)));
        if (!string.IsNullOrEmpty(generated))
        {
            return generated;
        }

        return sources.First(source => source.Contains($"partial class {className}", StringComparison.Ordinal));
    }

    private static string ExtractGeneratedEventBindingMethodName(string generatedSource)
    {
        const string marker = "private void __AXSG_EventBinding_";
        var markerIndex = generatedSource.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, "Expected generated event binding method.");
        var signatureStart = markerIndex + "private void ".Length;
        var signatureEnd = generatedSource.IndexOf('(', signatureStart);
        Assert.True(signatureEnd > signatureStart, "Expected generated event binding signature.");
        return generatedSource.Substring(signatureStart, signatureEnd - signatureStart);
    }

    private static string ReadRuntimeHotReloadStateTrackerSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Runtime.Avalonia",
            "XamlSourceGenHotReloadStateTracker.cs");
        return File.ReadAllText(path);
    }

    private static string GetBinderSourceText()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var bindingDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding");

        var sourceFiles = Directory.GetFiles(bindingDirectory, "AvaloniaSemanticBinder*.cs")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            sourceFiles.Select(File.ReadAllText));
    }
}
