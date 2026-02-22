using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Generator;

internal sealed class FrameworkXamlSourceGenerator : IIncrementalGenerator
{
    private readonly IXamlFrameworkProfile _frameworkProfile;

    internal FrameworkXamlSourceGenerator(IXamlFrameworkProfile frameworkProfile)
    {
        _frameworkProfile = frameworkProfile;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        XamlSourceGeneratorCompilerHost.Initialize(context, _frameworkProfile);
    }
}
