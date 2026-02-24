using System;
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

    private static void ResetRuntimeState()
    {
        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.StopSession();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
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
