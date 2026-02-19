namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenHotReloadTrigger
{
    MetadataUpdate = 0,
    IdePollingFallback = 1,
    Queued = 2
}
