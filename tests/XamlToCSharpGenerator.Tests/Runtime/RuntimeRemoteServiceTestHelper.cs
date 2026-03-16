using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

internal static class RuntimeRemoteServiceTestHelper
{
    public static void ResetRuntimeState()
    {
        XamlSourceGenHotReloadManager.ResetTestHooks();
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.Disable();
        XamlSourceGenHotDesignManager.Disable();
        XamlSourceGenHotDesignManager.ClearRegistrations();
        XamlSourceGenHotDesignManager.ResetAppliersToDefaults();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.ResetHandlersToDefaults();
        XamlSourceGenStudioManager.Disable();
        XamlSourceGenStudioManager.StopSession();
        XamlIncludeGraphRegistry.Clear();
        XamlSourceGenArtifactRefreshRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Clear();
        XamlSourceGenHotDesignCoreTools.ResetWorkspace();
    }
}
