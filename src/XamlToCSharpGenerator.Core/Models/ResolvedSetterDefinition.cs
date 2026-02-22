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
    ResolvedFrameworkPropertyPayload? FrameworkPayload = null)
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
            CreateCompatibilityPayload(AvaloniaPropertyOwnerTypeName, AvaloniaPropertyFieldName))
    {
    }

    public string? AvaloniaPropertyOwnerTypeName =>
        GetFrameworkPropertyOwnerTypeName(FrameworkProfileIds.Avalonia);

    public string? AvaloniaPropertyFieldName =>
        GetFrameworkPropertyFieldName(FrameworkProfileIds.Avalonia);

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

    private static ResolvedFrameworkPropertyPayload? CreateCompatibilityPayload(
        string? propertyOwnerTypeName,
        string? propertyFieldName)
    {
        if (string.IsNullOrWhiteSpace(propertyOwnerTypeName) &&
            string.IsNullOrWhiteSpace(propertyFieldName))
        {
            return null;
        }

        return new ResolvedFrameworkPropertyPayload(
            FrameworkProfileIds.Avalonia,
            propertyOwnerTypeName,
            propertyFieldName);
    }
}
