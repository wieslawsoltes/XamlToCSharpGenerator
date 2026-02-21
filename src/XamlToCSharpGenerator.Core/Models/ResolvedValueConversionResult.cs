namespace XamlToCSharpGenerator.Core.Models;

public readonly record struct ResolvedValueConversionResult(
    string Expression,
    ResolvedValueKind ValueKind,
    bool RequiresRuntimeServiceProvider = false,
    bool RequiresParentStack = false,
    bool RequiresProvideValueTarget = false,
    bool RequiresRootObject = false,
    bool RequiresBaseUri = false,
    bool RequiresStaticResourceResolver = false,
    bool IsRuntimeFallback = false,
    ResolvedResourceKeyExpression? ResourceKey = null,
    ResolvedValueRequirements ValueRequirements = default)
{
    public ResolvedValueRequirements EffectiveRequirements =>
        ValueRequirements.RequiresMarkupContext
            ? ValueRequirements
            : new ResolvedValueRequirements(
                NeedsServiceProvider: RequiresRuntimeServiceProvider,
                NeedsParentStack: RequiresParentStack,
                NeedsProvideValueTarget: RequiresProvideValueTarget,
                NeedsRootObject: RequiresRootObject,
                NeedsBaseUri: RequiresBaseUri);
}
