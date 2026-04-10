using System;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class IdentifierSanitizationService
{
    public string SanitizeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "UnnamedElement";
        }

        var chars = identifier.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_')
            {
                chars[index] = '_';
            }
        }

        if (!char.IsLetter(chars[0]) && chars[0] != '_')
        {
            return "_" + new string(chars);
        }

        return new string(chars);
    }
}
