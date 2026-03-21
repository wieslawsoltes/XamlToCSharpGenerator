using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Completion;

internal enum XamlMemberCompletionMode
{
    BindingPath = 0,
    Expression
}

internal static class XamlClrMemberCompletionFactory
{
    public static ImmutableArray<XamlCompletionItem> CreateMemberCompletions(
        INamedTypeSymbol receiverType,
        string prefix,
        XamlMemberCompletionMode mode,
        bool staticOnly = false,
        bool includeFieldsInBindingPath = false,
        bool allowMethodsWithParameters = false)
    {
        if (receiverType is null)
        {
            throw new ArgumentNullException(nameof(receiverType));
        }

        var normalizedPrefix = prefix?.Trim() ?? string.Empty;
        var builder = ImmutableArray.CreateBuilder<XamlCompletionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in EnumerateProperties(receiverType, staticOnly))
        {
            if (!MatchesPrefix(property.Name, normalizedPrefix) || !seen.Add("P:" + property.Name))
            {
                continue;
            }

            builder.Add(new XamlCompletionItem(
                property.Name,
                property.Name,
                XamlCompletionItemKind.Property,
                property.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                GetDocumentation(property),
                IsDeprecated(property)));
        }

        if (mode == XamlMemberCompletionMode.Expression || includeFieldsInBindingPath)
        {
            foreach (var field in EnumerateFields(receiverType, staticOnly))
            {
                if (!MatchesPrefix(field.Name, normalizedPrefix) || !seen.Add("F:" + field.Name))
                {
                    continue;
                }

                builder.Add(new XamlCompletionItem(
                    field.Name,
                    field.Name,
                    XamlCompletionItemKind.Property,
                    field.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    GetDocumentation(field),
                    IsDeprecated(field)));
            }
        }

        foreach (var method in EnumerateMethods(receiverType, mode, staticOnly, allowMethodsWithParameters))
        {
            if (!MatchesPrefix(method.Name, normalizedPrefix) || !seen.Add("M:" + method.Name))
            {
                continue;
            }

            builder.Add(new XamlCompletionItem(
                method.Name,
                method.Name + "()",
                XamlCompletionItemKind.Method,
                BuildMethodSignature(method),
                GetDocumentation(method),
                IsDeprecated(method)));
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<IPropertySymbol> EnumerateProperties(INamedTypeSymbol typeSymbol, bool staticOnly)
    {
        foreach (var current in EnumerateTypeHierarchy(typeSymbol))
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic != staticOnly ||
                    property.IsIndexer ||
                    property.GetMethod is null ||
                    property.GetMethod.MethodKind != MethodKind.PropertyGet)
                {
                    continue;
                }

                yield return property;
            }
        }
    }

    private static IEnumerable<IFieldSymbol> EnumerateFields(INamedTypeSymbol typeSymbol, bool staticOnly)
    {
        foreach (var current in EnumerateTypeHierarchy(typeSymbol))
        {
            foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic != staticOnly || field.IsImplicitlyDeclared)
                {
                    continue;
                }

                yield return field;
            }
        }
    }

    private static IEnumerable<IMethodSymbol> EnumerateMethods(
        INamedTypeSymbol typeSymbol,
        XamlMemberCompletionMode mode,
        bool staticOnly,
        bool allowMethodsWithParameters)
    {
        foreach (var current in EnumerateTypeHierarchy(typeSymbol))
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.IsStatic != staticOnly ||
                    method.IsImplicitlyDeclared ||
                    method.MethodKind != MethodKind.Ordinary ||
                    method.Name is "GetHashCode" or "ToString" or "Equals" or "GetType")
                {
                    continue;
                }

                if (mode == XamlMemberCompletionMode.BindingPath &&
                    ((!allowMethodsWithParameters && method.Parameters.Length > 0) || method.ReturnsVoid))
                {
                    continue;
                }

                yield return method;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchy(INamedTypeSymbol typeSymbol)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var key = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(key))
            {
                yield return current;
            }
        }

        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            var key = interfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(key))
            {
                yield return interfaceType;
            }
        }
    }

    private static bool MatchesPrefix(string candidate, string prefix)
    {
        return prefix.Length == 0 || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMethodSignature(IMethodSymbol method)
    {
        var parameters = string.Join(
            ", ",
            method.Parameters.Select(static parameter =>
                parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + " " + parameter.Name));

        return method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) +
               " " +
               method.Name +
               "(" +
               parameters +
               ")";
    }

    private static string? GetDocumentation(ISymbol symbol)
    {
        var documentation = symbol.GetDocumentationCommentXml();
        return string.IsNullOrWhiteSpace(documentation) ? null : documentation;
    }

    private static bool IsDeprecated(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(static attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), "System.ObsoleteAttribute", StringComparison.Ordinal));
    }
}
