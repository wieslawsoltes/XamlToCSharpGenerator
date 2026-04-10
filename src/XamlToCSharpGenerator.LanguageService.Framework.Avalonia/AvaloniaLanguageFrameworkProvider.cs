using System;
using System.Collections.Immutable;
using System.IO;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Avalonia.Framework;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.LanguageService.Framework.Avalonia;

public sealed class AvaloniaLanguageFrameworkProvider : IXamlLanguageFrameworkProvider
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private static readonly ImmutableArray<string> AvaloniaDefaultXmlNamespaceClrNamespaces =
    [
        "Avalonia.Controls",
        "Avalonia.Controls.Primitives",
        "Avalonia.Controls.Presenters",
        "Avalonia.Controls.Shapes",
        "Avalonia.Controls.Documents",
        "Avalonia.Controls.Chrome",
        "Avalonia.Controls.Embedding",
        "Avalonia.Controls.Notifications",
        "Avalonia.Controls.Converters",
        "Avalonia.Markup.Xaml.Templates",
        "Avalonia.Markup.Xaml.Styling",
        "Avalonia.Markup.Xaml.MarkupExtensions",
        "Avalonia.Styling",
        "Avalonia.Controls.Templates",
        "Avalonia.Input",
        "Avalonia.Automation",
        "Avalonia.Dialogs",
        "Avalonia.Dialogs.Internal",
        "Avalonia.Layout",
        "Avalonia.Media",
        "Avalonia.Media.Transformation",
        "Avalonia.Media.Imaging",
        "Avalonia.Animation",
        "Avalonia.Animation.Easings",
        "Avalonia"
    ];

    public static AvaloniaLanguageFrameworkProvider Instance { get; } = new();

    private AvaloniaLanguageFrameworkProvider()
    {
        Framework = new XamlLanguageFrameworkInfo(
            Id: FrameworkProfileIds.Avalonia,
            Profile: AvaloniaFrameworkProfile.Instance,
            DefaultXmlNamespace: AvaloniaDefaultXmlNamespace,
            XmlnsDefinitionAttributeMetadataNames:
            [
                "Avalonia.Metadata.XmlnsDefinitionAttribute",
                "XamlToCSharpGenerator.Runtime.SourceGenXmlnsDefinitionAttribute"
            ],
            XmlnsPrefixAttributeMetadataNames:
            [
                "Avalonia.Metadata.XmlnsPrefixAttribute",
                "XamlToCSharpGenerator.Runtime.SourceGenGlobalXmlnsPrefixAttribute"
            ],
            MarkupExtensionNamespaces:
            [
                "Avalonia.Markup.Xaml.MarkupExtensions",
                "Avalonia.Data",
                "Avalonia.Markup.Xaml",
                "System.Windows.Markup"
            ],
            PreferredProjectXamlItemName: "AvaloniaXaml",
            ProjectXamlItemNames:
            [
                "AvaloniaXaml",
                "Page",
                "ApplicationDefinition",
                "None",
                "Content",
                "EmbeddedResource",
                "AdditionalFiles"
            ],
            DirectiveCompletions:
            [
                XamlLanguageFrameworkCompletion.Create("x:DataType", "x:DataType=\"$0\"", "Compiled binding data type"),
                XamlLanguageFrameworkCompletion.Create("x:CompileBindings", "x:CompileBindings=\"True\"", "Compiled binding toggle")
            ],
            MarkupExtensionCompletions:
            [
                XamlLanguageFrameworkCompletion.Create("CompiledBinding", "{CompiledBinding $0}", "Compiled binding"),
                XamlLanguageFrameworkCompletion.Create("ReflectionBinding", "{ReflectionBinding $0}", "Reflection binding"),
                XamlLanguageFrameworkCompletion.Create("ResolveByName", "{ResolveByName $0}", "Resolve-by-name reference"),
                XamlLanguageFrameworkCompletion.Create("OnPlatform", "{OnPlatform $0}", "Platform-specific value"),
                XamlLanguageFrameworkCompletion.Create("OnFormFactor", "{OnFormFactor $0}", "Form-factor-specific value")
            ],
            SupportsPseudoClasses: true,
            PseudoClassesAttributeMetadataName: "Avalonia.Controls.Metadata.PseudoClassesAttribute",
            SupportsAssemblyResourceUris: true,
            IncludeSourceAssemblyClrNamespacesInDefaultXmlNamespace: true,
            UseCompiledBindingsByDefault: true,
            DefaultXmlNamespaceClrNamespaces: AvaloniaDefaultXmlNamespaceClrNamespaces);
    }

    public XamlLanguageFrameworkInfo Framework { get; }

    public int DetectionPriority => 200;

    public bool CanResolveFromProject(XDocument projectDocument, string projectPath)
    {
        _ = projectPath;
        return XamlLanguageFrameworkDetectionHelpers.HasItemElement(projectDocument, "AvaloniaXaml") ||
               XamlLanguageFrameworkDetectionHelpers.HasPackageReference(projectDocument, "Avalonia") ||
               XamlLanguageFrameworkDetectionHelpers.HasPackageReference(projectDocument, "Avalonia.Desktop") ||
               XamlLanguageFrameworkDetectionHelpers.HasPackageReference(projectDocument, "Avalonia.ReactiveUI");
    }

    public bool CanResolveFromCompilation(Compilation compilation)
    {
        return XamlLanguageFrameworkDetectionHelpers.HasType(compilation, "Avalonia.Application") ||
               XamlLanguageFrameworkDetectionHelpers.HasAssemblyPrefix(compilation, "Avalonia");
    }

    public bool CanResolveFromDocument(string filePath, string? documentText)
    {
        return string.Equals(Path.GetExtension(filePath), ".axaml", StringComparison.OrdinalIgnoreCase) ||
               XamlLanguageFrameworkDetectionHelpers.DocumentContains(documentText, AvaloniaDefaultXmlNamespace);
    }
}
