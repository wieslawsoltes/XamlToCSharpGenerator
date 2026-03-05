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

                              namespace TestApp.Controls
                              {
                                  public class UserControl { }
                                  public class StackPanel { }

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

                                  public class Button
                                  {
                                      public string Content { get; set; } = string.Empty;
                                  }

                                  public class TextBlock
                                  {
                                      public string Text { get; set; } = string.Empty;
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
