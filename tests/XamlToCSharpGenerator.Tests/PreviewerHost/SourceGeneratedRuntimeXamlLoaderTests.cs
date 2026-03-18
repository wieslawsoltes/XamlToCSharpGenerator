using System.Text;
using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using global::Avalonia.Markup.Xaml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Previewer.DesignerHost;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class SourceGeneratedRuntimeXamlLoaderTests
{
    [AvaloniaFact]
    public void PreparePreviewDocument_Rehydrates_LocalAssembly_And_BaseUri_When_Designer_Message_Omits_AssemblyPath()
    {
        PreviewHostOptions previousOptions = PreviewHostRuntimeState.Current;
        try
        {
            PreviewHostRuntimeState.Configure(new PreviewHostOptions(
                PreviewCompilerMode.Avalonia,
                null,
                null,
                typeof(SourceGeneratedRuntimeXamlLoaderTests).Assembly.Location,
                "/tmp/Preview.axaml",
                "/Pages/Preview.axaml",
                null,
                null));
            var document = new RuntimeXamlLoaderDocument("<UserControl />");
            var configuration = new RuntimeXamlLoaderConfiguration();

            RuntimeXamlLoaderDocument preparedDocument = SourceGeneratedRuntimeXamlLoaderInstaller.PreparePreviewDocument(
                document,
                "<UserControl />",
                configuration,
                out RuntimeXamlLoaderConfiguration preparedConfiguration);

            Assert.Equal(typeof(SourceGeneratedRuntimeXamlLoaderTests).Assembly, preparedConfiguration.LocalAssembly);
            Assert.Equal(
                new Uri("avares://XamlToCSharpGenerator.Tests/Pages/Preview.axaml"),
                preparedDocument.BaseUri);
        }
        finally
        {
            PreviewHostRuntimeState.Configure(previousOptions);
        }
    }

    [AvaloniaFact]
    public void LoadCore_Reuses_Initial_Baseline_For_Successful_Live_Overlay()
    {
        var loader = new SourceGeneratedRuntimeXamlLoader();
        var document = new RuntimeXamlLoaderDocument("<UserControl />");
        var configuration = new RuntimeXamlLoaderConfiguration();
        var baseline = new Border();
        var loadCount = 0;
        object? overlayBaseline = null;
        var overlaidRoot = new Border();

        SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
        try
        {
            var result = loader.LoadCore(
                document,
                configuration,
                "<UserControl />",
                sourceFilePath: null,
                assemblyPath: null,
                (_, _, _) =>
                {
                    loadCount++;
                    return baseline;
                },
                (RuntimeXamlLoaderDocument _, RuntimeXamlLoaderConfiguration _, object baselineRoot, string _, out object overlayResult) =>
                {
                    overlayBaseline = baselineRoot;
                    overlayResult = overlaidRoot;
                    return true;
                });

            Assert.Equal(1, loadCount);
            Assert.Same(baseline, overlayBaseline);
            Assert.Same(overlaidRoot, result);
        }
        finally
        {
            SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
        }
    }

    [AvaloniaFact]
    public void TryApplyLiveOverlay_Clears_Root_Collections_Declared_On_Base_Type_Property_Elements()
    {
        var method = typeof(SourceGeneratedRuntimeXamlLoader).GetMethod(
            "TryApplyLiveOverlay",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TryApplyLiveOverlay method was not found.");
        var root = new PreviewOverlayTestControl();
        var xaml = BuildOverlayCollectionResetXaml();
        var document = new RuntimeXamlLoaderDocument(
            new Uri("avares://XamlToCSharpGenerator.Tests/PreviewOverlay.axaml"),
            root,
            xaml);
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = typeof(SourceGeneratedRuntimeXamlLoaderTests).Assembly,
            DesignMode = true
        };
        var baseline = Assert.IsType<PreviewOverlayTestControl>(AvaloniaRuntimeXamlLoader.Load(document, configuration));

        var baselineResources = Assert.IsAssignableFrom<ResourceDictionary>(baseline.Resources);
        Assert.Equal("Orange", baselineResources["Accent"]);
        Assert.Single(baseline.Styles);
        Assert.Single(baseline.DataTemplates);

        object?[] args = [document, configuration, baseline, xaml, null];

        var success = Assert.IsType<bool>(method.Invoke(null, args));
        var result = Assert.IsType<PreviewOverlayTestControl>(args[4]);

        Assert.True(success);
        Assert.Same(baseline, result);

        var resultResources = Assert.IsAssignableFrom<ResourceDictionary>(result.Resources);
        Assert.Equal("Orange", resultResources["Accent"]);
        Assert.Single(resultResources);
        Assert.Single(result.Styles);
        Assert.Single(result.DataTemplates);

        var textBlock = Assert.IsType<TextBlock>(result.Content);
        Assert.Equal("Orange", textBlock.Text);
    }

    [AvaloniaFact]
    public void TryApplyLiveOverlay_Uses_Fresh_ResourceDictionary_Instance_To_Preserve_DesignPreviewWith()
    {
        var method = typeof(SourceGeneratedRuntimeXamlLoader).GetMethod(
            "TryApplyLiveOverlay",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TryApplyLiveOverlay method was not found.");
        const string xaml = """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Design.PreviewWith>
                <Border>
                  <TextBlock Text="TextBox theme preview" />
                </Border>
              </Design.PreviewWith>
              <Thickness x:Key="TextBoxPadding">8</Thickness>
            </ResourceDictionary>
            """;
        var baseline = new ResourceDictionary();
        var document = new RuntimeXamlLoaderDocument(
            new Uri("avares://XamlToCSharpGenerator.Tests/TextBox.axaml"),
            baseline,
            xaml);
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = typeof(SourceGeneratedRuntimeXamlLoaderTests).Assembly,
            DesignMode = true
        };
        object?[] args = [document, configuration, baseline, xaml, null];

        var success = Assert.IsType<bool>(method.Invoke(null, args));
        var result = Assert.IsType<ResourceDictionary>(args[4]);

        Assert.True(success);
        Assert.NotSame(baseline, result);
        var previewHost = Assert.IsType<Border>(PreviewSizingRootDecorator.Apply(result));
        Assert.Equal("TextBox theme preview", Assert.IsType<TextBlock>(previewHost.Child).Text);
    }

    [AvaloniaFact]
    public void LoadCore_Hydrates_Root_DataContext_From_XDataType_When_Unset()
    {
        var loader = new SourceGeneratedRuntimeXamlLoader();
        var root = new UserControl();
        var document = new RuntimeXamlLoaderDocument(
            new Uri("avares://XamlToCSharpGenerator.Tests/Preview.axaml"),
            root,
            BuildHydratedPreviewXaml());
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = typeof(PreviewHydratedViewModel).Assembly
        };
        object? overlayObservedDataContext = null;

        var result = loader.LoadCore(
            document,
            configuration,
            BuildHydratedPreviewXaml(),
            sourceFilePath: null,
            assemblyPath: null,
            (_, _, _) => root,
            (RuntimeXamlLoaderDocument _, RuntimeXamlLoaderConfiguration _, object baselineRoot, string _, out object overlayResult) =>
            {
                overlayObservedDataContext = Assert.IsType<UserControl>(baselineRoot).DataContext;
                overlayResult = baselineRoot;
                return true;
            });

        Assert.Same(root, result);
        Assert.IsType<PreviewHydratedViewModel>(root.DataContext);
        Assert.IsType<PreviewHydratedViewModel>(overlayObservedDataContext);
    }

    [AvaloniaFact]
    public void LoadCore_Does_Not_Override_Explicit_Root_DataContext()
    {
        var loader = new SourceGeneratedRuntimeXamlLoader();
        var explicitDataContext = new object();
        var root = new UserControl
        {
            DataContext = explicitDataContext
        };
        var document = new RuntimeXamlLoaderDocument(
            new Uri("avares://XamlToCSharpGenerator.Tests/Preview.axaml"),
            root,
            BuildHydratedPreviewXaml());
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = typeof(PreviewHydratedViewModel).Assembly
        };

        var result = loader.LoadCore(
            document,
            configuration,
            BuildHydratedPreviewXaml(),
            sourceFilePath: null,
            assemblyPath: null,
            (_, _, _) => root,
            (RuntimeXamlLoaderDocument _, RuntimeXamlLoaderConfiguration _, object baselineRoot, string _, out object overlayResult) =>
            {
                overlayResult = baselineRoot;
                return true;
            });

        Assert.Same(root, result);
        Assert.Same(explicitDataContext, root.DataContext);
    }

    [AvaloniaFact]
    public void LoadCore_Hydrates_Root_DataContext_From_External_XDataType_Assembly_On_Demand()
    {
        var loader = new SourceGeneratedRuntimeXamlLoader();
        var assemblyName = "AxsgPreviewHydratedVm_" + Guid.NewGuid().ToString("N");
        var assemblyImage = CompileExternalHydratedViewModelAssembly(assemblyName);
        using var resolver = new TestAssemblyResolveScope(assemblyName, assemblyImage);
        var root = new UserControl();
        var xaml = BuildExternalHydratedPreviewXaml(assemblyName);
        var document = new RuntimeXamlLoaderDocument(
            new Uri("avares://XamlToCSharpGenerator.Tests/Preview.axaml"),
            root,
            xaml);
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = typeof(PreviewHydratedViewModel).Assembly
        };

        var result = loader.LoadCore(
            document,
            configuration,
            xaml,
            sourceFilePath: null,
            assemblyPath: null,
            (_, _, _) => root,
            (RuntimeXamlLoaderDocument _, RuntimeXamlLoaderConfiguration _, object baselineRoot, string _, out object overlayResult) =>
            {
                overlayResult = baselineRoot;
                return true;
            });

        Assert.Same(root, result);
        Assert.True(resolver.WasResolved);
        Assert.NotNull(root.DataContext);
        Assert.Equal("ExternalPreviewHydration.ExternalPreviewHydratedViewModel", root.DataContext!.GetType().FullName);
        Assert.Equal(assemblyName, root.DataContext.GetType().Assembly.GetName().Name);
    }

    [AvaloniaFact]
    public void LoadCore_Clears_Stale_Last_Good_Overlay_When_Baseline_Is_Current()
    {
        var loader = new SourceGeneratedRuntimeXamlLoader();
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";
            var document = new RuntimeXamlLoaderDocument(xamlText)
            {
                Document = "View.axaml"
            };
            var configuration = new RuntimeXamlLoaderConfiguration();

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
            SourceGeneratedRuntimeXamlLoader.SetLastGoodOverlayForTests(document, sourceFilePath, "<UserControl Width=\"120\" />");

            var result = loader.LoadCore(
                document,
                configuration,
                xamlText,
                sourceFilePath,
                assemblyPath,
                (_, _, _) => new Border(),
                (RuntimeXamlLoaderDocument _, RuntimeXamlLoaderConfiguration _, object _, string _, out object overlayResult) =>
                {
                    overlayResult = new Border();
                    return false;
                });

            Assert.IsType<Border>(result);
            Assert.False(SourceGeneratedRuntimeXamlLoader.TryGetLastGoodOverlayForTests(document, sourceFilePath, out _));
        }
        finally
        {
            SourceGeneratedRuntimeXamlLoader.ClearLastGoodOverlayCacheForTests();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ReadXamlText_Preserves_Seekable_Stream_Position()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<UserControl />"));
        stream.Position = 4;
        var document = new RuntimeXamlLoaderDocument(stream);

        var actual = SourceGeneratedRuntimeXamlLoader.ReadXamlText(document);

        Assert.Equal("<UserControl />", actual);
        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public void CreatePreviewConfiguration_Forces_DesignMode_For_Preview_Loads()
    {
        var method = typeof(SourceGeneratedRuntimeXamlLoader).GetMethod(
            "CreatePreviewConfiguration",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreatePreviewConfiguration method was not found.");
        var original = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = typeof(SourceGeneratedRuntimeXamlLoaderTests).Assembly,
            UseCompiledBindingsByDefault = true,
            DesignMode = false,
            CreateSourceInfo = true
        };

        var cloned = Assert.IsType<RuntimeXamlLoaderConfiguration>(method.Invoke(null, [original]));

        Assert.Same(original.LocalAssembly, cloned.LocalAssembly);
        Assert.Equal(original.UseCompiledBindingsByDefault, cloned.UseCompiledBindingsByDefault);
        Assert.True(cloned.DesignMode);
        Assert.Equal(original.CreateSourceInfo, cloned.CreateSourceInfo);
        Assert.Null(cloned.DiagnosticHandler);
    }

    [Fact]
    public void ShouldApplyLiveOverlay_Returns_False_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.False(SourceGeneratedRuntimeXamlLoader.ShouldApplyLiveOverlay(
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyLiveOverlay_Returns_True_When_Text_Differs_From_Persisted_File()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");

            File.WriteAllText(sourceFilePath, "<UserControl Text=\"Saved\" />");
            File.WriteAllText(assemblyPath, string.Empty);

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyLiveOverlay(
                "<UserControl Text=\"Dirty\" />",
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldApplyLiveOverlay_Returns_True_When_Source_File_Is_Newer_Than_Assembly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now);
            File.SetLastWriteTimeUtc(assemblyPath, now.AddMinutes(-1));

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyLiveOverlay(
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ShouldApplyPreviewOverlay_Returns_True_For_ResourceDictionary_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "Theme.axaml");
            var assemblyPath = Path.Combine(tempRoot, "Library.dll");
            const string xamlText = "<ResourceDictionary />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyPreviewOverlay(
                new ResourceDictionary(),
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ShouldApplyPreviewOverlay_Returns_False_For_Control_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "View.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<UserControl />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.False(SourceGeneratedRuntimeXamlLoader.ShouldApplyPreviewOverlay(
                new Border(),
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ShouldApplyPreviewOverlay_Returns_True_For_Application_When_File_Matches_Current_Build_Output()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourceFilePath = Path.Combine(tempRoot, "App.axaml");
            var assemblyPath = Path.Combine(tempRoot, "App.dll");
            const string xamlText = "<Application />";

            File.WriteAllText(sourceFilePath, xamlText);
            File.WriteAllText(assemblyPath, string.Empty);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(sourceFilePath, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(assemblyPath, now);

            Assert.True(SourceGeneratedRuntimeXamlLoader.ShouldApplyPreviewOverlay(
                new Application(),
                xamlText,
                sourceFilePath,
                assemblyPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string BuildHydratedPreviewXaml()
    {
        return """
               <UserControl xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:local="clr-namespace:XamlToCSharpGenerator.Tests.PreviewerHost;assembly=XamlToCSharpGenerator.Tests"
                            x:DataType="local:PreviewHydratedViewModel" />
               """;
    }

    private static string BuildOverlayCollectionResetXaml()
    {
        return """
               <local:PreviewOverlayTestControl xmlns="https://github.com/avaloniaui"
                                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                                xmlns:local="clr-namespace:XamlToCSharpGenerator.Tests.PreviewerHost;assembly=XamlToCSharpGenerator.Tests"
                                                xmlns:system="using:System">
                   <UserControl.Styles>
                       <Style Selector="TextBlock" />
                   </UserControl.Styles>
                   <UserControl.Resources>
                       <system:String x:Key="Accent">Orange</system:String>
                   </UserControl.Resources>
                   <UserControl.DataTemplates>
                       <DataTemplate DataType="system:String">
                           <TextBlock Text="Template" />
                       </DataTemplate>
                   </UserControl.DataTemplates>
                   <TextBlock Text="{StaticResource Accent}" />
               </local:PreviewOverlayTestControl>
               """;
    }

    private static string BuildExternalHydratedPreviewXaml(string assemblyName)
    {
        return $$"""
               <UserControl xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:local="clr-namespace:ExternalPreviewHydration;assembly={{assemblyName}}"
                            x:DataType="local:ExternalPreviewHydratedViewModel" />
               """;
    }

    private static byte[] CompileExternalHydratedViewModelAssembly(string assemblyName)
    {
        var source = """
            namespace ExternalPreviewHydration;

            public sealed class ExternalPreviewHydratedViewModel
            {
                public string ProductName { get; } = "External";
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: assemblyName + ".cs");
        var references = CreateLoadedAssemblyMetadataReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            "Failed to emit external hydrated view-model test assembly: " +
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        return stream.ToArray();
    }

    private static IReadOnlyList<MetadataReference> CreateLoadedAssemblyMetadataReferences()
    {
        var references = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            var assembly = loadedAssemblies[index];
            if (assembly.IsDynamic ||
                string.IsNullOrWhiteSpace(assembly.Location) ||
                !seenPaths.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references;
    }

    private sealed class TestAssemblyResolveScope : IDisposable
    {
        private readonly string _assemblyName;
        private readonly byte[] _assemblyImage;

        public TestAssemblyResolveScope(string assemblyName, byte[] assemblyImage)
        {
            _assemblyName = assemblyName;
            _assemblyImage = assemblyImage;
            AssemblyLoadContext.Default.Resolving += OnResolving;
        }

        public bool WasResolved { get; private set; }

        public void Dispose()
        {
            AssemblyLoadContext.Default.Resolving -= OnResolving;
        }

        private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (!string.Equals(assemblyName.Name, _assemblyName, StringComparison.Ordinal))
            {
                return null;
            }

            WasResolved = true;
            return context.LoadFromStream(new MemoryStream(_assemblyImage, writable: false));
        }
    }
}
