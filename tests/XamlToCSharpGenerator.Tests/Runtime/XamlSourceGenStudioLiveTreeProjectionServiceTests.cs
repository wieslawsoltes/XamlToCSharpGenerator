using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    [AvaloniaFact]
    public void BuildLiveTree_Reuses_Cached_Preferred_Source_Lookup_When_Document_Text_Is_Unchanged()
    {
        ResetRuntimeState();

        var sourcePath = CreateTempXamlSource();
        const string buildUri = "avares://tests/StudioLiveTree.CachedLookup.axaml";

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

            var initialBuildCount = GetPreferredSourceLookupBuildCount();

            _ = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                root,
                SourceGenHotDesignHitTestMode.Logical,
                preferredBuildUri: buildUri,
                selectedSourceElementId: null);

            var afterFirstBuild = GetPreferredSourceLookupBuildCount();

            _ = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                root,
                SourceGenHotDesignHitTestMode.Logical,
                preferredBuildUri: buildUri,
                selectedSourceElementId: null);

            var afterSecondBuild = GetPreferredSourceLookupBuildCount();

            Assert.Equal(initialBuildCount + 1, afterFirstBuild);
            Assert.Equal(afterFirstBuild, afterSecondBuild);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [AvaloniaFact]
    public void BuildLiveTree_Does_Not_Cache_Empty_Preferred_Lookup_For_Unresolved_Document()
    {
        ResetRuntimeState();

        var sourcePath = CreateTempXamlSource();
        const string registeredBuildUri = "avares://tests/StudioLiveTree.RegisteredLookup.axaml";
        const string missingBuildUri = "avares://tests/StudioLiveTree.MissingLookup.axaml";

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
                    BuildUri = registeredBuildUri,
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

            var initialBuildCount = GetPreferredSourceLookupBuildCount();

            _ = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                root,
                SourceGenHotDesignHitTestMode.Logical,
                preferredBuildUri: missingBuildUri,
                selectedSourceElementId: null);

            var afterMissingBuild = GetPreferredSourceLookupBuildCount();

            XamlSourceGenHotDesignManager.Register(
                new StudioTarget(),
                _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = missingBuildUri,
                    SourcePath = sourcePath
                });

            var rebuilt = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                root,
                SourceGenHotDesignHitTestMode.Logical,
                preferredBuildUri: missingBuildUri,
                selectedSourceElementId: null);

            var afterRegisteredBuild = GetPreferredSourceLookupBuildCount();

            Assert.Equal(initialBuildCount, afterMissingBuild);
            Assert.Equal(initialBuildCount + 1, afterRegisteredBuild);
            Assert.Equal(missingBuildUri, Assert.Single(rebuilt).SourceBuildUri);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [AvaloniaFact]
    public void BuildLiveTree_Assigns_Distinct_SourceElementIds_For_Duplicate_Unnamed_Siblings()
    {
        ResetRuntimeState();

        var sourcePath = CreateTempDuplicateSiblingXamlSource();
        const string buildUri = "avares://tests/StudioLiveTree.DuplicateSiblings.axaml";

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

            var root = new UserControl
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new Button { Content = "First" },
                        new Button { Content = "Second" }
                    }
                }
            };

            var projected = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                root,
                SourceGenHotDesignHitTestMode.Logical,
                preferredBuildUri: buildUri,
                selectedSourceElementId: null);

            var liveRoot = Assert.Single(projected);
            var stackPanel = Assert.Single(liveRoot.Children);
            Assert.Equal(2, stackPanel.Children.Count);
            Assert.False(string.IsNullOrWhiteSpace(stackPanel.Children[0].SourceElementId));
            Assert.False(string.IsNullOrWhiteSpace(stackPanel.Children[1].SourceElementId));
            Assert.NotEqual(stackPanel.Children[0].SourceElementId, stackPanel.Children[1].SourceElementId);
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [AvaloniaFact]
    public void BuildLiveTree_Visual_Mode_Does_Not_Source_Map_Template_Only_Descendants()
    {
        ResetRuntimeState();
        EnsureFluentTheme();

        var sourcePath = CreateTempTemplateVisualXamlSource();
        const string buildUri = "avares://tests/StudioLiveTree.VisualTemplate.axaml";

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

            var root = new UserControl
            {
                Content = new StackPanel
                {
                    Name = "RootPanel",
                    Children =
                    {
                        new ListBox
                        {
                            Name = "ActionList",
                            ItemsSource = new[] { "Run" }
                        }
                    }
                }
            };

            var window = new Window
            {
                Width = 800,
                Height = 600,
                Content = root
            };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var projected = XamlSourceGenStudioLiveTreeProjectionService.BuildLiveTree(
                    root,
                    SourceGenHotDesignHitTestMode.Visual,
                    preferredBuildUri: buildUri,
                    selectedSourceElementId: null);

                var liveRoot = Assert.Single(projected);
                var actionList = FindByName(liveRoot, "ActionList");
                Assert.NotNull(actionList);
                Assert.False(string.IsNullOrWhiteSpace(actionList!.SourceElementId));

                var templateOnlyControl = root.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(control => control.TemplatedParent is not null);
                Assert.NotNull(templateOnlyControl);

                var templateOnlyNode = Flatten(liveRoot)
                    .FirstOrDefault(node =>
                        XamlSourceGenStudioLiveTreeProjectionService.TryResolveControlByLiveNodeId(
                            root,
                            SourceGenHotDesignHitTestMode.Visual,
                            node.Id,
                            out var resolvedControl) &&
                        ReferenceEquals(resolvedControl, templateOnlyControl));

                Assert.NotNull(templateOnlyNode);
                Assert.True(string.IsNullOrWhiteSpace(templateOnlyNode!.SourceElementId));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            ResetRuntimeState();
            DeleteFileIfExists(sourcePath);
        }
    }

    [Fact]
    public void ResolveLiveControlForElement_Uses_Exact_Live_Node_Id_For_Duplicate_Type_Matches()
    {
        var firstBorder = new Border();
        var secondBorder = new Border();
        var root = new StackPanel
        {
            Children =
            {
                firstBorder,
                secondBorder
            }
        };
        var selectedLiveNode = new SourceGenHotDesignElementNode(
            Id: "live:0/0",
            DisplayName: "[Border]",
            TypeName: "Border",
            XamlName: null,
            Classes: null,
            Depth: 1,
            IsSelected: true,
            Line: 0,
            Children: [],
            IsExpanded: true,
            DescendantCount: 0,
            SourceBuildUri: null,
            SourceElementId: null,
            IsLive: true);

        var resolved = XamlSourceGenStudioLiveTreeProjectionService.ResolveLiveControlForElement(
            root,
            SourceGenHotDesignHitTestMode.Logical,
            selectedLiveNode,
            preferredBuildUri: null);

        Assert.Same(firstBorder, resolved);
    }

    [AvaloniaFact]
    public void ResolveLiveControlForElement_Uses_SourceElement_For_Duplicate_Unnamed_Siblings()
    {
        ResetRuntimeState();

        var sourcePath = CreateTempDuplicateSiblingXamlSource();
        const string buildUri = "avares://tests/StudioLiveTree.ResolveDuplicateSiblings.axaml";

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

            Assert.True(XamlSourceGenHotDesignCoreTools.TryBuildElementTreeForDocument(buildUri, out var sourceElements));
            var sourceRoot = Assert.Single(sourceElements);
            var sourceStackPanel = Assert.Single(sourceRoot.Children);
            var secondSourceButton = sourceStackPanel.Children[1];

            var firstButton = new Button { Content = "First" };
            var secondButton = new Button { Content = "Second" };
            var root = new UserControl
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        firstButton,
                        secondButton
                    }
                }
            };

            var resolved = XamlSourceGenStudioLiveTreeProjectionService.ResolveLiveControlForElement(
                root,
                SourceGenHotDesignHitTestMode.Logical,
                new SourceGenHotDesignElementNode(
                    Id: secondSourceButton.Id,
                    DisplayName: secondSourceButton.DisplayName,
                    TypeName: secondSourceButton.TypeName,
                    XamlName: secondSourceButton.XamlName,
                    Classes: secondSourceButton.Classes,
                    Depth: secondSourceButton.Depth,
                    IsSelected: true,
                    Line: secondSourceButton.Line,
                    Children: secondSourceButton.Children,
                    IsExpanded: secondSourceButton.IsExpanded,
                    DescendantCount: secondSourceButton.DescendantCount,
                    SourceBuildUri: buildUri,
                    SourceElementId: secondSourceButton.Id,
                    IsLive: false),
                preferredBuildUri: buildUri);

            Assert.Same(secondButton, resolved);
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

    private static IEnumerable<SourceGenHotDesignElementNode> Flatten(SourceGenHotDesignElementNode node)
    {
        yield return node;

        for (var index = 0; index < node.Children.Count; index++)
        {
            foreach (var child in Flatten(node.Children[index]))
            {
                yield return child;
            }
        }
    }

    private static void EnsureFluentTheme()
    {
        var application = Application.Current ?? throw new InvalidOperationException("Avalonia application is not initialized.");

        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Insert(0, new FluentTheme());
        }

        application.RequestedThemeVariant = ThemeVariant.Default;
    }

    private static int GetPreferredSourceLookupBuildCount()
    {
        var field = typeof(XamlSourceGenStudioLiveTreeProjectionService).GetField(
            "_preferredSourceLookupBuildCount",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<int>(field!.GetValue(null));
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

    private static string CreateTempDuplicateSiblingXamlSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "AXSG-StudioLiveTree-Duplicate-" + Guid.NewGuid().ToString("N") + ".axaml");
        const string xaml =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel>
                    <Button Content="First" />
                    <Button Content="Second" />
                </StackPanel>
            </UserControl>
            """;
        File.WriteAllText(path, xaml);
        return path;
    }

    private static string CreateTempTemplateVisualXamlSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "AXSG-StudioLiveTree-TemplateVisual-" + Guid.NewGuid().ToString("N") + ".axaml");
        const string xaml =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel Name="RootPanel">
                    <ListBox Name="ActionList">
                        <x:String>Run</x:String>
                    </ListBox>
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
