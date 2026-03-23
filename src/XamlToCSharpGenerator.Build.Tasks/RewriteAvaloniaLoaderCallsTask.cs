using System;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace XamlToCSharpGenerator.Build.Tasks;

public sealed class RewriteAvaloniaLoaderCallsTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    public bool FailOnMissingGeneratedInitializer { get; set; } = true;

    public bool DebugSymbols { get; set; } = true;

    public string DebugType { get; set; } = string.Empty;

    public string AssemblyOriginatorKeyFile { get; set; } = string.Empty;

    public string KeyContainerName { get; set; } = string.Empty;

    public string ProjectDirectory { get; set; } = string.Empty;

    public bool Verbose { get; set; }

    public override bool Execute()
    {
        try
        {
            var weaver = new AvaloniaLoaderCallWeaver();
            var result = weaver.Rewrite(
                new AvaloniaLoaderCallWeaverConfiguration(
                    AssemblyPath,
                    FailOnMissingGeneratedInitializer,
                    DebugSymbols,
                    DebugType,
                    AssemblyOriginatorKeyFile,
                    KeyContainerName,
                    ProjectDirectory));

            foreach (var errorMessage in result.FatalErrorMessages)
            {
                Log.LogError(errorMessage);
            }

            foreach (var errorMessage in result.MissingInitializerMessages)
            {
                if (FailOnMissingGeneratedInitializer)
                {
                    Log.LogError(errorMessage);
                }
                else
                {
                    Log.LogWarning(errorMessage);
                }
            }

            if (Verbose || result.RewrittenCallCount > 0)
            {
                var importance = result.RewrittenCallCount > 0 ? MessageImportance.High : MessageImportance.Low;
                Log.LogMessage(
                    importance,
                    "[AXSG.Build] IL weaving inspected {0} type(s), matched {1} AvaloniaXamlLoader call(s), and rewrote {2} call(s) in '{3}'.",
                    result.InspectedTypeCount,
                    result.MatchedLoaderCallCount,
                    result.RewrittenCallCount,
                    AssemblyPath);
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: true, showDetail: true, file: AssemblyPath);
            return false;
        }
    }
}
