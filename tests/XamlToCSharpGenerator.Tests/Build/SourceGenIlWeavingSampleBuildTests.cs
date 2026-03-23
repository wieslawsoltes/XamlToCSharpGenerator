using System;
using Mono.Cecil;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class SourceGenIlWeavingSampleBuildTests
{
    [Fact]
    public void Sample_Build_Rewrites_Avalonia_Loader_Calls_To_Generated_Initializers()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(SourceGenIlWeavingSampleBuildHarness.GetDebugAssemblyPath());

        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "SourceGenIlWeavingSample.App",
            "Initialize",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["SourceGenIlWeavingSample.App"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "SourceGenIlWeavingSample.MainWindow",
            ".ctor",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["SourceGenIlWeavingSample.MainWindow"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "SourceGenIlWeavingSample.ServiceProviderPanel",
            ".ctor",
            explicitParameterCount: 1,
            expectedInitializerParameterTypes:
            [
                "System.IServiceProvider",
                "SourceGenIlWeavingSample.ServiceProviderPanel"
            ]);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "SourceGenIlWeavingSample");
    }

    [Fact]
    public void Sample_Build_Can_Disable_Il_Weaving_Via_MsBuild_Flag()
    {
        var artifact = SourceGenIlWeavingSampleBuildHarness.BuildWithProperties(
            "sourcegen-il-weaving-disabled",
            ("XamlSourceGenIlWeavingEnabled", "false"));

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

            AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
                assembly,
                "SourceGenIlWeavingSample.App",
                "Initialize",
                explicitParameterCount: 0,
                expectedLoaderParameterTypes: ["System.Object"]);
            AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
                assembly,
                "SourceGenIlWeavingSample.MainWindow",
                ".ctor",
                explicitParameterCount: 0,
                expectedLoaderParameterTypes: ["System.Object"]);
            AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
                assembly,
                "SourceGenIlWeavingSample.ServiceProviderPanel",
                ".ctor",
                explicitParameterCount: 1,
                expectedLoaderParameterTypes:
                [
                    "System.IServiceProvider",
                    "System.Object"
                ]);

            Assert.DoesNotContain("[AXSG.Build] IL weaving inspected", artifact.BuildOutput, StringComparison.Ordinal);
        }
        finally
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(artifact.WorkspaceDirectory);
        }
    }

    [Fact]
    public void Sample_Build_Can_Disable_Il_Weaving_Via_Avalonia_MsBuild_Alias()
    {
        var artifact = SourceGenIlWeavingSampleBuildHarness.BuildWithProperties(
            "sourcegen-il-weaving-disabled-avalonia-alias",
            ("AvaloniaSourceGenIlWeavingEnabled", "false"));

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

            AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
                assembly,
                "SourceGenIlWeavingSample.App",
                "Initialize",
                explicitParameterCount: 0,
                expectedLoaderParameterTypes: ["System.Object"]);
            AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
                assembly,
                "SourceGenIlWeavingSample.MainWindow",
                ".ctor",
                explicitParameterCount: 0,
                expectedLoaderParameterTypes: ["System.Object"]);
            AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
                assembly,
                "SourceGenIlWeavingSample.ServiceProviderPanel",
                ".ctor",
                explicitParameterCount: 1,
                expectedLoaderParameterTypes:
                [
                    "System.IServiceProvider",
                    "System.Object"
                ]);

            Assert.DoesNotContain("[AXSG.Build] IL weaving inspected", artifact.BuildOutput, StringComparison.Ordinal);
        }
        finally
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(artifact.WorkspaceDirectory);
        }
    }
}
