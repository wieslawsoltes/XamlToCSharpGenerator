using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class SourceGeneratedPreviewMarkupRuntimeInstaller
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        SourceGenPreviewMarkupRuntime.Install(new SourceGeneratedPreviewMarkupRuntime());
        _installed = true;
    }
}
