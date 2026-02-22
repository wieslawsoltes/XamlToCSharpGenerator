using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.NoUi.Framework;

namespace XamlToCSharpGenerator.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class NoUiXamlSourceGenerator : IIncrementalGenerator
{
    private readonly FrameworkXamlSourceGenerator _inner = new(NoUiFrameworkProfile.Instance);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        _inner.Initialize(context);
    }
}
