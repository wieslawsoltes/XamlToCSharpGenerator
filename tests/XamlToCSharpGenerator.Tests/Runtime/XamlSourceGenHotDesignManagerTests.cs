using System;
using System.Collections.Generic;
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
        Assert.Equal(SourceGenHotDesignDocumentRole.Root, document.DocumentRole);
        Assert.Equal(SourceGenHotDesignArtifactKind.View, document.ArtifactKind);
        Assert.Null(document.ScopeHints);
    }

    [Fact]
    public void Register_Publishes_Status_And_Document_Events()
    {
        ResetManager();

        SourceGenHotDesignStatus? latestStatus = null;
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor>? latestDocuments = null;
        XamlSourceGenHotDesignManager.HotDesignStatusChanged += OnStatusChanged;
        XamlSourceGenHotDesignManager.HotDesignDocumentsChanged += OnDocumentsChanged;

        try
        {
            XamlSourceGenHotDesignManager.Register(
                new HotDesignTarget(),
                static _ => { },
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = "avares://tests/EventedHotDesign.axaml",
                    SourcePath = "/tmp/EventedHotDesign.axaml"
                });

            Assert.NotNull(latestStatus);
            Assert.NotNull(latestDocuments);
            Assert.Equal(1, latestStatus!.RegisteredDocumentCount);
            Assert.Single(latestDocuments!);
            Assert.Equal("avares://tests/EventedHotDesign.axaml", latestDocuments[0].BuildUri);
        }
        finally
        {
            XamlSourceGenHotDesignManager.HotDesignStatusChanged -= OnStatusChanged;
            XamlSourceGenHotDesignManager.HotDesignDocumentsChanged -= OnDocumentsChanged;
        }

        return;

        void OnStatusChanged(SourceGenHotDesignStatus status)
        {
            latestStatus = status;
        }

        void OnDocumentsChanged(IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
        {
            latestDocuments = documents;
        }
    }

    [Fact]
    public void Register_Tracks_Document_Metadata_For_Studio_Scope_Resolution()
    {
        ResetManager();

        var instance = new HotDesignTarget();
        XamlSourceGenHotDesignManager.Register(
            instance,
            static target => ((HotDesignTarget)target).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/Theme.axaml",
                SourcePath = "/tmp/Theme.axaml",
                DocumentRole = SourceGenHotDesignDocumentRole.Theme,
                ArtifactKind = SourceGenHotDesignArtifactKind.ControlTheme,
                ScopeHints = new[] { "theme", "ControlTheme" }
            });

        var document = Assert.Single(XamlSourceGenHotDesignManager.GetRegisteredDocuments());
        Assert.Equal(SourceGenHotDesignDocumentRole.Theme, document.DocumentRole);
        Assert.Equal(SourceGenHotDesignArtifactKind.ControlTheme, document.ArtifactKind);
        Assert.NotNull(document.ScopeHints);
        Assert.Equal(2, document.ScopeHints!.Count);
        Assert.Equal("theme", document.ScopeHints[0]);
        Assert.Equal("ControlTheme", document.ScopeHints[1]);
    }

    [Fact]
    public void HotReload_Register_Mirrors_Document_Into_HotDesign_Registry()
    {
        ResetManager();

        var instance = new HotDesignTarget();
        XamlSourceGenHotReloadManager.Register(
            instance,
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/HotReloadMirror.axaml",
                SourcePath = "/tmp/HotReloadMirror.axaml"
            });

        var document = Assert.Single(XamlSourceGenHotDesignManager.GetRegisteredDocuments());
        Assert.Equal(typeof(HotDesignTarget), document.RootType);
        Assert.Equal("avares://tests/HotReloadMirror.axaml", document.BuildUri);
        Assert.Equal("/tmp/HotReloadMirror.axaml", document.SourcePath);
        Assert.Equal(SourceGenHotDesignDocumentRole.Root, document.DocumentRole);
        Assert.Equal(SourceGenHotDesignArtifactKind.View, document.ArtifactKind);
    }

    [Fact]
    public void HotReload_Register_Mirror_Preserves_Explicit_Tracking_Type()
    {
        ResetManager();

        XamlSourceGenHotReloadManager.Register(
            new global::Avalonia.Controls.ResourceDictionary(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                TrackingType = typeof(HotReloadMirrorDictionaryA),
                BuildUri = "avares://tests/HotReloadMirrorA.axaml",
                SourcePath = "/tmp/HotReloadMirrorA.axaml"
            });

        XamlSourceGenHotReloadManager.Register(
            new global::Avalonia.Controls.ResourceDictionary(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                TrackingType = typeof(HotReloadMirrorDictionaryB),
                BuildUri = "avares://tests/HotReloadMirrorB.axaml",
                SourcePath = "/tmp/HotReloadMirrorB.axaml"
            });

        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .OrderBy(static document => document.BuildUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(2, documents.Length);
        Assert.Equal(typeof(HotReloadMirrorDictionaryA), documents[0].RootType);
        Assert.Equal("avares://tests/HotReloadMirrorA.axaml", documents[0].BuildUri);
        Assert.Equal(typeof(HotReloadMirrorDictionaryB), documents[1].RootType);
        Assert.Equal("avares://tests/HotReloadMirrorB.axaml", documents[1].BuildUri);
    }

    [Fact]
    public void HotReload_ClearRegistrations_Removes_Mirrored_HotDesign_Documents()
    {
        ResetManager();

        var instance = new HotReloadMirrorTarget();
        XamlSourceGenHotReloadManager.Register(
            instance,
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/HotReloadMirrorClear.axaml",
                SourcePath = "/tmp/HotReloadMirrorClear.axaml"
            });

        Assert.Single(XamlSourceGenHotDesignManager.GetRegisteredDocuments());

        XamlSourceGenHotReloadManager.ClearRegistrations();

        Assert.Empty(XamlSourceGenHotDesignManager.GetRegisteredDocuments());
    }

    [Fact]
    public void HotReload_ClearRegistrations_Preserves_Explicit_HotDesign_Documents()
    {
        ResetManager();

        XamlSourceGenHotDesignManager.Register(
            new HotDesignTarget(),
            static target => ((HotDesignTarget)target).ApplyCount++,
            new SourceGenHotDesignRegistrationOptions
            {
                BuildUri = "avares://tests/ExplicitHotDesign.axaml",
                SourcePath = "/tmp/ExplicitHotDesign.axaml"
            });

        XamlSourceGenHotReloadManager.Register(
            new HotReloadMirrorTarget(),
            static _ => { },
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://tests/MirroredHotDesign.axaml",
                SourcePath = "/tmp/MirroredHotDesign.axaml"
            });

        var documents = XamlSourceGenHotDesignManager.GetRegisteredDocuments()
            .OrderBy(static document => document.BuildUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(2, documents.Length);

        XamlSourceGenHotReloadManager.ClearRegistrations();

        var remaining = Assert.Single(XamlSourceGenHotDesignManager.GetRegisteredDocuments());
        Assert.Equal(typeof(HotDesignTarget), remaining.RootType);
        Assert.Equal("avares://tests/ExplicitHotDesign.axaml", remaining.BuildUri);
        Assert.Equal("/tmp/ExplicitHotDesign.axaml", remaining.SourcePath);
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
            Assert.True(result.MinimalDiffApplied);
            Assert.Equal(3, result.MinimalDiffRemovedLength);
            Assert.Equal(3, result.MinimalDiffInsertedLength);
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
    public void ApplyUpdate_Persists_Source_File_With_NoOp_MinimalDiff_When_Text_Is_Unchanged()
    {
        ResetManager();
        XamlSourceGenHotDesignManager.Enable(new SourceGenHotDesignOptions
        {
            PersistChangesToSource = true,
            UseMinimalDiffPersistence = true,
            WaitForHotReload = false
        });

        var instance = new HotDesignTarget();
        var tempFile = Path.Combine(Path.GetTempPath(), "AXSG-HotDesign-" + Guid.NewGuid().ToString("N") + ".axaml");
        File.WriteAllText(tempFile, "<TextBlock Text=\"Same\"/>");

        try
        {
            XamlSourceGenHotDesignManager.Register(
                instance,
                static target => ((HotDesignTarget)target).ApplyCount++,
                new SourceGenHotDesignRegistrationOptions
                {
                    BuildUri = "avares://tests/PersistNoOp.axaml",
                    SourcePath = tempFile
                });

            var result = XamlSourceGenHotDesignManager.ApplyUpdate(new SourceGenHotDesignUpdateRequest
            {
                BuildUri = "avares://tests/PersistNoOp.axaml",
                XamlText = "<TextBlock Text=\"Same\"/>"
            });

            Assert.True(result.Succeeded);
            Assert.True(result.SourcePersisted);
            Assert.True(result.MinimalDiffApplied);
            Assert.Equal(0, result.MinimalDiffRemovedLength);
            Assert.Equal(0, result.MinimalDiffInsertedLength);
            Assert.Equal("<TextBlock Text=\"Same\"/>", File.ReadAllText(tempFile));
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
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }

    private sealed class HotDesignTarget
    {
        public int ApplyCount { get; set; }
    }

    private sealed class HotReloadMirrorDictionaryA : global::Avalonia.Controls.ResourceDictionary
    {
    }

    private sealed class HotReloadMirrorDictionaryB : global::Avalonia.Controls.ResourceDictionary
    {
    }

    private sealed class HotReloadMirrorTarget
    {
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
