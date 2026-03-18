using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public sealed class AxsgPreviewHotDesignQuerySupportTests
{
    [AvaloniaFact]
    public async Task GetLiveTree_CanRun_From_Background_Thread_For_Preview_Document()
    {
        ResetRuntimeState();
        string sourcePath = CreateTempFile("""
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <StackPanel x:Name="RootPanel">
    <Button x:Name="ActionButton" Content="Run" />
  </StackPanel>
</UserControl>
""");
        const string buildUri = "avares://tests/AxsgPreviewHotDesignQuerySupportTests/PreviewTree.axaml";

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                PersistChangesToSource = true,
                WaitForHotReload = false
            });

            var root = new UserControl
            {
                Content = new StackPanel
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
                }
            };

            AxsgPreviewHotDesignSessionBridge.UpdateCurrentDocument(
                root,
                File.ReadAllText(sourcePath),
                buildUri,
                sourcePath);

            SourceGenHotDesignLiveTreeSnapshot tree = await Task.Run(() =>
                AxsgPreviewHotDesignQuerySupport.GetLiveTree(
                    SourceGenHotDesignHitTestMode.Logical,
                    buildUri,
                    selectedElementId: null,
                    search: null));

            SourceGenHotDesignElementNode rootNode = Assert.Single(tree.Elements);
            Assert.Equal(buildUri, tree.ActiveBuildUri);
            Assert.NotNull(FindByName(rootNode, "ActionButton"));
        }
        finally
        {
            ResetRuntimeState();
            TryDelete(sourcePath);
        }
    }

    [AvaloniaFact]
    public async Task GetOverlaySnapshot_CanRun_From_Background_Thread_For_Preview_Document()
    {
        ResetRuntimeState();
        string sourcePath = CreateTempFile("""
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <StackPanel x:Name="RootPanel">
    <Button x:Name="ActionButton" Width="120" Height="40" HorizontalAlignment="Left" VerticalAlignment="Top" Content="Run" />
  </StackPanel>
</UserControl>
""");
        const string buildUri = "avares://tests/AxsgPreviewHotDesignQuerySupportTests/PreviewHitTest.axaml";

        var button = new Button
        {
            Name = "ActionButton",
            Width = 120,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = "Run"
        };
        var root = new UserControl
        {
            Width = 220,
            Height = 120,
            Content = new StackPanel
            {
                Name = "RootPanel",
                Children = { button }
            }
        };
        var window = new Window
        {
            Width = 260,
            Height = 180,
            Content = root
        };

        try
        {
            XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
            {
                PersistChangesToSource = true,
                WaitForHotReload = false
            });

            window.Show();
            Dispatcher.UIThread.RunJobs();

            AxsgPreviewHotDesignSessionBridge.UpdateCurrentDocument(
                root,
                File.ReadAllText(sourcePath),
                buildUri,
                sourcePath);

            SourceGenHotDesignWorkspaceSnapshot workspace = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot(buildUri, search: null);
            SourceGenHotDesignElementNode rootNode = Assert.Single(workspace.Elements);
            SourceGenHotDesignElementNode actionButton = FindByName(rootNode, "ActionButton")!;
            XamlSourceGenHotDesignTool.SelectElement(buildUri, actionButton.Id);

            SourceGenHotDesignOverlaySnapshot overlay = await Task.Run(() =>
                AxsgPreviewHotDesignQuerySupport.GetOverlaySnapshot(
                    SourceGenHotDesignHitTestMode.Logical,
                    buildUri,
                    actionButton.Id,
                    actionButton));

            Assert.Equal(buildUri, overlay.ActiveBuildUri);
            Assert.NotNull(overlay.Selected);
            Assert.Equal("ActionButton", overlay.Selected!.Element?.XamlName);
            Assert.NotNull(overlay.Selected.Bounds);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }

            ResetRuntimeState();
            TryDelete(sourcePath);
        }
    }

    private static SourceGenHotDesignElementNode? FindByName(SourceGenHotDesignElementNode node, string xamlName)
    {
        if (string.Equals(node.XamlName, xamlName, StringComparison.Ordinal))
        {
            return node;
        }

        for (int index = 0; index < node.Children.Count; index += 1)
        {
            SourceGenHotDesignElementNode? child = FindByName(node.Children[index], xamlName);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static string CreateTempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), "AXSG-PreviewHotDesign-" + Guid.NewGuid().ToString("N") + ".axaml");
        File.WriteAllText(path, content.Trim());
        return path;
    }

    private static void TryDelete(string path)
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
            // Best effort cleanup.
        }
    }

    private static void ResetRuntimeState()
    {
        XamlSourceGenStudioManager.Disable();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
        AxsgPreviewHotDesignSessionBridge.ClearCurrentDocument();
        AxsgPreviewHotDesignSessionBridge.ClearHoverElement();
    }
}
