using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class MsBuildCompilationProviderTests
{
    private const string MissingMetadataReferencePrefix =
        "Found project reference without a matching metadata reference:";

    [Fact]
    public async Task Repeated_Load_For_Same_Project_Does_Not_Report_Duplicate_Workspace_Error()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "axsg-msbuild-provider-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);
        try
        {
            var projectPath = Path.Combine(tempRoot, "SampleProject.csproj");
            File.WriteAllText(projectPath, """
                                       <Project Sdk="Microsoft.NET.Sdk">
                                         <PropertyGroup>
                                           <TargetFramework>net10.0</TargetFramework>
                                           <ImplicitUsings>enable</ImplicitUsings>
                                           <Nullable>enable</Nullable>
                                         </PropertyGroup>
                                       </Project>
                                       """);

            File.WriteAllText(Path.Combine(tempRoot, "SampleClass.cs"), "public sealed class SampleClass { }");
            var xamlPath = Path.Combine(tempRoot, "SampleView.axaml");
            File.WriteAllText(xamlPath, "<UserControl xmlns=\"https://github.com/avaloniaui\" />");

            using var provider = new MsBuildCompilationProvider();

            var firstSnapshot = await provider.GetCompilationAsync(xamlPath, tempRoot, CancellationToken.None);
            provider.Invalidate(xamlPath);
            var secondSnapshot = await provider.GetCompilationAsync(xamlPath, tempRoot, CancellationToken.None);

            Assert.DoesNotContain(firstSnapshot.Diagnostics, static d => d.Code == "AXSGLS0003");
            Assert.DoesNotContain(secondSnapshot.Diagnostics, static d => d.Code == "AXSGLS0003");
            Assert.NotNull(firstSnapshot.Compilation);
            Assert.NotNull(secondSnapshot.Compilation);
            Assert.Equal(firstSnapshot.ProjectPath, secondSnapshot.ProjectPath, ignoreCase: true);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup for temporary integration fixture.
            }
        }
    }

    [Fact]
    public async Task AnalyzerOnly_ProjectReference_Diagnostic_Is_Suppressed()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "axsg-msbuild-provider-analyzer-ref-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);
        try
        {
            var analyzerProjectPath = Path.Combine(tempRoot, "AnalyzerProject.csproj");
            File.WriteAllText(analyzerProjectPath, """
                                               <Project Sdk="Microsoft.NET.Sdk">
                                                 <PropertyGroup>
                                                   <TargetFramework>netstandard2.0</TargetFramework>
                                                 </PropertyGroup>
                                               </Project>
                                               """);
            File.WriteAllText(Path.Combine(tempRoot, "AnalyzerClass.cs"), "public sealed class AnalyzerClass { }");

            var appProjectPath = Path.Combine(tempRoot, "AppProject.csproj");
            File.WriteAllText(appProjectPath, """
                                          <Project Sdk="Microsoft.NET.Sdk">
                                            <PropertyGroup>
                                              <TargetFramework>net10.0</TargetFramework>
                                            </PropertyGroup>
                                            <ItemGroup>
                                              <ProjectReference Include="AnalyzerProject.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                                            </ItemGroup>
                                          </Project>
                                          """);
            File.WriteAllText(Path.Combine(tempRoot, "AppClass.cs"), "public sealed class AppClass { }");

            var xamlPath = Path.Combine(tempRoot, "MainView.axaml");
            File.WriteAllText(xamlPath, "<UserControl xmlns=\"https://github.com/avaloniaui\" />");

            using var provider = new MsBuildCompilationProvider();
            var snapshot = await provider.GetCompilationAsync(xamlPath, tempRoot, CancellationToken.None);

            Assert.DoesNotContain(snapshot.Diagnostics, static diagnostic =>
                diagnostic.Source == "MSBuildWorkspace" &&
                diagnostic.Message.StartsWith(MissingMetadataReferencePrefix, StringComparison.Ordinal));
            Assert.NotNull(snapshot.Compilation);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Canceled_Request_Does_Not_Poison_Cached_Project_Load()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "axsg-msbuild-provider-cancel-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);
        try
        {
            var projectPath = Path.Combine(tempRoot, "SampleProject.csproj");
            File.WriteAllText(projectPath, """
                                       <Project Sdk="Microsoft.NET.Sdk">
                                         <PropertyGroup>
                                           <TargetFramework>net10.0</TargetFramework>
                                           <ImplicitUsings>enable</ImplicitUsings>
                                           <Nullable>enable</Nullable>
                                         </PropertyGroup>
                                       </Project>
                                       """);

            File.WriteAllText(Path.Combine(tempRoot, "SampleClass.cs"), "public sealed class SampleClass { }");
            var xamlPath = Path.Combine(tempRoot, "SampleView.axaml");
            File.WriteAllText(xamlPath, "<UserControl xmlns=\"https://github.com/avaloniaui\" />");

            using var provider = new MsBuildCompilationProvider();
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.GetCompilationAsync(xamlPath, tempRoot, cancellationSource.Token));

            var snapshot = await provider.GetCompilationAsync(xamlPath, tempRoot, CancellationToken.None);

            Assert.DoesNotContain(snapshot.Diagnostics, static d => d.Code == "AXSGLS0003");
            Assert.NotNull(snapshot.Compilation);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }
}
