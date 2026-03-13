using System;
using Avalonia.Controls;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Creates shared deferred content entries for source-generated resource dictionaries.
/// </summary>
public static class SourceGenDeferredContentRuntime
{
    /// <summary>
    /// Wraps a deferred resource factory so <see cref="ResourceDictionary"/> can materialize it lazily.
    /// </summary>
    public static IDeferredContent CreateShared(Func<IServiceProvider?, object?> factory)
    {
        return CreateShared(creationServiceProvider: null, factory);
    }

    /// <summary>
    /// Wraps a deferred resource factory and preserves the creation-time service provider for later materialization.
    /// </summary>
    public static IDeferredContent CreateShared(IServiceProvider? creationServiceProvider, Func<IServiceProvider?, object?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new DeferredFactoryContent(creationServiceProvider, factory);
    }

    private sealed class DeferredFactoryContent : IDeferredContent
    {
        private readonly IServiceProvider? _creationServiceProvider;
        private readonly Func<IServiceProvider?, object?> _factory;

        public DeferredFactoryContent(IServiceProvider? creationServiceProvider, Func<IServiceProvider?, object?> factory)
        {
            _creationServiceProvider = creationServiceProvider;
            _factory = factory;
        }

        public object? Build(IServiceProvider? serviceProvider)
        {
            var effectiveServiceProvider = serviceProvider ?? _creationServiceProvider;
            var value = _factory(effectiveServiceProvider);
            return UnwrapDeferredValue(value, effectiveServiceProvider);
        }

        private static object? UnwrapDeferredValue(object? value, IServiceProvider? serviceProvider)
        {
            while (value is IDeferredContent deferredContent)
            {
                var unwrapped = deferredContent.Build(serviceProvider);
                if (ReferenceEquals(unwrapped, value))
                {
                    break;
                }

                value = unwrapped;
            }

            return value;
        }
    }
}
