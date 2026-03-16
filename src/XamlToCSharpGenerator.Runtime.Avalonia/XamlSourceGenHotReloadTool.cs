namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotReloadTool
{
    public static SourceGenHotReloadStatus GetStatus()
    {
        return XamlSourceGenHotReloadManager.GetStatus();
    }
}
