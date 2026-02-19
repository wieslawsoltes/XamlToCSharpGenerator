using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit.Abstractions;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class DifferentialFeatureCorpusTests
{
    private readonly ITestOutputHelper _output;

    public DifferentialFeatureCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> FeatureFixtures()
    {
        yield return new object[] { CreateBindingFixture() };
        yield return new object[] { CreateStyleFixture() };
        yield return new object[] { CreateTemplateFixture() };
        yield return new object[] { CreateIncludeResourceFixture() };
        yield return new object[] { CreateNameScopeReferenceFixture() };
        yield return new object[] { CreateDeferredTemplateResourceFixture() };
        yield return new object[] { CreateConstructionGrammarFixture() };
        yield return new object[] { CreateMarkupExtensionFallbackFixture() };
    }

    [Theory]
    [MemberData(nameof(FeatureFixtures))]
    public void Feature_Tagged_Fixture_Has_Equivalent_Backend_Build_Diagnostics(DifferentialFixture fixture)
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var runtimeProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime", "XamlToCSharpGenerator.Runtime.csproj");
        var coreProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Core", "XamlToCSharpGenerator.Core.csproj");
        var avaloniaProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "XamlToCSharpGenerator.Avalonia.csproj");
        var generatorProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj");

        var tempDir = Path.Combine(Path.GetTempPath(), "XamlToCSharpGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var projectPath = Path.Combine(tempDir, "DifferentialFeatureFixture.csproj");
            File.WriteAllText(projectPath, BuildProjectText(
                NormalizeForMsBuild(propsPath),
                NormalizeForMsBuild(targetsPath),
                NormalizeForMsBuild(runtimeProject),
                NormalizeForMsBuild(coreProject),
                NormalizeForMsBuild(avaloniaProject),
                NormalizeForMsBuild(generatorProject)));

            foreach (var file in fixture.Files)
            {
                File.WriteAllText(Path.Combine(tempDir, file.Key), file.Value);
            }

            var restore = RunProcess(tempDir, "dotnet", $"restore \"{projectPath}\" --nologo");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var sourceGenBuild = BuildFixture(projectPath, tempDir, backend: "SourceGen");
            Assert.True(sourceGenBuild.ExitCode == 0, sourceGenBuild.Output);
            Assert.DoesNotContain("CS8785", sourceGenBuild.Output, StringComparison.Ordinal);

            var sourceGenGeneratedDirectory = Path.Combine(tempDir, "obj", "generated");
            var sourceGenGeneratedFiles = Directory.Exists(sourceGenGeneratedDirectory)
                ? Directory.GetFiles(sourceGenGeneratedDirectory, "*.XamlSourceGen.g.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();
            Assert.NotEmpty(sourceGenGeneratedFiles);

            var sourceGenAssemblyPath = Path.Combine(tempDir, "bin", "Debug", "net10.0", "DifferentialFeatureFixture.dll");
            Assert.True(File.Exists(sourceGenAssemblyPath), sourceGenBuild.Output);

            var clean = RunProcess(
                tempDir,
                "dotnet",
                $"clean \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(clean.ExitCode == 0, clean.Output);

            var xamlIlBuild = BuildFixture(projectPath, tempDir, backend: "XamlIl");
            Assert.True(xamlIlBuild.ExitCode == 0, xamlIlBuild.Output);
            Assert.DoesNotContain("CS8785", xamlIlBuild.Output, StringComparison.Ordinal);

            var xamlIlAssemblyPath = Path.Combine(tempDir, "bin", "Debug", "net10.0", "DifferentialFeatureFixture.dll");
            Assert.True(File.Exists(xamlIlAssemblyPath), xamlIlBuild.Output);

            var sourceGenErrors = CountBuildErrors(sourceGenBuild.Output);
            var xamlIlErrors = CountBuildErrors(xamlIlBuild.Output);
            Assert.Equal(0, sourceGenErrors);
            Assert.Equal(0, xamlIlErrors);

            var sourceGenAxsg = ExtractAxsgDiagnostics(sourceGenBuild.Output);
            var xamlIlAxsg = ExtractAxsgDiagnostics(xamlIlBuild.Output);
            var sourceGenAxsgErrors = ExtractAxsgErrors(sourceGenBuild.Output);
            Assert.Empty(sourceGenAxsgErrors);

            _output.WriteLine($"DIFF|{fixture.FeatureTag}|sourcegen_errors={sourceGenErrors}|xamlil_errors={xamlIlErrors}");
            _output.WriteLine($"DIFF|{fixture.FeatureTag}|sourcegen_axsg={string.Join(',', sourceGenAxsg)}");
            _output.WriteLine($"DIFF|{fixture.FeatureTag}|sourcegen_axsg_errors={string.Join(',', sourceGenAxsgErrors)}");
            _output.WriteLine($"DIFF|{fixture.FeatureTag}|xamlil_axsg={string.Join(',', xamlIlAxsg)}");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static DifferentialFixture CreateBindingFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Styles />
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <StackPanel>
                        <TextBlock x:Name="Output" Text="{Binding Message}" />
                    </StackPanel>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public sealed class BindingViewModel
                {
                    public string Message { get; } = "HelloBinding";
                }

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        DataContext = new BindingViewModel();
                        // Build-only differential fixture.
                    }
                }
                """
        };

        return new DifferentialFixture("binding-basic", "bindings", files);
    }

    private static DifferentialFixture CreateStyleFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Styles>
                    <Style Selector="TextBlock.highlight">
                      <Setter Property="FontSize" Value="17" />
                    </Style>
                  </Application.Styles>
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <StackPanel>
                        <TextBlock x:Name="Styled" Classes="highlight" Text="StyleProbe" />
                    </StackPanel>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """
        };

        return new DifferentialFixture("style-selector", "styles", files);
    }

    private static DifferentialFixture CreateTemplateFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Styles />
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <ContentControl x:Name="ContentHost"
                                    Content="TemplateValue">
                        <ContentControl.ContentTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding}" />
                            </DataTemplate>
                        </ContentControl.ContentTemplate>
                    </ContentControl>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """
        };

        return new DifferentialFixture("template-basic", "templates", files);
    }

    private static DifferentialFixture CreateIncludeResourceFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Resources>
                    <ResourceDictionary>
                      <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude Source="/Colors.axaml" />
                      </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                  </Application.Resources>
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["Colors.axaml"] = """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="AccentBrush">#FF3A7AFE</SolidColorBrush>
                </ResourceDictionary>
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <Border x:Name="TargetBorder"
                            Width="8"
                            Height="8"
                            Background="{StaticResource AccentBrush}" />
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """
        };

        return new DifferentialFixture("include-resource", "resources", files);
    }

    private static DifferentialFixture CreateNameScopeReferenceFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Styles />
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <StackPanel>
                        <TextBox x:Name="SearchBox" Text="Needle" />
                        <TextBlock Text="{Binding Text, ElementName=SearchBox}" />
                    </StackPanel>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """
        };

        return new DifferentialFixture("namescope-reference-basic", "basictests-namescope-reference", files);
    }

    private static DifferentialFixture CreateDeferredTemplateResourceFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Resources>
                    <ResourceDictionary>
                      <SolidColorBrush x:Key="AccentBrush">Red</SolidColorBrush>
                    </ResourceDictionary>
                  </Application.Resources>
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <ContentControl Content="TemplateValue">
                        <ContentControl.ContentTemplate>
                            <DataTemplate>
                                <TextBlock Foreground="{StaticResource AccentBrush}"
                                           Text="{Binding}" />
                            </DataTemplate>
                        </ContentControl.ContentTemplate>
                    </ContentControl>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """
        };

        return new DifferentialFixture("deferred-template-resource-basic", "basictests-deferred-template-resource", files);
    }

    private static DifferentialFixture CreateConstructionGrammarFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Styles />
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialFeatureFixture"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <StackPanel>
                        <local:PairControl>
                            <x:Arguments>
                                <x:Int32>7</x:Int32>
                                <x:String>west</x:String>
                            </x:Arguments>
                        </local:PairControl>
                    </StackPanel>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["ConstructionTypes.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public sealed class PairControl : Control
                {
                    public PairControl(int left, string right)
                    {
                    }
                }
                """
        };

        return new DifferentialFixture("construction-grammar", "construction-grammar", files);
    }

    private static DifferentialFixture CreateMarkupExtensionFallbackFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Styles />
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialFeatureFixture"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <TextBlock Text="{local:Echo Hello, Prefix='>>'}" />
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["EchoExtension.cs"] = """
                using System;
                using Avalonia.Markup.Xaml;

                namespace DifferentialFeatureFixture;

                public sealed class EchoExtension : MarkupExtension
                {
                    public EchoExtension(string value)
                    {
                        Value = value;
                    }

                    public string Value { get; }

                    public string Prefix { get; set; } = string.Empty;

                    public override object? ProvideValue(IServiceProvider serviceProvider)
                    {
                        return Prefix + Value;
                    }
                }
                """
        };

        return new DifferentialFixture("markup-extension-fallback", "markup-extension-fallback", files);
    }

    private static DifferentialFixture CreateConstructionGrammarExtendedFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.axaml"] = """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFeatureFixture.App">
                  <Application.Styles />
                </Application>
                """,
            ["App.axaml.cs"] = """
                using Avalonia;

                namespace DifferentialFeatureFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialFeatureFixture"
                             x:Class="DifferentialFeatureFixture.MainView">
                    <StackPanel>
                        <local:GenericFactoryHolder x:TypeArguments="local:Payload" x:FactoryMethod="Create">
                            <x:Arguments>
                                <local:Payload Value="FromFactory" />
                            </x:Arguments>
                        </local:GenericFactoryHolder>
                    </StackPanel>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        // Build-only differential fixture.
                    }
                }
                """,
            ["ConstructionTypes.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialFeatureFixture;

                public sealed class Payload
                {
                    public string Value { get; set; } = string.Empty;
                }

                public sealed class GenericFactoryHolder<T> : Control
                {
                    public GenericFactoryHolder(T value)
                    {
                        Value = value;
                    }

                    public T Value { get; }

                    public static GenericFactoryHolder<T> Create(T value) => new(value);
                }
                """
        };

        return new DifferentialFixture("construction-grammar-extended", "construction-grammar-extended", files);
    }

    private static (int ExitCode, string Output) BuildFixture(string projectPath, string workingDirectory, string backend)
    {
        var arguments =
            $"build \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers " +
            $"-p:AvaloniaXamlCompilerBackend={backend}";
        return RunProcess(workingDirectory, "dotnet", arguments);
    }

    private static string BuildProjectText(
        string propsPath,
        string targetsPath,
        string runtimeProject,
        string coreProject,
        string avaloniaProject,
        string generatorProject)
    {
        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <Import Project="{propsPath}" Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'" />

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
  </ItemGroup>

  <ItemGroup Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'">
    <ProjectReference Include="{runtimeProject}" />
    <ProjectReference Include="{coreProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{avaloniaProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{generatorProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <Import Project="{targetsPath}" Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'" />
</Project>
""";
    }

    private static int CountBuildErrors(string output)
    {
        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Count(static line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] ExtractAxsgDiagnostics(string output)
    {
        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Contains("AXSG", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static line => line, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ExtractAxsgErrors(string output)
    {
        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line =>
                line.Contains("AXSG", StringComparison.Ordinal) &&
                line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static line => line, StringComparer.Ordinal)
            .ToArray();
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

    private static string NormalizeForMsBuild(string path)
    {
        return path.Replace('\\', '/');
    }

    public sealed record DifferentialFixture(string Id, string FeatureTag, IReadOnlyDictionary<string, string> Files)
    {
        public override string ToString()
        {
            return Id;
        }
    }
}
