using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public interface IXamlLanguageFrameworkProvider
{
    XamlLanguageFrameworkInfo Framework { get; }

    int DetectionPriority { get; }

    bool CanResolveFromProject(XDocument projectDocument, string projectPath);

    bool CanResolveFromCompilation(Compilation compilation);

    bool CanResolveFromDocument(string filePath, string? documentText);
}
