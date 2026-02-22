using System;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlFrameworkPropertyAliasPayload(
    string FrameworkId,
    string? PropertyOwnerTypeName,
    string? PropertyFieldName)
{
    public bool IsFramework(string frameworkId)
    {
        return string.Equals(FrameworkId, frameworkId, StringComparison.Ordinal);
    }
}
