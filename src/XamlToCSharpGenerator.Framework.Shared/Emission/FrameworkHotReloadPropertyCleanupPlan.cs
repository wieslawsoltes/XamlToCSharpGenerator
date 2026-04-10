namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed record FrameworkHotReloadPropertyCleanupPlan(
    string OwnerTypeName,
    string FieldName,
    string? PriorityExpression);
