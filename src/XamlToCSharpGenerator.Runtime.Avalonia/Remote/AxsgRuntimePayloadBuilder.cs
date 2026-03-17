using System;
using System.Collections.Generic;
using System.Linq;

namespace XamlToCSharpGenerator.Runtime;

internal static class AxsgRuntimePayloadBuilder
{
    public static object BuildHotReloadStatusPayload(SourceGenHotReloadStatus status)
    {
        return new
        {
            status.IsEnabled,
            status.IsIdePollingFallbackEnabled,
            status.RegisteredTypeCount,
            status.RegisteredBuildUriCount,
            transportMode = status.TransportMode.ToString(),
            lastTransportStatus = BuildHotReloadTransportStatusPayload(status.LastTransportStatus),
            lastRemoteOperation = BuildHotReloadRemoteOperationStatusPayload(status.LastRemoteOperationStatus)
        };
    }

    public static object? BuildHotReloadTransportStatusPayload(SourceGenHotReloadTransportStatus? status)
    {
        if (status is null)
        {
            return null;
        }

        return new
        {
            kind = status.Kind.ToString(),
            status.TransportName,
            mode = status.Mode.ToString(),
            status.Message,
            status.TimestampUtc,
            status.IsFallback,
            exception = status.Exception?.Message
        };
    }

    public static object? BuildHotReloadRemoteOperationStatusPayload(SourceGenHotReloadRemoteOperationStatus? status)
    {
        if (status is null)
        {
            return null;
        }

        return new
        {
            status.OperationId,
            status.RequestId,
            status.CorrelationId,
            state = status.State.ToString(),
            status.StartedAtUtc,
            status.CompletedAtUtc,
            request = new
            {
                status.Request.OperationId,
                status.Request.RequestId,
                status.Request.CorrelationId,
                status.Request.ApplyAll,
                status.Request.TypeNames,
                status.Request.BuildUris,
                status.Request.Trigger
            },
            result = status.Result is null ? null : new
            {
                status.Result.OperationId,
                status.Result.RequestId,
                status.Result.CorrelationId,
                state = status.Result.State.ToString(),
                isSuccess = status.Result.IsSuccess,
                status.Result.Message,
                status.Result.Diagnostics
            },
            status.Diagnostics
        };
    }

    public static object BuildHotReloadTrackedDocumentsPayload(IReadOnlyList<SourceGenHotReloadTrackedDocumentDescriptor> trackedDocuments)
    {
        return trackedDocuments.Select(static document => new
        {
            trackingTypeName = document.TrackingType.FullName,
            document.BuildUri,
            document.SourcePath,
            document.LiveInstanceCount,
            document.IsSourceWatched
        });
    }

    public static object BuildRuntimeEventsPayload(IReadOnlyList<AxsgRuntimeMcpEventEntry> events)
    {
        return events.Select(static entry => new
        {
            entry.Sequence,
            entry.Kind,
            entry.TimestampUtc,
            entry.Message,
            entry.Data
        });
    }

    public static object BuildHotDesignStatusPayload(SourceGenHotDesignStatus status)
    {
        return new
        {
            status.IsEnabled,
            status.RegisteredDocumentCount,
            status.RegisteredApplierCount,
            options = new
            {
                status.Options.PersistChangesToSource,
                status.Options.UseMinimalDiffPersistence,
                status.Options.WaitForHotReload,
                status.Options.HotReloadWaitTimeout,
                status.Options.FallbackToRuntimeApplyOnTimeout,
                status.Options.EnableTracing,
                status.Options.MaxHistoryEntries
            }
        };
    }

    public static object BuildHotDesignDocumentsPayload(IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
    {
        return documents.Select(static document => BuildHotDesignDocumentPayload(document));
    }

    public static object BuildHotDesignDocumentPayload(SourceGenHotDesignDocumentDescriptor document)
    {
        return new
        {
            rootTypeName = document.RootType.FullName,
            document.BuildUri,
            document.SourcePath,
            document.LiveInstanceCount,
            documentRole = document.DocumentRole.ToString(),
            artifactKind = document.ArtifactKind.ToString(),
            document.ScopeHints
        };
    }

    public static object BuildHotDesignSelectedDocumentPayload(
        string? activeBuildUri,
        SourceGenHotDesignDocumentDescriptor? document)
    {
        return new
        {
            activeBuildUri,
            document = document is null ? null : BuildHotDesignDocumentPayload(document)
        };
    }

    public static object BuildStudioStatusPayload(SourceGenStudioStatusSnapshot snapshot)
    {
        return new
        {
            snapshot.IsEnabled,
            snapshot.SessionId,
            currentState = snapshot.CurrentState.ToString(),
            snapshot.RegisteredDocumentCount,
            snapshot.ActiveScopeCount,
            options = new
            {
                snapshot.Options.PersistChangesToSource,
                waitMode = snapshot.Options.WaitMode.ToString(),
                updateTimeout = snapshot.Options.UpdateTimeout,
                fallbackPolicy = snapshot.Options.FallbackPolicy.ToString(),
                snapshot.Options.ShowOverlayIndicator,
                snapshot.Options.EnableExternalWindow,
                snapshot.Options.AutoOpenStudioWindowOnStartup,
                snapshot.Options.EnableTracing,
                canvasLayoutMode = snapshot.Options.CanvasLayoutMode.ToString(),
                snapshot.Options.MaxOperationHistoryEntries,
                snapshot.Options.EnableRemoteDesign,
                snapshot.Options.RemoteHost,
                snapshot.Options.RemotePort,
                snapshot.Options.VncEndpoint,
                snapshot.Options.AutoOpenVncViewerOnDesktop
            },
            remote = new
            {
                snapshot.Remote.IsEnabled,
                snapshot.Remote.IsListening,
                snapshot.Remote.Host,
                snapshot.Remote.Port,
                snapshot.Remote.ActiveClientCount,
                snapshot.Remote.LastError,
                snapshot.Remote.VncEndpoint,
                snapshot.Remote.UpdatedAtUtc
            },
            scopes = BuildStudioScopesPayload(snapshot.Scopes),
            operations = snapshot.Operations.Select(static operation => new
            {
                operation.OperationId,
                operation.RequestId,
                operation.CorrelationId,
                state = operation.State.ToString(),
                operation.StartedAtUtc,
                operation.CompletedAtUtc,
                request = new
                {
                    operation.Request.BuildUri,
                    operation.Request.TargetTypeName,
                    scopeKind = operation.Request.ScopeKind.ToString(),
                    operation.Request.ScopeId
                },
                result = operation.Result is null ? null : BuildStudioUpdateResultPayload(operation.Result),
                diagnostics = operation.Diagnostics
            })
        };
    }

    public static object BuildStudioScopesPayload(IReadOnlyList<SourceGenStudioScopeDescriptor> scopes)
    {
        return scopes.Select(static scope => BuildStudioScopePayload(scope));
    }

    public static object BuildStudioScopePayload(SourceGenStudioScopeDescriptor scope)
    {
        return new
        {
            scopeKind = scope.ScopeKind.ToString(),
            scope.Id,
            scope.DisplayName,
            targetTypeName = scope.TargetType?.FullName,
            scope.BuildUri
        };
    }

    public static object BuildStudioUpdateResultPayload(SourceGenStudioUpdateResult result)
    {
        return new
        {
            result.Succeeded,
            result.Message,
            result.OperationId,
            result.RequestId,
            result.CorrelationId,
            state = result.State.ToString(),
            result.SourcePersisted,
            result.LocalUpdateObserved,
            result.RuntimeFallbackApplied,
            result.BuildUri,
            targetTypeName = result.TargetType?.FullName,
            error = result.Error?.Message,
            result.Diagnostics
        };
    }

    public static object BuildStudioApplyPayload(
        SourceGenStudioUpdateResult result,
        SourceGenStudioStatusSnapshot status,
        object? workspace = null)
    {
        return new
        {
            applyResult = BuildStudioUpdateResultPayload(result),
            status = BuildStudioStatusPayload(status),
            workspace
        };
    }

    public static object BuildHotDesignApplyResultPayload(SourceGenHotDesignApplyResult result)
    {
        return new
        {
            result.Succeeded,
            result.Message,
            result.BuildUri,
            targetTypeName = result.TargetType?.FullName,
            result.SourcePath,
            result.SourcePersisted,
            result.MinimalDiffApplied,
            result.MinimalDiffStart,
            result.MinimalDiffRemovedLength,
            result.MinimalDiffInsertedLength,
            result.HotReloadObserved,
            result.RuntimeFallbackApplied,
            error = result.Error?.Message
        };
    }

    public static object BuildHotDesignWorkspacePayload(
        SourceGenHotDesignWorkspaceSnapshot workspace,
        SourceGenStudioStatusSnapshot studioStatus,
        SourceGenHotDesignHitTestMode hitTestMode)
    {
        return new
        {
            status = BuildHotDesignStatusPayload(workspace.Status),
            remote = new
            {
                studioStatus.Remote.IsEnabled,
                studioStatus.Remote.IsListening,
                studioStatus.Remote.Host,
                studioStatus.Remote.Port,
                studioStatus.Remote.ActiveClientCount,
                studioStatus.Remote.LastError,
                studioStatus.Remote.VncEndpoint,
                studioStatus.Remote.UpdatedAtUtc
            },
            mode = workspace.Mode.ToString(),
            propertyFilterMode = workspace.PropertyFilterMode.ToString(),
            hitTestMode = hitTestMode.ToString(),
            panels = new
            {
                workspace.Panels.ToolbarVisible,
                workspace.Panels.ElementsVisible,
                workspace.Panels.ToolboxVisible,
                workspace.Panels.CanvasVisible,
                workspace.Panels.PropertiesVisible
            },
            canvas = new
            {
                workspace.Canvas.Zoom,
                workspace.Canvas.FormFactor,
                workspace.Canvas.Width,
                workspace.Canvas.Height,
                workspace.Canvas.DarkTheme
            },
            workspace.ActiveBuildUri,
            workspace.SelectedElementId,
            workspace.CanUndo,
            workspace.CanRedo,
            workspace.CurrentXamlText,
            documents = BuildHotDesignDocumentsPayload(workspace.Documents),
            elements = BuildElementPayload(workspace.Elements),
            properties = workspace.Properties.Select(static property => new
            {
                property.Name,
                property.Value,
                property.TypeName,
                property.IsSet,
                property.IsAttached,
                property.IsMarkupExtension,
                quickSets = property.QuickSets.Select(static quickSet => new
                {
                    quickSet.Label,
                    quickSet.Value
                }),
                property.Category,
                property.Source,
                property.OwnerTypeName,
                property.EditorKind,
                property.IsPinned,
                property.IsReadOnly,
                property.CanReset,
                property.EnumOptions
            }),
            toolbox = workspace.Toolbox.Select(static category => new
            {
                category.Name,
                items = category.Items.Select(static item => new
                {
                    item.Name,
                    item.DisplayName,
                    item.Category,
                    item.XamlSnippet,
                    item.IsProjectControl,
                    item.Tags
                })
            })
        };
    }

    public static object BuildHotDesignSelectedElementPayload(
        string? activeBuildUri,
        string? selectedElementId,
        SourceGenHotDesignElementNode? element)
    {
        return new
        {
            activeBuildUri,
            selectedElementId,
            element = element is null ? null : BuildElementNodePayload(element)
        };
    }

    public static IReadOnlyList<object> BuildElementPayload(IReadOnlyList<SourceGenHotDesignElementNode> elements)
    {
        if (elements.Count == 0)
        {
            return Array.Empty<object>();
        }

        var output = new List<object>(elements.Count);
        for (int index = 0; index < elements.Count; index++)
        {
            output.Add(BuildElementNodePayload(elements[index]));
        }

        return output;
    }

    private static object BuildElementNodePayload(SourceGenHotDesignElementNode element)
    {
        return new
        {
            element.Id,
            element.DisplayName,
            element.TypeName,
            element.XamlName,
            element.Classes,
            element.Depth,
            element.IsSelected,
            element.Line,
            element.IsExpanded,
            element.DescendantCount,
            element.SourceBuildUri,
            element.SourceElementId,
            element.IsLive,
            children = BuildElementPayload(element.Children)
        };
    }
}
