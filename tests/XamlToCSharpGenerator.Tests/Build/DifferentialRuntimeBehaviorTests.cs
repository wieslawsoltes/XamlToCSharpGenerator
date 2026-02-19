using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit.Abstractions;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class DifferentialRuntimeBehaviorTests
{
    private readonly ITestOutputHelper _output;

    public DifferentialRuntimeBehaviorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> RuntimeFixtures()
    {
        yield return new object[] { CreateLifecycleInitializationFixture() };
        yield return new object[] { CreateDeferredTemplateResourceFixture() };
        yield return new object[] { CreateDeferredTemplateNestedResourceFixture() };
        yield return new object[] { CreateConstructionGrammarRuntimeFixture() };
        yield return new object[] { CreateMarkupExtensionContextRuntimeFixture() };
    }

    [Theory]
    [MemberData(nameof(RuntimeFixtures))]
    public void Runtime_Fixture_Behavior_Is_Equivalent_Between_Backends(DifferentialRuntimeFixture fixture)
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var runtimeProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime", "XamlToCSharpGenerator.Runtime.csproj");
        var coreProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Core", "XamlToCSharpGenerator.Core.csproj");
        var avaloniaProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "XamlToCSharpGenerator.Avalonia.csproj");
        var generatorProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj");

        var tempDir = Path.Combine(Path.GetTempPath(), "XamlToCSharpGenerator.Tests.Runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var projectPath = Path.Combine(tempDir, "DifferentialRuntimeFixture.csproj");
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

            var sourceGenRun = RunFixture(projectPath, tempDir, backend: "SourceGen");
            Assert.True(sourceGenRun.ExitCode == 0, sourceGenRun.Output);
            var sourceGenResult = ExtractRuntimeResult(sourceGenRun.Output);

            var clean = RunProcess(
                tempDir,
                "dotnet",
                $"clean \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(clean.ExitCode == 0, clean.Output);

            var xamlIlBuild = BuildFixture(projectPath, tempDir, backend: "XamlIl");
            Assert.True(xamlIlBuild.ExitCode == 0, xamlIlBuild.Output);
            Assert.DoesNotContain("CS8785", xamlIlBuild.Output, StringComparison.Ordinal);

            var xamlIlRun = RunFixture(projectPath, tempDir, backend: "XamlIl");
            Assert.True(xamlIlRun.ExitCode == 0, xamlIlRun.Output);
            var xamlIlResult = ExtractRuntimeResult(xamlIlRun.Output);

            _output.WriteLine($"RUNTIME-DIFF|{fixture.FeatureTag}|sourcegen={sourceGenResult}|xamlil={xamlIlResult}");
            Assert.Equal(xamlIlResult, sourceGenResult);
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

    private static DifferentialRuntimeFixture CreateLifecycleInitializationFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialRuntimeFixture"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <local:InitializationOrderTracker x:Name="Tracker" Width="100" />
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        var tracker = this.FindControl<InitializationOrderTracker>("Tracker");
                        return tracker is null ? "<missing>" : string.Join("|", tracker.Order);
                    }
                }
                """,
            ["InitializationOrderTracker.cs"] = """
                using System.Collections.Generic;
                using Avalonia;
                using Avalonia.Controls;
                using Avalonia.Metadata;

                namespace DifferentialRuntimeFixture;

                [UsableDuringInitialization]
                public sealed class InitializationOrderTracker : Border
                {
                    public List<string> Order { get; } = new();

                    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
                    {
                        if (change.Property == WidthProperty)
                        {
                            Order.Add(Parent is null ? "WidthBeforeParent" : "WidthAfterParent");
                        }

                        base.OnPropertyChanged(change);
                    }
                }
                """,
            ["Program.cs"] = """
                using System;

                var view = new DifferentialRuntimeFixture.MainView();
                Console.WriteLine("RESULT:" + view.GetReport());
                """
        };

        return new DifferentialRuntimeFixture("lifecycle-init-order", "runtime-lifecycle-order", files);
    }

    private static DifferentialRuntimeFixture CreateDeferredTemplateResourceFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <ContentControl x:Name="Host" Content="TemplateValue">
                        <ContentControl.Resources>
                            <SolidColorBrush x:Key="AccentBrush">Red</SolidColorBrush>
                        </ContentControl.Resources>
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
                using Avalonia.Media;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        var host = this.FindControl<ContentControl>("Host");
                        var built = host?.ContentTemplate?.Build(host.Content);

                        if (built is TextBlock textBlock && textBlock.Foreground is ISolidColorBrush brush)
                        {
                            return textBlock.Text + "|" + brush.Color;
                        }

                        return built?.GetType().FullName ?? "<null>";
                    }
                }
                """,
            ["Program.cs"] = """
                using System;

                var view = new DifferentialRuntimeFixture.MainView();
                Console.WriteLine("RESULT:" + view.GetReport());
                """
        };

        return new DifferentialRuntimeFixture("deferred-template-resource", "runtime-deferred-template-resource", files);
    }

    private static DifferentialRuntimeFixture CreateDeferredTemplateNestedResourceFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <ContentControl x:Name="Host" Content="OuterValue">
                        <ContentControl.Resources>
                            <SolidColorBrush x:Key="AccentBrush">#FF336699</SolidColorBrush>
                        </ContentControl.Resources>
                        <ContentControl.ContentTemplate>
                            <DataTemplate>
                                <ContentControl Content="InnerValue">
                                    <ContentControl.ContentTemplate>
                                        <DataTemplate>
                                            <TextBlock Foreground="{StaticResource AccentBrush}"
                                                       Text="{Binding}" />
                                        </DataTemplate>
                                    </ContentControl.ContentTemplate>
                                </ContentControl>
                            </DataTemplate>
                        </ContentControl.ContentTemplate>
                    </ContentControl>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;
                using Avalonia.Media;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        var host = this.FindControl<ContentControl>("Host");
                        var outer = host?.ContentTemplate?.Build(host.Content) as ContentControl;
                        var inner = outer?.ContentTemplate?.Build(outer.Content) as TextBlock;

                        if (inner?.Foreground is ISolidColorBrush brush)
                        {
                            return $"{inner.Text}|{brush.Color}";
                        }

                        return "<null>";
                    }
                }
                """,
            ["Program.cs"] = """
                using System;

                var view = new DifferentialRuntimeFixture.MainView();
                Console.WriteLine("RESULT:" + view.GetReport());
                """
        };

        return new DifferentialRuntimeFixture("deferred-template-nested-resource", "runtime-deferred-template-nested-resource", files);
    }

    private static DifferentialRuntimeFixture CreateConstructionGrammarRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialRuntimeFixture"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <StackPanel>
                        <local:PairControl x:Name="Pair">
                            <x:Arguments>
                                <x:Int32>7</x:Int32>
                                <x:String>west</x:String>
                            </x:Arguments>
                        </local:PairControl>
                    </StackPanel>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using System;
                using Avalonia.Controls;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        var pair = this.FindControl<PairControl>("Pair");
                        return $"{pair?.Left}|{pair?.Right}";
                    }
                }
                """,
            ["ConstructionTypes.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialRuntimeFixture;

                public sealed class PairControl : Control
                {
                    public PairControl(int left, string right)
                    {
                        Left = left;
                        Right = right;
                    }

                    public int Left { get; }

                    public string Right { get; }
                }

                """,
            ["Program.cs"] = """
                using System;

                var view = new DifferentialRuntimeFixture.MainView();
                Console.WriteLine("RESULT:" + view.GetReport());
                """
        };

        return new DifferentialRuntimeFixture("construction-grammar-runtime", "runtime-construction-grammar", files);
    }

    private static DifferentialRuntimeFixture CreateMarkupExtensionContextRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialRuntimeFixture"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <TextBlock x:Name="Output" Text="{local:Echo Hello, Prefix='>>'}" />
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        return this.FindControl<TextBlock>("Output")?.Text ?? "<null>";
                    }
                }
                """,
            ["EchoExtension.cs"] = """
                using System;
                using Avalonia.Markup.Xaml;
                using Avalonia.Markup.Xaml.XamlIl.Runtime;

                namespace DifferentialRuntimeFixture;

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
                        var provideValueTarget = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
                        var rootProvider = serviceProvider.GetService(typeof(IRootObjectProvider)) as IRootObjectProvider;
                        var uriContext = serviceProvider.GetService(typeof(IUriContext)) as IUriContext;
                        var parentStackProvider = serviceProvider.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) as IAvaloniaXamlIlParentStackProvider;
                        return $"{Prefix}{Value}|{(provideValueTarget?.TargetObject is not null)}|{(provideValueTarget?.TargetProperty is not null)}|{(rootProvider?.RootObject is not null)}|{uriContext?.BaseUri?.Scheme}|{(parentStackProvider is not null)}";
                    }
                }
                """,
            ["Program.cs"] = """
                using System;

                var view = new DifferentialRuntimeFixture.MainView();
                Console.WriteLine("RESULT:" + view.GetReport());
                """
        };

        return new DifferentialRuntimeFixture("markup-extension-context-runtime", "runtime-markup-extension-context", files);
    }

    private static DifferentialRuntimeFixture CreateConstructionFactoryArrayRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialRuntimeFixture"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <StackPanel>
                        <local:GenericFactoryHolder x:Name="FactoryHolder"
                                                    x:TypeArguments="local:Payload"
                                                    x:FactoryMethod="Create">
                            <x:Arguments>
                                <local:Payload Value="FromFactory" />
                            </x:Arguments>
                        </local:GenericFactoryHolder>
                    </StackPanel>
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        var holder = this.FindControl<GenericFactoryHolder<Payload>>("FactoryHolder");
                        return holder?.Value?.Value ?? "<none>";
                    }
                }
                """,
            ["ConstructionTypes.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialRuntimeFixture;

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
                """,
            ["Program.cs"] = """
                using System;

                var view = new DifferentialRuntimeFixture.MainView();
                Console.WriteLine("RESULT:" + view.GetReport());
                """
        };

        return new DifferentialRuntimeFixture(
            "construction-factory-runtime",
            "runtime-construction-factory",
            files);
    }

    private static (int ExitCode, string Output) BuildFixture(string projectPath, string workingDirectory, string backend)
    {
        var arguments =
            $"build \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers " +
            $"-p:AvaloniaXamlCompilerBackend={backend}";
        return RunProcess(workingDirectory, "dotnet", arguments);
    }

    private static (int ExitCode, string Output) RunFixture(string projectPath, string workingDirectory, string backend)
    {
        return RunProcess(
            workingDirectory,
            "dotnet",
            $"run \"{projectPath}\" --no-build --nologo -p:AvaloniaXamlCompilerBackend={backend}");
    }

    private static string ExtractRuntimeResult(string output)
    {
        var line = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Trim())
            .LastOrDefault(static value => value.StartsWith("RESULT:", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(line), "Fixture output did not contain RESULT: marker.\n" + output);
        return line![7..];
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
    <OutputType>Exe</OutputType>
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

    public sealed record DifferentialRuntimeFixture(string Id, string FeatureTag, IReadOnlyDictionary<string, string> Files)
    {
        public override string ToString()
        {
            return Id;
        }
    }
}
