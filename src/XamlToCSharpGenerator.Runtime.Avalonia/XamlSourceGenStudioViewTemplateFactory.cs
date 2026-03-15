using System;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Templates;

namespace XamlToCSharpGenerator.Runtime;

internal static class XamlSourceGenStudioViewTemplateFactory
{
    public static FuncDataTemplate<T> CreateTextBlockTemplate<T>(Func<T, string?> textSelector)
        where T : class
    {
        return new FuncDataTemplate<T>(
            (item, _) => new TextBlock
            {
                Text = item is null ? string.Empty : textSelector(item) ?? string.Empty
            },
            supportsRecycling: true);
    }
}
