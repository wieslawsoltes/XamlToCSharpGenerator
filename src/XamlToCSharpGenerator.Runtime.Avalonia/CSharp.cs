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
    public class CSharp
    {
        [Content]
        public string? Code { get; set; }
    }

    public class CSharpExtension : MarkupExtension
    {
        public string? Code { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            throw new NotSupportedException("CSharp requires source-generated compilation.");
        }
    }
}
