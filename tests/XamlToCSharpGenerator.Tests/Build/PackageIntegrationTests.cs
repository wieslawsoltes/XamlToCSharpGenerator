using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class PackageIntegrationTests
{
    private const string TestSigningKeyFileName = "test-key.snk";
    private const string TestSigningKeyBase64 =
        "BwIAAAAkAABSU0EyAAQAAAEAAQDDBWf/5tzwNqatta2E9Dfl8C75JPFNQezAR6zcMrvWkeWhVuGQa9p4EBILIBwRqNIND9uNW/0rJcYca5JsaleC8KDyah3dYs2ZugMogQrVXuk2Mu3WrLcbBLRKCWKXsC/nPWSkd03l8J5JLNLqvPhXgMoOs6PZ9RbLatuOeccvlIu+u3LPAQ82hBv02SCffSKFEXuHcRqQeeawvXLucoqNXCPJj7luSvp2S1wfMgfNmX0wzJuUNCH3xjW/aCNLCJapNIZm/30TNe9pk+UGtNxlO8INlaX6sMRbA/6OyhyaeMxMdHPNK5nModzFZhEwkUMD8bqj+ctI5PHgkd/sv9n8b/dvFvH3MzfNf68nNZ3OGXpSKLgCTXAq5Y8k1tI7+i+5exXR6Y47FjCN4cRsZRqFO0gFpO44jJIg3Heon0ScZzlgk29YnxO4bh/zWZAZJ866QrkxSgN/In6PwxtlVY0D/tMKAsN2qtN5rIpecsNjGwnzumn7uhlpqORiTbOSnCzoLlOqUYzEYh+ykDAM/Vxi8Gf7zdaHG+U5+IBgmjdO0Rj9V3t3ro0tMhyHqQUdnCjLeLnYrWZqlU+h6koLYd+AEbBUjVZ26JlQBgd+0jyVUhB6lrz3oj+FQ+eyB7Kn+DgfkkDRjOuypJIK0tqPLVWWezoFtilaaNOhoGbsm+KgnG1wtRSKWDkWO0mgY69+6uFBr2a/oaWVYWM/gxr1cKRHVFlnGkpCmXO2s0CxqgnWKf3dv5ghJ2iOd5o4Mq2ACW4=";
    private const string TestPublicSigningKeyFileName = "test-public-key.snk";
    private const string TestPublicSigningKeyBase64 =
        "ACQAAASAAACUAAAABgIAAAAkAABSU0ExAAQAAAEAAQDDBWf/5tzwNqatta2E9Dfl8C75JPFNQezAR6zcMrvWkeWhVuGQa9p4EBILIBwRqNIND9uNW/0rJcYca5JsaleC8KDyah3dYs2ZugMogQrVXuk2Mu3WrLcbBLRKCWKXsC/nPWSkd03l8J5JLNLqvPhXgMoOs6PZ9RbLatuOeccvlA==";

    [Fact]
    public void TopLevel_Package_Packs_And_Contains_Expected_Assets()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator", "XamlToCSharpGenerator.csproj");
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "package-integration");
        var outputDir = Path.Combine(workspaceDirectory, "packages");

        try
        {
            Directory.CreateDirectory(outputDir);
            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                BuildPackArguments(packageProject, outputDir, workspaceDirectory, packageVersion: null));

            Assert.True(result.ExitCode == 0, result.Output);

            var packagePath = Directory.GetFiles(outputDir, "XamlToCSharpGenerator.*.nupkg")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(packagePath), result.Output);

            using var stream = File.OpenRead(packagePath!);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.props");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.targets");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.Build.Tasks.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/Mono.Cecil.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/Mono.Cecil.Pdb.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/System.Buffers.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/System.Collections.Immutable.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/System.Memory.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/System.Reflection.Metadata.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/System.Runtime.CompilerServices.Unsafe.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Generator.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Compiler.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Framework.Abstractions.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.ExpressionSemantics.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.dll");
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(workspaceDirectory);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void Build_Package_Packs_With_Custom_Output_Globals()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "XamlToCSharpGenerator.Build.csproj");
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-package-integration");
        var outputDir = Path.Combine(workspaceDirectory, "packages");

        try
        {
            Directory.CreateDirectory(outputDir);

            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                BuildPackArguments(packageProject, outputDir, workspaceDirectory, CreateUniquePackageVersion()));

            Assert.True(result.ExitCode == 0, result.Output);

            var packagePath = Directory.GetFiles(outputDir, "XamlToCSharpGenerator.Build.*.nupkg")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(packagePath), result.Output);

            using var stream = File.OpenRead(packagePath!);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.Build.props");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.Build.targets");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.Build.Tasks.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/Mono.Cecil.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/Mono.Cecil.Pdb.dll");
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(workspaceDirectory);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void RuntimeAvalonia_Package_Packs_And_Declares_Runtime_Dependencies()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime.Avalonia", "XamlToCSharpGenerator.Runtime.Avalonia.csproj");
        var outputDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "runtime-avalonia-package-integration");

        try
        {
            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{outputDir}\"");

            Assert.True(result.ExitCode == 0, result.Output);

            var packagePath = Directory.GetFiles(outputDir, "XamlToCSharpGenerator.Runtime.Avalonia.*.nupkg")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(packagePath), result.Output);

            using var stream = File.OpenRead(packagePath!);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var nuspecStream = nuspecEntry.Open();
            var nuspec = XDocument.Load(nuspecStream);
            XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
            var dependencyIds = nuspec
                .Descendants(ns + "dependency")
                .Select(static element => (string?)element.Attribute("id"))
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .ToArray();

            Assert.Contains("XamlToCSharpGenerator.MiniLanguageParsing", dependencyIds);
            Assert.Contains("XamlToCSharpGenerator.RemoteProtocol", dependencyIds);
            Assert.Contains("XamlToCSharpGenerator.Runtime.Core", dependencyIds);
            Assert.Contains("Avalonia", dependencyIds);
            Assert.Contains("Avalonia.Markup.Xaml.Loader", dependencyIds);
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(outputDir);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Rewrites_Avalonia_Loader_Calls()
    {
        using var artifact = BuildTopLevelPackageConsumer("package-il-weaving-consumer");
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.App",
            "Initialize",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["PackageIlWeavingConsumer.App"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.MainWindow",
            ".ctor",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["PackageIlWeavingConsumer.MainWindow"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.ServiceProviderPanel",
            ".ctor",
            explicitParameterCount: 1,
            expectedInitializerParameterTypes:
            [
                "System.IServiceProvider",
                "PackageIlWeavingConsumer.ServiceProviderPanel"
            ]);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");
        Assert.Contains("[AXSG.Build] IL weaving inspected", artifact.BuildOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Can_Use_Legacy_Cecil_Backend()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-cecil-backend",
            ("XamlSourceGenIlWeavingBackend", "Cecil"));
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.App",
            "Initialize",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["PackageIlWeavingConsumer.App"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.MainWindow",
            ".ctor",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["PackageIlWeavingConsumer.MainWindow"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.ServiceProviderPanel",
            ".ctor",
            explicitParameterCount: 1,
            expectedInitializerParameterTypes:
            [
                "System.IServiceProvider",
                "PackageIlWeavingConsumer.ServiceProviderPanel"
            ]);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");
        Assert.Contains("[AXSG.Build] IL weaving inspected", artifact.BuildOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Can_Disable_Il_Weaving_Via_MsBuild_Flag()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-disabled",
            ("XamlSourceGenIlWeavingEnabled", "false"));
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
            assembly,
            "PackageIlWeavingConsumer.App",
            "Initialize",
            explicitParameterCount: 0,
            expectedLoaderParameterTypes: ["System.Object"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
            assembly,
            "PackageIlWeavingConsumer.MainWindow",
            ".ctor",
            explicitParameterCount: 0,
            expectedLoaderParameterTypes: ["System.Object"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
            assembly,
            "PackageIlWeavingConsumer.ServiceProviderPanel",
            ".ctor",
            explicitParameterCount: 1,
            expectedLoaderParameterTypes:
            [
                "System.IServiceProvider",
                "System.Object"
            ]);

        Assert.DoesNotContain("[AXSG.Build] IL weaving inspected", artifact.BuildOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Rewrites_Signed_Assemblies()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-signed",
            ("SignAssembly", "true"),
            ("AssemblyOriginatorKeyFile", TestSigningKeyFileName));
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");
        Assert.True(assembly.Name.HasPublicKey);
        Assert.NotEmpty(assembly.Name.PublicKey);
        AssertStrongNameSignatureContainsNonZeroBytes(artifact.AssemblyPath);
        AssertStrongNameVerifies(artifact.AssemblyPath);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Rewrites_PublicSigned_Assemblies()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-public-signed",
            ("SignAssembly", "true"),
            ("PublicSign", "true"),
            ("AssemblyOriginatorKeyFile", TestSigningKeyFileName));
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");
        Assert.True(assembly.Name.HasPublicKey);
        Assert.NotEmpty(assembly.Name.PublicKey);
        AssertStrongNameSignatureContainsOnlyZeros(artifact.AssemblyPath);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Rewrites_PublicSigned_Assemblies_With_Public_Key_Only()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-public-signed-public-key-only",
            ("SignAssembly", "true"),
            ("PublicSign", "true"),
            ("AssemblyOriginatorKeyFile", TestPublicSigningKeyFileName));
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");
        Assert.True(assembly.Name.HasPublicKey);
        Assert.NotEmpty(assembly.Name.PublicKey);
        AssertStrongNameSignatureContainsOnlyZeros(artifact.AssemblyPath);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Rewrites_DelaySigned_Assemblies()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-delay-signed",
            ("SignAssembly", "true"),
            ("DelaySign", "true"),
            ("AssemblyOriginatorKeyFile", TestSigningKeyFileName));
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");
        Assert.True(assembly.Name.HasPublicKey);
        Assert.NotEmpty(assembly.Name.PublicKey);
        AssertStrongNameSignatureContainsOnlyZeros(artifact.AssemblyPath);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Preserves_Embedded_Pdb_Symbols()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-embedded-pdb",
            ("DebugType", "embedded"),
            ("DebugSymbols", "true"));

        var pdbPath = Path.ChangeExtension(artifact.AssemblyPath, ".pdb");
        Assert.False(File.Exists(pdbPath), artifact.BuildOutput);

        using var assembly = AssemblyDefinition.ReadAssembly(
            artifact.AssemblyPath,
            new ReaderParameters
            {
                ReadSymbols = true,
                SymbolReaderProvider = new EmbeddedPortablePdbReaderProvider()
            });

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");

        var mainWindowConstructor = assembly.MainModule.Types
            .Single(type => string.Equals(type.FullName, "PackageIlWeavingConsumer.MainWindow", StringComparison.Ordinal))
            .Methods
            .Single(method => string.Equals(method.Name, ".ctor", StringComparison.Ordinal));

        Assert.True(mainWindowConstructor.DebugInformation.HasSequencePoints);
    }

    private static (int ExitCode, string Output) RunProcess(string workingDirectory, string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);

        var outputBuilder = new StringBuilder();
        outputBuilder.Append(stdoutTask.Result);
        outputBuilder.Append(stderrTask.Result);
        return (process.ExitCode, outputBuilder.ToString());
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    private static PackageConsumerArtifact BuildTopLevelPackageConsumer(
        string scenario,
        params (string Name, string Value)[] msbuildProperties)
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator", "XamlToCSharpGenerator.csproj");
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, scenario);

        try
        {
            var packageOutputDirectory = Path.Combine(workspaceDirectory, "packages");
            var consumerDirectory = Path.Combine(workspaceDirectory, "consumer");
            Directory.CreateDirectory(packageOutputDirectory);
            Directory.CreateDirectory(consumerDirectory);

            var packageVersion = CreateUniquePackageVersion();

            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var pack = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{packageOutputDirectory}\" -p:PackageVersion={packageVersion}");
            Assert.True(pack.ExitCode == 0, pack.Output);

            WritePackageConsumerFiles(consumerDirectory, packageOutputDirectory, packageVersion, msbuildProperties);

            var projectPath = Path.Combine(consumerDirectory, "PackageIlWeavingConsumer.csproj");
            var build = RunProcess(
                consumerDirectory,
                "dotnet",
                $"build \"{projectPath}\" -t:Rebuild -c Debug --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(build.ExitCode == 0, build.Output);

            var assemblyPath = Path.Combine(consumerDirectory, "bin", "Debug", "net10.0", "PackageIlWeavingConsumer.dll");
            Assert.True(File.Exists(assemblyPath), build.Output);

            return new PackageConsumerArtifact(workspaceDirectory, assemblyPath, build.Output);
        }
        catch
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(workspaceDirectory);
            throw;
        }
    }

    private static void WritePackageConsumerFiles(
        string consumerDirectory,
        string packageOutputDirectory,
        string packageVersion,
        IReadOnlyList<(string Name, string Value)> msbuildProperties)
    {
        var projectPath = Path.Combine(consumerDirectory, "PackageIlWeavingConsumer.csproj");
        var nuGetConfigPath = Path.Combine(consumerDirectory, "NuGet.Config");
        var propertyOverrides = string.Concat(msbuildProperties.Select(static property =>
            $"\n    <{property.Name}>{property.Value}</{property.Name}>"));

        WriteRequestedSigningAssets(consumerDirectory, msbuildProperties);

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>PackageIlWeavingConsumer</AssemblyName>
    <RootNamespace>PackageIlWeavingConsumer</RootNamespace>
    <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
    <AvaloniaSourceGenUseCompiledBindingsByDefault>true</AvaloniaSourceGenUseCompiledBindingsByDefault>{{propertyOverrides}}
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="XamlToCSharpGenerator" Version="{{packageVersion}}" />
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.12" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaXaml Update="App.axaml;MainWindow.axaml;ServiceProviderPanel.axaml"
                  SubType="Designer"
                  Visible="true" />
  </ItemGroup>
</Project>
""");

        var nuGetConfig = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement(
                        "add",
                        new XAttribute("key", "local"),
                        new XAttribute("value", packageOutputDirectory)),
                    new XElement(
                        "add",
                        new XAttribute("key", "nuget.org"),
                        new XAttribute("value", "https://api.nuget.org/v3/index.json")))));
        nuGetConfig.Save(nuGetConfigPath);

        File.WriteAllText(Path.Combine(consumerDirectory, "Program.cs"), """
using System;
using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace PackageIlWeavingConsumer;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseAvaloniaSourceGeneratedXaml();
    }
}
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "App.axaml"), """
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:themes="clr-namespace:Avalonia.Themes.Fluent;assembly=Avalonia.Themes.Fluent"
             x:Class="PackageIlWeavingConsumer.App">
  <Application.Styles>
    <themes:FluentTheme />
  </Application.Styles>
</Application>
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "App.axaml.cs"), """
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PackageIlWeavingConsumer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "MainWindow.axaml"), """
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="PackageIlWeavingConsumer.MainWindow"
        Width="480"
        Height="320"
        Title="IL Weaving Package Consumer">
  <TextBlock HorizontalAlignment="Center"
             VerticalAlignment="Center"
             Text="Package consumer IL weaving integration test" />
</Window>
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "MainWindow.axaml.cs"), """
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PackageIlWeavingConsumer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "ServiceProviderPanel.axaml"), """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="PackageIlWeavingConsumer.ServiceProviderPanel">
  <TextBlock Text="Service provider panel" />
</UserControl>
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "ServiceProviderPanel.axaml.cs"), """
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PackageIlWeavingConsumer;

public partial class ServiceProviderPanel : UserControl
{
    public ServiceProviderPanel(IServiceProvider serviceProvider)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
    }
}
""");

    }

    private static void WriteRequestedSigningAssets(
        string consumerDirectory,
        IReadOnlyList<(string Name, string Value)> msbuildProperties)
    {
        foreach (var property in msbuildProperties)
        {
            if (!string.Equals(property.Name, "AssemblyOriginatorKeyFile", StringComparison.Ordinal))
            {
                continue;
            }

            var keyPath = Path.IsPathRooted(property.Value)
                ? property.Value
                : Path.Combine(consumerDirectory, property.Value);
            var keyDirectory = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrWhiteSpace(keyDirectory))
            {
                Directory.CreateDirectory(keyDirectory);
            }

            var keyContents = string.Equals(Path.GetFileName(property.Value), TestPublicSigningKeyFileName, StringComparison.Ordinal)
                ? TestPublicSigningKeyBase64
                : TestSigningKeyBase64;
            File.WriteAllBytes(keyPath, Convert.FromBase64String(keyContents));
        }
    }

    private static void AssertStrongNameVerifies(string assemblyPath)
    {
        var toolPath = FindStrongNameTool();
        if (string.IsNullOrWhiteSpace(toolPath))
        {
            return;
        }

        var verification = RunProcess(
            Path.GetDirectoryName(assemblyPath)!,
            toolPath,
            $"-q -vf \"{assemblyPath}\"");

        Assert.True(verification.ExitCode == 0, verification.Output);
    }

    private static string? FindStrongNameTool()
    {
        var monoSn = "/Library/Frameworks/Mono.framework/Versions/Current/Commands/sn";
        if (File.Exists(monoSn))
        {
            return monoSn;
        }

        return null;
    }

    private static string CreateUniquePackageVersion()
    {
        return $"1.0.0-ilweaving-test-{Guid.NewGuid():N}";
    }

    private static string BuildPackArguments(
        string packageProject,
        string outputDirectory,
        string workspaceDirectory,
        string? packageVersion)
    {
        var restorePath = EnsureTrailingSeparator(Path.Combine(workspaceDirectory, "obj", "restore"));
        var outputPath = EnsureTrailingSeparator(Path.Combine(workspaceDirectory, "artifacts", "output"));
        var runtimeIdentifier = GetCurrentTestRuntimeIdentifier();
        var arguments = new StringBuilder()
            .Append("pack \"")
            .Append(packageProject)
            .Append("\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"")
            .Append(outputDirectory)
            .Append('"')
            .Append(" -p:MSBuildProjectExtensionsPath=\"")
            .Append(NormalizeForMsBuild(restorePath))
            .Append('"')
            .Append(" -p:OutputPath=\"")
            .Append(NormalizeForMsBuild(outputPath))
            .Append('"')
            .Append(" -p:RuntimeIdentifier=")
            .Append(runtimeIdentifier);

        if (!string.IsNullOrWhiteSpace(packageVersion))
        {
            arguments.Append(" -p:PackageVersion=").Append(packageVersion);
        }

        return arguments.ToString();
    }

    private static void AssertStrongNameSignatureContainsNonZeroBytes(string assemblyPath)
    {
        Assert.True(HasNonZeroStrongNameSignature(assemblyPath), $"Expected '{assemblyPath}' to contain a non-zero strong-name signature.");
    }

    private static void AssertStrongNameSignatureContainsOnlyZeros(string assemblyPath)
    {
        Assert.False(HasNonZeroStrongNameSignature(assemblyPath), $"Expected '{assemblyPath}' to preserve a zero-filled strong-name signature blob.");
    }

    private static bool HasNonZeroStrongNameSignature(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var corHeader = peReader.PEHeaders.CorHeader;
        Assert.NotNull(corHeader);

        var signatureDirectory = corHeader!.StrongNameSignatureDirectory;
        Assert.True(signatureDirectory.Size > 0, $"Expected '{assemblyPath}' to expose a strong-name signature directory.");

        var signatureReader = peReader.GetSectionData(signatureDirectory.RelativeVirtualAddress).GetReader(0, signatureDirectory.Size);
        var signatureBytes = signatureReader.ReadBytes(signatureDirectory.Size);
        for (var index = 0; index < signatureBytes.Length; index++)
        {
            if (signatureBytes[index] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetCurrentTestRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }

        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }

    private static string NormalizeForMsBuild(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var normalized = NormalizeForMsBuild(path);
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    private sealed class PackageConsumerArtifact : IDisposable
    {
        public PackageConsumerArtifact(string workspaceDirectory, string assemblyPath, string buildOutput)
        {
            WorkspaceDirectory = workspaceDirectory;
            AssemblyPath = assemblyPath;
            BuildOutput = buildOutput;
        }

        public string WorkspaceDirectory { get; }

        public string AssemblyPath { get; }

        public string BuildOutput { get; }

        public void Dispose()
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(WorkspaceDirectory);
        }
    }
}
