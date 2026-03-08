using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Avalonia.Parsing;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class SimpleXamlDocumentParserTests
{
    [Fact]
    public void Parse_Extracts_XClass_And_Named_Elements()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                               xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                               xmlns:vm="clr-namespace:Demo.ViewModels"
                               mc:Ignorable="d"
                               x:Class="Demo.MainView"
                               x:ClassModifier="Public"
                               x:Precompile="True"
                               x:DataType="vm:MainViewModel"
                               x:CompileBindings="True"
                               d:DesignWidth="800">
                      <UserControl.Resources>
                          <SolidColorBrush x:Key="PrimaryBrush" Color="Blue"/>
                          <ResourceDictionary.MergedDictionaries>
                              <ResourceInclude Source="avares://Demo.Assets/Resources/Colors.axaml" />
                          </ResourceDictionary.MergedDictionaries>
                          <ControlTheme x:Key="Theme.Button" TargetType="Button" ThemeVariant="Dark">
                              <Setter Property="Content" Value="{CompiledBinding Title}"/>
                          </ControlTheme>
                      </UserControl.Resources>
                      <UserControl.Styles>
                          <StyleInclude Source="/Styles/Common.axaml" />
                          <DataTemplate x:Key="PersonTemplate" x:DataType="vm:Person">
                              <TextBlock Name="InnerLabel"/>
                          </DataTemplate>
                          <Style Selector="TextBlock" x:DataType="vm:MainViewModel">
                              <Setter Property="Text" Value="{CompiledBinding Title}"/>
                          </Style>
                      </UserControl.Styles>
                      <TextBox Name="Input"/>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        Assert.Equal("Demo.MainView", document!.ClassFullName);
        Assert.Equal("Public", document.ClassModifier);
        Assert.True(document.Precompile);
        Assert.Equal(2, document.NamedElements.Length);
        Assert.Equal("Input", document.NamedElements[0].Name);
        Assert.Equal(3, document.Resources.Length);
        Assert.Single(document.Templates);
        Assert.Single(document.Styles);
        Assert.Single(document.ControlThemes);
        Assert.Equal(2, document.Includes.Length);
        Assert.Contains(document.Resources, resource => resource.Key == "PrimaryBrush");
        Assert.Contains(document.Resources, resource => resource.Key == "PersonTemplate");
        Assert.Contains(document.Resources, resource => resource.Key == "Theme.Button");
        Assert.Contains(document.Includes, include => include.Kind == "ResourceInclude" && include.MergeTarget == "MergedDictionaries");
        Assert.Contains(document.Includes, include => include.Kind == "StyleInclude" && include.MergeTarget == "Styles");
        Assert.Equal("DataTemplate", document.Templates[0].Kind);
        Assert.Equal("PersonTemplate", document.Templates[0].Key);
        Assert.Equal("TextBlock", document.Styles[0].Selector);
        Assert.Equal("Button", document.ControlThemes[0].TargetType);
        Assert.Equal("Dark", document.ControlThemes[0].ThemeVariant);
        Assert.Equal("vm:MainViewModel", document.RootObject.DataType);
        Assert.True(document.RootObject.CompileBindings);
        Assert.DoesNotContain(document.RootObject.PropertyAssignments, property => property.PropertyName == "DesignWidth");
    }

    [Fact]
    public void Parse_Extracts_ControlTemplate_TargetType()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView">
                      <UserControl.Resources>
                          <ControlTemplate x:Key="ButtonTemplate" TargetType="Button" />
                      </UserControl.Resources>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        var template = Assert.Single(document!.Templates);
        Assert.Equal("ControlTemplate", template.Kind);
        Assert.Equal("Button", template.TargetType);
    }

    [Fact]
    public void Parse_Does_Not_Assign_Nested_Style_Setters_To_Parent_Style()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView">
                      <UserControl.Styles>
                          <Style Selector="Button">
                              <Setter Property="Content" Value="Base" />
                              <Style Selector="^:pointerover">
                                  <Setter Property="Content" Value="Hover" />
                              </Style>
                          </Style>
                      </UserControl.Styles>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        Assert.Equal(2, document!.Styles.Length);

        var parentStyle = Assert.Single(document.Styles.Where(style => style.Selector == "Button"));
        var nestedStyle = Assert.Single(document.Styles.Where(style => style.Selector == "^:pointerover"));
        Assert.Single(parentStyle.Setters);
        Assert.Single(nestedStyle.Setters);
        Assert.Equal("Base", parentStyle.Setters[0].Value);
        Assert.Equal("Hover", nestedStyle.Setters[0].Value);
    }

    [Fact]
    public void Parse_Extracts_ControlTheme_Setters_From_Setters_Property_Element()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView">
                      <UserControl.Resources>
                          <ControlTheme x:Key="Theme.Button" TargetType="Button">
                              <ControlTheme.Setters>
                                  <Setter Property="Content" Value="Hello" />
                              </ControlTheme.Setters>
                          </ControlTheme>
                      </UserControl.Resources>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        var theme = Assert.Single(document!.ControlThemes);
        var setter = Assert.Single(theme.Setters);
        Assert.Equal("Content", setter.PropertyName);
        Assert.Equal("Hello", setter.Value);
    }

    [Fact]
    public void Parse_Classless_ResourceDictionary_Document_Does_Not_Report_Missing_Class()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "Colors.axaml",
            TargetPath: "Styles/Colors.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                      <SolidColorBrush x:Key="AccentBrush" Color="Blue" />
                  </ResourceDictionary>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AXSG0002");
        Assert.Equal("XamlToCSharpGenerator.Generated", document!.ClassNamespace);
        Assert.StartsWith("GeneratedXaml_Colors_", document.ClassName, StringComparison.Ordinal);
        Assert.Single(document.Resources);
    }

    [Fact]
    public void Parse_Normalizes_AvaloniaResource_TargetPath_Prefix()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "HamburgerMenu.xaml",
            TargetPath: "!/HamburgerMenu/HamburgerMenu.xaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  </ResourceDictionary>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        Assert.Equal("HamburgerMenu/HamburgerMenu.xaml", document!.TargetPath);
    }

    [Fact]
    public void Parse_Captures_Inline_Text_Content_On_Object_Node()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView">
                      <UserControl.Resources>
                          <SolidColorBrush x:Key="AccentBrush">Red</SolidColorBrush>
                      </UserControl.Resources>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        var resourceNode = Assert.Single(document!.RootObject.PropertyElements[0].ObjectValues);
        Assert.Equal("Red", resourceNode.TextContent);
    }

    [Fact]
    public void Parse_Trims_And_Joins_Multiple_Inline_Text_Fragments()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView">
                      <TextBlock>
                          Hello
                          <Run />
                          World
                      </TextBlock>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        var textNode = Assert.Single(document!.RootObject.ChildObjects);
        Assert.Equal("Hello World", textNode.TextContent);
    }

    [Fact]
    public void Parse_Extracts_Construction_Directives_And_Array_Item_Type()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               xmlns:local="clr-namespace:Demo.Controls"
                               x:Class="Demo.MainView">
                      <StackPanel>
                          <local:FactoryThing x:TypeArguments="x:String" x:FactoryMethod="Create">
                              <x:Arguments>
                                  <x:String>Hello</x:String>
                              </x:Arguments>
                          </local:FactoryThing>
                          <x:Array Type="x:Int32">
                              <x:Int32>1</x:Int32>
                          </x:Array>
                      </StackPanel>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));

        var stackPanel = Assert.Single(document!.RootObject.ChildObjects);
        Assert.Equal(2, stackPanel.ChildObjects.Length);

        var factoryNode = stackPanel.ChildObjects[0];
        Assert.Equal("Create", factoryNode.FactoryMethod);
        Assert.Single(factoryNode.TypeArguments);
        Assert.Equal("x:String", factoryNode.TypeArguments[0]);
        var argumentNode = Assert.Single(factoryNode.ConstructorArguments);
        Assert.Equal("Hello", argumentNode.TextContent);

        var arrayNode = stackPanel.ChildObjects[1];
        Assert.Equal("x:Int32", arrayNode.ArrayItemType);
    }

    [Fact]
    public void Parse_Preserves_Plain_Name_As_Node_Name_And_Property_Assignment()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView">
                      <TextBox Name="Input" />
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        var textBox = Assert.Single(document!.RootObject.ChildObjects);
        Assert.Equal("Input", textBox.Name);
        var nameAssignment = Assert.Single(textBox.PropertyAssignments.Where(static property => property.PropertyName == "Name"));
        Assert.Equal("Input", nameAssignment.Value);
    }

    [Fact]
    public void Parse_Supports_Global_Prefixes_And_Implicit_Default_Namespace()
    {
        var parser = CreateAvaloniaParser(
            ImmutableDictionary<string, string>.Empty
                .Add("x", "http://schemas.microsoft.com/winfx/2006/xaml")
                .Add("local", "using:Demo.Controls"),
            allowImplicitDefaultXmlns: true,
            implicitDefaultXmlns: "https://github.com/avaloniaui");
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl x:Class="Demo.MainView">
                      <local:CustomControl />
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        Assert.Equal("Demo.MainView", document!.ClassFullName);
        Assert.Equal("https://github.com/avaloniaui", document.RootObject.XmlNamespace);
        Assert.Equal("using:Demo.Controls", document.RootObject.ChildObjects[0].XmlNamespace);
        Assert.Equal("http://schemas.microsoft.com/winfx/2006/xaml", document.XmlNamespaces["x"]);
        Assert.Equal("using:Demo.Controls", document.XmlNamespaces["local"]);
        Assert.Equal("https://github.com/avaloniaui", document.XmlNamespaces[string.Empty]);
    }

    [Fact]
    public void Parse_Applies_Global_Prefixes_To_Directive_Values()
    {
        var parser = CreateAvaloniaParser(
            ImmutableDictionary<string, string>.Empty
                .Add("vm", "using:Demo.ViewModels"),
            allowImplicitDefaultXmlns: false,
            implicitDefaultXmlns: null);
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView"
                               x:DataType="vm:MainViewModel">
                      <TextBlock Text="{Binding Name}" />
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        Assert.Equal("vm:MainViewModel", document!.RootObject.DataType);
        Assert.Equal("using:Demo.ViewModels", document.XmlNamespaces["vm"]);
    }

    [Fact]
    public void Parse_Extracts_Conditional_Namespace_Metadata_For_Elements_And_Attributes()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "ConditionalView.axaml",
            TargetPath: "ConditionalView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               xmlns:cx="https://github.com/avaloniaui?ApiInformation.IsTypePresent('Avalonia.Controls.TextBlock')"
                               x:Class="Demo.ConditionalView">
                      <cx:TextBlock x:Name="ConditionalBlock" cx:Text="Hello" />
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));

        var conditionalNode = Assert.Single(document!.RootObject.ChildObjects);
        Assert.Equal("https://github.com/avaloniaui", conditionalNode.XmlNamespace);
        Assert.NotNull(conditionalNode.Condition);
        Assert.Equal("IsTypePresent", conditionalNode.Condition!.MethodName);
        Assert.Equal("Avalonia.Controls.TextBlock", Assert.Single(conditionalNode.Condition.Arguments));

        var conditionalAttribute = Assert.Single(conditionalNode.PropertyAssignments.Where(x => x.PropertyName == "Text"));
        Assert.NotNull(conditionalAttribute.Condition);
        Assert.Equal("IsTypePresent", conditionalAttribute.Condition!.MethodName);
    }

    [Fact]
    public void Parse_Reports_Invalid_Conditional_Namespace_Expression()
    {
        var parser = CreateAvaloniaParser();
        var input = new XamlFileInput(
            FilePath: "ConditionalView.axaml",
            TargetPath: "ConditionalView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               xmlns:cx="https://github.com/avaloniaui?ApiInformation.IsThingPresent('Avalonia.Controls.TextBlock')"
                               x:Class="Demo.ConditionalView">
                      <cx:TextBlock />
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AXSG0120");
    }

    [Fact]
    public void Parse_Without_Framework_Enrichers_Leaves_Framework_Collections_Empty()
    {
        var parser = new SimpleXamlDocumentParser();
        var input = new XamlFileInput(
            FilePath: "MainView.axaml",
            TargetPath: "MainView.axaml",
            SourceItemGroup: "AvaloniaXaml",
            Text: """
                  <UserControl xmlns="https://github.com/avaloniaui"
                               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                               x:Class="Demo.MainView">
                      <UserControl.Resources>
                          <SolidColorBrush x:Key="AccentBrush" Color="Blue" />
                      </UserControl.Resources>
                      <UserControl.Styles>
                          <Style Selector="TextBlock">
                              <Setter Property="Text" Value="Hello" />
                          </Style>
                      </UserControl.Styles>
                  </UserControl>
                  """);

        var (document, diagnostics) = parser.Parse(input);

        Assert.NotNull(document);
        Assert.Empty(diagnostics.Where(x => x.IsError));
        Assert.Empty(document!.Resources);
        Assert.Empty(document.Templates);
        Assert.Empty(document.Styles);
        Assert.Empty(document.ControlThemes);
        Assert.Empty(document.Includes);
    }

    private static SimpleXamlDocumentParser CreateAvaloniaParser(
        ImmutableDictionary<string, string>? globalXmlNamespaces = null,
        bool allowImplicitDefaultXmlns = false,
        string? implicitDefaultXmlns = null)
    {
        return new SimpleXamlDocumentParser(
            globalXmlNamespaces,
            allowImplicitDefaultXmlns,
            implicitDefaultXmlns,
            [AvaloniaDocumentFeatureEnricher.Instance]);
    }
}
