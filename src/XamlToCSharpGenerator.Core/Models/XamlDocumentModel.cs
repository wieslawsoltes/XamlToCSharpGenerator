using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlDocumentModel(
    string FilePath,
    string TargetPath,
    string? ClassFullName,
    string? ClassModifier,
    bool? Precompile,
    ImmutableDictionary<string, string> XmlNamespaces,
    XamlObjectNode RootObject,
    ImmutableArray<XamlNamedElement> NamedElements,
    ImmutableArray<XamlResourceDefinition> Resources,
    ImmutableArray<XamlTemplateDefinition> Templates,
    ImmutableArray<XamlStyleDefinition> Styles,
    ImmutableArray<XamlControlThemeDefinition> ControlThemes,
    ImmutableArray<XamlIncludeDefinition> Includes,
    bool IsValid)
{
    public bool IsClassBacked => !string.IsNullOrWhiteSpace(ClassFullName);

    public string ClassNamespace
    {
        get
        {
            if (!IsClassBacked)
            {
                return "XamlToCSharpGenerator.Generated";
            }

            var classFullName = ClassFullName!;
            var index = classFullName.LastIndexOf('.');
            return index < 0 ? string.Empty : classFullName.Substring(0, index);
        }
    }

    public string ClassName
    {
        get
        {
            if (!IsClassBacked)
            {
                return BuildClasslessArtifactName(TargetPath);
            }

            var classFullName = ClassFullName!;
            var index = classFullName.LastIndexOf('.');
            return index < 0 ? classFullName : classFullName.Substring(index + 1);
        }
    }

    private static string BuildClasslessArtifactName(string targetPath)
    {
        var normalized = targetPath.Replace('\\', '/');
        var fileToken = normalized;
        var slashIndex = fileToken.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < fileToken.Length - 1)
        {
            fileToken = fileToken.Substring(slashIndex + 1);
        }

        var dotIndex = fileToken.LastIndexOf('.');
        if (dotIndex > 0)
        {
            fileToken = fileToken.Substring(0, dotIndex);
        }

        var builder = new StringBuilder(fileToken.Length + 24);
        builder.Append("GeneratedXaml_");
        foreach (var ch in fileToken)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        if (builder.Length == "GeneratedXaml_".Length)
        {
            builder.Append("Document");
        }

        builder.Append('_');
        builder.Append(ComputeStableHashHex(normalized));
        return builder.ToString();
    }

    private static string ComputeStableHashHex(string value)
    {
        // Stable FNV-1a hash over normalized target path for deterministic artifact names.
        var hash = 2166136261u;
        foreach (var ch in value.ToLowerInvariant())
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
