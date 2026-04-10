using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Core.Diagnostics;
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
        try
        {
            XamlSourceGeneratorCompilerHost.Initialize(context, _frameworkProfile);
        }
        catch (Exception ex)
        {
            var message = $"[{_frameworkProfile.Id}] generator initialization failed: {ex}";
            var messageProvider = context.CompilationProvider.Select((_, _) => message);
            context.RegisterSourceOutput(messageProvider, static (sourceContext, reportedMessage) =>
                sourceContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticCatalog.InternalError,
                    Location.None,
                    reportedMessage)));
        }
    }
}
