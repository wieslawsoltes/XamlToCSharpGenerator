using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenStudioLiveTreeProjectionServiceTests
{
    [AvaloniaFact]
    public void BuildLiveTree_Logical_Mode_Projects_Hierarchical_Nodes()
    {
        ResetRuntimeState();

        var root = new StackPanel
        {
            Name = "RootPanel",
            Children =
            {
                new Button { Name = "ActionButton", Content = "Run" },
                new Border
                {
                    Name = "InfoBorder",
                    Child = new TextBlock { Name = "InfoText", Text = "Ready" }
                }
            }
        };

        var projected = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
            root,
            SourceGenHotDesignHitTestMode.Logical,
            preferredBuildUri: null,
            selectedSourceElementId: null);

        var liveRoot = Assert.Single(projected);
        Assert.True(liveRoot.IsLive);
        Assert.Equal("StackPanel", liveRoot.TypeName);
        Assert.Equal(0, liveRoot.Depth);
        Assert.True(liveRoot.Children.Count >= 2);
        Assert.Contains(liveRoot.Children, child => child.TypeName == "Button");
    }

    [AvaloniaFact]
    public void BuildLiveTree_Projects_Source_Mapping_For_Named_Control()
    {
        ResetRuntimeState();

        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioLiveTree.axaml";

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

            var root = new StackPanel
            {
                Name = "RootPanel",
                Children =
                {
                    new Button { Name = "ActionButton", Content = "Run" }
                }
            };

            var projected = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                root,
                SourceGenHotDesignHitTestMode.Logical,
                preferredBuildUri: buildUri,
                selectedSourceElementId: null);

            var rootNode = Assert.Single(projected);
            var actionButton = FindByName(rootNode, "ActionButton");
            Assert.NotNull(actionButton);
            Assert.Equal(buildUri, actionButton!.SourceBuildUri);
            Assert.False(string.IsNullOrWhiteSpace(actionButton.SourceElementId));
            Assert.True(actionButton.IsLive);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    private static SourceGenHotDesignElementNode? FindByName(SourceGenHotDesignElementNode node, string name)
    {
        if (string.Equals(node.XamlName, name, StringComparison.Ordinal))
        {
            return node;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = FindByName(node.Children[index], name);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
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
        var path = Path.Combine(Path.GetTempPath(), "AXSG-StudioLiveTree-" + Guid.NewGuid().ToString("N") + ".axaml");
        const string xaml =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel Name="RootPanel">
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
