using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedPropertyElementAssignment(
    string PropertyName,
    string? ClrPropertyOwnerTypeName,
    string? ClrPropertyTypeName,
    bool IsCollectionAdd,
    bool IsDictionaryMerge,
    ImmutableArray<ResolvedObjectNode> ObjectValues,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null,
    ResolvedFrameworkPropertyPayload? FrameworkPayload = null,
    bool PreserveBindingValue = false,
    ImmutableArray<ResolvedCollectionAddInstruction> CollectionAddInstructions = default)
{
    // Compatibility constructor retained while Avalonia call sites migrate.
    public ResolvedPropertyElementAssignment(
        string PropertyName,
        string? AvaloniaPropertyOwnerTypeName,
        string? AvaloniaPropertyFieldName,
        string? ClrPropertyOwnerTypeName,
        string? ClrPropertyTypeName,
        string? BindingPriorityExpression,
        bool IsCollectionAdd,
        bool IsDictionaryMerge,
        ImmutableArray<ResolvedObjectNode> ObjectValues,
        int Line,
        int Column,
        ConditionalXamlExpression? Condition = null,
        bool PreserveBindingValue = false,
        ImmutableArray<ResolvedCollectionAddInstruction> CollectionAddInstructions = default)
        : this(
            PropertyName,
            ClrPropertyOwnerTypeName,
            ClrPropertyTypeName,
            IsCollectionAdd,
            IsDictionaryMerge,
            ObjectValues,
            Line,
            Column,
            Condition,
            CreateCompatibilityPayload(
                AvaloniaPropertyOwnerTypeName,
                AvaloniaPropertyFieldName,
                BindingPriorityExpression),
            PreserveBindingValue,
            CollectionAddInstructions)
    {
    }

    public string? AvaloniaPropertyOwnerTypeName =>
        GetFrameworkPropertyOwnerTypeName(FrameworkProfileIds.Avalonia);

    public string? AvaloniaPropertyFieldName =>
        GetFrameworkPropertyFieldName(FrameworkProfileIds.Avalonia);

    public string? BindingPriorityExpression =>
        GetFrameworkValuePriorityExpression(FrameworkProfileIds.Avalonia);

    public string? GetFrameworkPropertyOwnerTypeName(string frameworkId)
    {
        if (FrameworkPayload is null || !FrameworkPayload.IsFramework(frameworkId))
        {
            return null;
        }

        return FrameworkPayload.PropertyOwnerTypeName;
    }

    public string? GetFrameworkPropertyFieldName(string frameworkId)
    {
        if (FrameworkPayload is null || !FrameworkPayload.IsFramework(frameworkId))
        {
            return null;
        }

        return FrameworkPayload.PropertyFieldName;
    }

    public string? GetFrameworkValuePriorityExpression(string frameworkId)
    {
        if (FrameworkPayload is null || !FrameworkPayload.IsFramework(frameworkId))
        {
            return null;
        }

        return FrameworkPayload.ValuePriorityExpression;
    }

    private static ResolvedFrameworkPropertyPayload? CreateCompatibilityPayload(
        string? propertyOwnerTypeName,
        string? propertyFieldName,
        string? valuePriorityExpression)
    {
        if (string.IsNullOrWhiteSpace(propertyOwnerTypeName) &&
            string.IsNullOrWhiteSpace(propertyFieldName) &&
            string.IsNullOrWhiteSpace(valuePriorityExpression))
        {
            return null;
        }

        return new ResolvedFrameworkPropertyPayload(
            FrameworkProfileIds.Avalonia,
            propertyOwnerTypeName,
            propertyFieldName,
            valuePriorityExpression);
    }
}
