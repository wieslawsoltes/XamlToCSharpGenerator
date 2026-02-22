using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignUpdateContext
{
    private readonly Action<object> _runtimeApplyAction;

    public SourceGenHotDesignUpdateContext(
        SourceGenHotDesignDocumentDescriptor document,
        SourceGenHotDesignUpdateRequest request,
        SourceGenHotDesignOptions options,
        IReadOnlyList<object> trackedInstances,
        Action<object> runtimeApplyAction)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        TrackedInstances = trackedInstances ?? throw new ArgumentNullException(nameof(trackedInstances));
        _runtimeApplyAction = runtimeApplyAction ?? throw new ArgumentNullException(nameof(runtimeApplyAction));
    }

    public SourceGenHotDesignDocumentDescriptor Document { get; }

    public SourceGenHotDesignUpdateRequest Request { get; }

    public SourceGenHotDesignOptions Options { get; }

    public IReadOnlyList<object> TrackedInstances { get; }

    public int ApplyRuntimeToTrackedInstances(Action<Exception>? onError = null)
    {
        var appliedCount = 0;
        for (var index = 0; index < TrackedInstances.Count; index++)
        {
            var instance = TrackedInstances[index];
            try
            {
                _runtimeApplyAction(instance);
                appliedCount++;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        return appliedCount;
    }
}
