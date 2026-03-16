using XamlToCSharpGenerator.PreviewerHost;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class PreviewUpdateResultTrackerTests
{
    [Fact]
    public async Task CompleteNext_Skips_FireAndForget_Result_Before_HotReload()
    {
        var tracker = new PreviewUpdateResultTracker();
        tracker.EnqueueFireAndForget();
        TaskCompletionSource<AxsgPreviewHostHotReloadResponse> pendingHotReload = tracker.EnqueueHotReload();

        tracker.CompleteNext(CreateResponse(true, null));
        Assert.False(pendingHotReload.Task.IsCompleted);

        AxsgPreviewHostHotReloadResponse completion = CreateResponse(false, "hot reload failed");
        tracker.CompleteNext(completion);

        AxsgPreviewHostHotReloadResponse result = await pendingHotReload.Task;
        Assert.False(result.Succeeded);
        Assert.Equal("hot reload failed", result.Error);
    }

    [Fact]
    public void RemoveHotReload_Removes_Queued_HotReload_Without_Disturbing_FireAndForget_Order()
    {
        var tracker = new PreviewUpdateResultTracker();
        tracker.EnqueueFireAndForget();
        TaskCompletionSource<AxsgPreviewHostHotReloadResponse> pendingHotReload = tracker.EnqueueHotReload();
        tracker.EnqueueFireAndForget();

        Assert.True(tracker.RemoveHotReload(pendingHotReload));
        Assert.False(pendingHotReload.Task.IsCompleted);

        tracker.CompleteNext(CreateResponse(true, null));
        tracker.CompleteNext(CreateResponse(true, null));

        Assert.False(pendingHotReload.Task.IsCompleted);
    }

    [Fact]
    public async Task FailAll_Completes_Pending_HotReload_And_Clears_Queue()
    {
        var tracker = new PreviewUpdateResultTracker();
        tracker.EnqueueFireAndForget();
        TaskCompletionSource<AxsgPreviewHostHotReloadResponse> pendingHotReload = tracker.EnqueueHotReload();

        AxsgPreviewHostHotReloadResponse failure = CreateResponse(false, "transport failed");
        AxsgPreviewHostHotReloadResponse? published = tracker.FailAll(failure);

        Assert.Same(failure, published);
        AxsgPreviewHostHotReloadResponse result = await pendingHotReload.Task;
        Assert.False(result.Succeeded);
        Assert.Equal("transport failed", result.Error);

        Assert.Null(tracker.CompleteNext(CreateResponse(true, null)));
    }

    private static AxsgPreviewHostHotReloadResponse CreateResponse(bool succeeded, string? error)
    {
        return new AxsgPreviewHostHotReloadResponse(
            succeeded,
            error,
            Exception: null,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }
}
