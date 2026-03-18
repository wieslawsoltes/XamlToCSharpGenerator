using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "/tmp/AvaloniaTypeIndexTests.cs");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        };

        return CSharpCompilation.Create(
            assemblyName: "AvaloniaTypeIndexTests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
