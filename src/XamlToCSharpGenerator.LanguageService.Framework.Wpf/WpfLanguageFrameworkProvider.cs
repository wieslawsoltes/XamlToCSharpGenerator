using System;
using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.LanguageService.Framework.Wpf;

public sealed class WpfLanguageFrameworkProvider : IXamlLanguageFrameworkProvider
{
    private const string PresentationXmlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public static WpfLanguageFrameworkProvider Instance { get; } = new();

    private WpfLanguageFrameworkProvider()
    {
        Framework = new XamlLanguageFrameworkInfo(
            Id: FrameworkProfileIds.Wpf,
            Profile: new PassiveXamlFrameworkProfile(
                FrameworkProfileIds.Wpf,
                PresentationXmlNamespace,
                preferredProjectXamlItemName: "Page",
                projectXamlItemNames:
                [
                    "Page",
                    "ApplicationDefinition",
                    "EmbeddedResource",
                    "Resource",
                    "Content",
                    "None",
                    "AdditionalFiles"
                ]),
            DefaultXmlNamespace: PresentationXmlNamespace,
            XmlnsDefinitionAttributeMetadataNames:
            [
                "System.Windows.Markup.XmlnsDefinitionAttribute"
            ],
            XmlnsPrefixAttributeMetadataNames:
            [
                "System.Windows.Markup.XmlnsPrefixAttribute"
            ],
            MarkupExtensionNamespaces:
            [
                "System.Windows",
                "System.Windows.Data",
                "System.Windows.Markup"
            ],
            PreferredProjectXamlItemName: "Page",
            ProjectXamlItemNames:
            [
                "Page",
                "ApplicationDefinition",
                "EmbeddedResource",
                "Resource",
                "Content",
                "None",
                "AdditionalFiles"
            ],
            DirectiveCompletions: ImmutableArray<XamlLanguageFrameworkCompletion>.Empty,
            MarkupExtensionCompletions: ImmutableArray<XamlLanguageFrameworkCompletion>.Empty);
    }

    public XamlLanguageFrameworkInfo Framework { get; }

    public int DetectionPriority => 100;

    public bool CanResolveFromProject(XDocument projectDocument, string projectPath)
    {
        _ = projectPath;
        return XamlLanguageFrameworkDetectionHelpers.HasTrueProperty(projectDocument, "UseWPF");
    }

    public bool CanResolveFromCompilation(Compilation compilation)
    {
        return XamlLanguageFrameworkDetectionHelpers.HasType(compilation, "System.Windows.Application") ||
               XamlLanguageFrameworkDetectionHelpers.HasAssembly(compilation, "PresentationFramework") ||
               XamlLanguageFrameworkDetectionHelpers.HasAssembly(compilation, "System.Xaml");
    }

    public bool CanResolveFromDocument(string filePath, string? documentText)
    {
        _ = filePath;
        return XamlLanguageFrameworkDetectionHelpers.DocumentContains(documentText, PresentationXmlNamespace) &&
               !XamlLanguageFrameworkDetectionHelpers.DocumentContains(documentText, "using:");
    }
}
