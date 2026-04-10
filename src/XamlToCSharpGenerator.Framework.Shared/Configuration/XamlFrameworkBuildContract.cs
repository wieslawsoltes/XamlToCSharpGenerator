using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Configuration;

public sealed class XamlFrameworkBuildContract : IXamlFrameworkBuildContract
{
    private readonly ImmutableHashSet<string> _extensions;
    private readonly bool _allowMissingSourceItemGroup;

    public XamlFrameworkBuildContract(
        string xamlSourceItemGroup,
        string transformRuleSourceItemGroup,
        IEnumerable<string> extensions,
        bool allowMissingSourceItemGroup)
    {
        XamlSourceItemGroup = xamlSourceItemGroup;
        TransformRuleSourceItemGroup = transformRuleSourceItemGroup;
        _allowMissingSourceItemGroup = allowMissingSourceItemGroup;
        _extensions = extensions.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string SourceItemGroupMetadataName => "build_metadata.AdditionalFiles.SourceItemGroup";

    public string TargetPathMetadataName => "build_metadata.AdditionalFiles.TargetPath";

    public string XamlSourceItemGroup { get; }

    public string TransformRuleSourceItemGroup { get; }

    public bool IsXamlPath(string path)
    {
        return _extensions.Contains(Path.GetExtension(path));
    }

    public bool IsXamlSourceItemGroup(string? sourceItemGroup)
    {
        if (string.IsNullOrWhiteSpace(sourceItemGroup))
        {
            return _allowMissingSourceItemGroup;
        }

        return string.Equals(sourceItemGroup!.Trim(), XamlSourceItemGroup, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTransformRuleSourceItemGroup(string? sourceItemGroup)
    {
        return string.Equals(sourceItemGroup?.Trim(), TransformRuleSourceItemGroup, StringComparison.OrdinalIgnoreCase);
    }

    public string NormalizeSourceItemGroup(string? sourceItemGroup)
    {
        if (string.IsNullOrWhiteSpace(sourceItemGroup))
        {
            return XamlSourceItemGroup;
        }

        var normalized = sourceItemGroup!.Trim();
        return IsXamlSourceItemGroup(normalized)
            ? normalized
            : XamlSourceItemGroup;
    }
}
