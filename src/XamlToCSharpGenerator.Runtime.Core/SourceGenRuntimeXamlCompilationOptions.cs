namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenRuntimeXamlCompilationOptions
{
    public bool EnableRuntimeCompilationFallback { get; set; } = true;

    public bool CacheCompiledDocuments { get; set; } = true;

    public bool UseCompiledBindingsByDefault { get; set; } = true;

    public bool CreateSourceInfo { get; set; }

    public bool StrictMode { get; set; }

    public bool CSharpExpressionsEnabled { get; set; }

    public bool ImplicitCSharpExpressionsEnabled { get; set; }

    public bool AllowImplicitXmlnsDeclaration { get; set; }

    public bool ImplicitStandardXmlnsPrefixesEnabled { get; set; }

    public string? ImplicitDefaultXmlns { get; set; }

    public bool TraceDiagnostics { get; set; }

    public SourceGenRuntimeXamlCompilationOptions Clone()
    {
        return new SourceGenRuntimeXamlCompilationOptions
        {
            EnableRuntimeCompilationFallback = EnableRuntimeCompilationFallback,
            CacheCompiledDocuments = CacheCompiledDocuments,
            UseCompiledBindingsByDefault = UseCompiledBindingsByDefault,
            CreateSourceInfo = CreateSourceInfo,
            StrictMode = StrictMode,
            CSharpExpressionsEnabled = CSharpExpressionsEnabled,
            ImplicitCSharpExpressionsEnabled = ImplicitCSharpExpressionsEnabled,
            AllowImplicitXmlnsDeclaration = AllowImplicitXmlnsDeclaration,
            ImplicitStandardXmlnsPrefixesEnabled = ImplicitStandardXmlnsPrefixesEnabled,
            ImplicitDefaultXmlns = ImplicitDefaultXmlns,
            TraceDiagnostics = TraceDiagnostics
        };
    }
}
