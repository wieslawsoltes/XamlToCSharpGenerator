using System;

namespace XamlToCSharpGenerator.Runtime;

public static class AvaloniaSourceGeneratedXamlLoader
{
    public static bool IsEnabled { get; private set; }

    public static void Enable()
    {
        IsEnabled = true;
    }

    public static bool TryLoad(IServiceProvider? serviceProvider, Uri uri, out object? value)
    {
        if (uri is null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        return XamlSourceGenRegistry.TryCreate(serviceProvider, uri.ToString(), out value);
    }
}
