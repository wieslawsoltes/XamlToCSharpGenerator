namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class Program
{
    public static void Main(string[] args)
    {
        SourceGeneratedRuntimeXamlLoaderInstaller.Install();
        global::Avalonia.DesignerSupport.Remote.RemoteDesignerEntryPoint.Main(args);
    }
}
