namespace XamlToCSharpGenerator.Core.Models;

public enum ResolvedValueKind
{
    Unknown = 0,
    Literal = 1,
    Binding = 2,
    TemplateBinding = 3,
    DynamicResourceBinding = 4,
    MarkupExtension = 5,
    RuntimeXamlFallback = 6
}
