using System.Threading;
using XamlToCSharpGenerator.Runtime;

[assembly: SourceGenHotReloadHandler(typeof(XamlToCSharpGenerator.Tests.Runtime.AssemblyLevelHotReloadHandler))]

namespace XamlToCSharpGenerator.Tests.Runtime;

public sealed class AssemblyLevelHotReloadHandler : ISourceGenHotReloadHandler
{
    private static int _reloadCompletedCount;

    public static int ReloadCompletedCount => Volatile.Read(ref _reloadCompletedCount);

    public static void Reset()
    {
        Interlocked.Exchange(ref _reloadCompletedCount, 0);
    }

    public void ReloadCompleted(SourceGenHotReloadUpdateContext context)
    {
        Interlocked.Increment(ref _reloadCompletedCount);
    }
}
