using System.Collections.Generic;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.PreviewerHost;

internal sealed class PreviewUpdateResultTracker
{
    private readonly object _sync = new();
    private readonly Queue<PendingUpdateResult> _pendingResults = new();

    public void EnqueueFireAndForget()
    {
        lock (_sync)
        {
            _pendingResults.Enqueue(new PendingUpdateResult(null));
        }
    }

    public TaskCompletionSource<AxsgPreviewHostHotReloadResponse> EnqueueHotReload()
    {
        var completionSource = CreateCompletionSource();

        lock (_sync)
        {
            foreach (PendingUpdateResult pendingResult in _pendingResults)
            {
                if (ReferenceEquals(pendingResult.HotReloadCompletionSource, null))
                {
                    continue;
                }

                throw new InvalidOperationException("A preview hot reload is already in progress.");
            }

            _pendingResults.Enqueue(new PendingUpdateResult(completionSource));
        }

        return completionSource;
    }

    public AxsgPreviewHostHotReloadResponse? CompleteNext(AxsgPreviewHostHotReloadResponse response)
    {
        TaskCompletionSource<AxsgPreviewHostHotReloadResponse>? completionSource = null;

        lock (_sync)
        {
            if (_pendingResults.Count > 0)
            {
                completionSource = _pendingResults.Dequeue().HotReloadCompletionSource;
            }
        }

        completionSource?.TrySetResult(response);
        return completionSource is null ? null : response;
    }

    public bool RemoveHotReload(TaskCompletionSource<AxsgPreviewHostHotReloadResponse> completionSource)
    {
        ArgumentNullException.ThrowIfNull(completionSource);

        lock (_sync)
        {
            if (_pendingResults.Count == 0)
            {
                return false;
            }

            var removed = false;
            var rebuiltQueue = new Queue<PendingUpdateResult>(_pendingResults.Count);
            while (_pendingResults.Count > 0)
            {
                PendingUpdateResult pendingResult = _pendingResults.Dequeue();
                if (!removed && ReferenceEquals(pendingResult.HotReloadCompletionSource, completionSource))
                {
                    removed = true;
                    continue;
                }

                rebuiltQueue.Enqueue(pendingResult);
            }

            while (rebuiltQueue.Count > 0)
            {
                _pendingResults.Enqueue(rebuiltQueue.Dequeue());
            }

            return removed;
        }
    }

    public AxsgPreviewHostHotReloadResponse? FailAll(AxsgPreviewHostHotReloadResponse response)
    {
        TaskCompletionSource<AxsgPreviewHostHotReloadResponse>? completionSource = null;

        lock (_sync)
        {
            while (_pendingResults.Count > 0)
            {
                TaskCompletionSource<AxsgPreviewHostHotReloadResponse>? pendingCompletionSource =
                    _pendingResults.Dequeue().HotReloadCompletionSource;
                if (pendingCompletionSource is not null)
                {
                    completionSource = pendingCompletionSource;
                }
            }
        }

        completionSource?.TrySetResult(response);
        return completionSource is null ? null : response;
    }

    private static TaskCompletionSource<AxsgPreviewHostHotReloadResponse> CreateCompletionSource()
    {
        return new TaskCompletionSource<AxsgPreviewHostHotReloadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record PendingUpdateResult(TaskCompletionSource<AxsgPreviewHostHotReloadResponse>? HotReloadCompletionSource);
}
