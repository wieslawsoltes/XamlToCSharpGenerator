namespace XamlToCSharpGenerator.LanguageService.Completion;

public enum XamlCompletionContextKind
{
    Unknown,
    ElementName,
    QualifiedPropertyElement,
    AttributeName,
    AttributeValue,
    MarkupExtension
}
