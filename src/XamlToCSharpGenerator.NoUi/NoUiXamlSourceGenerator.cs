using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.NoUi.Framework;

namespace XamlToCSharpGenerator.NoUi;

[Generator(LanguageNames.CSharp)]
public sealed class NoUiXamlSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        XamlSourceGeneratorCompilerHost.Initialize(context, NoUiFrameworkProfile.Instance);
    }
}
