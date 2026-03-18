using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlEventHandlerRefactoringProvider : IXamlRefactoringProvider
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;

    public XamlEventHandlerRefactoringProvider(
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

        if (analysis.XmlDocument?.Root is not XElement root ||
            !XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                document.Text,
                analysis.XmlDocument,
                context.Position,
                out XElement element,
                out XAttribute attribute,
                out SourceRange attributeNameRange,
                out SourceRange attributeValueRange) ||
            attribute.IsNamespaceDeclaration ||
            string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal) ||
            !HasDiagnosticForAttribute(analysis, "AXSG0600", attributeNameRange, attributeValueRange) ||
            !XamlEventHandlerNameSemantics.TryParseHandlerName(attribute.Value, out string? handlerName) ||
            string.IsNullOrWhiteSpace(handlerName) ||
            !TryResolveRootType(root, analysis.Compilation, out INamedTypeSymbol rootTypeSymbol) ||
            !TryResolveEventHandlerType(analysis, element, attribute, out INamedTypeSymbol delegateType) ||
            HasCompatibleInstanceMethod(rootTypeSymbol, handlerName!, delegateType) ||
            !TryBuildWorkspaceEdit(
                document.FilePath,
                rootTypeSymbol,
                handlerName!,
                delegateType,
                cancellationToken,
                out XamlWorkspaceEdit workspaceEdit,
                out bool hasExistingMethods))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        string title = hasExistingMethods
            ? $"AXSG: Add compatible event handler overload '{handlerName}'"
            : $"AXSG: Add event handler '{handlerName}'";
        return ImmutableArray.Create(
            new XamlRefactoringAction(
                Title: title,
                Kind: "quickfix",
                IsPreferred: true,
                Edit: workspaceEdit,
                Command: null));
    }

    private static bool HasDiagnosticForAttribute(
        XamlAnalysisResult analysis,
        string code,
        SourceRange attributeNameRange,
        SourceRange attributeValueRange)
    {
        return analysis.Diagnostics.Any(diagnostic =>
            string.Equals(diagnostic.Code, code, StringComparison.Ordinal) &&
            IntersectsAttributeSpan(diagnostic.Range.Start, attributeNameRange, attributeValueRange));
    }

    private static bool IntersectsAttributeSpan(
        SourcePosition position,
        SourceRange attributeNameRange,
        SourceRange attributeValueRange)
    {
        if (position.Line < attributeNameRange.Start.Line ||
            position.Line > attributeValueRange.End.Line)
        {
            return false;
        }

        if (position.Line == attributeNameRange.Start.Line &&
            position.Character < attributeNameRange.Start.Character)
        {
            return false;
        }

        if (position.Line == attributeValueRange.End.Line &&
            position.Character > attributeValueRange.End.Character)
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveRootType(
        XElement root,
        Compilation? compilation,
        out INamedTypeSymbol typeSymbol)
    {
        typeSymbol = null!;
        if (compilation is null)
        {
            return false;
        }

        XAttribute? classAttribute = root.Attributes().FirstOrDefault(static attribute =>
            string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal) &&
            string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal));
        if (classAttribute is null)
        {
            return false;
        }

        ISymbol? resolved = compilation.GetTypeByMetadataName(classAttribute.Value.Trim()) ??
                            compilation.GetTypeByMetadataName(classAttribute.Value.Trim().Replace('.', '+'));
        if (resolved is not INamedTypeSymbol namedType)
        {
            return false;
        }

        typeSymbol = namedType;
        return true;
    }

    private static bool TryResolveEventHandlerType(
        XamlAnalysisResult analysis,
        XElement element,
        XAttribute attribute,
        out INamedTypeSymbol delegateType)
    {
        delegateType = null!;
        if (!XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, element, out INamedTypeSymbol elementType))
        {
            return false;
        }

        string eventName = attribute.Name.LocalName;
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return false;
        }

        IEventSymbol? eventSymbol = FindEvent(elementType, eventName);
        if (eventSymbol?.Type is not INamedTypeSymbol namedDelegateType)
        {
            return false;
        }

        delegateType = namedDelegateType;
        return true;
    }

    private static IEventSymbol? FindEvent(INamedTypeSymbol typeSymbol, string eventName)
    {
        for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
        {
            IEventSymbol? eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    private static bool TryBuildWorkspaceEdit(
        string xamlFilePath,
        INamedTypeSymbol rootTypeSymbol,
        string handlerName,
        INamedTypeSymbol delegateType,
        CancellationToken cancellationToken,
        out XamlWorkspaceEdit workspaceEdit,
        out bool hasExistingMethods)
    {
        workspaceEdit = XamlWorkspaceEdit.Empty;
        hasExistingMethods = rootTypeSymbol
            .GetMembers(handlerName)
            .OfType<IMethodSymbol>()
            .Any(static method => !method.IsStatic && method.MethodKind == MethodKind.Ordinary);

        TypeDeclarationSyntax? declaration = SelectPreferredDeclaration(rootTypeSymbol, xamlFilePath, cancellationToken);
        if (declaration is null ||
            string.IsNullOrWhiteSpace(declaration.SyntaxTree.FilePath))
        {
            return false;
        }

        SourceText sourceText = declaration.SyntaxTree.GetText(cancellationToken);
        string insertionText = BuildHandlerStubText(sourceText, declaration, handlerName, delegateType);
        SourceRange insertionRange = CreateInsertionRange(sourceText, declaration.CloseBraceToken.SpanStart);

        workspaceEdit = new XamlWorkspaceEdit(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(
                UriPathHelper.ToDocumentUri(declaration.SyntaxTree.FilePath),
                ImmutableArray.Create(new XamlDocumentTextEdit(insertionRange, insertionText))));
        return true;
    }

    private static TypeDeclarationSyntax? SelectPreferredDeclaration(
        INamedTypeSymbol typeSymbol,
        string xamlFilePath,
        CancellationToken cancellationToken)
    {
        string xamlDirectory = System.IO.Path.GetDirectoryName(xamlFilePath) ?? string.Empty;

        return typeSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken) as TypeDeclarationSyntax)
            .Where(static declaration => declaration is not null && !string.IsNullOrWhiteSpace(declaration.SyntaxTree.FilePath))
            .Cast<TypeDeclarationSyntax>()
            .OrderByDescending(declaration => HasCodeBehindPathMatch(declaration.SyntaxTree.FilePath, typeSymbol.Name))
            .ThenByDescending(declaration => IsInSameDirectory(declaration.SyntaxTree.FilePath, xamlDirectory))
            .ThenBy(declaration => declaration.SyntaxTree.FilePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool HasCodeBehindPathMatch(string filePath, string typeName)
    {
        string fileName = System.IO.Path.GetFileName(filePath);
        return fileName.StartsWith(typeName + ".", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(System.IO.Path.GetFileNameWithoutExtension(filePath), typeName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInSameDirectory(string filePath, string expectedDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(expectedDirectory))
        {
            return false;
        }

        string? directory = System.IO.Path.GetDirectoryName(filePath);
        return string.Equals(directory, expectedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildHandlerStubText(
        SourceText sourceText,
        TypeDeclarationSyntax declaration,
        string handlerName,
        INamedTypeSymbol delegateType)
    {
        string newLine = sourceText.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string typeIndent = GetIndentation(sourceText, declaration);
        string memberIndent = typeIndent + "    ";
        string bodyIndent = memberIndent + "    ";
        IMethodSymbol? invokeMethod = delegateType.DelegateInvokeMethod;
        string returnTypeName = (invokeMethod?.ReturnType ?? delegateType).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string parameterList = BuildParameterList(invokeMethod);

        bool requiresThrowBody = invokeMethod is not null &&
            (!invokeMethod.ReturnsVoid || invokeMethod.Parameters.Any(static parameter => parameter.RefKind == RefKind.Out));

        string body = requiresThrowBody
            ? $"{memberIndent}{{{newLine}{bodyIndent}throw new global::System.NotImplementedException();{newLine}{memberIndent}}}"
            : $"{memberIndent}{{{newLine}{memberIndent}}}";

        return
            $"{newLine}{memberIndent}private {returnTypeName} {EscapeIdentifier(handlerName)}({parameterList}){newLine}" +
            $"{body}{newLine}";
    }

    private static string BuildParameterList(IMethodSymbol? invokeMethod)
    {
        if (invokeMethod is null || invokeMethod.Parameters.Length == 0)
        {
            return string.Empty;
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>(invokeMethod.Parameters.Length);
        for (int index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            IParameterSymbol parameter = invokeMethod.Parameters[index];
            string parameterName = BuildParameterName(parameter, index, usedNames);
            string modifier = parameter.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => parameter.IsParams ? "params " : string.Empty
            };

            parts.Add($"{modifier}{parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {parameterName}");
        }

        return string.Join(", ", parts);
    }

    private static string BuildParameterName(IParameterSymbol parameter, int index, ISet<string> usedNames)
    {
        string baseName = string.IsNullOrWhiteSpace(parameter.Name)
            ? $"arg{index}"
            : EscapeIdentifier(parameter.Name);
        string candidate = baseName;
        int suffix = 1;
        while (!usedNames.Add(candidate))
        {
            candidate = baseName + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private static string EscapeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "_";
        }

        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;
    }

    private static string GetIndentation(SourceText sourceText, TypeDeclarationSyntax declaration)
    {
        FileLinePositionSpan span = declaration.SyntaxTree.GetLineSpan(declaration.Span);
        int lineNumber = span.StartLinePosition.Line;
        TextLine line = sourceText.Lines[Math.Clamp(lineNumber, 0, sourceText.Lines.Count - 1)];
        string text = line.ToString();
        int nonWhitespaceIndex = 0;
        while (nonWhitespaceIndex < text.Length && char.IsWhiteSpace(text[nonWhitespaceIndex]))
        {
            nonWhitespaceIndex++;
        }

        return text.Substring(0, nonWhitespaceIndex);
    }

    private static SourceRange CreateInsertionRange(SourceText sourceText, int offset)
    {
        LinePosition linePosition = sourceText.Lines.GetLinePosition(offset);
        SourcePosition position = new(linePosition.Line, linePosition.Character);
        return new SourceRange(position, position);
    }

    private static bool HasCompatibleInstanceMethod(
        INamedTypeSymbol type,
        string methodName,
        ITypeSymbol delegateType)
    {
        if (delegateType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.DelegateInvokeMethod is not IMethodSymbol invokeMethod)
        {
            return HasInstanceMethod(type, methodName);
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (IMethodSymbol method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (IsMethodCompatibleWithDelegate(method, invokeMethod))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasInstanceMethod(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(methodName).OfType<IMethodSymbol>().Any(static method => !method.IsStatic && method.MethodKind == MethodKind.Ordinary))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMethodCompatibleWithDelegate(IMethodSymbol method, IMethodSymbol invokeMethod)
    {
        if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, invokeMethod.ReturnType) ||
            method.Parameters.Length != invokeMethod.Parameters.Length)
        {
            return false;
        }

        for (int index = 0; index < method.Parameters.Length; index++)
        {
            IParameterSymbol methodParameter = method.Parameters[index];
            IParameterSymbol delegateParameter = invokeMethod.Parameters[index];
            if (!SymbolEqualityComparer.Default.Equals(methodParameter.Type, delegateParameter.Type) ||
                methodParameter.RefKind != delegateParameter.RefKind)
            {
                return false;
            }
        }

        return true;
    }
}
