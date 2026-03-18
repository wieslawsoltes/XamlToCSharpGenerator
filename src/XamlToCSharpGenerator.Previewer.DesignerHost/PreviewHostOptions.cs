namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal enum PreviewCompilerMode
{
    Avalonia,
    SourceGenerated
}

internal sealed record PreviewHostOptions(
    PreviewCompilerMode CompilerMode,
    double? PreviewWidth,
    double? PreviewHeight,
    string? SourceAssemblyPath,
    string? SourceFilePath,
    string? XamlFileProjectPath,
    string? DesignHost,
    int? DesignPort);
