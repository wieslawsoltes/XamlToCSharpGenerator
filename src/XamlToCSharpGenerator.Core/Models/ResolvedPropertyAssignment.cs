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
    ResolvedFrameworkPropertyPayload? FrameworkPayload = null,
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
            CreateCompatibilityPayload(
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
