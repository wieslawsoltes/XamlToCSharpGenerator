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
    ResolvedFrameworkPropertyOperation? FrameworkPropertyOperation = null,
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
            CreateCompatibilityOperation(
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

    public ResolvedFrameworkPropertyPayload? FrameworkPayload =>
        FrameworkPropertyOperation?.ToCompatibilityPayload();

    public bool HasFrameworkPropertyOperation(string frameworkId)
    {
        return GetFrameworkPropertyOperation(frameworkId) is not null;
    }

    public ResolvedFrameworkPropertyOperation? GetFrameworkPropertyOperation(string frameworkId)
    {
        if (FrameworkPropertyOperation is null || !FrameworkPropertyOperation.IsFramework(frameworkId))
        {
            return null;
        }

        return FrameworkPropertyOperation;
    }

    public string? GetFrameworkPropertyOwnerTypeName(string frameworkId)
    {
        return GetFrameworkPropertyOperation(frameworkId)?.PropertyOwnerTypeName;
    }

    public string? GetFrameworkPropertyFieldName(string frameworkId)
    {
        return GetFrameworkPropertyOperation(frameworkId)?.PropertyFieldName;
    }

    public string? GetFrameworkValuePriorityExpression(string frameworkId)
    {
        return GetFrameworkPropertyOperation(frameworkId)?.ValuePriorityExpression;
    }

    private static ResolvedFrameworkPropertyOperation? CreateCompatibilityOperation(
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

        return new ResolvedFrameworkPropertyOperation(
            FrameworkProfileIds.Avalonia,
            propertyOwnerTypeName,
            propertyFieldName,
            valuePriorityExpression);
    }
}
