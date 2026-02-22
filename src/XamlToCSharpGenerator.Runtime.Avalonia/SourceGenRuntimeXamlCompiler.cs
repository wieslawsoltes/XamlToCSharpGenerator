using System;
using Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenRuntimeXamlCompiler
{
    public static object Load(
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration,
        SourceGenRuntimeXamlCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (options.TraceDiagnostics)
        {
            var uri = document.BaseUri?.ToString() ?? "<inline>";
            Console.WriteLine(
                "[XamlSourceGen.Runtime] Falling back to runtime XAML compilation for '" + uri + "'.");
        }

        try
        {
            return AvaloniaRuntimeXamlLoader.Load(document, configuration)
                ?? throw new XamlLoadException("Runtime XAML compiler returned null.");
        }
        catch (TypeLoadException ex)
        {
            throw new XamlLoadException(
                "Avalonia runtime XAML compiler type is unavailable. " +
                "Ensure Avalonia.Markup.Xaml.Loader is referenced.", ex);
        }
        catch (MissingMethodException ex)
        {
            throw new XamlLoadException(
                "Avalonia runtime XAML compiler does not expose expected Load(document, configuration) method.", ex);
        }
    }
}
