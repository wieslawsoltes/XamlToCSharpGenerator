namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class SetterIdentityPlanningService
{
    public string BuildIdentityKey(string propertyToken)
    {
        return propertyToken?.Trim() ?? string.Empty;
    }
}
