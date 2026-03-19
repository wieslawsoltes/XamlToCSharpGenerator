using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.LanguageService.Formatting;

internal sealed class XamlDocumentFormattingService
{
    public bool TryFormat(string? text, XamlFormattingOptions options, out string formattedText)
    {
        formattedText = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.None);
        }
        catch
        {
            return false;
        }

        var normalizedOptions = options.Normalize();
        var lineEnding = DetectLineEnding(text);
        var indentation = normalizedOptions.InsertSpaces
            ? new string(' ', normalizedOptions.TabSize)
            : "\t";

        var builder = new StringBuilder(text.Length + 64);
        var xmlWriterSettings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = indentation,
            NewLineChars = lineEnding,
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = document.Declaration is null
        };

        var declarationEncoding = ResolveDeclarationEncoding(document.Declaration);
        using (var stringWriter = new StringBuilderEncodingWriter(builder, declarationEncoding))
        using (var writer = XmlWriter.Create(stringWriter, xmlWriterSettings))
        {
            document.Save(writer);
        }

        formattedText = builder.ToString();
        return !string.Equals(formattedText, text, StringComparison.Ordinal);
    }

    private static string DetectLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                return index + 1 < text.Length && text[index + 1] == '\n'
                    ? "\r\n"
                    : "\r";
            }

            if (text[index] == '\n')
            {
                return "\n";
            }
        }

        return Environment.NewLine;
    }

    private static Encoding ResolveDeclarationEncoding(XDeclaration? declaration)
    {
        if (!string.IsNullOrWhiteSpace(declaration?.Encoding))
        {
            try
            {
                return Encoding.GetEncoding(declaration.Encoding);
            }
            catch
            {
                // Keep formatting resilient if the declaration contains a non-standard encoding name.
            }
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private sealed class StringBuilderEncodingWriter : StringWriter
    {
        private readonly Encoding _encoding;

        public StringBuilderEncodingWriter(StringBuilder builder, Encoding encoding)
            : base(builder)
        {
            _encoding = encoding;
        }

        public override Encoding Encoding => _encoding;
    }
}
