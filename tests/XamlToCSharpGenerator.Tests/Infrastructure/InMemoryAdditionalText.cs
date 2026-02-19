using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace XamlToCSharpGenerator.Tests.Infrastructure;

internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text, System.Text.Encoding.UTF8);
    }

    public override string Path { get; }

    public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
    {
        return _text;
    }
}
