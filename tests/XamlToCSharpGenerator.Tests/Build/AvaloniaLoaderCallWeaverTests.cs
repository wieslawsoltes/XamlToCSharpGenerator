using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using XamlToCSharpGenerator.Build.Tasks;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class AvaloniaLoaderCallWeaverTests
{
    [Fact]
    public void NonStrict_Mode_Preserves_Successful_Rewrites_When_One_Helper_Is_Missing()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "il-weaver-best-effort");

        try
        {
            var sourceAssemblyPath = SourceGenIlWeavingSampleBuildHarness.GetDebugAssemblyPath();
            var targetAssemblyPath = Path.Combine(workspaceDirectory, "SourceGenIlWeavingSample.dll");
            File.Copy(sourceAssemblyPath, targetAssemblyPath, overwrite: true);

            PrepareAssemblyForBestEffortWeaving(targetAssemblyPath);

            var weaver = new AvaloniaLoaderCallWeaver();
            var result = weaver.Rewrite(
                new AvaloniaLoaderCallWeaverConfiguration(
                    targetAssemblyPath,
                    FailOnMissingGeneratedInitializer: false,
                    DebugSymbols: false,
                    DebugType: null,
                    AssemblyOriginatorKeyFile: null,
                    KeyContainerName: null,
                    ProjectDirectory: workspaceDirectory));

            Assert.Empty(result.FatalErrorMessages);
            Assert.Single(result.MissingInitializerMessages);
            Assert.Equal(2, result.RewrittenCallCount);

            using var assembly = AssemblyDefinition.ReadAssembly(targetAssemblyPath);

            AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
                assembly,
                "SourceGenIlWeavingSample.App",
                "Initialize",
                explicitParameterCount: 0,
                expectedInitializerParameterTypes: ["SourceGenIlWeavingSample.App"]);
            AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
                assembly,
                "SourceGenIlWeavingSample.MainWindow",
                ".ctor",
                explicitParameterCount: 0,
                expectedLoaderParameterTypes: ["System.Object"]);
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
        }
        finally
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(workspaceDirectory);
        }
    }

    private static void PrepareAssemblyForBestEffortWeaving(string assemblyPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters
            {
                InMemory = true
            });
        var module = assembly.MainModule;
        var loaderType = new TypeReference(
            "Avalonia.Markup.Xaml",
            "AvaloniaXamlLoader",
            module,
            module.AssemblyReferences.First(reference => string.Equals(reference.Name, "Avalonia.Markup.Xaml", StringComparison.Ordinal)));
        var oneArgLoader = module.ImportReference(CreateLoaderMethodReference(module, loaderType, includeServiceProvider: false));
        var twoArgLoader = module.ImportReference(CreateLoaderMethodReference(module, loaderType, includeServiceProvider: true));

        ReplaceGeneratedInitializerCall(
            module.Types.Single(type => string.Equals(type.FullName, "SourceGenIlWeavingSample.App", StringComparison.Ordinal))
                .Methods.Single(method => string.Equals(method.Name, "Initialize", StringComparison.Ordinal)),
            oneArgLoader);

        var mainWindowType = assembly.MainModule.Types.Single(type => string.Equals(type.FullName, "SourceGenIlWeavingSample.MainWindow", StringComparison.Ordinal));
        ReplaceGeneratedInitializerCall(
            mainWindowType.Methods.Single(method => string.Equals(method.Name, ".ctor", StringComparison.Ordinal)),
            oneArgLoader);

        ReplaceGeneratedInitializerCall(
            module.Types.Single(type => string.Equals(type.FullName, "SourceGenIlWeavingSample.ServiceProviderPanel", StringComparison.Ordinal))
                .Methods.Single(method => string.Equals(method.Name, ".ctor", StringComparison.Ordinal) && method.Parameters.Count == 1),
            twoArgLoader);

        var initializer = mainWindowType.Methods.Single(
            method =>
                method.IsStatic &&
                string.Equals(method.Name, "__InitializeXamlSourceGenComponent", StringComparison.Ordinal) &&
                method.Parameters.Count == 1 &&
                string.Equals(method.Parameters[0].ParameterType.FullName, mainWindowType.FullName, StringComparison.Ordinal));

        mainWindowType.Methods.Remove(initializer);
        assembly.Write(assemblyPath);
    }

    private static void ReplaceGeneratedInitializerCall(MethodDefinition method, MethodReference loaderMethod)
    {
        var targetInstruction = method.Body.Instructions.Single(
            instruction =>
                instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call &&
                instruction.Operand is MethodReference calledMethod &&
                string.Equals(calledMethod.Name, "__InitializeXamlSourceGenComponent", StringComparison.Ordinal));

        targetInstruction.Operand = loaderMethod;
    }

    private static MethodReference CreateLoaderMethodReference(ModuleDefinition module, TypeReference loaderType, bool includeServiceProvider)
    {
        var method = new MethodReference("Load", module.TypeSystem.Void, loaderType)
        {
            HasThis = false
        };

        if (includeServiceProvider)
        {
            method.Parameters.Add(
                new ParameterDefinition(
                    new TypeReference("System", "IServiceProvider", module, module.TypeSystem.CoreLibrary)));
        }

        method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        return method;
    }
}
