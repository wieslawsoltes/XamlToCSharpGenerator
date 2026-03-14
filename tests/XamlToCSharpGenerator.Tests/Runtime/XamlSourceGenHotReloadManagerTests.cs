using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenHotReloadManagerTests : IDisposable
{
    public void Dispose()
    {
        GeneratedArtifactTestRestore.RestoreAllLoadedGeneratedArtifacts();
    }

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
    public void UpdateApplication_Uses_Explicit_Tracking_Type_For_Classless_Roots()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var firstCount = 0;
        var secondCount = 0;
        var first = new ResourceDictionary();
        var second = new ResourceDictionary();

        XamlSourceGenHotReloadManager.Register(
            first,
            _ => firstCount++,
            new SourceGenHotReloadRegistrationOptions
            {
                TrackingType = typeof(ClasslessTrackingTypeA),
                BuildUri = "avares://Demo/ThemeA.axaml",
                SourcePath = "/tmp/ThemeA.axaml"
            });
        XamlSourceGenHotReloadManager.Register(
            second,
            _ => secondCount++,
            new SourceGenHotReloadRegistrationOptions
            {
                TrackingType = typeof(ClasslessTrackingTypeB),
                BuildUri = "avares://Demo/ThemeB.axaml",
                SourcePath = "/tmp/ThemeB.axaml"
            });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ClasslessTrackingTypeA)]);

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
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
    public void UpdateApplication_Maps_Equivalent_Replacement_Type_Instance_To_Original_Type()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new MetadataOriginalReloadTarget();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        const string assemblyName = "Axsg.Dynamic.MetadataUpdateMapping";
        const string typeFullName = "Axsg.Dynamic.MetadataReplacementReloadTarget";
        var registeredReplacementType = CreateDynamicType(assemblyName, typeFullName);
        var incomingReplacementType = CreateDynamicType(assemblyName, typeFullName);

        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            registeredReplacementType,
            typeof(MetadataOriginalReloadTarget));

        XamlSourceGenHotReloadManager.UpdateApplication([incomingReplacementType]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Does_Not_Map_Different_FullName_With_Same_Simple_Name()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new MetadataOriginalReloadTarget();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        const string assemblyName = "Axsg.Dynamic.MetadataUpdateMapping";
        var registeredReplacementType = CreateDynamicType(
            assemblyName,
            "Axsg.Dynamic.NamespaceA.MetadataReplacementReloadTarget");
        var incomingReplacementType = CreateDynamicType(
            assemblyName,
            "Axsg.Dynamic.NamespaceB.MetadataReplacementReloadTarget");

        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            registeredReplacementType,
            typeof(MetadataOriginalReloadTarget));

        XamlSourceGenHotReloadManager.UpdateApplication([incomingReplacementType]);

        Assert.Equal(0, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Does_Not_Map_Same_FullName_From_Different_Assembly_Name()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new MetadataOriginalReloadTarget();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        const string typeFullName = "Axsg.Dynamic.MetadataReplacementReloadTarget";
        var registeredReplacementType = CreateDynamicType("Axsg.Dynamic.MetadataUpdateMapping.A", typeFullName);
        var incomingReplacementType = CreateDynamicType("Axsg.Dynamic.MetadataUpdateMapping.B", typeFullName);

        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            registeredReplacementType,
            typeof(MetadataOriginalReloadTarget));

        XamlSourceGenHotReloadManager.UpdateApplication([incomingReplacementType]);

        Assert.Equal(0, reloadCount);
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
    public async Task UpdateApplication_DirectEnable_Dispatches_To_Existing_UiThread_When_PlatformSetup_Is_Not_Marked()
    {
        ResetManager();
        var originalPlatformSetupCompleted = SourceGenDispatcherRuntime.IsPlatformSetupCompleted;
        SourceGenDispatcherRuntime.ResetForTests();
        _ = Dispatcher.UIThread;

        var reloadExecuted = new ManualResetEventSlim();
        var reloadRanOnUiThread = false;

        try
        {
            XamlSourceGenHotReloadManager.Enable();

            var instance = new Border();
            XamlSourceGenHotReloadManager.Register(instance, _ =>
            {
                reloadRanOnUiThread = Dispatcher.UIThread.CheckAccess();
                reloadExecuted.Set();
            });

            var updateTask = Task.Run(() => XamlSourceGenHotReloadManager.UpdateApplication([typeof(Border)]));
            for (var attempt = 0; attempt < 50 && !reloadExecuted.IsSet && !updateTask.IsCompleted; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            Dispatcher.UIThread.RunJobs();
            await updateTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(reloadExecuted.IsSet);
            Assert.True(reloadRanOnUiThread);
        }
        finally
        {
            ResetManager();
            if (originalPlatformSetupCompleted)
            {
                SourceGenDispatcherRuntime.MarkPlatformSetupCompleted();
            }
            else
            {
                SourceGenDispatcherRuntime.ResetForTests();
            }
        }
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
    public void UpdateApplication_Default_Handlers_Do_Not_Force_Template_Rematerialization()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var root = new TemplateProbeLayoutable();
        var child = new TemplateProbeLayoutable();
        root.AttachChild(child);

        XamlSourceGenHotReloadManager.Register(root, _ => { });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(TemplateProbeLayoutable)]);

        Assert.Equal(0, root.ApplyTemplateCount);
        Assert.Equal(0, child.ApplyTemplateCount);
    }

    [Fact]
    public void UpdateApplication_Default_Handlers_Preserve_TextBox_State_When_Rebuilding_Logical_Subtree()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var view = new StatefulTextView();
        view.Content = BuildEditableSurface(includeSpacer: true);

        var initialSurface = Assert.IsType<StackPanel>(view.Content);
        var firstBefore = Assert.IsType<TextBox>(initialSurface.Children[0]);
        var secondBefore = Assert.IsType<TextBox>(initialSurface.Children[2]);
        firstBefore.Text = "alpha";
        firstBefore.CaretIndex = 5;
        secondBefore.Text = "beta";
        secondBefore.SelectionStart = 1;
        secondBefore.SelectionEnd = 3;

        XamlSourceGenHotReloadManager.Register(
            view,
            static instance =>
            {
                var typedView = (StatefulTextView)instance;
                typedView.Content = BuildEditableSurface(includeSpacer: false);
            });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(StatefulTextView)]);

        var refreshedSurface = Assert.IsType<StackPanel>(view.Content);
        var firstAfter = Assert.IsType<TextBox>(refreshedSurface.Children[0]);
        var secondAfter = Assert.IsType<TextBox>(refreshedSurface.Children[1]);
        Assert.Equal("alpha", firstAfter.Text);
        Assert.Equal(5, firstAfter.CaretIndex);
        Assert.Equal("beta", secondAfter.Text);
        Assert.Equal(1, secondAfter.SelectionStart);
        Assert.Equal(3, secondAfter.SelectionEnd);
    }

    [Fact]
    public void UpdateApplication_Default_Handlers_Preserve_TextBox_State_When_Removing_Preceding_SameType_Control()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var view = new StatefulTextView();
        view.Content = BuildSemanticEditableSurface(includeLeadingTextBox: true);

        var initialSurface = Assert.IsType<StackPanel>(view.Content);
        var usernameBefore = Assert.IsType<TextBox>(initialSurface.Children[1]);
        var notesBefore = Assert.IsType<TextBox>(initialSurface.Children[2]);
        usernameBefore.Text = "john";
        usernameBefore.CaretIndex = 4;
        notesBefore.Text = "notes-123";
        notesBefore.SelectionStart = 0;
        notesBefore.SelectionEnd = 5;

        XamlSourceGenHotReloadManager.Register(
            view,
            static instance =>
            {
                var typedView = (StatefulTextView)instance;
                typedView.Content = BuildSemanticEditableSurface(includeLeadingTextBox: false);
            });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(StatefulTextView)]);

        var refreshedSurface = Assert.IsType<StackPanel>(view.Content);
        var usernameAfter = Assert.IsType<TextBox>(refreshedSurface.Children[0]);
        var notesAfter = Assert.IsType<TextBox>(refreshedSurface.Children[1]);
        Assert.Equal("john", usernameAfter.Text);
        Assert.Equal(4, usernameAfter.CaretIndex);
        Assert.Equal("notes-123", notesAfter.Text);
        Assert.Equal(0, notesAfter.SelectionStart);
        Assert.Equal(5, notesAfter.SelectionEnd);
    }

    [Fact]
    public void UpdateApplication_Default_Handlers_Preserve_TwoWay_Control_State_When_Rebuilding_Logical_Subtree()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var view = new StatefulTextView();
        view.Content = BuildInteractiveStateSurface(includeSpacer: true);

        var initialSurface = Assert.IsType<StackPanel>(view.Content);
        var sliderBefore = Assert.IsType<Slider>(initialSurface.Children[0]);
        var checkBoxBefore = Assert.IsType<CheckBox>(initialSurface.Children[2]);
        var comboBoxBefore = Assert.IsType<ComboBox>(initialSurface.Children[3]);

        sliderBefore.Value = 72.5;
        checkBoxBefore.IsChecked = true;
        comboBoxBefore.SelectedIndex = 2;

        XamlSourceGenHotReloadManager.Register(
            view,
            static instance =>
            {
                var typedView = (StatefulTextView)instance;
                typedView.Content = BuildInteractiveStateSurface(includeSpacer: false);
            });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(StatefulTextView)]);

        var refreshedSurface = Assert.IsType<StackPanel>(view.Content);
        var sliderAfter = Assert.IsType<Slider>(refreshedSurface.Children[0]);
        var checkBoxAfter = Assert.IsType<CheckBox>(refreshedSurface.Children[1]);
        var comboBoxAfter = Assert.IsType<ComboBox>(refreshedSurface.Children[2]);

        Assert.Equal(72.5, sliderAfter.Value);
        Assert.True(checkBoxAfter.IsChecked);
        Assert.Equal(2, comboBoxAfter.SelectedIndex);
    }

    [Fact]
    public void UpdateApplication_Default_StateTransfer_Is_Suppressed_When_Style_Reload_Operations_Are_In_Pipeline()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var view = new StatefulTextView();
        view.Content = BuildEditableSurface(includeSpacer: true);

        var styleProbe = new ThemeStyleReloadSentinel();
        var initialSurface = Assert.IsType<StackPanel>(view.Content);
        var firstBefore = Assert.IsType<TextBox>(initialSurface.Children[0]);
        firstBefore.Text = "alpha";

        XamlSourceGenHotReloadManager.Register(styleProbe, _ => { });
        XamlSourceGenHotReloadManager.Register(
            view,
            static instance =>
            {
                var typedView = (StatefulTextView)instance;
                typedView.Content = BuildEditableSurface(includeSpacer: false);
            });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(StatefulTextView), typeof(ThemeStyleReloadSentinel)]);

        var refreshedSurface = Assert.IsType<StackPanel>(view.Content);
        var firstAfter = Assert.IsType<TextBox>(refreshedSurface.Children[0]);
        Assert.NotEqual("alpha", firstAfter.Text);
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
    public void UpdateApplication_MetadataOnly_Mode_Completes_Metadata_Handshake_On_First_Metadata_Update()
    {
        ResetManager();

        var originalMode = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE");
        var originalTimeout = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS");
        var originalModifiableAssemblies = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
        var statuses = new List<SourceGenHotReloadTransportStatus>();

        try
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", "MetadataOnly");
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS", "1000");
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");

            void OnStatus(SourceGenHotReloadTransportStatus status)
            {
                lock (statuses)
                {
                    statuses.Add(status);
                }
            }

            XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged += OnStatus;
            try
            {
                XamlSourceGenHotReloadManager.Enable();

                var instance = new ReloadTargetA();
                XamlSourceGenHotReloadManager.Register(instance, _ => { });
                XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetA)]);
            }
            finally
            {
                XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged -= OnStatus;
            }

            Assert.True(ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.TransportSelected, "MetadataUpdate"));
            Assert.True(ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.HandshakeStarted, "MetadataUpdate"));
            Assert.True(ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.HandshakeCompleted, "MetadataUpdate"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", originalMode);
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS", originalTimeout);
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", originalModifiableAssemblies);
            ResetManager();
        }
    }

    [Fact]
    public void Enable_Auto_Mode_Times_Out_Metadata_Handshake_And_Attempts_Remote_Fallback()
    {
        ResetManager();

        var originalMode = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE");
        var originalTimeout = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS");
        var originalModifiableAssemblies = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
        var originalRemoteEndpoint = Environment.GetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName);
        var originalIosHotReloadEnabled = Environment.GetEnvironmentVariable("AXSG_IOS_HOTRELOAD_ENABLED");
        var statuses = new List<SourceGenHotReloadTransportStatus>();

        try
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", "Auto");
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS", "50");
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, null);
            Environment.SetEnvironmentVariable("AXSG_IOS_HOTRELOAD_ENABLED", "true");

            void OnStatus(SourceGenHotReloadTransportStatus status)
            {
                lock (statuses)
                {
                    statuses.Add(status);
                }
            }

            XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged += OnStatus;
            try
            {
                XamlSourceGenHotReloadManager.Enable();

                var completed = SpinWait.SpinUntil(
                    () =>
                    {
                        lock (statuses)
                        {
                            return ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.HandshakeFailed, "MetadataUpdate") &&
                                   ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.TransportSelected, "RemoteSocket", requireFallback: true);
                        }
                    },
                    millisecondsTimeout: 3000);

                Assert.True(completed);
            }
            finally
            {
                XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged -= OnStatus;
            }

            Assert.True(ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.HandshakeFailed, "RemoteSocket"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", originalMode);
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS", originalTimeout);
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", originalModifiableAssemblies);
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, originalRemoteEndpoint);
            Environment.SetEnvironmentVariable("AXSG_IOS_HOTRELOAD_ENABLED", originalIosHotReloadEnabled);
            ResetManager();
        }
    }

    [Fact]
    public void Enable_Auto_Mode_Does_Not_Fallback_Without_Mobile_Context_Or_Remote_Endpoint()
    {
        ResetManager();

        var originalMode = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE");
        var originalTimeout = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS");
        var originalModifiableAssemblies = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
        var originalRemoteEndpoint = Environment.GetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName);
        var originalIosHotReloadEnabled = Environment.GetEnvironmentVariable("AXSG_IOS_HOTRELOAD_ENABLED");
        var statuses = new List<SourceGenHotReloadTransportStatus>();

        try
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", "Auto");
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS", "50");
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, null);
            Environment.SetEnvironmentVariable("AXSG_IOS_HOTRELOAD_ENABLED", null);

            void OnStatus(SourceGenHotReloadTransportStatus status)
            {
                lock (statuses)
                {
                    statuses.Add(status);
                }
            }

            XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged += OnStatus;
            try
            {
                XamlSourceGenHotReloadManager.Enable();

                Thread.Sleep(200);
            }
            finally
            {
                XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged -= OnStatus;
            }

            Assert.False(ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.HandshakeFailed, "MetadataUpdate"));
            Assert.False(ContainsStatus(statuses, SourceGenHotReloadTransportStatusKind.TransportSelected, "RemoteSocket"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", originalMode);
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS", originalTimeout);
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", originalModifiableAssemblies);
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, originalRemoteEndpoint);
            Environment.SetEnvironmentVariable("AXSG_IOS_HOTRELOAD_ENABLED", originalIosHotReloadEnabled);
            ResetManager();
        }
    }

    [Fact]
    public void Disable_Resets_Transport_State_And_Next_Enable_Reinitializes_Transport()
    {
        ResetManager();

        var originalMode = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE");
        var originalModifiableAssemblies = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
        var statuses = new List<SourceGenHotReloadTransportStatus>();

        try
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", "MetadataOnly");
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");

            void OnStatus(SourceGenHotReloadTransportStatus status)
            {
                lock (statuses)
                {
                    statuses.Add(status);
                }
            }

            XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged += OnStatus;
            try
            {
                XamlSourceGenHotReloadManager.Enable();
                XamlSourceGenHotReloadManager.Disable();
                XamlSourceGenHotReloadManager.Enable();
            }
            finally
            {
                XamlSourceGenHotReloadManager.HotReloadTransportStatusChanged -= OnStatus;
            }

            var selectedCount = CountStatuses(statuses, SourceGenHotReloadTransportStatusKind.TransportSelected, "MetadataUpdate");
            Assert.True(selectedCount >= 2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", originalMode);
            Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", originalModifiableAssemblies);
            ResetManager();
        }
    }

    [Fact]
    public async Task RemoteOnly_Transport_Applies_Remote_Operation_And_Publishes_Ack_And_Status()
    {
        ResetManager();

        var originalMode = Environment.GetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE");
        var originalEndpoint = Environment.GetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName);
        var reloadCount = 0;
        var operationStatuses = new List<SourceGenHotReloadRemoteOperationStatus>();
        var ackReceived = new ManualResetEventSlim(initialState: false);
        string? ackState = null;
        bool? ackSuccess = null;
        long? ackOperationId = null;

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = Task.Run(() =>
        {
            using var socket = listener.AcceptTcpClient();
            using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            _ = reader.ReadLine();
            _ = reader.ReadLine();

            var applyPayload = JsonSerializer.Serialize(new
            {
                messageType = "apply",
                operationId = 42L,
                requestId = "remote-request-42",
                correlationId = 4200L,
                typeNames = new[]
                {
                    typeof(RemoteTransportReloadTarget).AssemblyQualifiedName
                }
            });
            writer.WriteLine(applyPayload);

            var ackPayload = reader.ReadLine();
            if (ackPayload is null)
            {
                return;
            }

            using var ackDocument = JsonDocument.Parse(ackPayload);
            var ackRoot = ackDocument.RootElement;
            if (ackRoot.TryGetProperty("operationId", out var operationElement) &&
                operationElement.TryGetInt64(out var parsedOperationId))
            {
                ackOperationId = parsedOperationId;
            }

            if (ackRoot.TryGetProperty("state", out var stateElement) &&
                stateElement.ValueKind == JsonValueKind.String)
            {
                ackState = stateElement.GetString();
            }

            if (ackRoot.TryGetProperty("isSuccess", out var successElement) &&
                (successElement.ValueKind == JsonValueKind.True || successElement.ValueKind == JsonValueKind.False))
            {
                ackSuccess = successElement.GetBoolean();
            }

            ackReceived.Set();
        });

        try
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", "RemoteOnly");
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, "tcp://127.0.0.1:" + port);

            var target = new RemoteTransportReloadTarget();
            XamlSourceGenHotReloadManager.Register(target, _ => Interlocked.Increment(ref reloadCount));

            void OnRemoteStatus(SourceGenHotReloadRemoteOperationStatus status)
            {
                lock (operationStatuses)
                {
                    operationStatuses.Add(status);
                }
            }

            XamlSourceGenHotReloadManager.HotReloadRemoteOperationStatusChanged += OnRemoteStatus;
            try
            {
                XamlSourceGenHotReloadManager.Enable();

                Assert.True(ackReceived.Wait(5000));
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref reloadCount) > 0, millisecondsTimeout: 3000));
                await acceptTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally
            {
                XamlSourceGenHotReloadManager.HotReloadRemoteOperationStatusChanged -= OnRemoteStatus;
            }

            Assert.Equal(1, reloadCount);
            Assert.Equal(42L, ackOperationId);
            Assert.Equal("Succeeded", ackState);
            Assert.True(ackSuccess);

            lock (operationStatuses)
            {
                Assert.Contains(operationStatuses, status => status.OperationId == 42L && status.State == SourceGenStudioOperationState.Applying);
                Assert.Contains(operationStatuses, status => status.OperationId == 42L && status.State == SourceGenStudioOperationState.Succeeded);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AXSG_HOTRELOAD_TRANSPORT_MODE", originalMode);
            Environment.SetEnvironmentVariable(RemoteSocketTransport.RemoteEndpointEnvVarName, originalEndpoint);
            ResetManager();
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
    public void UpdateApplication_MetadataUpdate_Disables_IdePollingFallback_After_First_Success()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.EnableIdePollingFallback(intervalMs: 250);

        var instance = new ReloadTargetA();
        XamlSourceGenHotReloadManager.Register(instance, _ => { });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetA)]);

        Assert.False(XamlSourceGenHotReloadManager.IsIdePollingFallbackEnabled);
    }

    [Fact]
    public void UpdateApplication_Coalesces_Duplicate_Metadata_Requests_In_Short_Window()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new DuplicateCoalesceTarget();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(DuplicateCoalesceTarget)]);
        XamlSourceGenHotReloadManager.UpdateApplication([typeof(DuplicateCoalesceTarget)]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Coalesces_Duplicate_Metadata_Requests_For_Nested_Generated_Types_Sharing_BuildUri()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new DuplicateCoalesceTarget();
        XamlSourceGenHotReloadManager.Register(
            instance,
            _ => reloadCount++,
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://Demo/Controls/ListBoxItem.xaml"
            });

        XamlSourceGenTypeUriRegistry.Register(
            typeof(DuplicateGeneratedUpdateOwner),
            "avares://Demo/Controls/ListBoxItem.xaml");
        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            typeof(DuplicateGeneratedUpdateOwner),
            typeof(DuplicateCoalesceTarget));
        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            typeof(DuplicateGeneratedUpdateOwner.NestedMetadataUpdateA),
            typeof(DuplicateCoalesceTarget));
        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            typeof(DuplicateGeneratedUpdateOwner.NestedMetadataUpdateB),
            typeof(DuplicateCoalesceTarget));

        XamlSourceGenHotReloadManager.UpdateApplication(
        [
            typeof(DuplicateGeneratedUpdateOwner),
            typeof(DuplicateGeneratedUpdateOwner.NestedMetadataUpdateA)
        ]);
        XamlSourceGenHotReloadManager.UpdateApplication(
        [
            typeof(DuplicateGeneratedUpdateOwner),
            typeof(DuplicateGeneratedUpdateOwner.NestedMetadataUpdateB)
        ]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Coalesces_Duplicate_Metadata_Requests_When_Uri_Matches_And_Helper_Type_Differs()
    {
        ResetManager();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new DuplicateCoalesceTarget();
        XamlSourceGenHotReloadManager.Register(
            instance,
            _ => reloadCount++,
            new SourceGenHotReloadRegistrationOptions
            {
                BuildUri = "avares://Demo/Controls/ListBoxItem.xaml"
            });

        XamlSourceGenTypeUriRegistry.Register(
            typeof(DuplicateGeneratedUpdateOwner),
            "avares://Demo/Controls/ListBoxItem.xaml");
        XamlSourceGenHotReloadManager.RegisterReplacementTypeMapping(
            typeof(DuplicateGeneratedUpdateOwner),
            typeof(DuplicateCoalesceTarget));

        XamlSourceGenHotReloadManager.UpdateApplication(
        [
            typeof(DuplicateGeneratedUpdateOwner),
            typeof(DuplicateGeneratedUpdateHelperA)
        ]);
        XamlSourceGenHotReloadManager.UpdateApplication(
        [
            typeof(DuplicateGeneratedUpdateOwner),
            typeof(DuplicateGeneratedUpdateHelperB)
        ]);

        Assert.Equal(1, reloadCount);
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

    private static Type CreateDynamicType(string assemblyName, string typeFullName)
    {
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assemblyName),
            AssemblyBuilderAccess.Run);
        var moduleBuilder = dynamicAssembly.DefineDynamicModule(assemblyName);
        var typeBuilder = moduleBuilder.DefineType(
            typeFullName,
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed);
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        return typeBuilder.CreateType() ?? throw new InvalidOperationException("Failed to create dynamic type.");
    }

    private static bool ContainsStatus(
        List<SourceGenHotReloadTransportStatus> statuses,
        SourceGenHotReloadTransportStatusKind kind,
        string transportName,
        bool requireFallback = false)
    {
        for (var index = 0; index < statuses.Count; index++)
        {
            var status = statuses[index];
            if (status.Kind != kind ||
                !string.Equals(status.TransportName, transportName, StringComparison.Ordinal))
            {
                continue;
            }

            if (requireFallback && !status.IsFallback)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static int CountStatuses(
        List<SourceGenHotReloadTransportStatus> statuses,
        SourceGenHotReloadTransportStatusKind kind,
        string transportName)
    {
        var count = 0;
        for (var index = 0; index < statuses.Count; index++)
        {
            var status = statuses[index];
            if (status.Kind == kind &&
                string.Equals(status.TransportName, transportName, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
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

    private sealed class DuplicateCoalesceTarget
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

    private sealed class DuplicateGeneratedUpdateOwner
    {
        public sealed class NestedMetadataUpdateA
        {
        }

        public sealed class NestedMetadataUpdateB
        {
        }
    }

    private sealed class DuplicateGeneratedUpdateHelperA
    {
    }

    private sealed class DuplicateGeneratedUpdateHelperB
    {
    }

    private sealed class ClasslessTrackingTypeA
    {
    }

    private sealed class ClasslessTrackingTypeB
    {
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

    private sealed class StatefulTextView : UserControl
    {
    }

    private sealed class ThemeStyleReloadSentinel : Style
    {
    }

    private sealed class RemoteTransportReloadTarget
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

    private static StackPanel BuildEditableSurface(bool includeSpacer)
    {
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new TextBox());
        if (includeSpacer)
        {
            stackPanel.Children.Add(new Border { Height = 4 });
        }

        stackPanel.Children.Add(new TextBox());
        return stackPanel;
    }

    private static StackPanel BuildSemanticEditableSurface(bool includeLeadingTextBox)
    {
        var stackPanel = new StackPanel();
        if (includeLeadingTextBox)
        {
            stackPanel.Children.Add(new TextBox { Watermark = "removed-before-hot-reload" });
        }

        stackPanel.Children.Add(new TextBox { Watermark = "username" });
        stackPanel.Children.Add(new TextBox { Watermark = "notes" });
        return stackPanel;
    }

    private static StackPanel BuildInteractiveStateSurface(bool includeSpacer)
    {
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new Slider { Minimum = 0, Maximum = 100 });
        if (includeSpacer)
        {
            stackPanel.Children.Add(new Border { Height = 4 });
        }

        stackPanel.Children.Add(new CheckBox { Content = "Accept terms" });
        stackPanel.Children.Add(new ComboBox
        {
            ItemsSource = new[] { "first", "second", "third" }
        });
        return stackPanel;
    }
}
