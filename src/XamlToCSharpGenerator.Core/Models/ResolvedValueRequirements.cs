namespace XamlToCSharpGenerator.Core.Models;

public readonly record struct ResolvedValueRequirements(
    bool NeedsServiceProvider = false,
    bool NeedsParentStack = false,
    bool NeedsProvideValueTarget = false,
    bool NeedsRootObject = false,
    bool NeedsBaseUri = false)
{
    public static ResolvedValueRequirements None => default;

    public bool RequiresMarkupContext =>
        NeedsServiceProvider ||
        NeedsParentStack ||
        NeedsProvideValueTarget ||
        NeedsRootObject ||
        NeedsBaseUri;

    public static ResolvedValueRequirements ForMarkupExtensionRuntime(bool includeParentStack)
    {
        return new ResolvedValueRequirements(
            NeedsServiceProvider: true,
            NeedsParentStack: includeParentStack,
            NeedsProvideValueTarget: true,
            NeedsRootObject: true,
            NeedsBaseUri: true);
    }
}
