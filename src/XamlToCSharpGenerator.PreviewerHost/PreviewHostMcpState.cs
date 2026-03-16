using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.PreviewerHost;

internal sealed class PreviewHostMcpState
{
    private const int MaxEvents = 128;

    private readonly object _gate = new();
    private readonly List<PreviewHostMcpEventEntry> _events = new();
    private long _nextSequence;
    private PreviewHostMcpStatusSnapshot _status = PreviewHostMcpStatusSnapshot.CreateInitial();

    public event Action<string>? ResourceUpdated;

    public event Action? ToolsListChanged;

    public event Action? ResourcesListChanged;

    public bool HasActiveSession
    {
        get
        {
            lock (_gate)
            {
                return _status.IsSessionActive;
            }
        }
    }

    public PreviewHostMcpStatusSnapshot GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public IReadOnlyList<PreviewHostMcpEventEntry> GetEvents()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    public PreviewHostMcpSessionSnapshot? GetCurrentSession()
    {
        lock (_gate)
        {
            return _status.IsSessionActive
                ? new PreviewHostMcpSessionSnapshot(
                    _status.PreviewUrl,
                    _status.SessionId,
                    _status.TransportPort,
                    _status.PreviewPort,
                    _status.PreviewCompilerMode,
                    _status.SourceAssemblyPath,
                    _status.XamlFileProjectPath,
                    _status.UpdatedAtUtc)
                : null;
        }
    }

    public void MarkStartRequested(AxsgPreviewHostStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool toolsChanged;
        bool resourcesChanged;
        lock (_gate)
        {
            toolsChanged = _status.IsSessionActive;
            resourcesChanged = _status.IsSessionActive;
            _status = _status with
            {
                Phase = "starting",
                IsSessionActive = false,
                PreviewUrl = null,
                SessionId = null,
                TransportPort = null,
                PreviewPort = null,
                HostExitCode = null,
                PreviewCompilerMode = request.PreviewCompilerMode,
                SourceAssemblyPath = request.SourceAssemblyPath,
                XamlFileProjectPath = request.XamlFileProjectPath,
                LastUpdateSucceeded = null,
                LastError = null,
                LastException = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked("startRequested", "Preview start requested.", new
            {
                request.PreviewCompilerMode,
                request.SourceAssemblyPath,
                request.XamlFileProjectPath,
                request.PreviewWidth,
                request.PreviewHeight,
                request.PreviewScale
            });
        }

        PublishStateChanged(includeCurrent: false, toolsChanged: toolsChanged, resourcesChanged: resourcesChanged);
    }

    public void MarkStarted(AxsgPreviewHostStartResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        bool toolsChanged;
        bool resourcesChanged;
        lock (_gate)
        {
            toolsChanged = !_status.IsSessionActive;
            resourcesChanged = !_status.IsSessionActive;

            _status = _status with
            {
                Phase = "running",
                IsSessionActive = true,
                PreviewUrl = response.PreviewUrl,
                SessionId = response.SessionId,
                TransportPort = response.TransportPort,
                PreviewPort = response.PreviewPort,
                HostExitCode = null,
                LastError = null,
                LastException = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked("started", "Preview session started.", new
            {
                response.PreviewUrl,
                response.SessionId,
                response.TransportPort,
                response.PreviewPort
            });
        }

        PublishStateChanged(includeCurrent: true, toolsChanged: toolsChanged, resourcesChanged: resourcesChanged);
    }

    public void MarkStartFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        bool toolsChanged;
        bool resourcesChanged;
        lock (_gate)
        {
            toolsChanged = _status.IsSessionActive;
            resourcesChanged = _status.IsSessionActive;

            _status = _status with
            {
                Phase = "failed",
                IsSessionActive = false,
                PreviewUrl = null,
                SessionId = null,
                TransportPort = null,
                PreviewPort = null,
                LastError = error,
                LastException = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked("startFailed", "Preview start failed.", new
            {
                error
            });
        }

        PublishStateChanged(includeCurrent: false, toolsChanged: toolsChanged, resourcesChanged: resourcesChanged);
    }

    public void MarkUpdateRequested()
    {
        lock (_gate)
        {
            if (!_status.IsSessionActive)
            {
                return;
            }

            _status = _status with
            {
                Phase = "updating",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked("updateRequested", "Preview update requested.", null);
        }

        PublishStateChanged(includeCurrent: true, toolsChanged: false, resourcesChanged: false);
    }

    public void MarkUpdateDispatchFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        bool includeCurrent;
        lock (_gate)
        {
            includeCurrent = _status.IsSessionActive;
            _status = _status with
            {
                Phase = includeCurrent ? "running" : "failed",
                LastUpdateSucceeded = false,
                LastError = error,
                LastException = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked("updateDispatchFailed", "Preview update dispatch failed.", new
            {
                error
            });
        }

        PublishStateChanged(includeCurrent: includeCurrent, toolsChanged: false, resourcesChanged: false);
    }

    public void MarkUpdateCompleted(AxsgPreviewHostUpdateResultEventPayload result)
    {
        ArgumentNullException.ThrowIfNull(result);

        bool includeCurrent;
        lock (_gate)
        {
            includeCurrent = _status.IsSessionActive;
            _status = _status with
            {
                Phase = _status.IsSessionActive ? "running" : _status.Phase,
                LastUpdateSucceeded = result.Succeeded,
                LastError = result.Error,
                LastException = result.Exception,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked(
                result.Succeeded ? "updateSucceeded" : "updateFailed",
                result.Succeeded ? "Preview update applied." : "Preview update failed.",
                new
                {
                    result.Succeeded,
                    result.Error,
                    result.Exception
                });
        }

        PublishStateChanged(includeCurrent: includeCurrent, toolsChanged: false, resourcesChanged: false);
    }

    public void MarkStopped()
    {
        bool toolsChanged;
        bool resourcesChanged;
        lock (_gate)
        {
            toolsChanged = _status.IsSessionActive;
            resourcesChanged = _status.IsSessionActive;

            _status = _status with
            {
                Phase = "idle",
                IsSessionActive = false,
                PreviewUrl = null,
                SessionId = null,
                TransportPort = null,
                PreviewPort = null,
                HostExitCode = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked("stopped", "Preview session stopped.", null);
        }

        PublishStateChanged(includeCurrent: false, toolsChanged: toolsChanged, resourcesChanged: resourcesChanged);
    }

    public void MarkHostExited(int? exitCode)
    {
        bool toolsChanged;
        bool resourcesChanged;
        lock (_gate)
        {
            toolsChanged = _status.IsSessionActive;
            resourcesChanged = _status.IsSessionActive;

            _status = _status with
            {
                Phase = "exited",
                IsSessionActive = false,
                PreviewUrl = null,
                SessionId = null,
                TransportPort = null,
                PreviewPort = null,
                HostExitCode = exitCode,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            AppendEventLocked("hostExited", "Preview host exited.", new
            {
                exitCode
            });
        }

        PublishStateChanged(includeCurrent: false, toolsChanged: toolsChanged, resourcesChanged: resourcesChanged);
    }

    public void AppendLog(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            AppendEventLocked("log", message, new
            {
                message
            });
        }

        ResourceUpdated?.Invoke(PreviewHostMcpServer.EventsResourceUri);
    }

    private void PublishStateChanged(bool includeCurrent, bool toolsChanged, bool resourcesChanged)
    {
        ResourceUpdated?.Invoke(PreviewHostMcpServer.StatusResourceUri);
        ResourceUpdated?.Invoke(PreviewHostMcpServer.EventsResourceUri);
        if (includeCurrent)
        {
            ResourceUpdated?.Invoke(PreviewHostMcpServer.CurrentSessionResourceUri);
        }

        if (toolsChanged)
        {
            ToolsListChanged?.Invoke();
        }

        if (resourcesChanged)
        {
            ResourcesListChanged?.Invoke();
        }
    }

    private void AppendEventLocked(string kind, string message, object? data)
    {
        _events.Add(new PreviewHostMcpEventEntry(
            Interlocked.Increment(ref _nextSequence),
            DateTimeOffset.UtcNow,
            kind,
            message,
            data));
        if (_events.Count > MaxEvents)
        {
            _events.RemoveAt(0);
        }
    }
}

internal sealed record PreviewHostMcpStatusSnapshot(
    string Phase,
    bool IsSessionActive,
    string? PreviewUrl,
    Guid? SessionId,
    int? TransportPort,
    int? PreviewPort,
    string? PreviewCompilerMode,
    string? SourceAssemblyPath,
    string? XamlFileProjectPath,
    int? HostExitCode,
    bool? LastUpdateSucceeded,
    string? LastError,
    AxsgPreviewHostExceptionDetails? LastException,
    DateTimeOffset UpdatedAtUtc)
{
    public static PreviewHostMcpStatusSnapshot CreateInitial()
    {
        return new PreviewHostMcpStatusSnapshot(
            Phase: "idle",
            IsSessionActive: false,
            PreviewUrl: null,
            SessionId: null,
            TransportPort: null,
            PreviewPort: null,
            PreviewCompilerMode: null,
            SourceAssemblyPath: null,
            XamlFileProjectPath: null,
            HostExitCode: null,
            LastUpdateSucceeded: null,
            LastError: null,
            LastException: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }
}

internal sealed record PreviewHostMcpSessionSnapshot(
    string? PreviewUrl,
    Guid? SessionId,
    int? TransportPort,
    int? PreviewPort,
    string? PreviewCompilerMode,
    string? SourceAssemblyPath,
    string? XamlFileProjectPath,
    DateTimeOffset UpdatedAtUtc);

internal sealed record PreviewHostMcpEventEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Kind,
    string Message,
    object? Data);
