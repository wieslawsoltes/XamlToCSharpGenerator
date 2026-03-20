using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class AvaloniaTypeIndexTests
{
    [Fact]
    public void TryGetTypeByClrNamespace_IncludesProjectTypes_WhenDefaultXmlnsMappingExists()
    {
        var compilation = CreateCompilation("""
                                            using System;

                                            namespace Avalonia.Metadata
                                            {
                                                [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                                public sealed class XmlnsDefinitionAttribute : Attribute
                                                {
                                                    public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                                }
                                            }

                                            [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Framework.Controls")]

                                            namespace Framework.Controls
                                            {
                                                public class TextBlock { }
                                            }

                                            namespace App.Pages
                                            {
                                                public class CompositionPage { }
                                            }
                                            """);

        var index = AvaloniaTypeIndex.Create(compilation);
        var resolved = index.TryGetTypeByClrNamespace("App.Pages", "CompositionPage", out var typeInfo);

        Assert.True(resolved);
        Assert.NotNull(typeInfo);
        Assert.Equal("App.Pages.CompositionPage", typeInfo!.FullTypeName);
    }

    [Fact]
    public void TryGetTypeByClrNamespace_ResolvesDuplicateTypeNamesAcrossNamespaces()
    {
        var compilation = CreateCompilation("""
                                            using System;

                                            namespace Avalonia.Metadata
                                            {
                                                [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                                public sealed class XmlnsDefinitionAttribute : Attribute
                                                {
                                                    public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                                }
                                            }

                                            [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Framework.Controls")]

                                            namespace Framework.Controls
                                            {
                                                public class Control { }
                                            }

                                            namespace App.Pages
                                            {
                                                public class SharedPage : Framework.Controls.Control { }
                                            }

                                            namespace App.Dialogs
                                            {
                                                public class SharedPage : Framework.Controls.Control { }
                                            }
                                            """);

        var index = AvaloniaTypeIndex.Create(compilation);

        Assert.True(index.TryGetTypeByClrNamespace("App.Pages", "SharedPage", out var pageType));
        Assert.NotNull(pageType);
        Assert.Equal("App.Pages.SharedPage", pageType!.FullTypeName);

        Assert.True(index.TryGetTypeByClrNamespace("App.Dialogs", "SharedPage", out var dialogType));
        Assert.NotNull(dialogType);
        Assert.Equal("App.Dialogs.SharedPage", dialogType!.FullTypeName);
    }

    [Fact]
    public void FindTypesByXmlTypeName_IncludesExplicitXmlnsMappingsForProjectTypes()
    {
        var compilation = CreateCompilation("""
                                            using System;

                                            [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("using:Host.Controls", "Host.Controls")]
                                            [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("using:Demo.Controls", "Demo.Controls")]

                                            namespace Avalonia.Metadata
                                            {
                                                [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                                public sealed class XmlnsDefinitionAttribute : Attribute
                                                {
                                                    public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                                }
                                            }

                                            namespace Host.Controls
                                            {
                                                public class UserControl { }
                                            }

                                            namespace Demo.Controls
                                            {
                                                public class ThemeDoodad { }
                                            }
                                            """);

        var index = AvaloniaTypeIndex.Create(compilation);
        var matches = index.FindTypesByXmlTypeName("ThemeDoodad");

        Assert.Contains(matches, static item =>
            item.FullTypeName == "Demo.Controls.ThemeDoodad" &&
            item.XmlNamespace == "using:Demo.Controls");
        Assert.Contains(matches, static item =>
            item.FullTypeName == "Demo.Controls.ThemeDoodad" &&
            item.XmlNamespace == "https://github.com/avaloniaui");
    }

    [Fact]
    public void TryGetTypeByClrNamespace_MapsExplicitArrayPseudoClassEntriesToInitializerElements()
    {
        const string source = """
                              using System;

                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Test.Controls")]

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

                              namespace Test.Controls
                              {
                                  public static class KnownPseudoClasses
                                  {
                                      public const string Active = ":active";
                                  }

                                  [Avalonia.Controls.Metadata.PseudoClassesAttribute(new[] { KnownPseudoClasses.Active, ":pointerover" })]
                                  public class Button
                                  {
                                  }
                              }
                              """;

        var compilation = CreateCompilation(source);
        var index = AvaloniaTypeIndex.Create(compilation);

        Assert.True(index.TryGetTypeByClrNamespace("Test.Controls", "Button", out var typeInfo));
        Assert.NotNull(typeInfo);

        var activePseudoClass = Assert.Single(typeInfo!.PseudoClasses, pseudoClass =>
            string.Equals(pseudoClass.Name, ":active", StringComparison.Ordinal));
        var pointerOverPseudoClass = Assert.Single(typeInfo.PseudoClasses, pseudoClass =>
            string.Equals(pseudoClass.Name, ":pointerover", StringComparison.Ordinal));

        Assert.NotNull(activePseudoClass.SourceLocation);
        Assert.NotNull(pointerOverPseudoClass.SourceLocation);
        Assert.Equal("Active", ReadRangeText(source, activePseudoClass.SourceLocation!.Value));
        Assert.Equal("\":pointerover\"", ReadRangeText(source, pointerOverPseudoClass.SourceLocation!.Value));
    }

    [Fact]
    public void TryGetTypeByClrNamespace_PreservesOrdinalsForNullPseudoClassArguments()
    {
        const string source = """
                              using System;

                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Test.Controls")]

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

                              namespace Test.Controls
                              {
                                  [Avalonia.Controls.Metadata.PseudoClassesAttribute(null, ":pointerover")]
                                  public class Button
                                  {
                                  }
                              }
                              """;

        var compilation = CreateCompilation(source);
        var index = AvaloniaTypeIndex.Create(compilation);

        Assert.True(index.TryGetTypeByClrNamespace("Test.Controls", "Button", out var typeInfo));
        Assert.NotNull(typeInfo);

        var pointerOverPseudoClass = Assert.Single(typeInfo!.PseudoClasses, pseudoClass =>
            string.Equals(pseudoClass.Name, ":pointerover", StringComparison.Ordinal));

        Assert.NotNull(pointerOverPseudoClass.SourceLocation);
        Assert.Equal("\":pointerover\"", ReadRangeText(source, pointerOverPseudoClass.SourceLocation!.Value));
    }

    [Fact]
    public void TryGetTypeByClrNamespace_PreservesOrdinalsForNullPseudoClassArrayElements()
    {
        const string source = """
                              using System;

                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Test.Controls")]

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

                              namespace Test.Controls
                              {
                                  [Avalonia.Controls.Metadata.PseudoClassesAttribute(new[] { null, ":pointerover" })]
                                  public class Button
                                  {
                                  }
                              }
                              """;

        var compilation = CreateCompilation(source);
        var index = AvaloniaTypeIndex.Create(compilation);

        Assert.True(index.TryGetTypeByClrNamespace("Test.Controls", "Button", out var typeInfo));
        Assert.NotNull(typeInfo);

        var pointerOverPseudoClass = Assert.Single(typeInfo!.PseudoClasses, pseudoClass =>
            string.Equals(pseudoClass.Name, ":pointerover", StringComparison.Ordinal));

        Assert.NotNull(pointerOverPseudoClass.SourceLocation);
        Assert.Equal("\":pointerover\"", ReadRangeText(source, pointerOverPseudoClass.SourceLocation!.Value));
    }

    [Fact]
    public async Task TryGetTypeByClrNamespace_HandlesPseudoClassesFromReferencedSourceCompilation()
    {
        const string referencedSource = """
                                        using System;

                                        [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Lib.Controls")]

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

                                        namespace Lib.Controls
                                        {
                                            public static class KnownPseudoClasses
                                            {
                                                public const string Active = ":active";
                                            }

                                            [Avalonia.Controls.Metadata.PseudoClassesAttribute(KnownPseudoClasses.Active, ":pointerover")]
                                            public class LibraryButton
                                            {
                                            }
                                        }
                                        """;

        const string appSource = """
                                 namespace App
                                 {
                                     public class RootViewModel
                                     {
                                     }
                                 }
                                 """;

        using var workspace = new AdhocWorkspace();
        var referencedProjectId = ProjectId.CreateNewId();
        var appProjectId = ProjectId.CreateNewId();
        var references = CreateBaseReferences();

        var solution = workspace.CurrentSolution
            .AddProject(referencedProjectId, "Lib.Controls", "Lib.Controls", LanguageNames.CSharp)
            .AddMetadataReferences(referencedProjectId, references)
            .AddDocument(
                DocumentId.CreateNewId(referencedProjectId),
                "Lib.Controls.cs",
                SourceText.From(referencedSource),
                filePath: "/tmp/Lib.Controls.cs")
            .AddProject(appProjectId, "App", "App", LanguageNames.CSharp)
            .AddMetadataReferences(appProjectId, references)
            .AddProjectReference(appProjectId, new ProjectReference(referencedProjectId))
            .AddDocument(
                DocumentId.CreateNewId(appProjectId),
                "App.cs",
                SourceText.From(appSource),
                filePath: "/tmp/App.cs");

        Assert.True(workspace.TryApplyChanges(solution));

        var compilation = await workspace.CurrentSolution.GetProject(appProjectId)!.GetCompilationAsync();
        Assert.NotNull(compilation);

        var index = AvaloniaTypeIndex.Create(compilation!);

        Assert.True(index.TryGetTypeByClrNamespace("Lib.Controls", "LibraryButton", out var typeInfo));
        Assert.NotNull(typeInfo);
        Assert.Contains(typeInfo!.PseudoClasses, pseudoClass =>
            string.Equals(pseudoClass.Name, ":active", StringComparison.Ordinal) &&
            pseudoClass.SourceLocation is { Uri: var activeUri } &&
            activeUri.Contains("Lib.Controls.cs", StringComparison.Ordinal));
        Assert.Contains(typeInfo.PseudoClasses, pseudoClass =>
            string.Equals(pseudoClass.Name, ":pointerover", StringComparison.Ordinal) &&
            pseudoClass.SourceLocation is { Uri: var pointerOverUri } &&
            pointerOverUri.Contains("Lib.Controls.cs", StringComparison.Ordinal));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string filePath = "/tmp/AvaloniaTypeIndexTests.cs",
        string assemblyName = "AvaloniaTypeIndexTests",
        params MetadataReference[] additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var references = new List<MetadataReference>(CreateBaseReferences());
        references.AddRange(additionalReferences);

        return CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference[] CreateBaseReferences()
    {
        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        ];
    }

    private static string ReadRangeText(string source, AvaloniaSymbolSourceLocation location)
    {
        var sourceText = SourceText.From(source);
        var start = sourceText.Lines.GetPosition(new LinePosition(
            location.Range.Start.Line,
            location.Range.Start.Character));
        var end = sourceText.Lines.GetPosition(new LinePosition(
            location.Range.End.Line,
            location.Range.End.Character));
        return sourceText.ToString(TextSpan.FromBounds(start, end));
    }
}
