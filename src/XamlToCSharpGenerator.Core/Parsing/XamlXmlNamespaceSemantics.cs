using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlXmlNamespaceSemantics
{
    private const string ClrNamespacePrefix = "clr-namespace:";
    private const string UsingNamespacePrefix = "using:";

    public static bool TryExtractClrNamespace(string xmlNamespace, out string clrNamespace)
    {
        return TryExtractClrNamespaceReference(xmlNamespace, out clrNamespace, out _);
    }

    public static bool TryExtractClrNamespaceReference(
        string xmlNamespace,
        out string clrNamespace,
        out string? assemblySimpleName)
    {
        clrNamespace = string.Empty;
        assemblySimpleName = null;
        if (string.IsNullOrWhiteSpace(xmlNamespace))
        {
            return false;
        }

        var normalized = xmlNamespace.Trim();
        string? namespacePayload = null;
        if (normalized.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal))
        {
            namespacePayload = normalized.Substring(ClrNamespacePrefix.Length);
        }
        else if (normalized.StartsWith(UsingNamespacePrefix, StringComparison.Ordinal))
        {
            namespacePayload = normalized.Substring(UsingNamespacePrefix.Length);
        }

        if (namespacePayload is null)
        {
            return false;
        }

        foreach (var rawSegment in namespacePayload.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            if (clrNamespace.Length == 0)
            {
                clrNamespace = segment;
                continue;
            }

            if (segment.StartsWith("assembly=", StringComparison.OrdinalIgnoreCase))
            {
                var candidateAssemblyName = segment.Substring("assembly=".Length).Trim();
                if (candidateAssemblyName.Length > 0)
                {
                    assemblySimpleName = candidateAssemblyName;
                }
            }
        }

        return clrNamespace.Length > 0;
    }

    public static bool TryBuildClrNamespaceMetadataName(
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity,
        out string metadataName)
    {
        metadataName = string.Empty;
        if (string.IsNullOrWhiteSpace(xmlTypeName) ||
            !TryExtractClrNamespace(xmlNamespace, out var clrNamespace))
        {
            return false;
        }

        metadataName = clrNamespace + "." + AppendGenericArity(xmlTypeName.Trim(), genericArity);
        return true;
    }

    private static string AppendGenericArity(string name, int? genericArity)
    {
        if (genericArity is null || genericArity <= 0)
        {
            return name;
        }

        return name + "`" + genericArity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
