using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class FluentThemeComparisonTests
{
    private const int ProcessTimeoutMilliseconds = 300_000;

    [Fact]
    public void FluentTheme_Rebuild_Has_No_Selector_Conversion_Or_CrossFile_ControlTheme_BasedOn_Warnings()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "samples",
            "Avalonia.Themes.Fluent",
            "Avalonia.Themes.Fluent.csproj");

        var result = RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{projectPath}\" --nologo -v:minimal -t:Rebuild -m:1 /nodeReuse:false --disable-build-servers -p:AvaloniaXamlCompilerBackend=SourceGen");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("warning AXSG0102", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0305", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0501", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0500", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0110", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0111", result.Output, StringComparison.Ordinal);

        var xamlIlResult = RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{projectPath}\" --nologo -v:minimal -t:Rebuild -m:1 /nodeReuse:false --disable-build-servers -p:AvaloniaXamlCompilerBackend=XamlIl");

        Assert.True(xamlIlResult.ExitCode == 0, xamlIlResult.Output);
    }

    [Fact]
    public void FluentTheme_SourceGen_Emits_Expected_Include_And_Resource_Wiring()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "samples",
            "Avalonia.Themes.Fluent",
            "Avalonia.Themes.Fluent.csproj");

        var sourceGenResult = RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{projectPath}\" --nologo -v:minimal -t:Rebuild -m:1 /nodeReuse:false --disable-build-servers -p:AvaloniaXamlCompilerBackend=SourceGen");

        Assert.True(sourceGenResult.ExitCode == 0, sourceGenResult.Output);

        var generatedRoot = Path.Combine(
            repositoryRoot,
            "samples",
            "Avalonia.Themes.Fluent",
            "obj",
            "GeneratedFiles");
        var generatedFiles = Directory.GetFiles(
                generatedRoot,
                "Avalonia.Themes.Fluent.FluentTheme.*.XamlSourceGen.g.cs",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(generatedFiles);

        var generatedSource = File.ReadAllText(generatedFiles[^1]);
        Assert.Contains(
            "XamlIncludeRegistry.Register(\"avares://Avalonia.Themes.Fluent/FluentTheme.xaml\", \"StyleInclude\", \"/Controls/FluentControls.xaml\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "XamlIncludeRegistry.Register(\"avares://Avalonia.Themes.Fluent/FluentTheme.xaml\", \"ResourceInclude\", \"/DensityStyles/Compact.xaml\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "__TryAddToDictionary(__n0, \"CompactStyles\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "__root.Resources = (global::Avalonia.Controls.IResourceDictionary)(__n0);",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::Avalonia.Markup.Xaml.Styling.StyleInclude",
            generatedSource,
            StringComparison.Ordinal);

        var sliderGeneratedFiles = Directory.GetFiles(
                generatedRoot,
                "XamlToCSharpGenerator.Generated.GeneratedXaml_Slider_*.XamlSourceGen.g.cs",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(sliderGeneratedFiles);

        var sliderGeneratedSource = File.ReadAllText(sliderGeneratedFiles[^1]);
        Assert.Contains(
            "AttachBindingNameScope(new global::Avalonia.Data.Binding(\"Thumb.Bounds\") { ElementName = \"PART_Track\", Priority = global::Avalonia.Data.BindingPriority.Style }",
            sliderGeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FluentTheme_Runtime_Probe_Matches_Selected_SourceGen_And_XamlIl_Behavior()
    {
        var repositoryRoot = GetRepositoryRoot();
        var probeProjectPath = Path.Combine(
            repositoryRoot,
            "tests",
            "FluentTheme.RuntimeProbe",
            "FluentTheme.RuntimeProbe.csproj");

        var sourceGenProbe = RunProbe(repositoryRoot, probeProjectPath, backend: "SourceGen");
        var xamlIlProbe = RunProbe(repositoryRoot, probeProjectPath, backend: "XamlIl");

        AssertSelectedControlThemeParity(sourceGenProbe, xamlIlProbe, "Theme.Button");
        AssertSelectedControlThemeParity(sourceGenProbe, xamlIlProbe, "Theme.TextBox");
        AssertSelectedControlThemeParity(sourceGenProbe, xamlIlProbe, "Theme.Window");
        AssertSelectedControlThemeParity(sourceGenProbe, xamlIlProbe, "Theme.FluentTextBoxButton");

        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.ButtonPadding.Default.Found"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.ButtonPadding.Default.Found"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.SystemControlForegroundBaseHighBrush.Default.Found"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.SystemControlForegroundBaseHighBrush.Default.Found"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.HorizontalMenuFlyoutPresenter.Default.Found"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.HorizontalMenuFlyoutPresenter.Default.Found"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Theme.ResourceCount"),
            GetRequiredProbeValue(sourceGenProbe, "Theme.ResourceCount"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Theme.Resources.MergedCount"),
            GetRequiredProbeValue(sourceGenProbe, "Theme.Resources.MergedCount"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.ButtonPadding.Compact.Summary"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.ButtonPadding.Compact.Summary"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.ButtonPadding.Default.Summary"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.ButtonPadding.Default.Summary"));

        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Button.ThemeFound"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Button.ThemeFound"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Button.TemplateFound"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Button.TemplateFound"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Button.Applied"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Button.Applied"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Button.VisualChildren"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Button.VisualChildren"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.TextBox.Applied"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.TextBox.Applied"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.TextBox.VisualChildren"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.TextBox.VisualChildren"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Window.ThemeFound"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.ThemeFound"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Window.TemplateFound"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.TemplateFound"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Window.RootType"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.RootType"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Window.OverlayLayerFound"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.OverlayLayerFound"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TemplateApply.Window.TitleBarFound"),
            GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.TitleBarFound"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Button.Applied"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.TextBox.ThemeFound"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.TextBox.TemplateFound"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.TextBox.Applied"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.ThemeFound"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.TemplateFound"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.OverlayLayerFound"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, "TemplateApply.Window.TitleBarFound"));
        Assert.DoesNotContain(sourceGenProbe.Keys, key => key.StartsWith("TemplateApply.TextBox.Exception.", StringComparison.Ordinal));
        Assert.DoesNotContain(sourceGenProbe.Keys, key => key.StartsWith("TemplateApply.Button.Exception.", StringComparison.Ordinal));
        Assert.DoesNotContain(sourceGenProbe.Keys, key => key.StartsWith("TemplateApply.Window.Exception.", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> RunProbe(string repositoryRoot, string probeProjectPath, string backend)
    {
        var buildResult = RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{probeProjectPath}\" --nologo -v:minimal -m:1 /nodeReuse:false --disable-build-servers -p:AvaloniaXamlCompilerBackend={backend}");
        Assert.True(buildResult.ExitCode == 0, buildResult.Output);

        var probeDllPath = Path.Combine(
            Path.GetDirectoryName(probeProjectPath)!,
            "bin",
            "Debug",
            "net10.0",
            "FluentTheme.RuntimeProbe.dll");
        Assert.True(File.Exists(probeDllPath), buildResult.Output);

        var runResult = RunProcess(
            repositoryRoot,
            "dotnet",
            $"exec \"{probeDllPath}\"");
        Assert.True(runResult.ExitCode == 0, runResult.Output);

        return ParseProbeOutput(runResult.Output);
    }

    private static void AssertSelectedControlThemeParity(
        IReadOnlyDictionary<string, string> sourceGenProbe,
        IReadOnlyDictionary<string, string> xamlIlProbe,
        string prefix)
    {
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, prefix + ".Found"),
            GetRequiredProbeValue(sourceGenProbe, prefix + ".Found"));
        Assert.Equal("true", GetRequiredProbeValue(sourceGenProbe, prefix + ".Found"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, prefix + ".HasTemplateSetter"),
            GetRequiredProbeValue(sourceGenProbe, prefix + ".HasTemplateSetter"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, prefix + ".Setters"),
            GetRequiredProbeValue(sourceGenProbe, prefix + ".Setters"));
    }

    private static Dictionary<string, string> ParseProbeOutput(string processOutput)
    {
        var jsonLine = processOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Reverse()
            .FirstOrDefault(line => line.TrimStart().StartsWith("{", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(jsonLine), processOutput);

        var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonLine!);
        Assert.NotNull(dictionary);
        return dictionary!;
    }

    private static string GetRequiredProbeValue(IReadOnlyDictionary<string, string> probe, string key)
    {
        Assert.True(probe.TryGetValue(key, out var value), $"Missing probe key '{key}'.");
        Assert.False(string.IsNullOrWhiteSpace(value), $"Probe key '{key}' is empty.");
        return value!;
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
        if (!process.WaitForExit(ProcessTimeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup for timed-out process.
            }

            return (-1, $"Timed out after {ProcessTimeoutMilliseconds}ms while running: {fileName} {arguments}");
        }

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
}
