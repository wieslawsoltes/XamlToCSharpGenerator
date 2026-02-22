namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkBuildContract
{
    string SourceItemGroupMetadataName { get; }

    string TargetPathMetadataName { get; }

    string XamlSourceItemGroup { get; }

    string TransformRuleSourceItemGroup { get; }

    bool IsXamlPath(string path);

    bool IsXamlSourceItemGroup(string? sourceItemGroup);

    bool IsTransformRuleSourceItemGroup(string? sourceItemGroup);

    string NormalizeSourceItemGroup(string? sourceItemGroup);
}
