using System;
using System.Collections.Concurrent;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenRegistry
{
    private static readonly IXamlSourceGenArtifactFactoryRegistry Registry = new InMemoryArtifactFactoryRegistry();

    public static event Action<string>? DuplicateUriRegistration
    {
        add => Registry.DuplicateUriRegistration += value;
        remove => Registry.DuplicateUriRegistration -= value;
    }

    public static event Action<string>? MissingUriRequested
    {
        add => Registry.MissingUriRequested += value;
        remove => Registry.MissingUriRequested -= value;
    }

    public static void Register(string uri, Func<IServiceProvider?, object> factory)
    {
        Registry.Register(uri, factory);
    }

    public static void Unregister(string uri)
    {
        Registry.Unregister(uri);
    }

    public static bool TryCreate(IServiceProvider? serviceProvider, string uri, out object? value)
    {
        return Registry.TryCreate(serviceProvider, uri, out value);
    }

    internal static bool TryCreateWithoutMissingNotification(
        IServiceProvider? serviceProvider,
        string uri,
        out object? value)
    {
        return Registry is InMemoryArtifactFactoryRegistry inMemoryRegistry
            ? inMemoryRegistry.TryCreate(serviceProvider, uri, out value, raiseMissingNotification: false)
            : Registry.TryCreate(serviceProvider, uri, out value);
    }

    public static void Clear()
    {
        Registry.Clear();
    }

    private sealed class InMemoryArtifactFactoryRegistry : IXamlSourceGenArtifactFactoryRegistry
    {
        private readonly IXamlSourceGenUriMapper _uriMapper = XamlSourceGenUriMapper.Default;
        private readonly ConcurrentDictionary<string, Func<IServiceProvider?, object>> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public event Action<string>? DuplicateUriRegistration;

        public event Action<string>? MissingUriRequested;

        public void Register(string uri, Func<IServiceProvider?, object> factory)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                throw new ArgumentException("URI must be provided.", nameof(uri));
            }

            var normalizedUri = _uriMapper.Normalize(uri);
            var providedFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (_entries.TryGetValue(normalizedUri, out var existing))
            {
                if (!ReferenceEquals(existing, providedFactory))
                {
                    _entries[normalizedUri] = providedFactory;
                }

                DuplicateUriRegistration?.Invoke(normalizedUri);
                return;
            }

            _entries[normalizedUri] = providedFactory;
        }

        public void Unregister(string uri)
        {
            var normalizedUri = _uriMapper.Normalize(uri);
            if (string.IsNullOrWhiteSpace(normalizedUri))
            {
                return;
            }

            _entries.TryRemove(normalizedUri, out _);
        }

        public bool TryCreate(IServiceProvider? serviceProvider, string uri, out object? value)
        {
            return TryCreate(serviceProvider, uri, out value, raiseMissingNotification: true);
        }

        internal bool TryCreate(
            IServiceProvider? serviceProvider,
            string uri,
            out object? value,
            bool raiseMissingNotification)
        {
            var normalizedUri = _uriMapper.Normalize(uri);
            if (_entries.TryGetValue(normalizedUri, out var factory))
            {
                value = factory(serviceProvider);
                return true;
            }

            value = null;
            if (raiseMissingNotification)
            {
                MissingUriRequested?.Invoke(normalizedUri);
            }

            return false;
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
