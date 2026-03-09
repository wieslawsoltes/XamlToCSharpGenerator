using System;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Metadata;

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
