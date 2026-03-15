using System.Reflection;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class SourceGeneratedPreviewMarkupRuntimeInstaller
{
    private static bool _attempted;

    public static bool IsInstalled { get; private set; }

    public static void Install()
    {
        if (_attempted)
        {
            return;
        }

        _attempted = true;

        try
        {
            var runtimeHostType = Type.GetType(
                "XamlToCSharpGenerator.Runtime.SourceGenPreviewMarkupRuntime, XamlToCSharpGenerator.Runtime.Avalonia",
                throwOnError: false);
            if (runtimeHostType is null)
            {
                return;
            }

            var installMethod = runtimeHostType
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(static method =>
                {
                    if (!string.Equals(method.Name, "Install", StringComparison.Ordinal) ||
                        method.GetParameters().Length != 1)
                    {
                        return false;
                    }

                    return typeof(Delegate).IsAssignableFrom(method.GetParameters()[0].ParameterType);
                });
            if (installMethod is null)
            {
                Console.WriteLine("[AXSG preview] Preview markup runtime install skipped: runtime callback API is unavailable.");
                return;
            }

            var runtime = new SourceGeneratedPreviewMarkupRuntime();
            var provideValueMethod = typeof(SourceGeneratedPreviewMarkupRuntime).GetMethod(
                nameof(SourceGeneratedPreviewMarkupRuntime.ProvideValue),
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("Preview markup runtime callback was not found.");
            var callback = Delegate.CreateDelegate(
                installMethod.GetParameters()[0].ParameterType,
                runtime,
                provideValueMethod);

            installMethod.Invoke(null, [callback]);
            IsInstalled = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AXSG preview] Preview markup runtime install skipped: " + ex.Message);
        }
    }
}
