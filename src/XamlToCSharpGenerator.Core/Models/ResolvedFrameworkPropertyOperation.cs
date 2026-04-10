using System;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedFrameworkPropertyOperation(
    string FrameworkId,
    string? PropertyOwnerTypeName,
    string? PropertyFieldName,
    string? ValuePriorityExpression = null)
{
    public bool IsFramework(string frameworkId)
    {
        return string.Equals(FrameworkId, frameworkId, StringComparison.Ordinal);
    }

    public ResolvedFrameworkPropertyPayload ToCompatibilityPayload()
    {
        return new ResolvedFrameworkPropertyPayload(
            FrameworkId,
            PropertyOwnerTypeName,
            PropertyFieldName,
            ValuePriorityExpression);
    }
}
