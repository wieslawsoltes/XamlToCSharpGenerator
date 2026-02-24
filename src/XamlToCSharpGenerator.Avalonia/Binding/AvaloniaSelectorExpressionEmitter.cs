using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class AvaloniaSelectorExpressionEmitter : ISelectorExpressionEmitter
{
    public string EmitOr(ImmutableArray<string> branchExpressions)
    {
        return "global::Avalonia.Styling.Selectors.Or(" + string.Join(", ", branchExpressions) + ")";
    }

    public string EmitDescendant(string previousExpression)
    {
        return "global::Avalonia.Styling.Selectors.Descendant(" + previousExpression + ")";
    }

    public string EmitChild(string previousExpression)
    {
        return "global::Avalonia.Styling.Selectors.Child(" + previousExpression + ")";
    }

    public string EmitTemplate(string previousExpression)
    {
        return "global::Avalonia.Styling.Selectors.Template(" + previousExpression + ")";
    }

    public string EmitNesting(string previousExpressionOrNull)
    {
        return "global::Avalonia.Styling.Selectors.Nesting(" + previousExpressionOrNull + ")";
    }

    public string EmitOfType(string previousExpressionOrNull, INamedTypeSymbol type)
    {
        return "global::Avalonia.Styling.Selectors.OfType(" +
               previousExpressionOrNull +
               ", typeof(" +
               type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               "))";
    }

    public string EmitClass(string previousExpressionOrNull, string className)
    {
        return "global::Avalonia.Styling.Selectors.Class(" +
               previousExpressionOrNull +
               ", \"" +
               Escape(className) +
               "\")";
    }

    public string EmitName(string previousExpressionOrNull, string name)
    {
        return "global::Avalonia.Styling.Selectors.Name(" +
               previousExpressionOrNull +
               ", \"" +
               Escape(name) +
               "\")";
    }

    public string EmitPseudoClass(string previousExpressionOrNull, string pseudoClassName)
    {
        return "global::Avalonia.Styling.Selectors.Class(" +
               previousExpressionOrNull +
               ", \":" +
               Escape(pseudoClassName) +
               "\")";
    }

    public string EmitIs(string previousExpressionOrNull, INamedTypeSymbol type)
    {
        return "global::Avalonia.Styling.Selectors.Is(" +
               previousExpressionOrNull +
               ", typeof(" +
               type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               "))";
    }

    public string EmitNot(string previousExpressionOrNull, string argumentExpression)
    {
        return "global::Avalonia.Styling.Selectors.Not(" +
               previousExpressionOrNull +
               ", " +
               argumentExpression +
               ")";
    }

    public string EmitNthChild(string previousExpressionOrNull, int step, int offset)
    {
        return "global::Avalonia.Styling.Selectors.NthChild(" +
               previousExpressionOrNull +
               ", " +
               step.ToString(CultureInfo.InvariantCulture) +
               ", " +
               offset.ToString(CultureInfo.InvariantCulture) +
               ")";
    }

    public string EmitNthLastChild(string previousExpressionOrNull, int step, int offset)
    {
        return "global::Avalonia.Styling.Selectors.NthLastChild(" +
               previousExpressionOrNull +
               ", " +
               step.ToString(CultureInfo.InvariantCulture) +
               ", " +
               offset.ToString(CultureInfo.InvariantCulture) +
               ")";
    }

    public string EmitPropertyEquals(string previousExpressionOrNull, string propertyExpression, string valueExpression)
    {
        return "global::Avalonia.Styling.Selectors.PropertyEquals(" +
               previousExpressionOrNull +
               ", " +
               propertyExpression +
               ", " +
               valueExpression +
               ")";
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", string.Empty)
            .Replace("\n", "\\n");
    }
}
