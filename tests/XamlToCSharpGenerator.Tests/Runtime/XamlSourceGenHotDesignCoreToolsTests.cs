using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenHotDesignCoreToolsTests
{
    [Fact]
    public void WorkspaceSnapshot_Returns_ElementTree_And_Properties()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Sample"">
  <StackPanel x:Name=""RootPanel"">
    <TextBlock x:Name=""TitleText"" Text=""Hello"" />
  </StackPanel>
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, "0");
            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri);
            Assert.Equal(buildUri, snapshot.ActiveBuildUri);
            Assert.NotEmpty(snapshot.Elements);

            var root = Assert.Single(snapshot.Elements);
            Assert.Equal("0", root.Id);
            Assert.Equal("Window", root.TypeName);
            Assert.NotEmpty(root.Children);
            Assert.True(root.IsExpanded);
            Assert.True(root.DescendantCount > 0);

            var titleProperty = Assert.Single(snapshot.Properties.Where(property => property.Name == "Title"));
            Assert.Equal("Local", titleProperty.Source);
            Assert.True(
                string.Equals(titleProperty.Category, "Content", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(titleProperty.Category, "General", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Text", titleProperty.EditorKind);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void WorkspaceSnapshot_Uses_HotReload_Registrations_When_Studio_Is_Enabled()
    {
        ResetRuntimeState();
        XamlSourceGenStudioManager.Enable(new SourceGenStudioOptions());

        var sourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel>
    <Button Name=""ActionButton"" Content=""Run"" />
  </StackPanel>
</UserControl>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotReloadManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotReloadRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, "0/0/0");
            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri);

            Assert.True(snapshot.Status.IsEnabled);
            Assert.Equal(buildUri, snapshot.ActiveBuildUri);
            Assert.Single(snapshot.Documents);
            Assert.NotEmpty(snapshot.Elements);
            Assert.Contains(snapshot.Properties, property => property.Name == "Content");
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void WorkspaceSnapshot_Uses_Resolved_Document_BuildUri_When_Requested_Uri_Is_Stale()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel>
    <Button Name=""ActionButton"" Content=""Run"" />
  </StackPanel>
</UserControl>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        var staleBuildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(staleBuildUri);

            Assert.Equal(buildUri, snapshot.ActiveBuildUri);
            var root = Assert.Single(snapshot.Elements);
            Assert.Equal(buildUri, root.SourceBuildUri);

            var followupSnapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot();
            Assert.Equal(buildUri, followupSnapshot.ActiveBuildUri);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void WorkspaceSnapshot_AllPropertyMode_Includes_Default_Metadata()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <TextBlock Text=""Hello"" />
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignCoreTools.SetPropertyFilterMode(SourceGenHotDesignPropertyFilterMode.All);
            XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, "0/0");
            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri);

            var isVisible = snapshot.Properties.FirstOrDefault(property =>
                string.Equals(property.Name, "IsVisible", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "Visual.IsVisible", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(isVisible);
            Assert.Equal("Default", isVisible!.Source);
            Assert.Equal("Boolean", isVisible.EditorKind);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void WorkspaceSnapshot_SmartPropertyMode_Includes_Common_Metadata_For_Unset_Element()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel>
    <Border />
  </StackPanel>
</UserControl>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignCoreTools.SetPropertyFilterMode(SourceGenHotDesignPropertyFilterMode.Smart);
            XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, "0/0");
            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri);

            Assert.NotEmpty(snapshot.Properties);

            var name = Assert.Single(snapshot.Properties.Where(property => property.Name == "Name"));
            Assert.False(name.IsSet);
            Assert.Equal("Default", name.Source);

            var width = Assert.Single(snapshot.Properties.Where(property => property.Name == "Width"));
            Assert.False(width.IsSet);
            Assert.Equal("Default", width.Source);

            var isVisible = Assert.Single(snapshot.Properties.Where(property => property.Name == "IsVisible"));
            Assert.False(isVisible.IsSet);
            Assert.Equal("Default", isVisible.Source);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void WorkspaceSnapshot_Property_Metadata_Detects_Markup_Expressions_Using_Envelope_Semantics()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <TextBlock x:Name=""TitleText"" Text=""{Binding Name}"" Tag=""LiteralValue"" />
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, "0/0");
            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri);

            var textProperty = Assert.Single(snapshot.Properties.Where(property => property.Name == "Text"));
            Assert.True(textProperty.IsMarkupExtension);

            var tagProperty = Assert.Single(snapshot.Properties.Where(property => property.Name == "Tag"));
            Assert.False(tagProperty.IsMarkupExtension);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void TryBuildElementTreeForDocument_Returns_Current_Document_Elements()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel x:Name=""RootPanel"">
    <Button x:Name=""ActionButton"" Content=""Run"" />
  </StackPanel>
</UserControl>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            var built = XamlSourceGenHotDesignCoreTools.TryBuildElementTreeForDocument(buildUri, out var elements);

            Assert.True(built);
            var root = Assert.Single(elements);
            Assert.Equal("UserControl", root.TypeName);
            Assert.Equal(buildUri, root.SourceBuildUri);
            Assert.Equal("0", root.SourceElementId);
            Assert.Contains(root.Children, child => child.TypeName == "StackPanel");
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void TryBuildElementTreeForDocument_Requires_Exact_BuildUri_Match()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel x:Name=""RootPanel"">
    <Button x:Name=""ActionButton"" Content=""Run"" />
  </StackPanel>
</UserControl>");

        var registeredBuildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        var missingBuildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = registeredBuildUri,
                    SourcePath = sourcePath
                });

            var built = XamlSourceGenHotDesignCoreTools.TryBuildElementTreeForDocument(missingBuildUri, out var elements);

            Assert.False(built);
            Assert.Empty(elements);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public async Task ApplyPropertyUpdate_Supports_Undo_And_Redo()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel x:Name=""RootPanel"">
    <TextBlock x:Name=""TitleText"" Text=""Hello"" />
  </StackPanel>
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignCoreTools.SelectDocument(buildUri);
            XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, "0/0/0");

            var updateResult = await XamlSourceGenHotDesignCoreTools.ApplyPropertyUpdateAsync(new SourceGenHotDesignPropertyUpdateRequest
            {
                BuildUri = buildUri,
                ElementId = "0/0/0",
                PropertyName = "Text",
                PropertyValue = "Updated"
            });

            Assert.True(updateResult.Succeeded);
            Assert.Contains("Updated", File.ReadAllText(sourcePath), StringComparison.Ordinal);

            var undoResult = await XamlSourceGenHotDesignCoreTools.UndoAsync(buildUri);
            Assert.True(undoResult.Succeeded);
            Assert.Contains("Hello", File.ReadAllText(sourcePath), StringComparison.Ordinal);

            var redoResult = await XamlSourceGenHotDesignCoreTools.RedoAsync(buildUri);
            Assert.True(redoResult.Succeeded);
            Assert.Contains("Updated", File.ReadAllText(sourcePath), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public async Task ApplyPropertyUpdate_Undo_And_Redo_Preserve_Exact_Source_Text()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        const string original =
            """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel x:Name='RootPanel'>
                <TextBlock
                    x:Name="TitleText"
                    Text = 'Hello'
                    Width = "120" />
              </StackPanel>
            </Window>
            """;
        const string expected =
            """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel x:Name='RootPanel'>
                <TextBlock
                    x:Name="TitleText"
                    Text = 'Updated'
                    Width = "120" />
              </StackPanel>
            </Window>
            """;

        var sourcePath = CreateTempFile(original);
        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            XamlSourceGenHotDesignCoreTools.SelectDocument(buildUri);
            XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, "0/0/0");

            var updateResult = await XamlSourceGenHotDesignCoreTools.ApplyPropertyUpdateAsync(new SourceGenHotDesignPropertyUpdateRequest
            {
                BuildUri = buildUri,
                ElementId = "0/0/0",
                PropertyName = "Text",
                PropertyValue = "Updated"
            });

            Assert.True(updateResult.Succeeded);
            Assert.Equal(expected, File.ReadAllText(sourcePath));

            var undoResult = await XamlSourceGenHotDesignCoreTools.UndoAsync(buildUri);
            Assert.True(undoResult.Succeeded);
            Assert.Equal(original, File.ReadAllText(sourcePath));

            var redoResult = await XamlSourceGenHotDesignCoreTools.RedoAsync(buildUri);
            Assert.True(redoResult.Succeeded);
            Assert.Equal(expected, File.ReadAllText(sourcePath));
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public async Task Insert_And_Remove_Element_Updates_Source()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var sourcePath = CreateTempFile(@"
<Window xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel x:Name=""RootPanel"">
    <TextBlock x:Name=""TitleText"" Text=""Hello"" />
  </StackPanel>
</Window>");

        var buildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + ".axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = buildUri,
                    SourcePath = sourcePath
                });

            var insertResult = await XamlSourceGenHotDesignCoreTools.InsertElementAsync(new SourceGenHotDesignElementInsertRequest
            {
                BuildUri = buildUri,
                ParentElementId = "0/0",
                ElementName = "Button"
            });

            Assert.True(insertResult.Succeeded);
            Assert.Contains("Button", File.ReadAllText(sourcePath), StringComparison.Ordinal);

            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri, "Button");
            var buttonNode = FindByTypeName(snapshot.Elements, "Button");
            Assert.NotNull(buttonNode);

            var removeResult = await XamlSourceGenHotDesignCoreTools.RemoveElementAsync(new SourceGenHotDesignElementRemoveRequest
            {
                BuildUri = buildUri,
                ElementId = buttonNode!.Id
            });

            Assert.True(removeResult.Succeeded);
            Assert.DoesNotContain("<Button", File.ReadAllText(sourcePath), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    [Fact]
    public void TryResolveElementForLiveSelection_Resolves_Matching_Element_Across_Documents()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var appSourcePath = CreateTempFile(@"
<Application xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Application.Styles>
    <Style Selector=""TextBlock"" />
  </Application.Styles>
</Application>");

        var viewSourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel>
    <Button Name=""ActionButton"" Content=""Run"" />
  </StackPanel>
</UserControl>");

        var appBuildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/App.axaml";
        var viewBuildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/Views/MainView.axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = appBuildUri,
                    SourcePath = appSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Root,
                    ArtifactKind = SourceGenHotDesignArtifactKind.Application
                });

            XamlSourceGenHotDesignManager.Register(
                new HotDesignTargetView(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = viewBuildUri,
                    SourcePath = viewSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Root,
                    ArtifactKind = SourceGenHotDesignArtifactKind.View
                });

            XamlSourceGenHotDesignCoreTools.SelectDocument(appBuildUri);

            var resolved = XamlSourceGenHotDesignCoreTools.TryResolveElementForLiveSelection(
                new List<string> { "ActionButton" },
                Array.Empty<string>(),
                out var resolvedBuildUri,
                out var resolvedElementId);

            Assert.True(resolved);
            Assert.Equal(viewBuildUri, resolvedBuildUri);
            Assert.False(string.IsNullOrWhiteSpace(resolvedElementId));
            Assert.NotEqual("0", resolvedElementId);
        }
        finally
        {
            TryDelete(appSourcePath);
            TryDelete(viewSourcePath);
        }
    }

    [Fact]
    public void TryResolveElementForLiveSelection_TypeFallback_Can_Reject_Ambiguous_Matches()
    {
        ResetRuntimeState();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var firstViewSourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel>
    <Button Content=""First"" />
  </StackPanel>
</UserControl>");

        var secondViewSourcePath = CreateTempFile(@"
<UserControl xmlns=""https://github.com/avaloniaui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <StackPanel>
    <Button Content=""Second"" />
  </StackPanel>
</UserControl>");

        var firstBuildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/Views/First.axaml";
        var secondBuildUri = "avares://tests/" + Guid.NewGuid().ToString("N") + "/Views/Second.axaml";
        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTargetView(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = firstBuildUri,
                    SourcePath = firstViewSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Root,
                    ArtifactKind = SourceGenHotDesignArtifactKind.View
                });

            XamlSourceGenHotDesignManager.Register(
                new HotDesignTargetViewAlt(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = secondBuildUri,
                    SourcePath = secondViewSourcePath,
                    DocumentRole = SourceGenHotDesignDocumentRole.Root,
                    ArtifactKind = SourceGenHotDesignArtifactKind.View
                });

            var ambiguous = XamlSourceGenHotDesignCoreTools.TryResolveElementForLiveSelection(
                Array.Empty<string>(),
                new[] { "Button" },
                preferredBuildUri: null,
                allowAmbiguousTypeFallback: false,
                out var ambiguousBuildUri,
                out var ambiguousElementId);
            Assert.False(ambiguous);
            Assert.Null(ambiguousBuildUri);
            Assert.Null(ambiguousElementId);

            var resolvedWithPreference = XamlSourceGenHotDesignCoreTools.TryResolveElementForLiveSelection(
                Array.Empty<string>(),
                new[] { "Button" },
                preferredBuildUri: secondBuildUri,
                allowAmbiguousTypeFallback: false,
                out var resolvedBuildUri,
                out var resolvedElementId);
            Assert.True(resolvedWithPreference);
            Assert.Equal(secondBuildUri, resolvedBuildUri);
            Assert.False(string.IsNullOrWhiteSpace(resolvedElementId));
            Assert.NotEqual("0", resolvedElementId);
        }
        finally
        {
            TryDelete(firstViewSourcePath);
            TryDelete(secondViewSourcePath);
        }
    }

    private static SourceGenHotDesignElementNode? FindByTypeName(
        IReadOnlyList<SourceGenHotDesignElementNode> nodes,
        string typeName)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (string.Equals(node.TypeName, typeName, StringComparison.Ordinal))
            {
                return node;
            }

            var found = FindByTypeName(node.Children, typeName);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "AXSG-HotDesign-CoreTools-" + Guid.NewGuid().ToString("N") + ".axaml");
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
        XamlSourceGenStudioManager.StopSession();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }

    private sealed class HotDesignTarget;

    private sealed class HotDesignTargetView;

    private sealed class HotDesignTargetViewAlt;
}
