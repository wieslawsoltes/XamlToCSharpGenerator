using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public static class XamlLanguageFrameworkDetectionHelpers
{
    public static bool MatchesProjectSdk(XDocument document, string sdkToken)
    {
        var sdk = document.Root?.Attribute("Sdk")?.Value?.Trim();
        return !string.IsNullOrWhiteSpace(sdk) &&
               sdk.IndexOf(sdkToken, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool HasTrueProperty(XDocument document, string propertyName)
    {
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            .Select(static element => element.Value?.Trim())
            .Any(static value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasItemElement(XDocument document, string itemName)
    {
        return document
            .Descendants()
            .Any(element => string.Equals(element.Name.LocalName, itemName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasPackageReference(XDocument document, string packageId)
    {
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
            .Any(element =>
                string.Equals(element.Attribute("Include")?.Value?.Trim(), packageId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase))?.Value?.Trim(),
                    packageId,
                    StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasType(Compilation compilation, string metadataName)
    {
        return compilation.GetTypeByMetadataName(metadataName) is not null;
    }

    public static bool HasAssembly(Compilation compilation, string assemblyName)
    {
        return compilation.SourceModule.ReferencedAssemblySymbols.Any(assembly =>
            assembly is not null &&
            string.Equals(assembly.Identity.Name, assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasAssemblyPrefix(Compilation compilation, string assemblyPrefix)
    {
        return compilation.SourceModule.ReferencedAssemblySymbols.Any(assembly =>
            assembly is not null &&
            assembly.Identity.Name.StartsWith(assemblyPrefix, StringComparison.OrdinalIgnoreCase));
    }

    public static bool DocumentContains(string? documentText, string fragment)
    {
        return !string.IsNullOrWhiteSpace(documentText) &&
               documentText.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
