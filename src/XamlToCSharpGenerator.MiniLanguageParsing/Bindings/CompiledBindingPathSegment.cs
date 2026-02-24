using System.Collections.Immutable;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public readonly struct CompiledBindingPathSegment
{
    public CompiledBindingPathSegment(
        string memberName,
        ImmutableArray<string> indexers,
        string? castTypeToken,
        bool isMethodCall,
        bool acceptsNull,
        ImmutableArray<string> methodArguments,
        bool isAttachedProperty,
        string? attachedOwnerTypeToken,
        int streamCount)
    {
        MemberName = memberName;
        Indexers = indexers;
        CastTypeToken = castTypeToken;
        IsMethodCall = isMethodCall;
        AcceptsNull = acceptsNull;
        MethodArguments = methodArguments;
        IsAttachedProperty = isAttachedProperty;
        AttachedOwnerTypeToken = attachedOwnerTypeToken;
        StreamCount = streamCount;
    }

    public string MemberName { get; }

    public ImmutableArray<string> Indexers { get; }

    public string? CastTypeToken { get; }

    public bool IsMethodCall { get; }

    public bool AcceptsNull { get; }

    public ImmutableArray<string> MethodArguments { get; }

    public bool IsAttachedProperty { get; }

    public string? AttachedOwnerTypeToken { get; }

    public int StreamCount { get; }
}
