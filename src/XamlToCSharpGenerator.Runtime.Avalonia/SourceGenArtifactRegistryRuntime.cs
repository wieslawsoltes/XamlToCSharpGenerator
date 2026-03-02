using System;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Centralized helpers for generated artifact registry lifecycle operations.
/// </summary>
public static class SourceGenArtifactRegistryRuntime
{
    /// <summary>
    /// Clears all generated artifact registries for a single XAML document URI.
    /// </summary>
    public static void ResetDocumentRegistries(string documentUri)
    {
        if (string.IsNullOrWhiteSpace(documentUri))
        {
            throw new ArgumentException("Document URI must be provided.", nameof(documentUri));
        }

        XamlSourceGenRegistry.Unregister(documentUri);
        XamlSourceInfoRegistry.Clear(documentUri);
        XamlResourceRegistry.Clear(documentUri);
        XamlTemplateRegistry.Clear(documentUri);
        XamlStyleRegistry.Clear(documentUri);
        XamlControlThemeRegistry.Clear(documentUri);
        XamlIncludeRegistry.Clear(documentUri);
        XamlIncludeGraphRegistry.Clear(documentUri);
        XamlCompiledBindingRegistry.Clear(documentUri);
    }
}
