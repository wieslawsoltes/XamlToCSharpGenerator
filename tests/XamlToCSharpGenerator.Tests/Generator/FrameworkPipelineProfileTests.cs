using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Generator;

namespace XamlToCSharpGenerator.Tests.Generator;

public class FrameworkPipelineProfileTests
{
    [Theory]
    [InlineData("Avalonia")]
    [InlineData("NoUi")]
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
                _ => (
                    Generator: (IIncrementalGenerator)new NoUiXamlSourceGenerator(),
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
        var generatedSource = Assert.Single(runResult.Results[0].GeneratedSources);
        Assert.StartsWith(expectedHintPrefix, generatedSource.HintName, StringComparison.Ordinal);

        var generatedText = updatedCompilation.SyntaxTrees.Last().ToString();
        Assert.Contains(expectedGeneratedToken, generatedText);
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
}
