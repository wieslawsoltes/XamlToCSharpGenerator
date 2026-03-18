using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlClassModifierRefactoringProvider : IXamlRefactoringProvider
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;

    public XamlClassModifierRefactoringProvider(
        XamlDocumentStore documentStore,
        XamlCompilerAnalysisService analysisService)
    {
        _documentStore = documentStore;
        _analysisService = analysisService;
    }

    public async Task<ImmutableArray<XamlRefactoringAction>> GetCodeActionsAsync(
        XamlRefactoringContext context,
        CancellationToken cancellationToken)
    {
        LanguageServiceDocument? document = await XamlRefactoringDocumentResolver
            .ResolveDocumentAsync(_documentStore, context, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        XamlAnalysisResult analysis = await _analysisService
            .AnalyzeAsync(
                document,
                context.Options with
                {
                    IncludeCompilationDiagnostics = false,
                    IncludeSemanticDiagnostics = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (analysis.XmlDocument?.Root is not XElement root)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        bool hasInvalidModifierDiagnostic = analysis.Diagnostics.Any(static diagnostic =>
            string.Equals(diagnostic.Code, "AXSG0104", StringComparison.Ordinal));
        bool hasMismatchDiagnostic = analysis.Diagnostics.Any(static diagnostic =>
            string.Equals(diagnostic.Code, "AXSG0105", StringComparison.Ordinal));
        if (!hasInvalidModifierDiagnostic && !hasMismatchDiagnostic)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        XAttribute? classModifierAttribute = root.Attributes().FirstOrDefault(IsXClassModifierAttribute);
        if (classModifierAttribute is null ||
            !ShouldOfferForPosition(document.Text, analysis.XmlDocument, root, classModifierAttribute, context.Position))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(document.Text, classModifierAttribute, out SourceRange valueRange))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        string replacementModifier = ResolveDesiredModifier(analysis.Compilation, root) ?? "internal";
        if (string.Equals(classModifierAttribute.Value.Trim(), replacementModifier, StringComparison.Ordinal))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        XamlWorkspaceEdit workspaceEdit = new(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(
                document.Uri,
                ImmutableArray.Create(new XamlDocumentTextEdit(valueRange, replacementModifier))));

        return ImmutableArray.Create(
            new XamlRefactoringAction(
                Title: "AXSG: Set x:ClassModifier to " + replacementModifier,
                Kind: "quickfix",
                IsPreferred: true,
                Edit: workspaceEdit,
                Command: null));
    }

    private static bool ShouldOfferForPosition(
        string sourceText,
        XDocument xmlDocument,
        XElement root,
        XAttribute classModifierAttribute,
        SourcePosition position)
    {
        if (XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                sourceText,
                xmlDocument,
                position,
                out _,
                out XAttribute attribute,
                out _,
                out _))
        {
            return ReferenceEquals(attribute, classModifierAttribute) || IsXClassAttribute(attribute);
        }

        if (root is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return position.Line == 0 && position.Character == 0;
        }

        int rootLine = Math.Max(0, lineInfo.LineNumber - 1);
        int rootStartCharacter = Math.Max(0, lineInfo.LinePosition - 2);
        return position.Line == rootLine && position.Character <= rootStartCharacter;
    }

    private static string? ResolveDesiredModifier(Compilation? compilation, XElement root)
    {
        XAttribute? classAttribute = root.Attributes().FirstOrDefault(IsXClassAttribute);
        if (classAttribute is null)
        {
            return null;
        }

        INamedTypeSymbol? classSymbol = ResolveTypeSymbol(compilation, classAttribute.Value.Trim()) as INamedTypeSymbol;
        return classSymbol is null
            ? null
            : ToCSharpClassModifier(classSymbol.DeclaredAccessibility);
    }

    private static bool IsXClassModifierAttribute(XAttribute attribute)
    {
        return string.Equals(attribute.Name.LocalName, "ClassModifier", StringComparison.Ordinal) &&
               string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal);
    }

    private static bool IsXClassAttribute(XAttribute attribute)
    {
        return string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal) &&
               string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal);
    }

    private static ISymbol? ResolveTypeSymbol(Compilation? compilation, string fullTypeName)
    {
        if (compilation is null || string.IsNullOrWhiteSpace(fullTypeName))
        {
            return null;
        }

        return compilation.GetTypeByMetadataName(fullTypeName)
               ?? compilation.GetTypeByMetadataName(fullTypeName.Replace('.', '+'));
    }

    private static string ToCSharpClassModifier(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };
    }
}
