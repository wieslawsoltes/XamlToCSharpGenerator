using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlTransformConfiguration(
    ImmutableArray<XamlTypeAliasRule> TypeAliases,
    ImmutableArray<XamlPropertyAliasRule> PropertyAliases)
{
    public static XamlTransformConfiguration Empty { get; } = new(
        ImmutableArray<XamlTypeAliasRule>.Empty,
        ImmutableArray<XamlPropertyAliasRule>.Empty);
}

public sealed record XamlTypeAliasRule(
    string XmlNamespace,
    string XamlTypeName,
    string ClrTypeName,
    string Source,
    int Line,
    int Column);

public sealed record XamlPropertyAliasRule(
    string TargetTypeName,
    string XamlPropertyName,
    string? ClrPropertyName,
    string Source,
    int Line,
    int Column,
    XamlFrameworkPropertyAliasPayload? FrameworkPayload = null)
{
    // Compatibility constructor retained while Avalonia call sites migrate.
    public XamlPropertyAliasRule(
        string TargetTypeName,
        string XamlPropertyName,
        string? ClrPropertyName,
        string? AvaloniaPropertyOwnerTypeName,
        string? AvaloniaPropertyFieldName,
        string Source,
        int Line,
        int Column)
        : this(
            TargetTypeName,
            XamlPropertyName,
            ClrPropertyName,
            Source,
            Line,
            Column,
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

    private static XamlFrameworkPropertyAliasPayload? CreateCompatibilityPayload(
        string? propertyOwnerTypeName,
        string? propertyFieldName)
    {
        if (string.IsNullOrWhiteSpace(propertyOwnerTypeName) &&
            string.IsNullOrWhiteSpace(propertyFieldName))
        {
            return null;
        }

        return new XamlFrameworkPropertyAliasPayload(
            FrameworkProfileIds.Avalonia,
            propertyOwnerTypeName,
            propertyFieldName);
    }
}
