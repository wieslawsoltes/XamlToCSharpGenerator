using System;
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

            var snapshot = XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri);
            Assert.Equal(buildUri, snapshot.ActiveBuildUri);
            Assert.NotEmpty(snapshot.Elements);

            var root = Assert.Single(snapshot.Elements);
            Assert.Equal("0", root.Id);
            Assert.Equal("Window", root.TypeName);
            Assert.NotEmpty(root.Children);

            Assert.Contains(snapshot.Properties, property => property.Name == "Title");
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
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }

    private sealed class HotDesignTarget;
}
