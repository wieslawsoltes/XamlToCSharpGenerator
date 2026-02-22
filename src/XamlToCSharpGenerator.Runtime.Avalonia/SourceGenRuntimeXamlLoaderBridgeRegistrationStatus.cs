namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenRuntimeXamlLoaderBridgeRegistrationStatus
{
    NotAttempted = 0,
    RegisteredDynamicProxy = 1,
    RuntimeLoaderInterfaceMissing = 2,
    LocatorUnavailable = 3,
    DynamicBridgeDisabledBySwitch = 4,
    DynamicCodeUnsupported = 5,
    DynamicProxyUnavailable = 6
}
