namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record MarkupContextTokenSet(
    string ServiceProviderToken,
    string RootObjectToken,
    string IntermediateRootObjectToken,
    string TargetObjectToken,
    string TargetPropertyToken,
    string BaseUriToken,
    string ParentStackToken);
