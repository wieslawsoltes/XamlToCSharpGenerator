namespace XamlToCSharpGenerator.Runtime;

public enum SourceGenStudioOperationState
{
    Ready = 0,
    Applying = 1,
    Succeeded = 2,
    Failed = 3,
    TimedOut = 4,
    Canceled = 5
}
