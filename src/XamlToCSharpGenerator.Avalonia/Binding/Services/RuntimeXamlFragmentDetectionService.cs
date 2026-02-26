using System;
using System.IO;
using System.Xml;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class RuntimeXamlFragmentDetectionService
{
    public bool IsValidFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var settings = new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = false,
            XmlResolver = null
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(trimmed), settings);
            var hasElement = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    hasElement = true;
                }
            }

            return hasElement;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
