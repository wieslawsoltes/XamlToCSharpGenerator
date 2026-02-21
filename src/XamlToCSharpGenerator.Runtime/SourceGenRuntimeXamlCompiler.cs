using System;
using System.Reflection;
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

        var runtimeCompilerType = Type.GetType(
            "Avalonia.Markup.Xaml.XamlIl.AvaloniaXamlIlRuntimeCompiler, Avalonia.Markup.Xaml",
            throwOnError: false)
            ?? throw new XamlLoadException(
                "Avalonia runtime XAML compiler type is unavailable. " +
                "Ensure Avalonia.Markup.Xaml runtime compiler components are referenced.");

        var loadMethod = runtimeCompilerType.GetMethod(
            "Load",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(RuntimeXamlLoaderDocument), typeof(RuntimeXamlLoaderConfiguration)],
            modifiers: null)
            ?? throw new XamlLoadException(
                "Avalonia runtime XAML compiler does not expose expected Load(document, configuration) method.");

        try
        {
            return loadMethod.Invoke(null, [document, configuration])
                ?? throw new XamlLoadException("Runtime XAML compiler returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
