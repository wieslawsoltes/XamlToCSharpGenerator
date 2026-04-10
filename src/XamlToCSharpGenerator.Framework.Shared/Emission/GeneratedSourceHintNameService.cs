using System;
using System.Text;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class GeneratedSourceHintNameService
{
    public string BuildHintName(
        string baseName,
        string sourceIdentity,
        bool ignoreCaseHash = false)
    {
        var sanitizedBaseName = SanitizeHintSegment(baseName);
        if (string.IsNullOrWhiteSpace(sanitizedBaseName))
        {
            sanitizedBaseName = "GeneratedXaml";
        }

        var hash = ignoreCaseHash
            ? StableHashSemantics.ComputeFnv1aHexIgnoreCase(sourceIdentity)
            : StableHashSemantics.ComputeFnv1aHex(sourceIdentity);
        return sanitizedBaseName + "." + hash + ".XamlSourceGen.g.cs";
    }

    private static string SanitizeHintSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value!.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '.' or '_' ? ch : '_');
        }

        return builder.ToString();
    }
}
