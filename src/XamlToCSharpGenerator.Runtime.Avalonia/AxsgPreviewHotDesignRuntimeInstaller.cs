using System;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Configures preview-only hot-design runtime services for the bundled designer host.
/// </summary>
public static class AxsgPreviewHotDesignRuntimeInstaller
{
    private static readonly object Sync = new();

    private static bool _applierRegistered;
    private static XamlSourceGenStudioRemoteDesignServer? _remoteServer;
    private static string? _remoteHost;
    private static int _remotePort;

    public static void Initialize(string? remoteHost, int? remotePort)
    {
        EnsurePreviewApplierRegistered();

        if (remotePort is not > 0)
        {
            return;
        }

        string resolvedHost = string.IsNullOrWhiteSpace(remoteHost) ? "127.0.0.1" : remoteHost.Trim();
        lock (Sync)
        {
            if (_remoteServer is not null &&
                string.Equals(_remoteHost, resolvedHost, StringComparison.OrdinalIgnoreCase) &&
                _remotePort == remotePort.Value)
            {
                return;
            }

            _remoteServer?.Dispose();
            _remoteServer = new XamlSourceGenStudioRemoteDesignServer(new SourceGenStudioOptions
            {
                EnableRemoteDesign = true,
                RemoteHost = resolvedHost,
                RemotePort = remotePort.Value,
                EnableExternalWindow = false,
                ShowOverlayIndicator = false,
                AutoOpenStudioWindowOnStartup = false,
                AutoOpenVncViewerOnDesktop = false
            });
            _remoteServer.Start();
            _remoteHost = resolvedHost;
            _remotePort = remotePort.Value;
        }
    }

    private static void EnsurePreviewApplierRegistered()
    {
        lock (Sync)
        {
            if (_applierRegistered)
            {
                return;
            }

            XamlSourceGenHotDesignManager.RegisterApplier(new PreviewHotDesignUpdateApplier());
            _applierRegistered = true;
        }
    }

    private sealed class PreviewHotDesignUpdateApplier : ISourceGenHotDesignUpdateApplier
    {
        public int Priority => 1000;

        public bool CanApply(SourceGenHotDesignUpdateContext context)
        {
            if (context is null)
            {
                return false;
            }

            if (context.Options.PersistChangesToSource)
            {
                return false;
            }

            return AxsgPreviewHotDesignSessionBridge.TryGetCurrentDocument(
                       out _,
                       out var buildUri,
                       out _,
                       out _)
                   && string.Equals(buildUri, context.Document.BuildUri, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<SourceGenHotDesignApplyResult> ApplyAsync(
            SourceGenHotDesignUpdateContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            AxsgPreviewHotDesignSessionBridge.TryGetCurrentDocument(
                out _,
                out _,
                out _,
                out var currentXamlText);

            MinimalTextPatch patch = MinimalTextDiff.CreatePatch(currentXamlText ?? string.Empty, context.Request.XamlText);
            XamlSourceGenHotDesignTool.SetCurrentDocumentText(context.Document.BuildUri, context.Request.XamlText);

            return ValueTask.FromResult(new SourceGenHotDesignApplyResult(
                Succeeded: true,
                Message: "Queued preview hot-design update for editor synchronization.",
                BuildUri: context.Document.BuildUri,
                TargetType: context.Document.RootType,
                SourcePath: context.Document.SourcePath,
                SourcePersisted: false,
                MinimalDiffApplied: true,
                MinimalDiffStart: patch.Start,
                MinimalDiffRemovedLength: patch.RemovedLength,
                MinimalDiffInsertedLength: patch.InsertedLength,
                HotReloadObserved: false,
                RuntimeFallbackApplied: false));
        }
    }
}
