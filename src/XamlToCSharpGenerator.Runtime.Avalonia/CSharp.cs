using System;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Markup.Xaml.MarkupExtensions;
using global::Avalonia.Metadata;

namespace XamlToCSharpGenerator.Runtime
{
    public class CSharp : Markup.CSharp
    {
    }

    public class CSharpExtension : Markup.CSharpExtension
    {
    }
}

namespace XamlToCSharpGenerator.Runtime.Markup
{
    public class CSharp : MarkupExtension
    {
        [Content]
        public string? Code { get; set; }

        public string? CodeBase64Url { get; set; }

        public string? DependencyNamesBase64Url { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (SourceGenPreviewMarkupRuntime.TryProvideValue(
                    Code,
                    CodeBase64Url,
                    DependencyNamesBase64Url,
                    serviceProvider,
                    out var value))
            {
                return value!;
            }

            throw new NotSupportedException("CSharp requires source-generated compilation.");
        }
    }

    public class CSharpExtension : CSharp
    {
    }
}
