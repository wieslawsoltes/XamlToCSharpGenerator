using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenNameReferenceHelper
{
    public static object? ResolveByName(object? anchor, string name)
    {
        if (anchor is null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (anchor is INameScope directNameScope)
        {
            return directNameScope.Find(name);
        }

        if (anchor is StyledElement styledElement &&
            NameScope.GetNameScope(styledElement) is { } elementNameScope)
        {
            return elementNameScope.Find(name);
        }

        if (anchor is ILogical logical &&
            logical.FindNameScope() is { } ancestorNameScope)
        {
            return ancestorNameScope.Find(name);
        }

        return null;
    }
}
