namespace XamlToCSharpGenerator.Build.Tasks;

internal enum AvaloniaLoaderCallKind
{
    ObjectWithoutServiceProvider = 0,
    ObjectWithServiceProvider = 1,
    UriWithoutServiceProvider = 2,
    UriWithServiceProvider = 3
}
