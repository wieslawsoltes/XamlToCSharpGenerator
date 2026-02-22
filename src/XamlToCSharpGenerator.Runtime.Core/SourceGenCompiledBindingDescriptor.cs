using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenCompiledBindingDescriptor
{
    public SourceGenCompiledBindingDescriptor(
        string uri,
        string targetTypeName,
        string targetPropertyName,
        string path,
        string sourceTypeName,
        Func<object, object?> accessor)
    {
        Uri = uri;
        TargetTypeName = targetTypeName;
        TargetPropertyName = targetPropertyName;
        Path = path;
        SourceTypeName = sourceTypeName;
        Accessor = accessor;
    }

    public string Uri { get; }

    public string TargetTypeName { get; }

    public string TargetPropertyName { get; }

    public string Path { get; }

    public string SourceTypeName { get; }

    public Func<object, object?> Accessor { get; }
}
