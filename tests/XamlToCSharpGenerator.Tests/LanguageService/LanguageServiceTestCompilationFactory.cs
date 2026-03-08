using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

internal static class LanguageServiceTestCompilationFactory
{
    public const string SymbolSourceFilePath = "/tmp/LanguageServiceTestTypes.cs";

    public static Compilation CreateCompilation()
    {
        const string source = """
                              using System;

                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]

                              namespace Avalonia.Metadata
                              {
                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsDefinitionAttribute : Attribute
                                  {
                                      public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                  }
                              }

                              namespace Avalonia.Controls.Metadata
                              {
                                  [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                                  public sealed class PseudoClassesAttribute : Attribute
                                  {
                                      public PseudoClassesAttribute(params string[] pseudoClasses) { }
                                  }
                              }

                              namespace TestApp.Controls
                              {
                                  public static class TestPseudoClasses
                                  {
                                      public const string ButtonPressed = ":pressed";
                                      public const string ExpanderExpanded = ":expanded";
                                  }

                                  public class UserControl { }
                                  public class StackPanel { }
                                  public class Border
                                  {
                                      public object Child { get; set; } = new object();
                                  }

                                  public class Control
                                  {
                                  }

                                  public class Visual
                                  {
                                      public double Opacity { get; set; }
                                  }

                                  public class Shape : Visual
                                  {
                                      public string Stroke { get; set; } = string.Empty;
                                  }

                                  public class Path : Shape
                                  {
                                      public string Data { get; set; } = string.Empty;
                                  }

                                  [Avalonia.Controls.Metadata.PseudoClassesAttribute(TestPseudoClasses.ButtonPressed, ":pointerover")]
                                  public class Button : Control
                                  {
                                      public string Content { get; set; } = string.Empty;
                                      public event EventHandler? Click;
                                  }

                                  public class TextBlock
                                  {
                                      public string Text { get; set; } = string.Empty;
                                  }

                                  [Avalonia.Controls.Metadata.PseudoClassesAttribute(TestPseudoClasses.ExpanderExpanded)]
                                  public class Expander : Control
                                  {
                                  }

                                  public class CustomerViewModel
                                  {
                                      public string DisplayName { get; set; } = string.Empty;
                                  }

                                  public class MainWindowViewModel
                                  {
                                      public string Name { get; set; } = string.Empty;
                                      public string FirstName { get; set; } = string.Empty;
                                      public string LastName { get; set; } = string.Empty;
                                      public string ProductName { get; set; } = string.Empty;
                                      public int Count { get; set; }
                                      public int Quantity { get; set; }
                                      public int ClickCount { get; set; }
                                      public CustomerViewModel Customer { get; set; } = new CustomerViewModel();
                                      public bool IsLoading { get; set; }
                                      public bool HasAccount { get; set; }
                                      public bool AgreedToTerms { get; set; }
                                      public bool IsVip { get; set; }
                                      public decimal Price { get; set; }
                                      public CustomerViewModel GetCustomer() => Customer;
                                      public string FormatSummary(string firstName, string lastName, int count) => firstName + lastName + count;
                                  }

                                  public class MyExtension
                                  {
                                  }
                              }
                              """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: SymbolSourceFilePath);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        };

        return CSharpCompilation.Create(
            "InMemoryLanguageServiceTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

internal sealed class InMemoryCompilationProvider : ICompilationProvider
{
    private readonly Compilation _compilation;

    public InMemoryCompilationProvider(Compilation compilation)
    {
        _compilation = compilation;
    }

    public Task<CompilationSnapshot> GetCompilationAsync(
        string filePath,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new CompilationSnapshot(
            ProjectPath: workspaceRoot,
            Project: null,
            Compilation: _compilation,
            Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
    }

    public void Invalidate(string filePath)
    {
    }

    public void Dispose()
    {
    }
}
