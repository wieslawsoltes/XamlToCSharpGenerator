namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedSetterDefinition(
    string PropertyName,
    string ValueExpression,
    bool IsCompiledBinding,
    string? CompiledBindingPath,
    string? CompiledBindingSourceTypeName,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null,
    ResolvedValueKind ValueKind = ResolvedValueKind.Unknown,
    bool RequiresStaticResourceResolver = false,
    ResolvedValueRequirements ValueRequirements = default,
    ResolvedFrameworkPropertyOperation? FrameworkPropertyOperation = null)
{
    // Compatibility constructor retained while Avalonia call sites migrate.
    public ResolvedSetterDefinition(
        string PropertyName,
        string ValueExpression,
        bool IsCompiledBinding,
        string? CompiledBindingPath,
        string? CompiledBindingSourceTypeName,
        string? AvaloniaPropertyOwnerTypeName,
        string? AvaloniaPropertyFieldName,
        int Line,
        int Column,
        ConditionalXamlExpression? Condition = null,
        ResolvedValueKind ValueKind = ResolvedValueKind.Unknown,
        bool RequiresStaticResourceResolver = false,
        ResolvedValueRequirements ValueRequirements = default)
        : this(
            PropertyName,
            ValueExpression,
            IsCompiledBinding,
            CompiledBindingPath,
            CompiledBindingSourceTypeName,
            Line,
            Column,
            Condition,
            ValueKind,
            RequiresStaticResourceResolver,
            ValueRequirements,
            CreateCompatibilityOperation(AvaloniaPropertyOwnerTypeName, AvaloniaPropertyFieldName))
    {
    }

    public string? AvaloniaPropertyOwnerTypeName =>
        GetFrameworkPropertyOwnerTypeName(FrameworkProfileIds.Avalonia);

    public string? AvaloniaPropertyFieldName =>
        GetFrameworkPropertyFieldName(FrameworkProfileIds.Avalonia);

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

    private static ResolvedFrameworkPropertyOperation? CreateCompatibilityOperation(
        string? propertyOwnerTypeName,
        string? propertyFieldName)
    {
        if (string.IsNullOrWhiteSpace(propertyOwnerTypeName) &&
            string.IsNullOrWhiteSpace(propertyFieldName))
        {
            return null;
        }

        return new ResolvedFrameworkPropertyOperation(
            FrameworkProfileIds.Avalonia,
            propertyOwnerTypeName,
            propertyFieldName);
    }
}
