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
    private static readonly string[] RoslynTransientFailureMarkers =
    {
        "BoundStepThroughSequencePoint.<Span>k__BackingField",
        "ILOpCodeExtensions.StackPushCount",
        "SignatureData.ReturnParam"
    };

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
        yield return new object[] { CreateMissingStaticResourceRuntimeFixture() };
        yield return new object[] { CreateTemplatePropertyElementRuntimeFixture() };
        yield return new object[] { CreateStyleElementNameBindingRuntimeFixture() };
        yield return new object[] { CreateTemplateElementNameBindingRuntimeFixture() };
        yield return new object[] { CreateResolveByNameRuntimeFixture() };
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
        var compilerProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Compiler", "XamlToCSharpGenerator.Compiler.csproj");
        var frameworkProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Framework.Abstractions", "XamlToCSharpGenerator.Framework.Abstractions.csproj");
        var expressionSemanticsProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.ExpressionSemantics", "XamlToCSharpGenerator.ExpressionSemantics.csproj");
        var avaloniaProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "XamlToCSharpGenerator.Avalonia.csproj");
        var miniLanguageParsingProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.MiniLanguageParsing", "XamlToCSharpGenerator.MiniLanguageParsing.csproj");
        var generatorProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj");

        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "runtime-diff");

        try
        {
            var projectPath = Path.Combine(tempDir, "DifferentialRuntimeFixture.csproj");
            File.WriteAllText(projectPath, BuildProjectText(
                NormalizeForMsBuild(propsPath),
                NormalizeForMsBuild(targetsPath),
                NormalizeForMsBuild(runtimeProject),
                NormalizeForMsBuild(coreProject),
                NormalizeForMsBuild(compilerProject),
                NormalizeForMsBuild(frameworkProject),
                NormalizeForMsBuild(expressionSemanticsProject),
                NormalizeForMsBuild(avaloniaProject),
                NormalizeForMsBuild(miniLanguageParsingProject),
                NormalizeForMsBuild(generatorProject)));

            foreach (var file in fixture.Files)
            {
                File.WriteAllText(Path.Combine(tempDir, file.Key), file.Value);
            }

            var restore = RunProcess(
                tempDir,
                "dotnet",
                $"restore \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
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
                $"clean \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers -p:BuildProjectReferences=false");
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
                BuildTestWorkspacePaths.TryDeleteDirectory(tempDir);
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

    private static DifferentialRuntimeFixture CreateMissingStaticResourceRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <Border Background="{StaticResource MissingBrush}" />
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
                }
                """,
            ["Program.cs"] = """
                using System;

                try
                {
                    _ = new DifferentialRuntimeFixture.MainView();
                    Console.WriteLine("RESULT:OK");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RESULT:EX|" + ex.GetType().FullName);
                }
                """
        };

        return new DifferentialRuntimeFixture(
            "missing-static-resource-runtime",
            "runtime-missing-static-resource",
            files);
    }

    private static DifferentialRuntimeFixture CreateTemplatePropertyElementRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:chrome="clr-namespace:Avalonia.Controls.Chrome;assembly=Avalonia.Controls"
                             xmlns:primitives="clr-namespace:Avalonia.Controls.Primitives;assembly=Avalonia.Controls"
                             xmlns:local="clr-namespace:DifferentialRuntimeFixture"
                             x:Class="DifferentialRuntimeFixture.MainView">
                  <UserControl.Resources>
                    <ControlTheme x:Key="{x:Type local:TemplateHostControl}" TargetType="local:TemplateHostControl">
                      <Setter Property="Template">
                        <ControlTemplate>
                          <primitives:VisualLayerManager>
                            <primitives:VisualLayerManager.ChromeOverlayLayer>
                              <chrome:TitleBar />
                            </primitives:VisualLayerManager.ChromeOverlayLayer>
                            <ContentPresenter Name="PART_ContentPresenter"
                                              Content="{TemplateBinding Tag}" />
                          </primitives:VisualLayerManager>
                        </ControlTemplate>
                      </Setter>
                    </ControlTheme>
                  </UserControl.Resources>
                  <local:TemplateHostControl x:Name="Host"
                                             Theme="{StaticResource {x:Type local:TemplateHostControl}}"
                                             Tag="Hello" />
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using System.Linq;
                using Avalonia.Controls;
                using Avalonia.Controls.Chrome;
                using Avalonia.Controls.Primitives;
                using Avalonia.Controls.Templates;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        var host = this.FindControl<TemplateHostControl>("Host");
                        var theme = host?.Theme as Avalonia.Styling.ControlTheme;
                        var template = theme?.Setters
                            .OfType<Avalonia.Styling.Setter>()
                            .FirstOrDefault(static setter => setter.Property == TemplatedControl.TemplateProperty)?
                            .Value as IControlTemplate;
                        if (host is null || template is null)
                        {
                            return "<missing>";
                        }

                        var templateResult = template.Build(host);
                        if (templateResult.Result is not VisualLayerManager manager)
                        {
                            return templateResult.Result?.GetType().FullName ?? "<null>";
                        }

                        var overlayLayer = manager.ChromeOverlayLayer;
                        var overlayChild = overlayLayer?.Children.OfType<TitleBar>().FirstOrDefault();
                        return $"{overlayLayer is not null}|{overlayChild is not null}";
                    }
                }
                """,
            ["TemplateHostControl.cs"] = """
                using Avalonia.Controls.Primitives;

                namespace DifferentialRuntimeFixture;

                public sealed class TemplateHostControl : TemplatedControl
                {
                }
                """,
            ["Program.cs"] = """
                using System;

                try
                {
                    var view = new DifferentialRuntimeFixture.MainView();
                    Console.WriteLine("RESULT:" + view.GetReport());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RESULT:EX|" + ex.GetType().FullName);
                }
                """
        };

        return new DifferentialRuntimeFixture(
            "template-property-element-runtime",
            "runtime-template-property-element",
            files);
    }

    private static DifferentialRuntimeFixture CreateStyleElementNameBindingRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialRuntimeFixture.MainView">
                  <UserControl.Styles>
                    <Style Selector="TextBlock.anchor-bound">
                      <Setter Property="Text" Value="{Binding #Anchor.Tag}" />
                    </Style>
                  </UserControl.Styles>

                  <StackPanel>
                    <Border x:Name="Anchor" Tag="anchor-value" />
                    <TextBlock x:Name="Output" Classes="anchor-bound" />
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
                        var output = this.FindControl<TextBlock>("Output");
                        return output?.Text ?? "<null>";
                    }
                }
                """,
            ["Program.cs"] = """
                using System;

                try
                {
                    var view = new DifferentialRuntimeFixture.MainView();
                    Console.WriteLine("RESULT:" + view.GetReport());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RESULT:EX|" + ex.GetType().FullName);
                }
                """
        };

        return new DifferentialRuntimeFixture(
            "style-element-name-binding-runtime",
            "runtime-style-element-name-binding",
            files);
    }

    private static DifferentialRuntimeFixture CreateTemplateElementNameBindingRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialRuntimeFixture"
                             x:Class="DifferentialRuntimeFixture.MainView">
                  <UserControl.Resources>
                    <ControlTheme x:Key="{x:Type local:TemplateHostControl}" TargetType="local:TemplateHostControl">
                      <Setter Property="Template">
                        <ControlTemplate>
                          <StackPanel>
                            <Border x:Name="Anchor" Tag="anchor-value" />
                            <TextBlock x:Name="Output" Text="{Binding #Anchor.Tag}" />
                          </StackPanel>
                        </ControlTemplate>
                      </Setter>
                    </ControlTheme>
                  </UserControl.Resources>
                  <local:TemplateHostControl x:Name="Host"
                                             Theme="{StaticResource {x:Type local:TemplateHostControl}}" />
                </UserControl>
                """,
            ["MainView.axaml.cs"] = """
                using System.Linq;
                using Avalonia.Controls;
                using Avalonia.Controls.Templates;
                using Avalonia.Styling;

                namespace DifferentialRuntimeFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                        InitializeComponent();
                    }

                    public string GetReport()
                    {
                        var host = this.FindControl<TemplateHostControl>("Host");
                        var theme = host?.Theme as ControlTheme;
                        var template = theme?.Setters
                            .OfType<Setter>()
                            .FirstOrDefault(static setter => setter.Property == Avalonia.Controls.Primitives.TemplatedControl.TemplateProperty)?
                            .Value as IControlTemplate;
                        if (host is null || template is null)
                        {
                            return "<missing>";
                        }

                        var templateResult = template.Build(host);
                        var panel = templateResult.Result as StackPanel;
                        var output = panel?.Children.OfType<TextBlock>().FirstOrDefault(static text => text.Name == "Output");
                        return output?.Text ?? "<null>";
                    }
                }
                """,
            ["TemplateHostControl.cs"] = """
                using Avalonia.Controls.Primitives;

                namespace DifferentialRuntimeFixture;

                public sealed class TemplateHostControl : TemplatedControl
                {
                }
                """,
            ["Program.cs"] = """
                using System;

                try
                {
                    var view = new DifferentialRuntimeFixture.MainView();
                    Console.WriteLine("RESULT:" + view.GetReport());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RESULT:EX|" + ex.GetType().FullName);
                }
                """
        };

        return new DifferentialRuntimeFixture(
            "template-element-name-binding-runtime",
            "runtime-template-element-name-binding",
            files);
    }

    private static DifferentialRuntimeFixture CreateResolveByNameRuntimeFixture()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainView.axaml"] = """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:DifferentialRuntimeFixture"
                             x:Class="DifferentialRuntimeFixture.MainView">
                    <StackPanel>
                        <TextBlock x:Name="Anchor" Text="ResolveMe" />
                        <local:ResolveByNameHost x:Name="Host" Target="Anchor" />
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
                        var host = this.FindControl<ResolveByNameHost>("Host");
                        var anchor = this.FindControl<TextBlock>("Anchor");
                        return Convert.ToString(ReferenceEquals(host?.Target, anchor));
                    }
                }
                """,
            ["ResolveByNameHost.cs"] = """
                using Avalonia.Controls;

                namespace DifferentialRuntimeFixture;

                public sealed class ResolveByNameHost : Control
                {
                    [ResolveByName]
                    public object? Target { get; set; }
                }
                """,
            ["Program.cs"] = """
                using System;

                try
                {
                    var view = new DifferentialRuntimeFixture.MainView();
                    Console.WriteLine("RESULT:" + view.GetReport());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("RESULT:EX|" + ex.GetType().FullName);
                }
                """
        };

        return new DifferentialRuntimeFixture(
            "resolve-by-name-runtime",
            "runtime-resolve-by-name",
            files);
    }

    private static (int ExitCode, string Output) BuildFixture(string projectPath, string workingDirectory, string backend)
    {
        var arguments =
            $"build \"{projectPath}\" --nologo -t:Rebuild -m:1 /nodeReuse:false --disable-build-servers " +
            $"-p:AvaloniaXamlCompilerBackend={backend} " +
            "-p:UseSharedCompilation=false " +
            "-p:ProduceReferenceAssembly=false";
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
        string compilerProject,
        string frameworkProject,
        string expressionSemanticsProject,
        string avaloniaProject,
        string miniLanguageParsingProject,
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
    <ProjectReference Include="{compilerProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{frameworkProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{expressionSemanticsProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{avaloniaProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{miniLanguageParsingProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{generatorProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <Import Project="{targetsPath}" Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'" />
</Project>
""";
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
        var output = outputBuilder.ToString();

        if (allowRetry &&
            ShouldRetryAfterTransientRoslynFailure(fileName, arguments, process.ExitCode, output))
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
