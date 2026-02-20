using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenHotDesignManagerTests
{
    [Fact]
    public void Register_Tracks_Document_For_Runtime_Instance()
    {
        ResetManager();

        var instance = new HotDesignTarget();
        XamlSourceGenHotDesignManager.Register(
            instance,
            static target => ((HotDesignTarget)target).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/HotDesignTarget.axaml",
                SourcePath = "/tmp/HotDesignTarget.axaml"
            });

        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments();
        var document = Assert.Single(documents);
        Assert.Equal(typeof(HotDesignTarget), document.RootType);
        Assert.Equal("avares://tests/HotDesignTarget.axaml", document.BuildUri);
        Assert.Equal("/tmp/HotDesignTarget.axaml", document.SourcePath);
        Assert.Equal(1, document.LiveInstanceCount);
    }

    [Fact]
    public void ApplyUpdate_RuntimeOnly_Uses_RuntimeApply_Action()
    {
        ResetManager();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = false,
            WaitForHotReload = false
        });

        var instance = new HotDesignTarget();
        XamlSourceGenHotDesignManager.Register(
            instance,
            static target => ((HotDesignTarget)target).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/RuntimeOnly.axaml"
            });

        var result = XamlSourceGenHotDesignManager.ApplyUpdate(new SourceGenHotDesignUpdateRequest
        {
            BuildUri = "avares://tests/RuntimeOnly.axaml",
            XamlText = "<TextBlock Text=\"Updated\"/>"
        });

        Assert.True(result.Succeeded);
        Assert.True(result.RuntimeFallbackApplied);
        Assert.False(result.SourcePersisted);
        Assert.Equal(1, instance.ApplyCount);
    }

    [Fact]
    public void ApplyUpdate_Persists_Source_File_When_Configured()
    {
        ResetManager();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            WaitForHotReload = false
        });

        var instance = new HotDesignTarget();
        var tempFile = Path.Combine(Path.GetTempPath(), "AXSG-HotDesign-" + Guid.NewGuid().ToString("N") + ".axaml");
        File.WriteAllText(tempFile, "<TextBlock Text=\"Old\"/>");

        try
        {
            XamlSourceGenHotDesignManager.Register(
                instance,
                static target => ((HotDesignTarget)target).ApplyCount++,
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = "avares://tests/Persist.axaml",
                    SourcePath = tempFile
                });

            var result = XamlSourceGenHotDesignManager.ApplyUpdate(new SourceGenHotDesignUpdateRequest
            {
                BuildUri = "avares://tests/Persist.axaml",
                XamlText = "<TextBlock Text=\"New\"/>"
            });

            Assert.True(result.Succeeded);
            Assert.True(result.SourcePersisted);
            Assert.Equal("<TextBlock Text=\"New\"/>", File.ReadAllText(tempFile));
            Assert.Equal(0, instance.ApplyCount);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // Best effort temp cleanup.
            }
        }
    }

    [Fact]
    public void ApplyUpdate_Returns_Failure_When_HotDesign_Is_Disabled()
    {
        ResetManager();

        var instance = new HotDesignTarget();
        XamlSourceGenHotDesignManager.Register(
            instance,
            static target => ((HotDesignTarget)target).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/Disabled.axaml"
            });

        var result = XamlSourceGenHotDesignManager.ApplyUpdate(new SourceGenHotDesignUpdateRequest
        {
            BuildUri = "avares://tests/Disabled.axaml",
            XamlText = "<TextBlock/>"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("disabled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyUpdate_Can_Be_Handled_By_Custom_Applier()
    {
        ResetManager();
        XamlSourceGenHotDesignManager.Enable();

        var instance = new HotDesignTarget();
        XamlSourceGenHotDesignManager.Register(
            instance,
            static target => ((HotDesignTarget)target).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/CustomApplier.axaml"
            });

        var applier = new RecordingHotDesignApplier();
        XamlSourceGenHotDesignManager.RegisterApplier(applier);

        var result = await XamlSourceGenHotDesignManager.ApplyUpdateAsync(new SourceGenHotDesignUpdateRequest
        {
            BuildUri = "avares://tests/CustomApplier.axaml",
            XamlText = "<TextBlock Text=\"FromApplier\"/>"
        });

        Assert.True(result.Succeeded);
        Assert.True(applier.WasInvoked);
        Assert.Equal("custom", result.Message);
    }

    private static void ResetManager()
    {
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
    }

    private sealed class HotDesignTarget
    {
        public int ApplyCount { get; set; }
    }

    private sealed class RecordingHotDesignApplier : ISourceGenHotDesignUpdateApplier
    {
        public int Priority => 500;

        public bool WasInvoked { get; private set; }

        public bool CanApply(SourceGenHotDesignUpdateContext context)
        {
            return true;
        }

        public ValueTask<SourceGenHotDesignApplyResult> ApplyAsync(
            SourceGenHotDesignUpdateContext context,
            CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return ValueTask.FromResult(new SourceGenHotDesignApplyResult(
                Succeeded: true,
                Message: "custom",
                BuildUri: context.Document.BuildUri,
                TargetType: context.Document.RootType));
        }
    }
}
