using System;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenServiceProviderUtilities
{
    public static IServiceProvider EnsureNotNull(IServiceProvider? serviceProvider)
    {
        return serviceProvider ?? EmptyServiceProvider.Instance;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
