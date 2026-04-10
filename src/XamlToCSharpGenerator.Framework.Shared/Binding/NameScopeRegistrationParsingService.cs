using System;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class NameScopeRegistrationParsingService
{
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly Func<string, string> _normalizePropertyName;

    public NameScopeRegistrationParsingService(
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        Func<string, string> normalizePropertyName)
    {
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
        _normalizePropertyName = normalizePropertyName ?? throw new ArgumentNullException(nameof(normalizePropertyName));
    }

    public bool TryGetNodeNameScopeRegistration(
        XamlObjectNode node,
        out string registeredName)
    {
        registeredName = string.Empty;

        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            registeredName = node.Name!.Trim();
            return registeredName.Length > 0;
        }

        foreach (var assignment in node.PropertyAssignments)
        {
            var normalizedName = _normalizePropertyName(assignment.PropertyName);
            if (!string.Equals(normalizedName, "Name", StringComparison.Ordinal))
            {
                continue;
            }

            var rawValue = assignment.Value?.Trim();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            if (_tryParseMarkupExtension(rawValue!, out var markup) &&
                markup.PositionalArguments.Length > 0)
            {
                registeredName = string.Empty;
                return false;
            }
            else
            {
                registeredName = rawValue!;
            }

            return registeredName.Length > 0;
        }

        return false;
    }
}
