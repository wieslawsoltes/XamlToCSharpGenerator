using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.LanguageService.Framework.Maui;

public sealed class MauiLanguageFrameworkProvider : IXamlLanguageFrameworkProvider
{
    private const string MauiDefaultXmlNamespace = "http://schemas.microsoft.com/dotnet/2021/maui";

    public static MauiLanguageFrameworkProvider Instance { get; } = new();

    private MauiLanguageFrameworkProvider()
    {
        Framework = new XamlLanguageFrameworkInfo(
            Id: FrameworkProfileIds.Maui,
            Profile: new PassiveXamlFrameworkProfile(
                FrameworkProfileIds.Maui,
                MauiDefaultXmlNamespace,
                preferredProjectXamlItemName: "MauiXaml",
                projectXamlItemNames:
                [
                    "MauiXaml",
                    "Page",
                    "ApplicationDefinition",
                    "EmbeddedResource",
                    "Content",
                    "None",
                    "AdditionalFiles"
                ]),
            DefaultXmlNamespace: MauiDefaultXmlNamespace,
            XmlnsDefinitionAttributeMetadataNames:
            [
                "Microsoft.Maui.Controls.XmlnsDefinitionAttribute",
                "Microsoft.Maui.Controls.Xaml.XmlnsDefinitionAttribute"
            ],
            XmlnsPrefixAttributeMetadataNames:
            [
                "Microsoft.Maui.Controls.XmlnsPrefixAttribute",
                "Microsoft.Maui.Controls.Xaml.XmlnsPrefixAttribute"
            ],
            MarkupExtensionNamespaces:
            [
                "Microsoft.Maui.Controls",
                "Microsoft.Maui.Controls.Xaml",
                "Microsoft.Maui.Controls.Xaml.MarkupExtensions",
                "System.Windows.Markup"
            ],
            PreferredProjectXamlItemName: "MauiXaml",
            ProjectXamlItemNames:
            [
                "MauiXaml",
                "Page",
                "ApplicationDefinition",
                "EmbeddedResource",
                "Content",
                "None",
                "AdditionalFiles"
            ],
            DirectiveCompletions:
            [
                XamlLanguageFrameworkCompletion.Create("x:DataType", "x:DataType=\"$0\"", "Compiled binding data type")
            ],
            MarkupExtensionCompletions:
            [
                XamlLanguageFrameworkCompletion.Create("OnPlatform", "{OnPlatform $0}", "Platform-specific value"),
                XamlLanguageFrameworkCompletion.Create("OnFormFactor", "{OnFormFactor $0}", "Form-factor-specific value")
            ]);
    }

    public XamlLanguageFrameworkInfo Framework { get; }

    public int DetectionPriority => 400;

    public bool CanResolveFromProject(XDocument projectDocument, string projectPath)
    {
        _ = projectPath;
        return XamlLanguageFrameworkDetectionHelpers.MatchesProjectSdk(projectDocument, "Microsoft.Maui.Sdk") ||
               XamlLanguageFrameworkDetectionHelpers.HasTrueProperty(projectDocument, "UseMaui") ||
               XamlLanguageFrameworkDetectionHelpers.HasItemElement(projectDocument, "MauiXaml");
    }

    public bool CanResolveFromCompilation(Compilation compilation)
    {
        return XamlLanguageFrameworkDetectionHelpers.HasType(compilation, "Microsoft.Maui.Controls.Application") ||
               XamlLanguageFrameworkDetectionHelpers.HasAssembly(compilation, "Microsoft.Maui.Controls");
    }

    public bool CanResolveFromDocument(string filePath, string? documentText)
    {
        _ = filePath;
        return XamlLanguageFrameworkDetectionHelpers.DocumentContains(documentText, MauiDefaultXmlNamespace);
    }
}
