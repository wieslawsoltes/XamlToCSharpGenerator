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
    private static readonly string[] RoslynTransientFailureMarkers =
    {
        "BoundStepThroughSequencePoint.<Span>k__BackingField",
        "ILOpCodeExtensions.StackPushCount"
    };
    private static readonly Lazy<FluentThemeBuildOutput> SourceGenBuildOutput =
        new(() => BuildFluentTheme("SourceGen"), isThreadSafe: true);
    private static readonly Lazy<FluentThemeBuildOutput> XamlIlBuildOutput =
        new(() => BuildFluentTheme("XamlIl"), isThreadSafe: true);
    private static readonly Lazy<Dictionary<string, string>> SourceGenProbeOutput =
        new(() => RunProbe("SourceGen"), isThreadSafe: true);
    private static readonly Lazy<Dictionary<string, string>> XamlIlProbeOutput =
        new(() => RunProbe("XamlIl"), isThreadSafe: true);

    [Fact]
    public void FluentTheme_Rebuild_Has_No_Selector_Conversion_Or_CrossFile_ControlTheme_BasedOn_Warnings()
    {
        var result = SourceGenBuildOutput.Value.BuildResult;

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("warning AXSG0102", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0305", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0501", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0500", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0110", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("warning AXSG0111", result.Output, StringComparison.Ordinal);

        var xamlIlResult = XamlIlBuildOutput.Value.BuildResult;

        Assert.True(xamlIlResult.ExitCode == 0, xamlIlResult.Output);
    }

    [Fact]
    public void FluentTheme_SourceGen_Emits_Expected_Include_And_Resource_Wiring()
    {
        var sourceGenOutput = SourceGenBuildOutput.Value;
        var sourceGenResult = sourceGenOutput.BuildResult;

        Assert.True(sourceGenResult.ExitCode == 0, sourceGenResult.Output);
        var generatedRoot = sourceGenOutput.GeneratedRoot;
        var generatedFiles = Directory.GetFiles(
                generatedRoot,
                "Avalonia.Themes.Fluent.FluentTheme.*.XamlSourceGen.g.cs",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(generatedFiles);

        var generatedSource = File.ReadAllText(generatedFiles[^1]);
        Assert.Contains(
            "XamlIncludeRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenIncludeDescriptor(\"avares://Avalonia.Themes.Fluent/FluentTheme.xaml\", \"StyleInclude\", \"/Controls/FluentControls.xaml\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "XamlIncludeRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenIncludeDescriptor(\"avares://Avalonia.Themes.Fluent/FluentTheme.xaml\", \"ResourceInclude\", \"/DensityStyles/Compact.xaml\"",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "TryAddToDictionary(__n0, \"CompactStyles\"",
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
                "*GeneratedXaml_Slider_*.XamlSourceGen.g.cs",
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
    public void FluentTheme_SourceGen_Preserves_TemplateBinding_Mode_And_Converters_For_Interactive_Controls()
    {
        var sourceGenOutput = SourceGenBuildOutput.Value;
        var sourceGenResult = sourceGenOutput.BuildResult;

        Assert.True(sourceGenResult.ExitCode == 0, sourceGenResult.Output);
        var generatedRoot = sourceGenOutput.GeneratedRoot;

        var comboBoxSources = Directory.GetFiles(
                generatedRoot,
                "*GeneratedXaml_ComboBox_*.XamlSourceGen.g.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.NotEmpty(comboBoxSources);
        Assert.Contains(
            comboBoxSources,
            source => source.Contains(
                "TemplateBinding(global::Avalonia.Controls.ComboBox.IsEditableProperty) { Converter = global::Avalonia.Data.Converters.BoolConverters.Not }",
                StringComparison.Ordinal));

        var expanderSources = Directory.GetFiles(
                generatedRoot,
                "*GeneratedXaml_Expander_*.XamlSourceGen.g.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.NotEmpty(expanderSources);
        Assert.Contains(
            expanderSources,
            source => source.Contains(
                "TemplateBinding(global::Avalonia.Controls.Expander.IsExpandedProperty) { Mode = global::Avalonia.Data.BindingMode.TwoWay }",
                StringComparison.Ordinal));

        var treeViewSources = Directory.GetFiles(
                generatedRoot,
                "*GeneratedXaml_TreeViewItem_*.XamlSourceGen.g.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.NotEmpty(treeViewSources);
        Assert.Contains(
            treeViewSources,
            source => source.Contains(
                "TemplateBinding(global::Avalonia.Controls.TreeViewItem.IsExpandedProperty) { Mode = global::Avalonia.Data.BindingMode.TwoWay }",
                StringComparison.Ordinal));

        var sliderSources = Directory.GetFiles(
                generatedRoot,
                "*GeneratedXaml_Slider_*.XamlSourceGen.g.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.NotEmpty(sliderSources);
        Assert.Contains(
            sliderSources,
            source => source.Contains(
                "TemplateBinding(global::Avalonia.Controls.Primitives.RangeBase.ValueProperty) { Mode = global::Avalonia.Data.BindingMode.TwoWay }",
                StringComparison.Ordinal));
    }

    [Fact]
    public void FluentTheme_Runtime_Probe_Matches_Selected_SourceGen_And_XamlIl_Behavior()
    {
        var sourceGenProbe = SourceGenProbeOutput.Value;
        var xamlIlProbe = XamlIlProbeOutput.Value;

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
            GetRequiredProbeValue(xamlIlProbe, "Resource.SystemControlHighlightAccentBrush.Default.Summary"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.SystemControlHighlightAccentBrush.Default.Summary"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.TextControlBorderBrushFocused.Default.Summary"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.TextControlBorderBrushFocused.Default.Summary"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.TextControlSelectionHighlightColor.Default.Summary"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.TextControlSelectionHighlightColor.Default.Summary"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "Resource.ToggleButtonBackgroundChecked.Default.Summary"),
            GetRequiredProbeValue(sourceGenProbe, "Resource.ToggleButtonBackgroundChecked.Default.Summary"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "TextBoxStates.Focus.BorderBrush"),
            GetRequiredProbeValue(sourceGenProbe, "TextBoxStates.Focus.BorderBrush"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "SliderStates.PointerOver.ThumbBackground"),
            GetRequiredProbeValue(sourceGenProbe, "SliderStates.PointerOver.ThumbBackground"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "SliderStates.Pressed.ThumbBackground"),
            GetRequiredProbeValue(sourceGenProbe, "SliderStates.Pressed.ThumbBackground"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "SliderStates.PointerOver.IncreaseBackground"),
            GetRequiredProbeValue(sourceGenProbe, "SliderStates.PointerOver.IncreaseBackground"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "SliderStates.TemplateFound"),
            GetRequiredProbeValue(sourceGenProbe, "SliderStates.TemplateFound"));
        Assert.Equal(
            GetRequiredProbeValue(xamlIlProbe, "SliderStates.ThumbFound"),
            GetRequiredProbeValue(sourceGenProbe, "SliderStates.ThumbFound"));

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

    private static FluentThemeBuildOutput BuildFluentTheme(string backend)
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "samples",
            "Avalonia.Themes.Fluent",
            "Avalonia.Themes.Fluent.csproj");

        var buildResult = RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{projectPath}\" --nologo -v:minimal -t:Rebuild -m:1 /nodeReuse:false --disable-build-servers " +
            $"-p:AvaloniaXamlCompilerBackend={backend}");
        Assert.True(buildResult.ExitCode == 0, buildResult.Output);

        return new FluentThemeBuildOutput(
            buildResult,
            Path.Combine(repositoryRoot, "samples", "Avalonia.Themes.Fluent", "obj", "GeneratedFiles"));
    }

    private static Dictionary<string, string> RunProbe(string backend)
    {
        var repositoryRoot = GetRepositoryRoot();
        var probeProjectPath = Path.Combine(
            repositoryRoot,
            "tests",
            "FluentTheme.RuntimeProbe",
            "FluentTheme.RuntimeProbe.csproj");

        var buildResult = RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{probeProjectPath}\" --nologo -v:minimal -m:1 /nodeReuse:false --disable-build-servers " +
            $"-p:AvaloniaXamlCompilerBackend={backend}");
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
        return RunProcess(workingDirectory, fileName, arguments, allowRetry: true);
    }

    private static (int ExitCode, string Output) RunProcess(
        string workingDirectory,
        string fileName,
        string arguments,
        bool allowRetry)
    {
        var effectiveArguments = NormalizeDotnetBuildArguments(fileName, arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = effectiveArguments,
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

            return (-1, $"Timed out after {ProcessTimeoutMilliseconds}ms while running: {fileName} {effectiveArguments}");
        }

        System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);

        var outputBuilder = new StringBuilder();
        outputBuilder.Append(stdoutTask.Result);
        outputBuilder.Append(stderrTask.Result);
        var output = outputBuilder.ToString();

        if (allowRetry &&
            ShouldRetryAfterTransientRoslynFailure(fileName, effectiveArguments, process.ExitCode, output))
        {
            var retry = RunProcess(workingDirectory, fileName, arguments, allowRetry: false);
            var retryOutput = new StringBuilder(output.Length + retry.Output.Length + 128);
            retryOutput.AppendLine("[Transient Roslyn compiler failure detected; retrying once.]");
            retryOutput.AppendLine(output);
            retryOutput.AppendLine("[Retry result follows:]");
            retryOutput.Append(retry.Output);
            return (retry.ExitCode, retryOutput.ToString());
        }

        return (process.ExitCode, output);
    }

    private static string NormalizeDotnetBuildArguments(string fileName, string arguments)
    {
        if (!string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
            !arguments.Contains("build", StringComparison.OrdinalIgnoreCase) ||
            (arguments.Contains("UseSharedCompilation", StringComparison.OrdinalIgnoreCase) &&
             arguments.Contains("ProduceReferenceAssembly", StringComparison.OrdinalIgnoreCase)))
        {
            return arguments;
        }

        var normalized = arguments;
        if (!normalized.Contains("UseSharedCompilation", StringComparison.OrdinalIgnoreCase))
        {
            normalized += " -p:UseSharedCompilation=false";
        }

        if (!normalized.Contains("ProduceReferenceAssembly", StringComparison.OrdinalIgnoreCase))
        {
            normalized += " -p:ProduceReferenceAssembly=false";
        }

        return normalized;
    }

    private static bool ShouldRetryAfterTransientRoslynFailure(
        string fileName,
        string arguments,
        int exitCode,
        string output)
    {
        var hasRoslynMissingMemberFailure =
            output.Contains("MissingFieldException", StringComparison.Ordinal) ||
            output.Contains("MissingMethodException", StringComparison.Ordinal);
        var hasKnownMarker = RoslynTransientFailureMarkers.Any(marker => output.Contains(marker, StringComparison.Ordinal));

        return exitCode != 0 &&
               string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) &&
               arguments.Contains("build", StringComparison.OrdinalIgnoreCase) &&
               hasRoslynMissingMemberFailure &&
               hasKnownMarker;
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    private sealed record FluentThemeBuildOutput((int ExitCode, string Output) BuildResult, string GeneratedRoot);
}
