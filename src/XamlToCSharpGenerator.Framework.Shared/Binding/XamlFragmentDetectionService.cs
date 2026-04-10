using System;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XamlFragmentDetectionService
{
    public bool IsValidFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("<", StringComparison.Ordinal) ||
            !trimmed.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = XElement.Parse("<__axsg_fragment_root__>" + trimmed + "</__axsg_fragment_root__>", LoadOptions.PreserveWhitespace);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
