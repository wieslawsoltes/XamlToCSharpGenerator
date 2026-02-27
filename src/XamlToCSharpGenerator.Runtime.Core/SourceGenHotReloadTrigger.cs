namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenHotReloadTrigger
{
    MetadataUpdate = 0,
    IdePollingFallback = 1,
    RemoteTransport = 2,
    Queued = 3
}
