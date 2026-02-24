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
            Assert.Equal("0", viewModel.SelectedElementId);
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

    private sealed class StudioTarget;
}
