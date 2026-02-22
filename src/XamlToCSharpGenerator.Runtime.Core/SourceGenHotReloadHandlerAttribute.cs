using System;

namespace XamlToCSharpGenerator.Runtime;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SourceGenHotReloadHandlerAttribute : Attribute
{
    public SourceGenHotReloadHandlerAttribute(Type handlerType)
    {
        HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
    }

    public SourceGenHotReloadHandlerAttribute(Type elementType, Type handlerType)
    {
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
    }

    public Type? ElementType { get; }

    public Type HandlerType { get; }
}
