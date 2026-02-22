using System;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedFrameworkPropertyPayload(
    string FrameworkId,
    string? PropertyOwnerTypeName,
    string? PropertyFieldName,
    string? ValuePriorityExpression = null)
{
    public bool IsFramework(string frameworkId)
    {
        return string.Equals(FrameworkId, frameworkId, StringComparison.Ordinal);
    }
}
