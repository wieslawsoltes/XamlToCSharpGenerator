using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenHotReloadManagerTests
{
    [Fact]
    public void UpdateApplication_Reloads_Registered_Instance_For_Matching_Type()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new ReloadTargetA();
        XamlSourceGenHotReloadManager.Register(instance, target =>
        {
            ((ReloadTargetA)target).ReloadCount++;
            reloadCount++;
        });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetA)]);

        Assert.Equal(1, reloadCount);
        Assert.Equal(1, instance.ReloadCount);
    }

    [Fact]
    public void UpdateApplication_Does_Not_Reload_When_Disabled()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Disable();

        var reloadCount = 0;
        var instance = new ReloadTargetB();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetB)]);

        Assert.Equal(0, reloadCount);
    }

    [Fact]
    public void UpdateApplication_With_Null_Types_Reloads_All_Tracked_Types()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var firstCount = 0;
        var secondCount = 0;
        var first = new ReloadTargetC();
        var second = new ReloadTargetD();

        XamlSourceGenHotReloadManager.Register(first, _ => firstCount++);
        XamlSourceGenHotReloadManager.Register(second, _ => secondCount++);

        XamlSourceGenHotReloadManager.UpdateApplication(null);

        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public void UpdateApplication_Normalizes_Generic_Type_Keys()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new GenericReloadTarget<int>();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(GenericReloadTarget<int>)]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Maps_MetadataUpdate_Replacement_Type_To_Original_Type()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new MetadataOriginalReloadTarget();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);
        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            typeof(MetadataReplacementReloadTarget),
            typeof(MetadataOriginalReloadTarget));

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(MetadataReplacementReloadTarget)]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Queues_Reentrant_Update_And_Replays_It_After_Current_Pass()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var requestedNestedUpdate = false;
        var instance = new ReentrantReloadTarget();

        XamlSourceGenHotReloadManager.Register(instance, _ =>
        {
            var currentCount = Interlocked.Increment(ref reloadCount);
            if (currentCount != 1 || requestedNestedUpdate)
            {
                return;
            }

            requestedNestedUpdate = true;
            XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReentrantReloadTarget)]);
        });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReentrantReloadTarget)]);

        Assert.Equal(2, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Executes_Registration_State_Transfer_And_Handler_Phases()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var handler = new RecordingHotReloadHandler();
        XamlSourceGenHotReloadManager.RegisterHandler(handler);

        var beforeReloadCount = 0;
        var afterReloadCount = 0;
        var target = new StatefulReloadTarget { Value = 42 };

        XamlSourceGenHotReloadManager.Register(
            target,
            static instance => ((StatefulReloadTarget)instance).Value = 0,
            new SourceGenHotReloadRegistrationOptions
            {
                BeforeReload = _ => beforeReloadCount++,
                CaptureState = static instance => ((StatefulReloadTarget)instance).Value,
                RestoreState = static (instance, state) =>
                {
                    if (state is int value)
                    {
                        ((StatefulReloadTarget)instance).Value = value;
                    }
                },
                AfterReload = _ => afterReloadCount++
            });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(StatefulReloadTarget)]);

        Assert.Equal(42, target.Value);
        Assert.Equal(1, beforeReloadCount);
        Assert.Equal(1, afterReloadCount);
        Assert.Equal(1, handler.BeforeVisualTreeUpdateCount);
        Assert.Equal(1, handler.AfterVisualTreeUpdateCount);
        Assert.Equal(1, handler.CaptureStateCount);
        Assert.Equal(1, handler.BeforeElementReloadCount);
        Assert.Equal(1, handler.AfterElementReloadCount);
        Assert.Equal(1, handler.ReloadCompletedCount);
    }

    [Fact]
    public void UpdateApplication_Default_Template_Rematerialization_Handler_Reapplies_Descendant_Templates()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var root = new TemplateProbeLayoutable();
        var child = new TemplateProbeLayoutable();
        root.AttachChild(child);

        XamlSourceGenHotReloadManager.Register(root, _ => { });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(TemplateProbeLayoutable)]);

        Assert.True(root.ApplyTemplateCount >= 1);
        Assert.True(child.ApplyTemplateCount >= 1);
    }

    [Fact]
    public void UpdateApplication_Raises_RudeEdit_Event_For_Shape_Change_Failures()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var target = new RudeEditTarget();
        Type? observedType = null;
        Exception? observedException = null;

        void OnRudeEdit(Type type, Exception exception)
        {
            observedType = type;
            observedException = exception;
        }

        XamlSourceGenHotReloadManager.HotReloadRudeEditDetected += OnRudeEdit;
        try
        {
            XamlSourceGenHotReloadManager.Register(target, _ => throw new MissingMethodException("Rude edit simulated"));
            XamlSourceGenHotReloadManager.UpdateApplication([typeof(RudeEditTarget)]);
        }
        finally
        {
            XamlSourceGenHotReloadManager.HotReloadRudeEditDetected -= OnRudeEdit;
        }

        Assert.Equal(typeof(RudeEditTarget), observedType);
        Assert.IsType<MissingMethodException>(observedException);
    }

    [Fact]
    public void UpdateApplication_Custom_Policy_Handler_Can_Cleanup_And_Reapply_AppOwned_SideEffects()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var target = new SideEffectTarget();
        target.SideEffects.Add("stale-style");
        target.SideEffects.Add("manual-flag");

        var policy = SourceGenHotReloadPolicies.Create<SideEffectTarget, List<string>>(
            priority: 50,
            captureState: static (_, instance) => new List<string>(instance.SideEffects),
            beforeElementReload: static (_, instance, _) => instance.SideEffects.Clear(),
            afterElementReload: static (_, instance, state) =>
            {
                if (state is null)
                {
                    return;
                }

                foreach (var value in state)
                {
                    if (value.StartsWith("manual-", StringComparison.Ordinal))
                    {
                        instance.SideEffects.Add(value);
                    }
                }
            });

        XamlSourceGenHotReloadManager.RegisterHandler(policy);
        XamlSourceGenHotReloadManager.Register(target, static instance =>
        {
            ((SideEffectTarget)instance).SideEffects.Add("generated-new");
        });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(SideEffectTarget)]);

        Assert.DoesNotContain("stale-style", target.SideEffects);
        Assert.Contains("manual-flag", target.SideEffects);
        Assert.Contains("generated-new", target.SideEffects);
    }

    [Fact]
    public void UpdateApplication_Raises_HotReload_Pipeline_Events_With_Context()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        SourceGenHotReloadUpdateContext? startedContext = null;
        SourceGenHotReloadUpdateContext? completedContext = null;
        var target = new PipelineEventTarget();

        XamlSourceGenHotReloadManager.Register(target, _ => { });
        XamlSourceGenHotReloadManager.HotReloadPipelineStarted += OnStarted;
        XamlSourceGenHotReloadManager.HotReloadPipelineCompleted += OnCompleted;

        try
        {
            XamlSourceGenHotReloadManager.UpdateApplication([typeof(PipelineEventTarget)]);
        }
        finally
        {
            XamlSourceGenHotReloadManager.HotReloadPipelineStarted -= OnStarted;
            XamlSourceGenHotReloadManager.HotReloadPipelineCompleted -= OnCompleted;
        }

        Assert.NotNull(startedContext);
        Assert.NotNull(completedContext);
        Assert.Equal(SourceGenHotReloadTrigger.MetadataUpdate, completedContext!.Trigger);
        Assert.Equal(1, completedContext.OperationCount);
        Assert.Single(completedContext.ReloadedTypes);
        Assert.Equal(typeof(PipelineEventTarget), completedContext.ReloadedTypes[0]);
        return;

        void OnStarted(SourceGenHotReloadUpdateContext context)
        {
            startedContext = context;
        }

        void OnCompleted(SourceGenHotReloadUpdateContext context)
        {
            completedContext = context;
        }
    }

    [Fact]
    public void UpdateApplication_Uses_Explicitly_Registered_HotReload_Handler()
    {
        ResetManager();
        AssemblyLevelHotReloadHandler.Reset();
        XamlSourceGenHotReloadManager.Enable();
        var target = new AssemblyLevelHandlerReloadTarget();
        XamlSourceGenHotReloadManager.RegisterHandler(new AssemblyLevelHotReloadHandler());

        XamlSourceGenHotReloadManager.Register(target, _ => { });
        XamlSourceGenHotReloadManager.UpdateApplication([typeof(AssemblyLevelHandlerReloadTarget)]);

        Assert.True(AssemblyLevelHotReloadHandler.ReloadCompletedCount > 0);
    }

    [Fact]
    public void UpdateApplication_Raises_HotReloaded_Event()
    {
        ResetManager();
        Type[]? observedTypes = null;

        void Handler(Type[]? updatedTypes)
        {
            observedTypes = updatedTypes;
        }

        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.HotReloaded += Handler;
        try
        {
            XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetEvent)]);
        }
        finally
        {
            XamlSourceGenHotReloadManager.HotReloaded -= Handler;
        }

        Assert.NotNull(observedTypes);
        Assert.Single(observedTypes!);
        Assert.Equal(typeof(ReloadTargetEvent), observedTypes![0]);
    }

    [Fact]
    public void RequiresUiDispatchForInstance_Returns_True_For_Style_And_ResourceProvider()
    {
        Assert.True(XamlSourceGenHotReloadManager.RequiresUiDispatchForInstance(new global::Avalonia.Styling.Styles()));
        Assert.True(XamlSourceGenHotReloadManager.RequiresUiDispatchForInstance(new global::Avalonia.Controls.ResourceDictionary()));
        Assert.False(XamlSourceGenHotReloadManager.RequiresUiDispatchForInstance(new object()));
        Assert.False(XamlSourceGenHotReloadManager.RequiresUiDispatchForInstance(null));
    }

    [Fact]
    public void UpdateApplication_Notifies_Hosted_Resources_For_Styles()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var host = new ResourceHostProbeApplication();
        var notificationCount = 0;
        host.ResourcesChanged += static (_, _) => { };
        host.ResourcesChanged += (_, _) => notificationCount++;
        var style = new global::Avalonia.Styling.Styles();
        ((global::Avalonia.Controls.IResourceProvider)style).AddOwner(host);

        var reloadCount = 0;
        XamlSourceGenHotReloadManager.Register(style, _ => reloadCount++);

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(global::Avalonia.Styling.Styles)]);

        Assert.Equal(1, reloadCount);
        Assert.True(notificationCount > 0);
    }

    [Fact]
    public void UpdateApplication_Expands_Changed_Include_BuildUri_To_Owning_Registered_Types()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var owner = new IncludeOwnerReloadTarget();
        XamlSourceGenHotReloadManager.Register(
            owner,
            _ => reloadCount++,
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://Demo/FluentTheme.xaml"
            });

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/FluentTheme.xaml",
            "avares://Demo/Controls/FluentControls.xaml",
            "Styles");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Controls/FluentControls.xaml",
            "avares://Demo/Controls/ListBoxItem.xaml",
            "Styles");

        XamlSourceGenTypeUriRegistry.Register(
            typeof(ChangedLeafGeneratedDocument),
            "avares://Demo/Controls/ListBoxItem.xaml");

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ChangedLeafGeneratedDocument)]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Resolves_Nested_MetadataUpdate_Type_To_Registered_Include_BuildUri()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var owner = new IncludeOwnerReloadTarget();
        XamlSourceGenHotReloadManager.Register(
            owner,
            _ => reloadCount++,
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://Demo/FluentTheme.xaml"
            });

        XamlIncludeGraphRegistry.Register(
            "avares://Demo/FluentTheme.xaml",
            "avares://Demo/Controls/FluentControls.xaml",
            "Styles");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Controls/FluentControls.xaml",
            "avares://Demo/Controls/TabItem.xaml",
            "Styles");

        XamlSourceGenTypeUriRegistry.Register(
            typeof(ChangedLeafGeneratedDocumentWithNestedType),
            "avares://Demo/Controls/TabItem.xaml");

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ChangedLeafGeneratedDocumentWithNestedType.NestedMetadataType)]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Refreshes_Artifacts_For_Updated_Type_Before_Reload()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var refreshCount = 0;
        XamlSourceGenArtifactRefreshRegistry.Register(
            typeof(ArtifactRefreshTarget),
            () => refreshCount++);

        var reloadCount = 0;
        var instance = new ArtifactRefreshTarget();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ArtifactRefreshTarget)]);

        Assert.Equal(1, refreshCount);
        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void EnableIdePollingFallback_CanBe_Enabled_And_Disabled()
    {
        ResetManager();

        XamlSourceGenHotReloadManager.EnableIdePollingFallback(intervalMs: 250);
        Assert.True(XamlSourceGenHotReloadManager.IsIdePollingFallbackEnabled);

        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        Assert.False(XamlSourceGenHotReloadManager.IsIdePollingFallbackEnabled);
    }

    [Fact]
    public void UpdateApplication_NativeCallback_Keeps_IdePollingFallback_Enabled()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.EnableIdePollingFallback(intervalMs: 250);

        var instance = new ReloadTargetA();
        XamlSourceGenHotReloadManager.Register(instance, _ => { });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetA)]);

        Assert.True(XamlSourceGenHotReloadManager.IsIdePollingFallbackEnabled);
    }

    [Fact]
    public void EnableIdePollingFallback_Rejects_Too_Short_Interval()
    {
        ResetManager();

        Assert.Throws<ArgumentOutOfRangeException>(() => XamlSourceGenHotReloadManager.EnableIdePollingFallback(intervalMs: 99));
    }

    [Fact]
    public void ShouldEnableIdePollingFallbackFromEnvironment_Checks_Dotnet_Modifiable_Assemblies()
    {
        ResetManager();
        var original = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");

        try
        {
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");
            Assert.True(XamlSourceGenHotReloadManager.ShouldEnableIdePollingFallbackFromEnvironment());

            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", null);
            Assert.False(XamlSourceGenHotReloadManager.ShouldEnableIdePollingFallbackFromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", original);
        }
    }

    [Fact]
    public void IdePollingFallback_SourceFileChange_Triggers_Reload_Attempts()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.EnableIdePollingFallback(intervalMs: 100);

        var reloadCount = 0;
        var tempPath = Path.Combine(Path.GetTempPath(), "AXSG-HotReload-" + Guid.NewGuid().ToString("N") + ".axaml");
        File.WriteAllText(tempPath, "<TextBlock/>");

        try
        {
            var instance = new ReloadTargetA();
            XamlSourceGenHotReloadManager.Register(instance, _ => Interlocked.Increment(ref reloadCount), tempPath);

            File.WriteAllText(tempPath, "<TextBlock Text=\"Updated\"/>");
            var reloaded = SpinWait.SpinUntil(() => Volatile.Read(ref reloadCount) > 0, millisecondsTimeout: 3000);

            Assert.True(reloaded);
            Assert.True(reloadCount > 0);
        }
        finally
        {
            XamlSourceGenHotReloadManager.DisableIdePollingFallback();
            XamlSourceGenHotReloadManager.ClearRegistrations();
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort temp cleanup.
            }
        }
    }

    private static void ResetManager()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.ResetHandlersToDefaults();
        XamlIncludeGraphRegistry.Clear();
        XamlSourceGenArtifactRefreshRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Clear();
    }

    private sealed class ReloadTargetA
    {
        public int ReloadCount { get; set; }
    }

    private sealed class ReloadTargetB
    {
    }

    private sealed class ReloadTargetC
    {
    }

    private sealed class ReloadTargetD
    {
    }

    private sealed class ReloadTargetEvent
    {
    }

    private sealed class IncludeOwnerReloadTarget
    {
    }

    private sealed class ArtifactRefreshTarget
    {
    }

    private sealed class ChangedLeafGeneratedDocument
    {
    }

    private sealed class ChangedLeafGeneratedDocumentWithNestedType
    {
        public sealed class NestedMetadataType
        {
        }
    }

    private sealed class GenericReloadTarget<T>
    {
    }

    private sealed class MetadataOriginalReloadTarget
    {
    }

    [MetadataUpdateOriginalType(typeof(MetadataOriginalReloadTarget))]
    private sealed class MetadataReplacementReloadTarget
    {
    }

    private sealed class ReentrantReloadTarget
    {
    }

    private sealed class StatefulReloadTarget
    {
        public int Value { get; set; }
    }

    private sealed class RudeEditTarget
    {
    }

    private sealed class SideEffectTarget
    {
        public List<string> SideEffects { get; } = [];
    }

    private sealed class TemplateProbeLayoutable : Layoutable
    {
        public int ApplyTemplateCount { get; private set; }

        public override void ApplyTemplate()
        {
            ApplyTemplateCount++;
            base.ApplyTemplate();
        }

        public void AttachChild(TemplateProbeLayoutable child)
        {
            VisualChildren.Add(child);
        }
    }

    private sealed class PipelineEventTarget
    {
    }

    private sealed class AssemblyLevelHandlerReloadTarget
    {
    }

    private sealed class ResourceHostProbeApplication : global::Avalonia.Application
    {
    }

    private sealed class RecordingHotReloadHandler : ISourceGenHotReloadHandler
    {
        public int BeforeVisualTreeUpdateCount { get; private set; }

        public int CaptureStateCount { get; private set; }

        public int BeforeElementReloadCount { get; private set; }

        public int AfterElementReloadCount { get; private set; }

        public int AfterVisualTreeUpdateCount { get; private set; }

        public int ReloadCompletedCount { get; private set; }

        public void BeforeVisualTreeUpdate(SourceGenHotReloadUpdateContext context)
        {
            BeforeVisualTreeUpdateCount++;
        }

        public object? CaptureState(Type reloadType, object instance)
        {
            CaptureStateCount++;
            return "handler-state";
        }

        public void BeforeElementReload(Type reloadType, object instance, object? state)
        {
            Assert.Equal("handler-state", state);
            BeforeElementReloadCount++;
        }

        public void AfterElementReload(Type reloadType, object instance, object? state)
        {
            Assert.Equal("handler-state", state);
            AfterElementReloadCount++;
        }

        public void AfterVisualTreeUpdate(SourceGenHotReloadUpdateContext context)
        {
            AfterVisualTreeUpdateCount++;
        }

        public void ReloadCompleted(SourceGenHotReloadUpdateContext context)
        {
            ReloadCompletedCount++;
        }
    }
}
