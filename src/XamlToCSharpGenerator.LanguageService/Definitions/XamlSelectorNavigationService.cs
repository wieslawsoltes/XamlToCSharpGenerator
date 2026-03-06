using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal enum XamlSelectorNavigationTargetKind
{
    Unknown = 0,
    Type = 1,
    StyleClass = 2,
    PseudoClass = 3,
}

internal readonly struct XamlSelectorNavigationTarget
{
    public XamlSelectorNavigationTarget(
        XamlSelectorNavigationTargetKind kind,
        string name,
        string? typeContextToken)
    {
        Kind = kind;
        Name = name;
        TypeContextToken = typeContextToken;
    }

    public XamlSelectorNavigationTargetKind Kind { get; }

    public string Name { get; }

    public string? TypeContextToken { get; }
}

internal readonly struct XamlSelectorNavigationReference
{
    public XamlSelectorNavigationReference(XamlSelectorNavigationTarget target, SourceRange range)
    {
        Target = target;
        Range = range;
    }

    public XamlSelectorNavigationTarget Target { get; }

    public SourceRange Range { get; }
}

internal static class XamlSelectorNavigationService
{
    public static bool TryResolveReferenceAtOffset(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlSelectorNavigationReference reference)
    {
        reference = default;
        if (!TryResolveSelectorAttributeReferenceAtPosition(analysis, position, out var selectorTarget, out var range))
        {
            return false;
        }

        reference = new XamlSelectorNavigationReference(selectorTarget, range);
        return true;
    }

    public static bool TryResolveTargetAtOffset(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlSelectorNavigationTarget target)
    {
        target = default;
        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (offset < 0)
        {
            return false;
        }

        if (TryResolveSelectorAttributeReferenceAtPosition(analysis, position, out target, out _))
        {
            return true;
        }

        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        if (context.Kind == XamlCompletionContextKind.AttributeValue &&
            IsClassesAttributeValueContext(context.CurrentAttributeName) &&
            !string.IsNullOrWhiteSpace(context.Token))
        {
            target = new XamlSelectorNavigationTarget(
                XamlSelectorNavigationTargetKind.StyleClass,
                context.Token,
                typeContextToken: null);
            return true;
        }

        if (context.Kind == XamlCompletionContextKind.AttributeName &&
            TryResolveClassesPropertyTarget(context.Token, offset - context.TokenStartOffset, out var className))
        {
            target = new XamlSelectorNavigationTarget(
                XamlSelectorNavigationTargetKind.StyleClass,
                className,
                typeContextToken: null);
            return true;
        }

        return false;
    }

    private static bool TryResolveSelectorAttributeReferenceAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlSelectorNavigationTarget target,
        out SourceRange range)
    {
        target = default;
        range = default;

        if (!TryFindSelectorAttributeAtPosition(analysis, position, out var selectorElement, out var selectorAttribute, out var selectorValueRange))
        {
            return false;
        }

        var absoluteOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var selectorValueStartOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, selectorValueRange.Start);
        if (absoluteOffset < selectorValueStartOffset)
        {
            return false;
        }

        var relativeOffset = absoluteOffset - selectorValueStartOffset;
        if (!SelectorReferenceSemantics.TryFindReferenceAtOffset(selectorAttribute.Value, relativeOffset, out var selectorReference))
        {
            return false;
        }

        var kind = selectorReference.Kind switch
        {
            SelectorReferenceKind.Type => XamlSelectorNavigationTargetKind.Type,
            SelectorReferenceKind.StyleClass => XamlSelectorNavigationTargetKind.StyleClass,
            SelectorReferenceKind.PseudoClass => XamlSelectorNavigationTargetKind.PseudoClass,
            _ => XamlSelectorNavigationTargetKind.Unknown
        };
        if (kind == XamlSelectorNavigationTargetKind.Unknown)
        {
            return false;
        }

        var typeContextToken = kind == XamlSelectorNavigationTargetKind.PseudoClass
            ? ResolveEffectiveTypeContextToken(analysis, selectorElement, position, selectorReference.TypeContextToken)
            : selectorReference.TypeContextToken;
        target = new XamlSelectorNavigationTarget(kind, selectorReference.Name, typeContextToken);
        range = new SourceRange(
            TextCoordinateHelper.GetPosition(analysis.Document.Text, selectorValueStartOffset + selectorReference.Start),
            TextCoordinateHelper.GetPosition(analysis.Document.Text, selectorValueStartOffset + selectorReference.Start + selectorReference.Length));
        return true;
    }

    private static bool IsClassesAttributeValueContext(string? attributeName)
    {
        return string.Equals(GetLocalName(attributeName), "Classes", StringComparison.Ordinal);
    }

    private static bool TryResolveClassesPropertyTarget(
        string token,
        int relativeOffset,
        out string className)
    {
        className = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var localToken = GetLocalName(token);
        const string prefix = "Classes.";
        if (!localToken.StartsWith(prefix, StringComparison.Ordinal) ||
            localToken.Length <= prefix.Length ||
            relativeOffset <= prefix.Length)
        {
            return false;
        }

        className = localToken.Substring(prefix.Length);
        return className.Length > 0;
    }

    private static string? ResolveEffectiveTypeContextToken(
        XamlAnalysisResult analysis,
        XElement? selectorElement,
        SourcePosition position,
        string? immediateTypeContextToken)
    {
        if (!string.IsNullOrWhiteSpace(immediateTypeContextToken))
        {
            return immediateTypeContextToken;
        }

        selectorElement ??= TryFindSelectorElementAtPosition(analysis, position);
        if (selectorElement is null)
        {
            return null;
        }

        for (var current = selectorElement; current is not null; current = current.Parent)
        {
            if (string.Equals(current.Name.LocalName, "Style", StringComparison.Ordinal))
            {
                var selectorAttribute = current.Attributes()
                    .FirstOrDefault(static attribute =>
                        !attribute.IsNamespaceDeclaration &&
                        string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal));
                if (selectorAttribute is not null &&
                    XamlStyleNavigationSemantics.TryExtractTargetTypeTokenFromSelector(
                        selectorAttribute.Value,
                        out var styleTypeToken))
                {
                    return styleTypeToken;
                }
            }

            if (string.Equals(current.Name.LocalName, "ControlTheme", StringComparison.Ordinal))
            {
                var targetTypeAttribute = current.Attributes()
                    .FirstOrDefault(static attribute =>
                        !attribute.IsNamespaceDeclaration &&
                        string.Equals(attribute.Name.LocalName, "TargetType", StringComparison.Ordinal));
                if (targetTypeAttribute is not null &&
                    XamlStyleNavigationSemantics.TryNormalizeControlThemeTargetType(
                        targetTypeAttribute.Value,
                        out var controlThemeTypeToken))
                {
                    return controlThemeTypeToken;
                }
            }
        }

        return null;
    }

    private static XElement? TryFindSelectorElementAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position)
    {
        return TryFindSelectorAttributeAtPosition(analysis, position, out var selectorElement, out _, out _)
            ? selectorElement
            : null;
    }

    private static bool TryFindSelectorAttributeAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XElement selectorElement,
        out XAttribute selectorAttribute,
        out SourceRange selectorValueRange)
    {
        selectorElement = null!;
        selectorAttribute = null!;
        selectorValueRange = default;
        var root = analysis.XmlDocument?.Root;
        if (root is null)
        {
            return false;
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    !string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal) ||
                    !TryCreateAttributeValueRange(analysis.Document.Text, attribute, out var range) ||
                    !IsWithinRange(position, range))
                {
                    continue;
                }

                selectorElement = element;
                selectorAttribute = attribute;
                selectorValueRange = range;
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinRange(SourcePosition position, SourceRange range)
    {
        if (position.Line < range.Start.Line || position.Line > range.End.Line)
        {
            return false;
        }

        if (position.Line == range.Start.Line && position.Character < range.Start.Character)
        {
            return false;
        }

        if (position.Line == range.End.Line && position.Character > range.End.Character)
        {
            return false;
        }

        return true;
    }

    private static bool TryCreateAttributeValueRange(string text, XAttribute attribute, out SourceRange range)
    {
        range = default;
        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var startPosition = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var offset = TextCoordinateHelper.GetOffset(text, startPosition);
        if (offset < 0 || offset >= text.Length)
        {
            return false;
        }

        var equalsIndex = text.IndexOf('=', offset);
        if (equalsIndex < 0)
        {
            return false;
        }

        var quoteIndex = equalsIndex + 1;
        while (quoteIndex < text.Length && char.IsWhiteSpace(text[quoteIndex]))
        {
            quoteIndex++;
        }

        if (quoteIndex >= text.Length)
        {
            return false;
        }

        var quote = text[quoteIndex];
        if (quote != '"' && quote != '\'')
        {
            return false;
        }

        var valueStart = quoteIndex + 1;
        var valueEnd = text.IndexOf(quote, valueStart);
        if (valueEnd < 0)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, valueStart),
            TextCoordinateHelper.GetPosition(text, valueEnd));
        return true;
    }

    private static string GetLocalName(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return string.Empty;
        }

        var separator = qualifiedName.IndexOf(':');
        return separator >= 0 && separator + 1 < qualifiedName.Length
            ? qualifiedName.Substring(separator + 1)
            : qualifiedName;
    }
}
