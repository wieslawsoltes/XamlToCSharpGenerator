using System;
using System.Collections.Specialized;
using System.IO;
using Avalonia.Controls;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenStudioShellViewModelTests
{
    [Fact]
    public void TryHandleLiveSurfacePointerPressed_Selects_Element_By_Name_In_Design_Mode()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioShell.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            Assert.Equal(SourceGenHotDesignHitTestMode.Logical, viewModel.HitTestMode);
            var handled = viewModel.TryHandleLiveSurfacePointerPressed(new Button
            {
                Name = "ActionButton"
            });

            Assert.True(handled);
            Assert.NotNull(viewModel.SelectedElement);
            Assert.Equal("ActionButton", viewModel.SelectedElement!.XamlName);
            Assert.NotEqual("0", viewModel.SelectedElementId);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [Fact]
    public void TryHandleLiveSurfacePointerPressed_Does_Not_Select_In_Interactive_Mode()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioShell.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            viewModel.WorkspaceMode = SourceGenHotDesignWorkspaceMode.Interactive;

            var handled = viewModel.TryHandleLiveSurfacePointerPressed(new Button
            {
                Name = "ActionButton"
            });

            Assert.False(handled);
            Assert.False(string.IsNullOrWhiteSpace(viewModel.SelectedElementId));
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [Fact]
    public void TryHandleLiveSurfacePointerPressed_Resolves_Element_From_NonActive_Document()
    {
        ResetRuntimeState();
        var appSourcePath = CreateTempAppXamlSource();
        var viewSourcePath = CreateTempXamlSource();
        const string appBuildUri = "avares://tests/StudioShell.App.axaml";
        const string viewBuildUri = "avares://tests/StudioShell.View.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioAppTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = appBuildUri,
                    SourcePath = appSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Root,
                    ArtifactKind = SourceGenHotDesignArtifactKind.Application
                });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = viewBuildUri,
                    SourcePath = viewSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Root,
                    ArtifactKind = SourceGenHotDesignArtifactKind.View
                });

            XamlSourceGenHotDesignCoreTools.SelectDocument(appBuildUri);
            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            Assert.Equal(appBuildUri, viewModel.ActiveBuildUri);

            var handled = viewModel.TryHandleLiveSurfacePointerPressed(new Button
            {
                Name = "ActionButton"
            });

            Assert.True(handled);
            Assert.Equal(viewBuildUri, viewModel.ActiveBuildUri);
            Assert.NotNull(viewModel.SelectedElement);
            Assert.Equal("ActionButton", viewModel.SelectedElement!.XamlName);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(appSourcePath);
            DeleteFileIfExists(viewSourcePath);
        }
    }

    [Fact]
    public void SelectedTemplateDocument_Loads_Text_From_Selected_Template_Document()
    {
        ResetRuntimeState();
        var viewSourcePath = CreateTempXamlSource();
        var templateSourcePath = CreateTempTemplateXamlSource();
        const string viewBuildUri = "avares://tests/StudioShell.View.axaml";
        const string templateBuildUri = "avares://tests/StudioShell.ButtonTemplate.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = viewBuildUri,
                    SourcePath = viewSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Root,
                    ArtifactKind = SourceGenHotDesignArtifactKind.View
                });

            XamlSourceGenHotDesignManager.Register(
                new StudioTemplateTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = templateBuildUri,
                    SourcePath = templateSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Template,
                    ArtifactKind = SourceGenHotDesignArtifactKind.Template
                });

            XamlSourceGenHotDesignCoreTools.SelectDocument(viewBuildUri);
            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            Assert.Equal(viewBuildUri, viewModel.ActiveBuildUri);

            var templateDocument = Assert.Single(viewModel.TemplateDocuments, document =>
                string.Equals(document.BuildUri, templateBuildUri, StringComparison.OrdinalIgnoreCase));
            viewModel.SelectedTemplateDocument = templateDocument;

            Assert.Contains("ControlTheme", viewModel.TemplateXamlText, StringComparison.Ordinal);
            Assert.Contains("Property=\"Background\"", viewModel.TemplateXamlText, StringComparison.Ordinal);
            Assert.Contains("Value=\"Red\"", viewModel.TemplateXamlText, StringComparison.Ordinal);
            Assert.DoesNotContain("ActionButton", viewModel.TemplateXamlText, StringComparison.Ordinal);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(viewSourcePath);
            DeleteFileIfExists(templateSourcePath);
        }
    }

    [Fact]
    public void Logical_Mode_Selection_Populates_Source_Properties()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioShell.Properties.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            var buttonNode = FindById(Assert.Single(viewModel.DisplayElements), "0/0/0");

            viewModel.SelectedElement = buttonNode;

            Assert.NotNull(viewModel.SelectedElement);
            Assert.Equal("0/0/0", viewModel.SelectedElementId);
            Assert.Contains(viewModel.Properties, property => property.Name == "Content");
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [Fact]
    public void Selecting_Source_Node_Uses_Node_BuildUri_When_Active_Document_Differs()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempXamlSource();
        var appSourcePath = CreateTempAppXamlSource();
        const string viewBuildUri = "avares://tests/StudioShell.Primary.axaml";
        const string appBuildUri = "avares://tests/StudioShell.App.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = viewBuildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignManager.Register(
                new global::Avalonia.Application(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = appBuildUri,
                    SourcePath = appSourcePath
                });

            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            var buttonNode = FindById(Assert.Single(viewModel.DisplayElements), "0/0/0");
            Assert.Equal(viewBuildUri, buttonNode.SourceBuildUri);

            viewModel.SelectedDocument = Assert.Single(viewModel.Documents.Where(document => document.BuildUri == appBuildUri));
            Assert.Equal(appBuildUri, viewModel.ActiveBuildUri);

            viewModel.SelectedElement = buttonNode;

            Assert.Equal(viewBuildUri, viewModel.ActiveBuildUri);
            Assert.Equal("0/0/0", viewModel.SelectedElementId);
            Assert.Contains(viewModel.Properties, property => property.Name == "Content");
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
            DeleteFileIfExists(appSourcePath);
        }
    }

    [Fact]
    public void HitTestMode_Switches_DisplayElements_Between_Source_And_Live_Trees()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioShell.ModeSwitch.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            viewModel.UpdateLiveElementTree(new StackPanel
            {
                Name = "RootPanel",
                Children =
                {
                    new Button
                    {
                        Name = "ActionButton",
                        Content = "Run"
                    }
                }
            });

            var logicalRoot = Assert.Single(viewModel.DisplayElements);
            Assert.False(logicalRoot.IsLive);
            Assert.Equal("0", logicalRoot.Id);

            viewModel.HitTestMode = SourceGenHotDesignHitTestMode.Visual;

            var visualRoot = Assert.Single(viewModel.DisplayElements);
            Assert.True(visualRoot.IsLive);
            Assert.Equal("live:0", visualRoot.Id);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [Fact]
    public void Selecting_Live_Runtime_Node_Populates_Runtime_Properties()
    {
        ResetRuntimeState();
        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

        using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
        viewModel.UpdateLiveElementTree(new StackPanel
        {
            Children =
            {
                new Button
                {
                    Name = "ActionButton",
                    Content = "Run"
                }
            }
        });

        viewModel.HitTestMode = SourceGenHotDesignHitTestMode.Visual;
        var liveRoot = Assert.Single(viewModel.DisplayElements);
        var liveButton = Assert.Single(liveRoot.Children);

        viewModel.SelectedElement = liveButton;

        Assert.NotNull(viewModel.SelectedElement);
        Assert.Equal(liveButton.Id, viewModel.SelectedElement!.Id);
        Assert.NotEmpty(viewModel.Properties);
        Assert.Contains(viewModel.Properties, property => property.Name == "Name");
        Assert.Contains(viewModel.Properties, property => property.Name == "Content");
    }

    [Fact]
    public void ClearLiveElementTree_Falls_Back_To_Source_DisplayElements()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioShell.LiveFallback.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            viewModel.UpdateLiveElementTree(new StackPanel
            {
                Name = "RootPanel",
                Children =
                {
                    new Button
                    {
                        Name = "ActionButton",
                        Content = "Run"
                    }
                }
            });

            viewModel.HitTestMode = SourceGenHotDesignHitTestMode.Visual;
            var liveRoot = Assert.Single(viewModel.DisplayElements);
            Assert.True(liveRoot.IsLive);

            viewModel.ClearLiveElementTree();

            var sourceRoot = Assert.Single(viewModel.DisplayElements);
            Assert.False(sourceRoot.IsLive);
            Assert.Equal("0", sourceRoot.Id);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [Fact]
    public void RefreshCommand_Does_Not_Republish_Unchanged_Workspace_Collections()
    {
        ResetRuntimeState();
        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioShell.Refresh.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                WaitForHotReload = false,
                PersistChangesToSource = true
            });

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

            using var viewModel = new XamlSourceGenStudioShellViewModel(new SourceGenStudioOptions());
            var documentChanges = 0;
            var elementChanges = 0;
            var displayElementChanges = 0;
            var propertyChanges = 0;
            var toolboxChanges = 0;

            NotifyCollectionChangedEventHandler documentHandler = (_, _) => documentChanges++;
            NotifyCollectionChangedEventHandler elementHandler = (_, _) => elementChanges++;
            NotifyCollectionChangedEventHandler displayElementHandler = (_, _) => displayElementChanges++;
            NotifyCollectionChangedEventHandler propertyHandler = (_, _) => propertyChanges++;
            NotifyCollectionChangedEventHandler toolboxHandler = (_, _) => toolboxChanges++;

            viewModel.Documents.CollectionChanged += documentHandler;
            viewModel.Elements.CollectionChanged += elementHandler;
            viewModel.DisplayElements.CollectionChanged += displayElementHandler;
            viewModel.Properties.CollectionChanged += propertyHandler;
            viewModel.ToolboxItems.CollectionChanged += toolboxHandler;

            try
            {
                viewModel.RefreshCommand.Execute(null);
                documentChanges = 0;
                elementChanges = 0;
                displayElementChanges = 0;
                propertyChanges = 0;
                toolboxChanges = 0;

                viewModel.RefreshCommand.Execute(null);

                Assert.Equal(0, documentChanges);
                Assert.Equal(0, elementChanges);
                Assert.Equal(0, displayElementChanges);
                Assert.Equal(0, propertyChanges);
                Assert.Equal(0, toolboxChanges);
            }
            finally
            {
                viewModel.Documents.CollectionChanged -= documentHandler;
                viewModel.Elements.CollectionChanged -= elementHandler;
                viewModel.DisplayElements.CollectionChanged -= displayElementHandler;
                viewModel.Properties.CollectionChanged -= propertyHandler;
                viewModel.ToolboxItems.CollectionChanged -= toolboxHandler;
            }
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    private static void ResetRuntimeState()
    {
        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.StopSession();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }

    private static SourceGenHotDesignElementNode FindById(SourceGenHotDesignElementNode node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal))
        {
            return node;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            var found = TryFindById(node.Children[index], id);
            if (found is not null)
            {
                return found;
            }
        }

        throw new InvalidOperationException("Could not find element id: " + id);
    }

    private static SourceGenHotDesignElementNode? TryFindById(SourceGenHotDesignElementNode node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal))
        {
            return node;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            var found = TryFindById(node.Children[index], id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string CreateTempXamlSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "AXSG-StudioShell-" + Guid.NewGuid().ToString("N") + ".axaml");
        const string xaml =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel>
                    <Button Name="ActionButton" Content="Run" />
                    <TextBlock Name="StatusText" Text="Ready" />
                </StackPanel>
            </UserControl>
            """;
        File.WriteAllText(path, xaml);
        return path;
    }

    private static string CreateTempAppXamlSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "AXSG-StudioShell-App-" + Guid.NewGuid().ToString("N") + ".axaml");
        const string xaml =
            """
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Application.Styles>
                    <Style Selector="TextBlock" />
                </Application.Styles>
            </Application>
            """;
        File.WriteAllText(path, xaml);
        return path;
    }

    private static string CreateTempTemplateXamlSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "AXSG-StudioShell-Template-" + Guid.NewGuid().ToString("N") + ".axaml");
        const string xaml =
            """
            <ControlTheme xmlns="https://github.com/avaloniaui"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          x:Key="{x:Type Button}"
                          TargetType="Button">
              <Setter Property="Background" Value="Red" />
            </ControlTheme>
            """;
        File.WriteAllText(path, xaml);
        return path;
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort temp cleanup only.
        }
    }

    private sealed class StudioAppTarget;

    private sealed class StudioTarget;

    private sealed class StudioTemplateTarget;
}
