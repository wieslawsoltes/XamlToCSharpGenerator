using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using XamlToCSharpGenerator.Build.Tasks;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class AvaloniaLoaderCallWeaverTests
{
    [Theory]
    [InlineData("Metadata")]
    [InlineData("Cecil")]
    public void NonStrict_Mode_Preserves_Successful_Rewrites_When_One_Helper_Is_Missing(string backend)
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
                    PublicSign: false,
                    DelaySign: false,
                    ProjectDirectory: workspaceDirectory,
                    ReferencePaths: null,
                    Backend: backend));

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

    [Theory]
    [InlineData("Metadata")]
    [InlineData("Cecil")]
    public void Rewrite_Resolves_External_References_From_MsBuild_Reference_Paths(string backend)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "il-weaver-resolver");

        try
        {
            var sourceDirectory = Path.Combine(workspaceDirectory, "source");
            var referenceDirectory = Path.Combine(workspaceDirectory, "references");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(referenceDirectory);

            var dependencyAssemblyPath = Path.Combine(referenceDirectory, "ExternalDependency.dll");
            var loaderAssemblyPath = Path.Combine(referenceDirectory, "Avalonia.Markup.Xaml.dll");
            var targetAssemblyPath = Path.Combine(sourceDirectory, "SampleApp.dll");

            EmitAssemblyToFile(
                dependencyAssemblyPath,
                """
                namespace ExternalDependency;

                public enum ExternalKind
                {
                    None = 0,
                    Primary = 1
                }
                """);
            EmitAssemblyToFile(
                loaderAssemblyPath,
                """
                using System;

                namespace Avalonia.Markup.Xaml;

                public static class AvaloniaXamlLoader
                {
                    public static void Load(object target)
                    {
                    }

                    public static void Load(IServiceProvider services, object target)
                    {
                    }
                }
                """);
            EmitAssemblyToFile(
                targetAssemblyPath,
                """
                using Avalonia.Markup.Xaml;
                using ExternalDependency;

                namespace SampleApp;

                public partial class SampleView
                {
                    public SampleView()
                    {
                        AvaloniaXamlLoader.Load(this);
                    }

                    public void Configure(ExternalKind kind = ExternalKind.Primary)
                    {
                    }

                    private static void __InitializeXamlSourceGenComponent(SampleView self)
                    {
                    }
                }
                """,
                [
                    MetadataReference.CreateFromFile(loaderAssemblyPath),
                    MetadataReference.CreateFromFile(dependencyAssemblyPath)
                ]);

            var weaver = new AvaloniaLoaderCallWeaver();
            var result = weaver.Rewrite(
                new AvaloniaLoaderCallWeaverConfiguration(
                    targetAssemblyPath,
                    FailOnMissingGeneratedInitializer: true,
                    DebugSymbols: false,
                    DebugType: null,
                    AssemblyOriginatorKeyFile: null,
                    KeyContainerName: null,
                    PublicSign: false,
                    DelaySign: false,
                    ProjectDirectory: workspaceDirectory,
                    ReferencePaths:
                    [
                        loaderAssemblyPath,
                        dependencyAssemblyPath
                    ],
                    Backend: backend));

            Assert.Empty(result.FatalErrorMessages);
            Assert.Empty(result.MissingInitializerMessages);
            Assert.Equal(1, result.RewrittenCallCount);

            using var assembly = AssemblyDefinition.ReadAssembly(targetAssemblyPath);

            AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
                assembly,
                "SampleApp.SampleView",
                ".ctor",
                explicitParameterCount: 0,
                expectedInitializerParameterTypes: ["SampleApp.SampleView"]);
        }
        finally
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(workspaceDirectory);
        }
    }

    [Theory]
    [InlineData("Metadata")]
    [InlineData("Cecil")]
    public void Rewrite_Replaces_Uri_Loader_Calls_With_SourceGenerated_Runtime_Loader(string backend)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "il-weaver-uri-loader");

        try
        {
            var sourceDirectory = Path.Combine(workspaceDirectory, "source");
            var referenceDirectory = Path.Combine(workspaceDirectory, "references");
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(referenceDirectory);

            var loaderAssemblyPath = Path.Combine(referenceDirectory, "Avalonia.Markup.Xaml.dll");
            var targetAssemblyPath = Path.Combine(sourceDirectory, "UriLoaderSample.dll");

            EmitAssemblyToFile(
                loaderAssemblyPath,
                """
                using System;

                namespace Avalonia.Markup.Xaml;

                public static class AvaloniaXamlLoader
                {
                    public static object Load(Uri uri, Uri? baseUri = null)
                    {
                        return new object();
                    }

                    public static object Load(IServiceProvider services, Uri uri, Uri? baseUri = null)
                    {
                        return new object();
                    }
                }
                """);
            EmitAssemblyToFile(
                targetAssemblyPath,
                """
                using System;
                using Avalonia.Markup.Xaml;

                namespace UriLoaderSample;

                public static class LoaderProbe
                {
                    public static object LoadAbsolute(Uri uri)
                    {
                        return AvaloniaXamlLoader.Load(uri);
                    }

                    public static object LoadRelative(IServiceProvider services, Uri uri, Uri baseUri)
                    {
                        return AvaloniaXamlLoader.Load(services, uri, baseUri);
                    }
                }
                """,
                [
                    MetadataReference.CreateFromFile(loaderAssemblyPath)
                ]);

            var weaver = new AvaloniaLoaderCallWeaver();
            var result = weaver.Rewrite(
                new AvaloniaLoaderCallWeaverConfiguration(
                    targetAssemblyPath,
                    FailOnMissingGeneratedInitializer: true,
                    DebugSymbols: false,
                    DebugType: null,
                    AssemblyOriginatorKeyFile: null,
                    KeyContainerName: null,
                    PublicSign: false,
                    DelaySign: false,
                    ProjectDirectory: workspaceDirectory,
                    ReferencePaths:
                    [
                        loaderAssemblyPath
                    ],
                    Backend: backend));

            Assert.Empty(result.FatalErrorMessages);
            Assert.Empty(result.MissingInitializerMessages);
            Assert.Equal(2, result.RewrittenCallCount);

            using var assembly = AssemblyDefinition.ReadAssembly(targetAssemblyPath);

            AvaloniaLoaderCallAssertions.AssertMethodCallsSourceGeneratedUriLoader(
                assembly,
                "UriLoaderSample.LoaderProbe",
                "LoadAbsolute",
                explicitParameterCount: 1,
                expectedLoaderParameterTypes:
                [
                    "System.Uri",
                    "System.Uri"
                ]);
            AvaloniaLoaderCallAssertions.AssertMethodCallsSourceGeneratedUriLoader(
                assembly,
                "UriLoaderSample.LoaderProbe",
                "LoadRelative",
                explicitParameterCount: 3,
                expectedLoaderParameterTypes:
                [
                    "System.IServiceProvider",
                    "System.Uri",
                    "System.Uri"
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

    private static void EmitAssemblyToFile(
        string outputPath,
        string source,
        IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        string assemblyName = Path.GetFileNameWithoutExtension(outputPath);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: assemblyName + ".cs");
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location)
        };

        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var emitResult = compilation.Emit(outputPath);
        Assert.True(
            emitResult.Success,
            "Failed to emit test assembly '" + assemblyName + "': " +
            string.Join(Environment.NewLine, emitResult.Diagnostics));
    }
}
