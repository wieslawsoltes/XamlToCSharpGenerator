using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.NoUi.Framework;

public sealed class NoUiFrameworkSemanticConventions
{
    public static XamlFrameworkSemanticConventions Instance { get; } =
        XamlFrameworkSemanticConventions.Empty;

    private NoUiFrameworkSemanticConventions()
    {
    }
}
