namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedPropertyAssignment(
    string PropertyName,
    string ValueExpression,
    string? ClrPropertyOwnerTypeName,
    string? ClrPropertyTypeName,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null,
    ResolvedValueKind ValueKind = ResolvedValueKind.Unknown,
    bool RequiresStaticResourceResolver = false,
    ResolvedValueRequirements ValueRequirements = default,
    ResolvedFrameworkPropertyOperation? FrameworkPropertyOperation = null,
    bool PreserveBindingValue = false,
    bool RequiresObjectInitializer = false,
    string? ClrSetterUnsafeAccessorMethodName = null,
    bool IsInitOnlyClrProperty = false,
    bool IsRequiredClrProperty = false)
{
    // Compatibility constructor retained while Avalonia call sites migrate.
    public ResolvedPropertyAssignment(
        string PropertyName,
        string ValueExpression,
        string? AvaloniaPropertyOwnerTypeName,
        string? AvaloniaPropertyFieldName,
        string? ClrPropertyOwnerTypeName,
        string? ClrPropertyTypeName,
        string? BindingPriorityExpression,
        int Line,
        int Column,
        ConditionalXamlExpression? Condition = null,
        ResolvedValueKind ValueKind = ResolvedValueKind.Unknown,
        bool RequiresStaticResourceResolver = false,
        ResolvedValueRequirements ValueRequirements = default,
        bool PreserveBindingValue = false,
        bool RequiresObjectInitializer = false,
        string? ClrSetterUnsafeAccessorMethodName = null,
        bool IsInitOnlyClrProperty = false,
        bool IsRequiredClrProperty = false)
        : this(
            PropertyName,
            ValueExpression,
            ClrPropertyOwnerTypeName,
            ClrPropertyTypeName,
            Line,
            Column,
            Condition,
            ValueKind,
            RequiresStaticResourceResolver,
            ValueRequirements,
            CreateCompatibilityOperation(
                AvaloniaPropertyOwnerTypeName,
                AvaloniaPropertyFieldName,
                BindingPriorityExpression),
            PreserveBindingValue,
            RequiresObjectInitializer,
            ClrSetterUnsafeAccessorMethodName,
            IsInitOnlyClrProperty,
            IsRequiredClrProperty)
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
