namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewHostRuntimeState
{
    private static readonly object Sync = new();
    private static PreviewHostOptions _options = new(PreviewCompilerMode.Avalonia, null, null, null, null, null, null, null, null);

    public static void Configure(PreviewHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (Sync)
        {
            _options = options;
        }
    }

    public static PreviewHostOptions Current
    {
        get
        {
            lock (Sync)
            {
                return _options;
            }
        }
    }
}
