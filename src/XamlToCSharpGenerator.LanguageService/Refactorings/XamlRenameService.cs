using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.LanguageService.Workspace;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlRenameService
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly XamlDocumentStore _documentStore;
    private readonly ICompilationProvider _compilationProvider;
    private readonly XamlCompilerAnalysisService _analysisService;

    public XamlRenameService(
        XamlDocumentStore documentStore,
        ICompilationProvider compilationProvider,
        XamlCompilerAnalysisService analysisService)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _compilationProvider = compilationProvider ?? throw new ArgumentNullException(nameof(compilationProvider));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
    }

    public async Task<XamlPrepareRenameResult?> PrepareRenameAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        if (IsXamlDocument(uri))
        {
            var analysis = await GetXamlAnalysisAsync(uri, options, documentTextOverride, cancellationToken).ConfigureAwait(false);
            if (analysis is null || !TryResolveXamlRenameTarget(analysis, position, out var target))
            {
                return null;
            }

            return new XamlPrepareRenameResult(target.Range, target.CurrentName);
        }

        var roslynTarget = await TryResolveRoslynRenameTargetAsync(
            uri,
            position,
            options,
            documentTextOverride,
            cancellationToken).ConfigureAwait(false);
        return roslynTarget is null
            ? null
            : new XamlPrepareRenameResult(roslynTarget.Value.Range, roslynTarget.Value.CurrentName);
    }

    public async Task<XamlWorkspaceEdit> RenameAsync(
        string uri,
        SourcePosition position,
        string newName,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return XamlWorkspaceEdit.Empty;
        }

        if (IsXamlDocument(uri))
        {
            var analysis = await GetXamlAnalysisAsync(uri, options, documentTextOverride, cancellationToken).ConfigureAwait(false);
            if (analysis is null || !TryResolveXamlRenameTarget(analysis, position, out var xamlTarget))
            {
                return XamlWorkspaceEdit.Empty;
            }

            if (!ValidateNewName(xamlTarget.Kind, newName))
            {
                return XamlWorkspaceEdit.Empty;
            }

            return xamlTarget.Kind switch
            {
                RenameTargetKind.NamedElement => RenameLocalNamedElement(analysis, xamlTarget.CurrentName, newName),
                RenameTargetKind.ResourceKey => await RenameProjectResourceKeyAsync(
                    analysis,
                    xamlTarget.CurrentName,
                    newName,
                    options,
                    cancellationToken).ConfigureAwait(false),
                RenameTargetKind.StyleClass => await RenameProjectStyleClassAsync(
                    analysis,
                    xamlTarget.CurrentName,
                    newName,
                    options,
                    cancellationToken).ConfigureAwait(false),
                RenameTargetKind.PseudoClass => await RenameProjectPseudoClassAsync(
                    analysis,
                    position,
                    xamlTarget.CurrentName,
                    newName,
                    options,
                    cancellationToken).ConfigureAwait(false),
                RenameTargetKind.ClrType when xamlTarget.Symbol is not null => await RenameRoslynSymbolAsync(
                    xamlTarget.Symbol,
                    analysis.Document.FilePath,
                    documentTextOverride,
                    options,
                    newName,
                    cancellationToken).ConfigureAwait(false),
                RenameTargetKind.ClrProperty when xamlTarget.Symbol is not null => await RenameRoslynSymbolAsync(
                    xamlTarget.Symbol,
                    analysis.Document.FilePath,
                    documentTextOverride,
                    options,
                    newName,
                    cancellationToken).ConfigureAwait(false),
                _ => XamlWorkspaceEdit.Empty
            };
        }

        var roslynTarget = await TryResolveRoslynRenameTargetAsync(
            uri,
            position,
            options,
            documentTextOverride,
            cancellationToken).ConfigureAwait(false);
        if (roslynTarget is null || !ValidateNewName(roslynTarget.Value.Kind, newName))
        {
            return XamlWorkspaceEdit.Empty;
        }

        return await RenameRoslynSymbolAsync(
            roslynTarget.Value.Symbol,
            UriPathHelper.ToFilePath(uri),
            documentTextOverride,
            options,
            newName,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<XamlWorkspaceEdit> RenameRoslynSymbolAsync(
        ISymbol symbol,
        string sourceFilePath,
        string? documentTextOverride,
        XamlLanguageServiceOptions options,
        string newName,
        CancellationToken cancellationToken)
    {
        var snapshot = await _compilationProvider
            .GetCompilationAsync(sourceFilePath, options.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot.Project is null)
        {
            return XamlWorkspaceEdit.Empty;
        }

        var originalSolution = snapshot.Project.Solution;
        var workingSolution = ApplyDocumentTextOverride(originalSolution, sourceFilePath, documentTextOverride);
        var solutionSymbol = await ResolveSymbolInSolutionAsync(symbol, workingSolution, cancellationToken).ConfigureAwait(false);
        if (solutionSymbol is null)
        {
            return XamlWorkspaceEdit.Empty;
        }

        var renameOptions = new SymbolRenameOptions(
            RenameOverloads: false,
            RenameInStrings: false,
            RenameInComments: false,
            RenameFile: false);
        var renamedSolution = await Renamer
            .RenameSymbolAsync(workingSolution, solutionSymbol, renameOptions, newName, cancellationToken)
            .ConfigureAwait(false);

        var changesBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<XamlDocumentTextEdit>>(StringComparer.Ordinal);
        AddRoslynDocumentChanges(changesBuilder, workingSolution, renamedSolution, cancellationToken);
        await AddXamlPropagationChangesAsync(
            changesBuilder,
            snapshot.ProjectPath,
            options,
            solutionSymbol,
            newName,
            cancellationToken).ConfigureAwait(false);

        return new XamlWorkspaceEdit(changesBuilder.ToImmutable());
    }

    private async Task AddXamlPropagationChangesAsync(
        ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Builder changesBuilder,
        string? projectPath,
        XamlLanguageServiceOptions options,
        ISymbol symbol,
        string newName,
        CancellationToken cancellationToken)
    {
        if (!TryResolveXamlClrRenameTarget(symbol, out var clrTarget))
        {
            return;
        }

        var xamlFilePaths = XamlProjectFileDiscoveryService.DiscoverProjectXamlFilePaths(projectPath, currentFilePath: null);
        foreach (var xamlFilePath in xamlFilePaths)
        {
            var analysis = await AnalyzeProjectXamlFileAsync(xamlFilePath, options, cancellationToken).ConfigureAwait(false);
            if (analysis is null)
            {
                continue;
            }

            ImmutableArray<XamlDocumentTextEdit> edits = clrTarget.Kind switch
            {
                RenameTargetKind.ClrType when clrTarget.TypeFullName is not null =>
                    BuildTypeRenameEditsForDocument(analysis, clrTarget.TypeFullName, clrTarget.CurrentName, newName),
                RenameTargetKind.ClrProperty when clrTarget.TypeFullName is not null =>
                    BuildPropertyRenameEditsForDocument(analysis, clrTarget.TypeFullName, clrTarget.CurrentName, newName),
                _ => ImmutableArray<XamlDocumentTextEdit>.Empty
            };

            if (edits.IsDefaultOrEmpty)
            {
                continue;
            }

            MergeDocumentEdits(changesBuilder, UriPathHelper.ToDocumentUri(xamlFilePath), edits);
        }
    }

    private async Task<XamlAnalysisResult?> AnalyzeProjectXamlFileAsync(
        string filePath,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var openDocument = _documentStore.GetByFilePath(filePath);
        string text;
        var version = 0;

        if (openDocument is not null)
        {
            text = openDocument.Text;
            version = openDocument.Version;
        }
        else
        {
            try
            {
                text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        var uri = openDocument?.Uri ?? UriPathHelper.ToDocumentUri(filePath);
        var document = new LanguageServiceDocument(uri, filePath, text, version);
        return await _analysisService.AnalyzeAsync(document, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImmutableArray<string>> GetProjectXamlFilePathsAsync(
        string currentFilePath,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var snapshot = await _compilationProvider
            .GetCompilationAsync(currentFilePath, options.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        var discoveredPaths = XamlProjectFileDiscoveryService.DiscoverProjectXamlFilePaths(snapshot.ProjectPath, currentFilePath);
        if (!discoveredPaths.IsDefaultOrEmpty)
        {
            return discoveredPaths;
        }

        return string.IsNullOrWhiteSpace(currentFilePath)
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(Path.GetFullPath(currentFilePath));
    }

    private async Task<XamlWorkspaceEdit> RenameProjectResourceKeyAsync(
        XamlAnalysisResult analysis,
        string currentName,
        string newName,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var changesBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<XamlDocumentTextEdit>>(StringComparer.Ordinal);
        foreach (var filePath in await GetProjectXamlFilePathsAsync(analysis.Document.FilePath, options, cancellationToken).ConfigureAwait(false))
        {
            var sourceAnalysis = await AnalyzeProjectXamlFileAsync(filePath, options, cancellationToken).ConfigureAwait(false);
            if (sourceAnalysis is null)
            {
                continue;
            }

            var edits = BuildResourceKeyRenameEditsForDocument(sourceAnalysis, currentName, newName);
            if (!edits.IsDefaultOrEmpty)
            {
                MergeDocumentEdits(changesBuilder, UriPathHelper.ToDocumentUri(filePath), edits);
            }
        }

        return new XamlWorkspaceEdit(changesBuilder.ToImmutable());
    }

    private async Task<XamlWorkspaceEdit> RenameProjectStyleClassAsync(
        XamlAnalysisResult analysis,
        string currentName,
        string newName,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var changesBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<XamlDocumentTextEdit>>(StringComparer.Ordinal);
        foreach (var filePath in await GetProjectXamlFilePathsAsync(analysis.Document.FilePath, options, cancellationToken).ConfigureAwait(false))
        {
            var sourceAnalysis = await AnalyzeProjectXamlFileAsync(filePath, options, cancellationToken).ConfigureAwait(false);
            if (sourceAnalysis is null)
            {
                continue;
            }

            var edits = BuildStyleClassRenameEditsForDocument(sourceAnalysis, currentName, newName);
            if (!edits.IsDefaultOrEmpty)
            {
                MergeDocumentEdits(changesBuilder, UriPathHelper.ToDocumentUri(filePath), edits);
            }
        }

        return new XamlWorkspaceEdit(changesBuilder.ToImmutable());
    }

    private async Task<XamlWorkspaceEdit> RenameProjectPseudoClassAsync(
        XamlAnalysisResult analysis,
        SourcePosition position,
        string currentName,
        string newName,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var normalizedCurrentName = ":" + TrimPseudoClassPrefix(currentName);
        var trimmedNewName = TrimPseudoClassPrefix(newName);
        var changesBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<XamlDocumentTextEdit>>(StringComparer.Ordinal);

        foreach (var filePath in await GetProjectXamlFilePathsAsync(analysis.Document.FilePath, options, cancellationToken).ConfigureAwait(false))
        {
            var sourceAnalysis = await AnalyzeProjectXamlFileAsync(filePath, options, cancellationToken).ConfigureAwait(false);
            if (sourceAnalysis is null)
            {
                continue;
            }

            var edits = BuildPseudoClassRenameEditsForDocument(sourceAnalysis, normalizedCurrentName, trimmedNewName);
            if (!edits.IsDefaultOrEmpty)
            {
                MergeDocumentEdits(changesBuilder, UriPathHelper.ToDocumentUri(filePath), edits);
            }
        }

        if (XamlSelectorNavigationService.TryResolveTargetAtOffset(analysis, position, out var selectorTarget) &&
            selectorTarget.Kind == XamlSelectorNavigationTargetKind.PseudoClass &&
            TryCreatePseudoClassDeclarationEdit(analysis, selectorTarget, trimmedNewName, out var declarationUri, out var declarationEdit))
        {
            MergeDocumentEdits(
                changesBuilder,
                declarationUri,
                ImmutableArray.Create(declarationEdit));
        }

        return new XamlWorkspaceEdit(changesBuilder.ToImmutable());
    }

    private async Task<XamlAnalysisResult?> GetXamlAnalysisAsync(
        string uri,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        var document = _documentStore.Get(uri);
        if (document is null)
        {
            var filePath = UriPathHelper.ToFilePath(uri);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var text = documentTextOverride;
            if (text is null)
            {
                text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            }

            document = new LanguageServiceDocument(uri, filePath, text, 0);
        }
        else if (documentTextOverride is not null && !string.Equals(document.Text, documentTextOverride, StringComparison.Ordinal))
        {
            document = document with { Text = documentTextOverride };
        }

        return await _analysisService.AnalyzeAsync(document, options, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryResolveXamlRenameTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out RenameTarget target)
    {
        var text = analysis.Document.Text;
        var offset = TextCoordinateHelper.GetOffset(text, position);
        if (offset < 0)
        {
            target = default;
            return false;
        }

        foreach (var range in XamlResourceReferenceNavigationSemantics.FindResourceReferenceRanges(text, string.Empty))
        {
            _ = range;
        }

        if (TryResolveResourceKeyRenameTarget(analysis, position, offset, out target))
        {
            return true;
        }

        if (TryResolveBindingRenameTarget(analysis, position, out target))
        {
            return true;
        }

        if (TryResolveSelectorRenameTarget(analysis, position, out target))
        {
            return true;
        }

        if (TryResolveMarkupExtensionTypeRenameTarget(analysis, position, offset, out target))
        {
            return true;
        }

        if (TryResolveNamedElementRenameTarget(analysis, offset, out target))
        {
            return true;
        }

        var context = XamlCompletionContextDetector.Detect(text, position);
        var prefixMap = analysis.PrefixMap;
        var token = string.IsNullOrWhiteSpace(context.Token) ? context.CurrentAttributeValue : context.Token;

        if (context.Kind == XamlCompletionContextKind.AttributeValue &&
            XamlStyleNavigationSemantics.IsSetterPropertyAttribute(context.CurrentElementName, context.CurrentAttributeName) &&
            TryResolveSetterPropertyRenameTarget(analysis, position, context, token, out target))
        {
            return true;
        }

        if (context.Kind is XamlCompletionContextKind.AttributeValue or XamlCompletionContextKind.MarkupExtension &&
            XamlTypeReferenceNavigationResolver.TryResolve(
                analysis,
                prefixMap,
                context.CurrentAttributeName,
                string.IsNullOrWhiteSpace(token) ? context.CurrentAttributeValue : token,
                out var resolvedTypeReference) &&
            TryResolveClrTypeRenameTarget(
                analysis,
                resolvedTypeReference.FullTypeName,
                CreateTypeRenameRange(
                    text,
                    ResolveTokenRange(text, context, position),
                    token ?? context.CurrentAttributeValue ?? string.Empty),
                out target))
        {
            return true;
        }

        if (context.Kind == XamlCompletionContextKind.ElementName &&
            XamlClrSymbolResolver.TryResolveTypeInfo(analysis.TypeIndex!, prefixMap, token, out var typeInfo) &&
            typeInfo is not null &&
            TryCreateElementTypeRenameRange(text, position, out var elementTypeRange) &&
            TryResolveClrTypeRenameTarget(analysis, typeInfo.FullTypeName, elementTypeRange, out target))
        {
            return true;
        }

        if (context.Kind == XamlCompletionContextKind.AttributeName &&
            TryResolveAttributePropertyRenameTarget(analysis, context, out target))
        {
            return true;
        }

        target = default;
        return false;
    }

    private static bool TryResolveNamedElementRenameTarget(
        XamlAnalysisResult analysis,
        int offset,
        out RenameTarget target)
    {
        target = default;
        var identifier = XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
        if (string.IsNullOrWhiteSpace(identifier) ||
            analysis.ParsedDocument is null ||
            !HasNamedElementDeclaration(analysis.ParsedDocument, identifier))
        {
            return false;
        }

        var range = CreateIdentifierRangeAtOffset(analysis.Document.Text, offset);
        if (range is null)
        {
            return false;
        }

        target = new RenameTarget(RenameTargetKind.NamedElement, range.Value, identifier, Symbol: null, TypeFullName: null);
        return true;
    }

    private static bool TryResolveResourceKeyRenameTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        int offset,
        out RenameTarget target)
    {
        target = default;
        if (analysis.ParsedDocument is null)
        {
            return false;
        }

        foreach (var range in XamlResourceReferenceNavigationSemantics.FindResourceReferenceRanges(
                     analysis.Document.Text,
                     XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset)))
        {
            if (!ContainsPosition(range, position))
            {
                continue;
            }

            var currentName = ReadRangeText(analysis.Document.Text, range);
            target = new RenameTarget(RenameTargetKind.ResourceKey, range, currentName, Symbol: null, TypeFullName: null);
            return true;
        }

        var identifier = XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
        if (string.IsNullOrWhiteSpace(identifier) || !HasResourceDeclaration(analysis.ParsedDocument, identifier))
        {
            return false;
        }

        var declarationRange = CreateIdentifierRangeAtOffset(analysis.Document.Text, offset);
        if (declarationRange is null)
        {
            return false;
        }

        target = new RenameTarget(RenameTargetKind.ResourceKey, declarationRange.Value, identifier, Symbol: null, TypeFullName: null);
        return true;
    }

    private static bool TryResolveBindingRenameTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out RenameTarget target)
    {
        target = default;
        if (!XamlBindingNavigationService.TryResolveNavigationTarget(analysis, position, out var bindingTarget))
        {
            return false;
        }

        return bindingTarget.Kind switch
        {
            XamlBindingNavigationTargetKind.Type when bindingTarget.TypeReference is { } typeReference =>
                TryResolveClrTypeRenameTarget(
                    analysis,
                    typeReference.FullTypeName,
                    CreateTypeRenameRange(analysis.Document.Text, bindingTarget.UsageRange, ReadRangeText(analysis.Document.Text, bindingTarget.UsageRange)),
                    out target),
            XamlBindingNavigationTargetKind.Property when bindingTarget.OwnerTypeInfo is not null && bindingTarget.PropertyInfo is not null =>
                TryResolveClrPropertyRenameTarget(
                    analysis,
                    bindingTarget.OwnerTypeInfo.FullTypeName,
                    bindingTarget.PropertyInfo.Name,
                    CreatePropertyRenameRange(analysis.Document.Text, bindingTarget.UsageRange),
                    out target),
            _ => false
        };
    }

    private static bool TryResolveSelectorRenameTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out RenameTarget target)
    {
        target = default;
        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        var absoluteRange = default(SourceRange);
        var selectedText = string.Empty;
        var selectorTarget = default(XamlSelectorNavigationTarget);

        if (XamlSelectorNavigationService.TryResolveReferenceAtOffset(analysis, position, out var selectorReference))
        {
            selectorTarget = selectorReference.Target;
            absoluteRange = selectorReference.Range;
            selectedText = ReadRangeText(analysis.Document.Text, absoluteRange);
        }
        else if (context.Kind == XamlCompletionContextKind.AttributeValue &&
                 string.Equals(GetLocalName(context.CurrentAttributeName), "Classes", StringComparison.Ordinal))
        {
            if (!XamlSelectorNavigationService.TryResolveTargetAtOffset(analysis, position, out selectorTarget))
            {
                return false;
            }

            absoluteRange = ResolveTokenRange(analysis.Document.Text, context, position);
            selectedText = ReadRangeText(analysis.Document.Text, absoluteRange);
        }
        else if (context.Kind == XamlCompletionContextKind.AttributeName)
        {
            if (!XamlSelectorNavigationService.TryResolveTargetAtOffset(analysis, position, out selectorTarget))
            {
                return false;
            }

            var tokenRange = ResolveTokenRange(analysis.Document.Text, context, position);
            var tokenText = ReadRangeText(analysis.Document.Text, tokenRange);
            const string prefix = "Classes.";
            var localTokenText = GetLocalName(tokenText);
            if (!localTokenText.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var tokenStartOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, tokenRange.Start);
            var classStartOffset = tokenStartOffset + tokenText.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
            absoluteRange = new SourceRange(
                TextCoordinateHelper.GetPosition(analysis.Document.Text, classStartOffset),
                TextCoordinateHelper.GetPosition(analysis.Document.Text, classStartOffset + localTokenText.Length - prefix.Length));
            selectedText = ReadRangeText(analysis.Document.Text, absoluteRange);
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return false;
        }

        switch (selectorTarget.Kind)
        {
            case XamlSelectorNavigationTargetKind.Type:
                if (!XamlClrSymbolResolver.TryResolveTypeInfo(
                        analysis.TypeIndex!,
                        analysis.PrefixMap,
                        selectorTarget.Name,
                        out var typeInfo) ||
                    typeInfo is null)
                {
                    return false;
                }

                return TryResolveClrTypeRenameTarget(
                    analysis,
                    typeInfo.FullTypeName,
                    CreateTypeRenameRange(analysis.Document.Text, absoluteRange, selectedText),
                    out target);

            case XamlSelectorNavigationTargetKind.StyleClass:
                target = new RenameTarget(RenameTargetKind.StyleClass, absoluteRange, selectedText, Symbol: null, TypeFullName: null);
                return true;

            case XamlSelectorNavigationTargetKind.PseudoClass:
                target = new RenameTarget(RenameTargetKind.PseudoClass, absoluteRange, selectedText, Symbol: null, TypeFullName: null);
                return true;

            default:
                return false;
        }
    }

    private static bool TryResolveMarkupExtensionTypeRenameTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        int offset,
        out RenameTarget target)
    {
        target = default;
        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        if (context.Kind != XamlCompletionContextKind.MarkupExtension ||
            !XamlMarkupExtensionNavigationSemantics.TryResolveClassTokenAtOffset(
                analysis.Document.Text,
                offset,
                out var classToken) ||
            !XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
                analysis,
                analysis.PrefixMap,
                classToken.Name,
                out var resolvedTypeReference))
        {
            return false;
        }

        var range = new SourceRange(
            TextCoordinateHelper.GetPosition(analysis.Document.Text, classToken.Start),
            TextCoordinateHelper.GetPosition(analysis.Document.Text, classToken.Start + classToken.Length));
        return TryResolveClrTypeRenameTarget(
            analysis,
            resolvedTypeReference.FullTypeName,
            CreateTypeRenameRange(analysis.Document.Text, range, classToken.Name),
            out target);
    }

    private static bool TryResolveSetterPropertyRenameTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        XamlCompletionContext context,
        string? propertyToken,
        out RenameTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        var ownerTypeToken = context.CurrentElementName;
        if (!propertyToken.Contains('.', StringComparison.Ordinal) &&
            XamlStyleNavigationSemantics.TryResolveStyleSetterOwnerTypeToken(
                analysis,
                position,
                propertyToken,
                out var resolvedOwnerTypeToken))
        {
            ownerTypeToken = resolvedOwnerTypeToken;
        }

        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex!,
                analysis.PrefixMap,
                ownerTypeToken,
                propertyToken,
                out var propertyInfo,
                out var ownerTypeInfo) ||
            propertyInfo is null ||
            ownerTypeInfo is null)
        {
            return false;
        }

        return TryResolveClrPropertyRenameTarget(
            analysis,
            ownerTypeInfo.FullTypeName,
            propertyInfo.Name,
            CreatePropertyRenameRange(
                analysis.Document.Text,
                ResolveTokenRange(analysis.Document.Text, context, position)),
            out target);
    }

    private static bool TryResolveAttributePropertyRenameTarget(
        XamlAnalysisResult analysis,
        XamlCompletionContext context,
        out RenameTarget target)
    {
        target = default;
        var propertyToken = string.IsNullOrWhiteSpace(context.Token)
            ? context.CurrentAttributeName
            : context.Token;
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex!,
                analysis.PrefixMap,
                context.CurrentElementName,
                propertyToken,
                out var propertyInfo,
                out var ownerTypeInfo) ||
            propertyInfo is null ||
            ownerTypeInfo is null)
        {
            return false;
        }

        return TryResolveClrPropertyRenameTarget(
            analysis,
            ownerTypeInfo.FullTypeName,
            propertyInfo.Name,
            CreatePropertyRenameRange(analysis.Document.Text, ResolveTokenRange(analysis.Document.Text, context, default)),
            out target);
    }

    private static bool TryResolveClrTypeRenameTarget(
        XamlAnalysisResult analysis,
        string fullTypeName,
        SourceRange range,
        out RenameTarget target)
    {
        target = default;
        var symbol = ResolveTypeSymbol(analysis.Compilation, fullTypeName);
        if (symbol is null)
        {
            return false;
        }

        target = new RenameTarget(RenameTargetKind.ClrType, range, symbol.Name, symbol, fullTypeName);
        return true;
    }

    private static bool TryResolveClrPropertyRenameTarget(
        XamlAnalysisResult analysis,
        string ownerTypeFullName,
        string propertyName,
        SourceRange range,
        out RenameTarget target)
    {
        target = default;
        var symbol = ResolvePropertySymbol(analysis.Compilation, ownerTypeFullName, propertyName);
        if (symbol is null)
        {
            return false;
        }

        target = new RenameTarget(RenameTargetKind.ClrProperty, range, symbol.Name, symbol, ownerTypeFullName);
        return true;
    }

    private async Task<RoslynRenameTarget?> TryResolveRoslynRenameTargetAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        var filePath = UriPathHelper.ToFilePath(uri);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var snapshot = await _compilationProvider
            .GetCompilationAsync(filePath, options.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot.Project is null)
        {
            return null;
        }

        var solution = ApplyDocumentTextOverride(snapshot.Project.Solution, filePath, documentTextOverride);
        var document = FindDocumentByFilePath(solution, filePath);
        if (document is null)
        {
            return null;
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null)
        {
            return null;
        }

        var offset = sourceText.Lines.GetPosition(new LinePosition(position.Line, position.Character));
        var token = FindRenameToken(syntaxRoot, sourceText, offset);
        var symbol = await ResolveSymbolAtPositionAsync(document, syntaxRoot, sourceText, offset, cancellationToken).ConfigureAwait(false);
        if (symbol is null || !TryResolveXamlClrRenameTarget(symbol, out var targetKind))
        {
            return null;
        }

        var startLine = sourceText.Lines.GetLinePosition(token.Span.Start);
        var endLine = sourceText.Lines.GetLinePosition(token.Span.End);
        var range = new SourceRange(
            new SourcePosition(startLine.Line, startLine.Character),
            new SourcePosition(endLine.Line, endLine.Character));
        return new RoslynRenameTarget(targetKind.Kind, range, symbol.Name, symbol);
    }

    private static async Task<ISymbol?> ResolveSymbolAtPositionAsync(
        Document document,
        SyntaxNode syntaxRoot,
        SourceText sourceText,
        int offset,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return null;
        }

        var token = FindRenameToken(syntaxRoot, sourceText, offset);
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declaredSymbol is not null)
            {
                return declaredSymbol;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
            if (symbolInfo is not null)
            {
                return symbolInfo;
            }

            var memberGroup = semanticModel.GetMemberGroup(node, cancellationToken);
            if (memberGroup.Length > 0)
            {
                return memberGroup[0];
            }
        }

        return null;
    }

    private static SyntaxToken FindRenameToken(SyntaxNode syntaxRoot, SourceText sourceText, int offset)
    {
        var boundedOffset = Math.Max(0, Math.Min(offset, sourceText.Length == 0 ? 0 : sourceText.Length - 1));
        return syntaxRoot.FindToken(boundedOffset);
    }

    private static Solution ApplyDocumentTextOverride(Solution solution, string filePath, string? documentTextOverride)
    {
        if (string.IsNullOrWhiteSpace(documentTextOverride))
        {
            return solution;
        }

        var sourceText = SourceText.From(documentTextOverride);

        foreach (var documentId in solution.GetDocumentIdsWithFilePath(filePath).ToImmutableArray())
        {
            if (solution.GetDocument(documentId) is null)
            {
                continue;
            }

            solution = solution.WithDocumentText(documentId, sourceText, PreservationMode.PreserveIdentity);
        }

        foreach (var documentId in GetAdditionalDocumentIdsWithFilePath(solution, filePath))
        {
            if (solution.GetAdditionalDocument(documentId) is null)
            {
                continue;
            }

            solution = solution.WithAdditionalDocumentText(documentId, sourceText, PreservationMode.PreserveIdentity);
        }

        return solution;
    }

    private static async Task<ISymbol?> ResolveSymbolInSolutionAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken)
    {
        if (!TryResolveXamlClrRenameTarget(symbol, out var target))
        {
            return null;
        }

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            ISymbol? candidate = target.Kind switch
            {
                RenameTargetKind.ClrType => ResolveTypeSymbol(compilation, target.TypeFullName),
                RenameTargetKind.ClrProperty => ResolvePropertySymbol(compilation, target.TypeFullName, target.CurrentName),
                _ => null
            };
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static ImmutableArray<DocumentId> GetAdditionalDocumentIdsWithFilePath(Solution solution, string filePath)
    {
        var builder = ImmutableArray.CreateBuilder<DocumentId>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.AdditionalDocuments)
            {
                if (string.Equals(document.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Add(document.Id);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static void AddRoslynDocumentChanges(
        ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Builder changesBuilder,
        Solution originalSolution,
        Solution renamedSolution,
        CancellationToken cancellationToken)
    {
        var changedSolution = renamedSolution.GetChanges(originalSolution);
        foreach (var projectChanges in changedSolution.GetProjectChanges())
        {
            foreach (var documentId in projectChanges.GetChangedDocuments())
            {
                var oldDocument = originalSolution.GetDocument(documentId);
                var newDocument = renamedSolution.GetDocument(documentId);
                if (oldDocument?.FilePath is null || newDocument is null)
                {
                    continue;
                }

                var textChanges = newDocument.GetTextChangesAsync(oldDocument, cancellationToken).GetAwaiter().GetResult();
                if (!textChanges.Any())
                {
                    continue;
                }

                var edits = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
                foreach (var textChange in textChanges)
                {
                    var lineSpan = textChange.Span.ToString();
                    _ = lineSpan;
                    var oldText = oldDocument.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
                    var span = textChange.Span;
                    var startLine = oldText.Lines.GetLinePosition(span.Start);
                    var endLine = oldText.Lines.GetLinePosition(span.End);
                    edits.Add(new XamlDocumentTextEdit(
                        new SourceRange(
                            new SourcePosition(startLine.Line, startLine.Character),
                            new SourcePosition(endLine.Line, endLine.Character)),
                        textChange.NewText ?? string.Empty));
                }

                MergeDocumentEdits(
                    changesBuilder,
                    UriPathHelper.ToDocumentUri(oldDocument.FilePath),
                    edits.ToImmutable());
            }
        }
    }

    private static ImmutableArray<XamlDocumentTextEdit> BuildTypeRenameEditsForDocument(
        XamlAnalysisResult analysis,
        string targetFullTypeName,
        string oldName,
        string newName)
    {
        if (analysis.TypeIndex is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        if (!analysis.TypeIndex.TryGetTypeByFullTypeName(targetFullTypeName, out var typeInfo) || typeInfo is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var root = analysis.XmlDocument?.Root;
        if (root is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            if (string.Equals(element.Name.LocalName, typeInfo.XmlTypeName, StringComparison.Ordinal) &&
                TryResolveTypeInfoByXmlNamespace(analysis, element.Name.NamespaceName, element.Name.LocalName, out var elementTypeInfo) &&
                elementTypeInfo is not null &&
                string.Equals(elementTypeInfo.FullTypeName, typeInfo.FullTypeName, StringComparison.Ordinal) &&
                TryCreateElementTypeRenameRange(analysis.Document.Text, element, out var elementRange))
            {
                AddEdit(builder, seen, elementRange, ComputeTypeReplacement(ReadRangeText(analysis.Document.Text, elementRange), oldName, newName));
            }

            var sourcePrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                if (TryAddSelectorTypeRenameEdit(
                        analysis,
                        sourcePrefixMap,
                        element,
                        attribute,
                        typeInfo,
                        oldName,
                        newName,
                        builder,
                        seen))
                {
                    continue;
                }

                if (TryAddMarkupExtensionTypeRenameEdit(
                        analysis,
                        sourcePrefixMap,
                        attribute,
                        typeInfo,
                        oldName,
                        newName,
                        builder,
                        seen))
                {
                    continue;
                }

                if (attribute.Value.IndexOf('{') >= 0)
                {
                    foreach (var bindingRange in XamlBindingNavigationService.FindTypeReferenceRanges(
                                 analysis,
                                 analysis.Document.Text,
                                 element,
                                 attribute,
                                 typeInfo.FullTypeName))
                    {
                        AddEdit(
                            builder,
                            seen,
                            CreateTypeRenameRange(analysis.Document.Text, bindingRange, ReadRangeText(analysis.Document.Text, bindingRange)),
                            ComputeTypeReplacement(ReadRangeText(analysis.Document.Text, bindingRange), oldName, newName));
                    }
                }

                if (!XamlTypeReferenceNavigationResolver.IsTypeReferenceAttribute(attribute) ||
                    !XamlTypeReferenceNavigationResolver.TryResolve(
                        analysis,
                        sourcePrefixMap,
                        attribute.Name.LocalName,
                        attribute.Value,
                        out var typeReference) ||
                    !string.Equals(typeReference.FullTypeName, typeInfo.FullTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryCreateAttributeValueRange(analysis.Document.Text, attribute, out var valueRange))
                {
                    var renameRange = CreateTypeRenameRange(analysis.Document.Text, valueRange, attribute.Value);
                    AddEdit(
                        builder,
                        seen,
                        renameRange,
                        ComputeTypeReplacement(ReadRangeText(analysis.Document.Text, renameRange), oldName, newName));
                }
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlDocumentTextEdit> BuildPropertyRenameEditsForDocument(
        XamlAnalysisResult analysis,
        string ownerTypeFullName,
        string propertyName,
        string newName)
    {
        if (analysis.TypeIndex is null ||
            !analysis.TypeIndex.TryGetTypeByFullTypeName(ownerTypeFullName, out var ownerTypeInfo) ||
            ownerTypeInfo is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        var propertyInfo = ownerTypeInfo.Properties.FirstOrDefault(property =>
            string.Equals(property.Name, propertyName, StringComparison.Ordinal));
        if (propertyInfo is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var root = analysis.XmlDocument?.Root;
        if (root is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            AvaloniaTypeInfo? elementTypeInfo = null;
            if (string.Equals(element.Name.LocalName, propertyInfo.Name, StringComparison.Ordinal) ||
                ElementMayReferencePropertyByAttribute(element, propertyInfo.Name))
            {
                TryResolveTypeInfoByXmlNamespace(
                    analysis,
                    element.Name.NamespaceName,
                    element.Name.LocalName,
                    out elementTypeInfo);
            }

            var sourcePrefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                if (TryAddSetterPropertyRenameEdit(
                        analysis,
                        sourcePrefixMap,
                        element,
                        attribute,
                        ownerTypeInfo,
                        propertyInfo,
                        newName,
                        builder,
                        seen))
                {
                    continue;
                }

                if (attribute.Value.IndexOf('{') >= 0)
                {
                    foreach (var bindingRange in XamlBindingNavigationService.FindPropertyReferenceRanges(
                                 analysis,
                                 analysis.Document.Text,
                                 element,
                                 attribute,
                                 ownerTypeInfo,
                                 propertyInfo))
                    {
                        AddEdit(builder, seen, bindingRange, newName);
                    }
                }

                if (!IsPropertyReferenceMatch(
                        analysis,
                        element,
                        elementTypeInfo,
                        attribute,
                        ownerTypeInfo,
                        propertyInfo))
                {
                    continue;
                }

                if (TryCreateAttributeNameRange(analysis.Document.Text, attribute, out var range))
                {
                    AddEdit(
                        builder,
                        seen,
                        CreatePropertyRenameRange(analysis.Document.Text, range),
                        newName);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlDocumentTextEdit> BuildResourceKeyRenameEditsForDocument(
        XamlAnalysisResult analysis,
        string currentName,
        string newName)
    {
        var root = analysis.XmlDocument?.Root;
        if (analysis.ParsedDocument is null || root is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddResourceKeyDeclarationEdits(analysis.Document.Text, root, currentName, newName, builder, seen);

        foreach (var range in XamlResourceReferenceNavigationSemantics.FindResourceReferenceRanges(analysis.Document.Text, currentName))
        {
            AddEdit(builder, seen, range, newName);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlDocumentTextEdit> BuildStyleClassRenameEditsForDocument(
        XamlAnalysisResult analysis,
        string currentName,
        string newName)
    {
        var builder = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var root = analysis.XmlDocument?.Root;
        if (root is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                AddSelectorStyleClassRenameEdits(analysis.Document.Text, element, attribute, currentName, newName, builder, seen);
                AddClassesValueRenameEdits(analysis.Document.Text, attribute, currentName, newName, builder, seen);
                AddClassesPropertyRenameEdits(analysis.Document.Text, attribute, currentName, newName, builder, seen);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlDocumentTextEdit> BuildPseudoClassRenameEditsForDocument(
        XamlAnalysisResult analysis,
        string normalizedCurrentName,
        string trimmedNewName)
    {
        var builder = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var root = analysis.XmlDocument?.Root;
        if (root is null)
        {
            return ImmutableArray<XamlDocumentTextEdit>.Empty;
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                AddSelectorPseudoClassRenameEdits(
                    analysis.Document.Text,
                    element,
                    attribute,
                    normalizedCurrentName,
                    trimmedNewName,
                    builder,
                    seen);
            }
        }

        return builder.ToImmutable();
    }

    private static XamlWorkspaceEdit RenameLocalNamedElement(
        XamlAnalysisResult analysis,
        string identifier,
        string newName)
    {
        var root = analysis.XmlDocument?.Root;
        if (analysis.ParsedDocument is null || root is null)
        {
            return XamlWorkspaceEdit.Empty;
        }

        var uri = UriPathHelper.ToDocumentUri(analysis.Document.FilePath);
        var builder = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddNamedElementDeclarationEdits(analysis.Document.Text, root, identifier, newName, builder, seen);

        foreach (var range in XamlNavigationTextSemantics.FindElementReferenceRanges(analysis.Document.Text, identifier))
        {
            AddEdit(builder, seen, range, newName);
        }

        return BuildSingleDocumentWorkspaceEdit(uri, builder.ToImmutable());
    }

    private static void AddResourceDefinitionEdits(
        ImmutableArray<XamlResourceDefinition> definitions,
        string identifier,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        foreach (var definition in definitions)
        {
            if (!string.Equals(definition.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var range = new SourceRange(
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1)),
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1 + identifier.Length)));
            AddEdit(builder, seen, range, newName);
        }
    }

    private static void AddTemplateDefinitionEdits(
        ImmutableArray<XamlTemplateDefinition> definitions,
        string identifier,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        foreach (var definition in definitions)
        {
            if (!string.Equals(definition.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var range = new SourceRange(
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1)),
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1 + identifier.Length)));
            AddEdit(builder, seen, range, newName);
        }
    }

    private static void AddStyleDefinitionEdits(
        ImmutableArray<XamlStyleDefinition> definitions,
        string identifier,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        foreach (var definition in definitions)
        {
            if (!string.Equals(definition.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var range = new SourceRange(
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1)),
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1 + identifier.Length)));
            AddEdit(builder, seen, range, newName);
        }
    }

    private static void AddControlThemeDefinitionEdits(
        ImmutableArray<XamlControlThemeDefinition> definitions,
        string identifier,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        foreach (var definition in definitions)
        {
            if (!string.Equals(definition.Key, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var range = new SourceRange(
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1)),
                new SourcePosition(Math.Max(0, definition.Line - 1), Math.Max(0, definition.Column - 1 + identifier.Length)));
            AddEdit(builder, seen, range, newName);
        }
    }

    private static void AddNamedElementDeclarationEdits(
        string sourceText,
        XElement root,
        string identifier,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!IsNamedElementDeclarationAttribute(attribute) ||
                    !string.Equals(attribute.Value, identifier, StringComparison.Ordinal) ||
                    !TryCreateAttributeValueRange(sourceText, attribute, out var range))
                {
                    continue;
                }

                AddEdit(builder, seen, range, newName);
            }
        }
    }

    private static void AddResourceKeyDeclarationEdits(
        string sourceText,
        XElement root,
        string identifier,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!IsResourceKeyDeclarationAttribute(attribute) ||
                    !string.Equals(attribute.Value, identifier, StringComparison.Ordinal) ||
                    !TryCreateAttributeValueRange(sourceText, attribute, out var range))
                {
                    continue;
                }

                AddEdit(builder, seen, range, newName);
            }
        }
    }

    private static XamlWorkspaceEdit BuildSingleDocumentWorkspaceEdit(string uri, ImmutableArray<XamlDocumentTextEdit> edits)
    {
        if (edits.IsDefaultOrEmpty)
        {
            return XamlWorkspaceEdit.Empty;
        }

        return new XamlWorkspaceEdit(ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>
            .Empty
            .Add(uri, SortEdits(edits)));
    }

    private static bool TryResolveXamlClrRenameTarget(ISymbol symbol, out XamlClrRenameTarget target)
    {
        target = default;
        symbol = symbol switch
        {
            IAliasSymbol aliasSymbol => aliasSymbol.Target,
            _ => symbol
        };

        if (symbol is INamedTypeSymbol typeSymbol)
        {
            target = new XamlClrRenameTarget(
                RenameTargetKind.ClrType,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                typeSymbol.Name);
            return true;
        }

        if (symbol is IPropertySymbol propertySymbol && propertySymbol.ContainingType is not null)
        {
            target = new XamlClrRenameTarget(
                RenameTargetKind.ClrProperty,
                propertySymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                propertySymbol.Name);
            return true;
        }

        return false;
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

    private static ISymbol? ResolvePropertySymbol(Compilation? compilation, string ownerTypeFullName, string propertyName)
    {
        var typeSymbol = ResolveTypeSymbol(compilation, ownerTypeFullName) as INamedTypeSymbol;
        return typeSymbol?
            .GetMembers(propertyName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault();
    }

    private static bool ValidateNewName(RenameTargetKind kind, string newName)
    {
        return kind switch
        {
            RenameTargetKind.ResourceKey => newName.Length > 0,
            RenameTargetKind.StyleClass => IsValidStyleLikeName(newName),
            RenameTargetKind.PseudoClass => IsValidStyleLikeName(TrimPseudoClassPrefix(newName)),
            _ => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(newName)
        };
    }

    private static bool IsValidStyleLikeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!MiniLanguageSyntaxFacts.IsStyleClassPart(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string TrimPseudoClassPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith(":", StringComparison.Ordinal)
            ? trimmed.Substring(1)
            : trimmed;
    }

    private static bool HasNamedElementDeclaration(XamlDocumentModel document, string identifier)
    {
        foreach (var namedElement in document.NamedElements)
        {
            if (string.Equals(namedElement.Name, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasResourceDeclaration(XamlDocumentModel document, string identifier)
    {
        foreach (var resource in document.Resources)
        {
            if (string.Equals(resource.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var template in document.Templates)
        {
            if (string.Equals(template.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var style in document.Styles)
        {
            if (string.Equals(style.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var controlTheme in document.ControlThemes)
        {
            if (string.Equals(controlTheme.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static SourceRange? CreateIdentifierRangeAtOffset(string text, int offset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = Math.Max(0, Math.Min(offset, text.Length));
        while (start > 0 && IsIdentifierCharacter(text[start - 1]))
        {
            start--;
        }

        var end = Math.Max(0, Math.Min(offset, text.Length));
        while (end < text.Length && IsIdentifierCharacter(text[end]))
        {
            end++;
        }

        if (end <= start)
        {
            return null;
        }

        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, start),
            TextCoordinateHelper.GetPosition(text, end));
    }

    private static bool IsIdentifierCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or ':' or '.' or '-';
    }

    private static SourceRange ResolveTokenRange(string text, XamlCompletionContext context, SourcePosition fallbackPosition)
    {
        if (context.TokenEndOffset > context.TokenStartOffset)
        {
            return new SourceRange(
                TextCoordinateHelper.GetPosition(text, context.TokenStartOffset),
                TextCoordinateHelper.GetPosition(text, context.TokenEndOffset));
        }

        return new SourceRange(fallbackPosition, fallbackPosition);
    }

    private static SourceRange CreateTypeRenameRange(string text, SourceRange range, string tokenText)
    {
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        if (startOffset < 0)
        {
            return range;
        }

        var localStart = 0;
        if (tokenText.StartsWith("{", StringComparison.Ordinal))
        {
            var typeIndex = tokenText.IndexOf("x:Type", StringComparison.Ordinal);
            if (typeIndex >= 0)
            {
                var payloadStart = typeIndex + "x:Type".Length;
                while (payloadStart < tokenText.Length && char.IsWhiteSpace(tokenText[payloadStart]))
                {
                    payloadStart++;
                }

                localStart = payloadStart;
                tokenText = tokenText.Substring(payloadStart);
            }
        }

        var separatorIndex = tokenText.LastIndexOfAny([':', '.']);
        if (separatorIndex >= 0 && separatorIndex + 1 < tokenText.Length)
        {
            localStart += separatorIndex + 1;
            tokenText = tokenText.Substring(separatorIndex + 1);
        }

        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, startOffset + localStart),
            TextCoordinateHelper.GetPosition(text, startOffset + localStart + tokenText.Length));
    }

    private static SourceRange CreatePropertyRenameRange(string text, SourceRange range)
    {
        var tokenText = ReadRangeText(text, range);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        if (startOffset < 0)
        {
            return range;
        }

        var separatorIndex = tokenText.LastIndexOf('.');
        if (separatorIndex < 0 || separatorIndex + 1 >= tokenText.Length)
        {
            return range;
        }

        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, startOffset + separatorIndex + 1),
            TextCoordinateHelper.GetPosition(text, startOffset + tokenText.Length));
    }

    private static bool TryCreateElementTypeRenameRange(string text, SourcePosition position, out SourceRange range)
    {
        range = default;
        var offset = TextCoordinateHelper.GetOffset(text, position);
        if (offset < 0)
        {
            return false;
        }

        var openTag = text.LastIndexOf('<', Math.Max(0, offset));
        if (openTag < 0)
        {
            return false;
        }

        var nameStart = openTag + 1;
        if (nameStart < text.Length && text[nameStart] == '/')
        {
            nameStart++;
        }

        while (nameStart < text.Length && char.IsWhiteSpace(text[nameStart]))
        {
            nameStart++;
        }

        var length = 0;
        while (nameStart + length < text.Length && IsIdentifierCharacter(text[nameStart + length]))
        {
            length++;
        }

        if (length <= 0)
        {
            return false;
        }

        var fullRange = new SourceRange(
            TextCoordinateHelper.GetPosition(text, nameStart),
            TextCoordinateHelper.GetPosition(text, nameStart + length));
        range = CreateTypeRenameRange(text, fullRange, text.Substring(nameStart, length));
        return true;
    }

    private static bool TryCreateElementTypeRenameRange(string text, XElement element, out SourceRange range)
    {
        range = default;
        if (element is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        return TryCreateElementTypeRenameRange(
            text,
            new SourcePosition(Math.Max(0, lineInfo.LineNumber - 1), Math.Max(0, lineInfo.LinePosition - 1)),
            out range);
    }

    private static string ReadRangeText(string text, SourceRange range)
    {
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        if (startOffset < 0 || endOffset < startOffset || endOffset > text.Length)
        {
            return string.Empty;
        }

        return text.Substring(startOffset, endOffset - startOffset);
    }

    private static bool TryResolveTypeInfoByXmlNamespace(
        XamlAnalysisResult analysis,
        string xmlNamespace,
        string xmlTypeName,
        out AvaloniaTypeInfo? typeInfo)
    {
        typeInfo = null;
        if (analysis.TypeIndex is null)
        {
            return false;
        }

        if (XamlClrSymbolResolver.TryResolveClrNamespace(xmlNamespace, out var clrNamespace))
        {
            return analysis.TypeIndex.TryGetTypeByClrNamespace(clrNamespace, xmlTypeName, out typeInfo) &&
                   typeInfo is not null;
        }

        return analysis.TypeIndex.TryGetType(xmlNamespace, xmlTypeName, out typeInfo) &&
               typeInfo is not null;
    }

    private static bool TryAddSelectorTypeRenameEdit(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        XElement element,
        XAttribute attribute,
        AvaloniaTypeInfo targetTypeInfo,
        string oldName,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        if (!string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal) ||
            !string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var selectorReference in SelectorReferenceSemantics.EnumerateReferences(attribute.Value))
        {
            if (selectorReference.Kind != SelectorReferenceKind.Type ||
                string.IsNullOrWhiteSpace(selectorReference.Name) ||
                !XamlClrSymbolResolver.TryResolveTypeInfo(
                    analysis.TypeIndex!,
                    prefixMap,
                    selectorReference.Name,
                    out var selectorTypeInfo) ||
                selectorTypeInfo is null ||
                !string.Equals(selectorTypeInfo.FullTypeName, targetTypeInfo.FullTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryCreateAttributeValueTokenRange(
                    analysis.Document.Text,
                    attribute,
                    selectorReference.Start,
                    selectorReference.Length,
                    out var range))
            {
                continue;
            }

            var renameRange = CreateTypeRenameRange(analysis.Document.Text, range, ReadRangeText(analysis.Document.Text, range));
            AddEdit(builder, seen, renameRange, ComputeTypeReplacement(ReadRangeText(analysis.Document.Text, renameRange), oldName, newName));
            return true;
        }

        return false;
    }

    private static bool TryAddMarkupExtensionTypeRenameEdit(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        XAttribute attribute,
        AvaloniaTypeInfo targetTypeInfo,
        string oldName,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        foreach (var classToken in XamlMarkupExtensionNavigationSemantics.EnumerateClassTokens(attribute.Value))
        {
            if (!XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
                    analysis,
                    prefixMap,
                    classToken.Name,
                    out var resolvedTypeReference) ||
                !string.Equals(resolvedTypeReference.FullTypeName, targetTypeInfo.FullTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryCreateAttributeValueTokenRange(
                    analysis.Document.Text,
                    attribute,
                    classToken.Start,
                    classToken.Length,
                    out var range))
            {
                continue;
            }

            var renameRange = CreateTypeRenameRange(analysis.Document.Text, range, ReadRangeText(analysis.Document.Text, range));
            AddEdit(builder, seen, renameRange, ComputeTypeReplacement(ReadRangeText(analysis.Document.Text, renameRange), oldName, newName));
            return true;
        }

        return false;
    }

    private static bool TryAddSetterPropertyRenameEdit(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        XElement element,
        XAttribute attribute,
        AvaloniaTypeInfo targetOwnerType,
        AvaloniaPropertyInfo targetProperty,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        if (!string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal) ||
            !string.Equals(attribute.Name.LocalName, "Property", StringComparison.Ordinal))
        {
            return false;
        }

        var propertyToken = attribute.Value?.Trim();
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        var ownerTypeToken = ResolveSetterOwnerTypeToken(element);
        var ownerTokenForResolution = propertyToken.Contains('.', StringComparison.Ordinal)
            ? element.Name.LocalName
            : ownerTypeToken;
        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex!,
                prefixMap,
                ownerTokenForResolution,
                propertyToken,
                out var candidateProperty,
                out var candidateOwnerType) ||
            candidateProperty is null ||
            candidateOwnerType is null ||
            !IsSamePropertySymbol(candidateProperty, candidateOwnerType, targetProperty, targetOwnerType))
        {
            return false;
        }

        if (!TryCreateAttributeValueRange(analysis.Document.Text, attribute, out var range))
        {
            return false;
        }

        AddEdit(
            builder,
            seen,
            CreatePropertyRenameRange(analysis.Document.Text, range),
            newName);
        return true;
    }

    private static bool IsPropertyReferenceMatch(
        XamlAnalysisResult analysis,
        XElement element,
        AvaloniaTypeInfo? elementTypeInfo,
        XAttribute attribute,
        AvaloniaTypeInfo targetOwnerType,
        AvaloniaPropertyInfo targetProperty)
    {
        var attributeName = attribute.Name.LocalName;
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return false;
        }

        var separator = attributeName.IndexOf('.');
        if (separator >= 0)
        {
            var ownerToken = separator > 0 ? attributeName.Substring(0, separator) : string.Empty;
            var propertyToken = separator + 1 < attributeName.Length
                ? attributeName.Substring(separator + 1)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(ownerToken) ||
                !string.Equals(propertyToken, targetProperty.Name, StringComparison.Ordinal))
            {
                return false;
            }

            var ownerNamespace = string.IsNullOrWhiteSpace(attribute.Name.NamespaceName)
                ? element.GetDefaultNamespace().NamespaceName
                : attribute.Name.NamespaceName;
            if (!TryResolveTypeInfoByXmlNamespace(analysis, ownerNamespace, ownerToken, out var attributeOwnerType) ||
                attributeOwnerType is null)
            {
                return false;
            }

            var candidateProperty = attributeOwnerType.Properties.FirstOrDefault(property =>
                string.Equals(property.Name, propertyToken, StringComparison.Ordinal));
            if (candidateProperty is null)
            {
                return false;
            }

            return IsSamePropertySymbol(candidateProperty, attributeOwnerType, targetProperty, targetOwnerType);
        }

        if (!string.Equals(attributeName, targetProperty.Name, StringComparison.Ordinal) ||
            elementTypeInfo is null)
        {
            return false;
        }

        var matchedProperty = elementTypeInfo.Properties.FirstOrDefault(property =>
            string.Equals(property.Name, attributeName, StringComparison.Ordinal));
        if (matchedProperty is null)
        {
            return false;
        }

        return IsSamePropertySymbol(matchedProperty, elementTypeInfo, targetProperty, targetOwnerType);
    }

    private static bool ElementMayReferencePropertyByAttribute(XElement element, string propertyName)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var attributeName = attribute.Name.LocalName;
            if (string.Equals(attributeName, propertyName, StringComparison.Ordinal))
            {
                return true;
            }

            var separator = attributeName.LastIndexOf('.');
            if (separator >= 0 &&
                separator + 1 < attributeName.Length &&
                string.Equals(attributeName.Substring(separator + 1), propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveSetterOwnerTypeToken(XElement setterElement)
    {
        for (var current = setterElement.Parent; current is not null; current = current.Parent)
        {
            if (string.Equals(current.Name.LocalName, "Style", StringComparison.Ordinal))
            {
                var selector = current.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.NamespaceName.Length == 0 &&
                        string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal))
                    ?.Value;
                return XamlStyleNavigationSemantics.TryExtractTargetTypeTokenFromSelector(selector, out var styleTypeToken)
                    ? styleTypeToken
                    : null;
            }

            if (string.Equals(current.Name.LocalName, "ControlTheme", StringComparison.Ordinal))
            {
                var targetType = current.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.NamespaceName.Length == 0 &&
                        string.Equals(attribute.Name.LocalName, "TargetType", StringComparison.Ordinal))
                    ?.Value;
                return XamlStyleNavigationSemantics.TryNormalizeControlThemeTargetType(targetType, out var controlThemeTypeToken)
                    ? controlThemeTypeToken
                    : null;
            }
        }

        return null;
    }

    private static bool IsSamePropertySymbol(
        AvaloniaPropertyInfo candidateProperty,
        AvaloniaTypeInfo candidateOwnerType,
        AvaloniaPropertyInfo targetProperty,
        AvaloniaTypeInfo targetOwnerType)
    {
        if (candidateProperty.SourceLocation is { } candidateSource &&
            targetProperty.SourceLocation is { } targetSource)
        {
            return candidateSource.Equals(targetSource);
        }

        if (!string.Equals(candidateProperty.Name, targetProperty.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (targetProperty.IsAttached)
        {
            return string.Equals(
                candidateOwnerType.FullTypeName,
                targetOwnerType.FullTypeName,
                StringComparison.Ordinal);
        }

        if (string.Equals(
                candidateOwnerType.FullTypeName,
                targetOwnerType.FullTypeName,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(candidateProperty.TypeName, targetProperty.TypeName, StringComparison.Ordinal);
    }

    private static bool IsNamedElementDeclarationAttribute(XAttribute attribute)
    {
        if (!string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal))
        {
            return false;
        }

        return attribute.Name.NamespaceName.Length == 0 ||
               string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal);
    }

    private static bool IsResourceKeyDeclarationAttribute(XAttribute attribute)
    {
        return string.Equals(attribute.Name.LocalName, "Key", StringComparison.Ordinal) &&
               string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal);
    }

    private static void AddSelectorStyleClassRenameEdits(
        string sourceText,
        XElement element,
        XAttribute attribute,
        string currentName,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        if (!string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal) ||
            !string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var selectorReference in SelectorReferenceSemantics.EnumerateReferences(attribute.Value))
        {
            if (selectorReference.Kind != SelectorReferenceKind.StyleClass ||
                !string.Equals(selectorReference.Name, currentName, StringComparison.Ordinal) ||
                !TryCreateAttributeValueTokenRange(sourceText, attribute, selectorReference.Start, selectorReference.Length, out var range))
            {
                continue;
            }

            AddEdit(builder, seen, range, newName);
        }
    }

    private static void AddClassesValueRenameEdits(
        string sourceText,
        XAttribute attribute,
        string currentName,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        if (!string.Equals(GetLocalName(attribute.Name.LocalName), "Classes", StringComparison.Ordinal) ||
            !TryCreateAttributeValueRange(sourceText, attribute, out var valueRange))
        {
            return;
        }

        var value = attribute.Value;
        var valueStartOffset = TextCoordinateHelper.GetOffset(sourceText, valueRange.Start);
        if (valueStartOffset < 0)
        {
            return;
        }

        var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            if (index >= value.Length)
            {
                break;
            }

            var start = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            var token = value.Substring(start, index - start);
            if (!string.Equals(token, currentName, StringComparison.Ordinal))
            {
                continue;
            }

            AddEdit(
                builder,
                seen,
                new SourceRange(
                    TextCoordinateHelper.GetPosition(sourceText, valueStartOffset + start),
                    TextCoordinateHelper.GetPosition(sourceText, valueStartOffset + start + token.Length)),
                newName);
        }
    }

    private static void AddClassesPropertyRenameEdits(
        string sourceText,
        XAttribute attribute,
        string currentName,
        string newName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        var localName = GetLocalName(attribute.Name.LocalName);
        const string prefix = "Classes.";
        if (!localName.StartsWith(prefix, StringComparison.Ordinal) ||
            !string.Equals(localName.Substring(prefix.Length), currentName, StringComparison.Ordinal) ||
            !TryCreateAttributeNameRange(sourceText, attribute, out var nameRange))
        {
            return;
        }

        var nameStartOffset = TextCoordinateHelper.GetOffset(sourceText, nameRange.Start);
        if (nameStartOffset < 0)
        {
            return;
        }

        AddEdit(
            builder,
            seen,
            new SourceRange(
                TextCoordinateHelper.GetPosition(sourceText, nameStartOffset + prefix.Length),
                TextCoordinateHelper.GetPosition(sourceText, nameStartOffset + prefix.Length + currentName.Length)),
            newName);
    }

    private static void AddSelectorPseudoClassRenameEdits(
        string sourceText,
        XElement element,
        XAttribute attribute,
        string normalizedCurrentName,
        string trimmedNewName,
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen)
    {
        if (!string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal) ||
            !string.Equals(attribute.Name.LocalName, "Selector", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var selectorReference in SelectorReferenceSemantics.EnumerateReferences(attribute.Value))
        {
            if (selectorReference.Kind != SelectorReferenceKind.PseudoClass ||
                !string.Equals(selectorReference.Name, normalizedCurrentName, StringComparison.Ordinal) ||
                !TryCreateAttributeValueTokenRange(sourceText, attribute, selectorReference.Start, selectorReference.Length, out var range))
            {
                continue;
            }

            AddEdit(builder, seen, range, trimmedNewName);
        }
    }

    private static bool TryCreatePseudoClassDeclarationEdit(
        XamlAnalysisResult analysis,
        XamlSelectorNavigationTarget selectorTarget,
        string trimmedNewName,
        out string declarationUri,
        out XamlDocumentTextEdit declarationEdit)
    {
        declarationUri = string.Empty;
        declarationEdit = default!;

        if (!TryResolvePseudoClassInfo(analysis, selectorTarget, out var pseudoClassInfo) ||
            pseudoClassInfo.SourceLocation is not { } sourceLocation)
        {
            return false;
        }

        var declarationText = LoadDocumentText(sourceLocation.Uri);
        if (declarationText is null)
        {
            return false;
        }

        var range = sourceLocation.Range;
        var currentText = ReadRangeText(declarationText, range);
        if (string.IsNullOrWhiteSpace(currentText))
        {
            return false;
        }

        var replacement = TryComputePseudoClassDeclarationReplacement(currentText, trimmedNewName);
        if (replacement is null)
        {
            return false;
        }

        declarationUri = sourceLocation.Uri;
        declarationEdit = new XamlDocumentTextEdit(range, replacement);
        return true;
    }

    private static bool TryResolvePseudoClassInfo(
        XamlAnalysisResult analysis,
        XamlSelectorNavigationTarget selectorTarget,
        out AvaloniaPseudoClassInfo pseudoClassInfo)
    {
        pseudoClassInfo = default!;
        if (analysis.TypeIndex is null ||
            string.IsNullOrWhiteSpace(selectorTarget.TypeContextToken) ||
            !XamlClrSymbolResolver.TryResolveTypeInfo(
                analysis.TypeIndex,
                analysis.PrefixMap,
                selectorTarget.TypeContextToken,
                out var typeInfo) ||
            typeInfo is null)
        {
            return false;
        }

        foreach (var candidate in typeInfo.PseudoClasses)
        {
            if (!string.Equals(candidate.Name, selectorTarget.Name, StringComparison.Ordinal))
            {
                continue;
            }

            pseudoClassInfo = candidate;
            return true;
        }

        return false;
    }

    private static string? TryComputePseudoClassDeclarationReplacement(string currentText, string trimmedNewName)
    {
        if (string.IsNullOrWhiteSpace(currentText))
        {
            return null;
        }

        var colonPrefixedName = ":" + trimmedNewName;
        if (currentText.Length >= 2 &&
            ((currentText[0] == '"' && currentText[^1] == '"') ||
             (currentText[0] == '\'' && currentText[^1] == '\'')))
        {
            return currentText[0] + colonPrefixedName + currentText[^1].ToString();
        }

        if (string.Equals(currentText, colonPrefixedName, StringComparison.Ordinal) ||
            string.Equals(currentText, trimmedNewName, StringComparison.Ordinal))
        {
            return colonPrefixedName;
        }

        return null;
    }

    private static string? LoadDocumentText(string uri)
    {
        var filePath = UriPathHelper.ToFilePath(uri);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryCreateAttributeValueTokenRange(
        string text,
        XAttribute attribute,
        int tokenOffsetInValue,
        int tokenLength,
        out SourceRange range)
    {
        range = default;
        if (!TryCreateAttributeValueRange(text, attribute, out var valueRange) ||
            tokenOffsetInValue < 0 ||
            tokenLength <= 0)
        {
            return false;
        }

        var valueStartOffset = TextCoordinateHelper.GetOffset(text, valueRange.Start);
        if (valueStartOffset < 0)
        {
            return false;
        }

        var tokenStartOffset = valueStartOffset + tokenOffsetInValue;
        var tokenEndOffset = tokenStartOffset + tokenLength;
        if (tokenStartOffset < 0 || tokenEndOffset > text.Length)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, tokenStartOffset),
            TextCoordinateHelper.GetPosition(text, tokenEndOffset));
        return true;
    }

    private static bool TryCreateAttributeNameRange(string text, XAttribute attribute, out SourceRange range)
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

        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }

        var length = 0;
        while (offset + length < text.Length && IsIdentifierCharacter(text[offset + length]))
        {
            length++;
        }

        if (length <= 0)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, offset),
            TextCoordinateHelper.GetPosition(text, offset + length));
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

        if (quoteIndex >= text.Length || (text[quoteIndex] != '"' && text[quoteIndex] != '\''))
        {
            return false;
        }

        var quote = text[quoteIndex];
        var valueStart = quoteIndex + 1;
        var valueEnd = text.IndexOf(quote, valueStart);
        if (valueEnd < valueStart)
        {
            return false;
        }

        range = new SourceRange(
            TextCoordinateHelper.GetPosition(text, valueStart),
            TextCoordinateHelper.GetPosition(text, valueEnd));
        return true;
    }

    private static string ComputeTypeReplacement(string currentOccurrenceName, string oldName, string newName)
    {
        if (string.Equals(currentOccurrenceName, oldName, StringComparison.Ordinal))
        {
            return newName;
        }

        var trimmedOld = oldName.EndsWith("Extension", StringComparison.Ordinal)
            ? oldName.Substring(0, oldName.Length - "Extension".Length)
            : oldName;
        if (!string.Equals(currentOccurrenceName, trimmedOld, StringComparison.Ordinal))
        {
            return newName;
        }

        return newName.EndsWith("Extension", StringComparison.Ordinal)
            ? newName.Substring(0, newName.Length - "Extension".Length)
            : newName;
    }

    private static bool ContainsPosition(SourceRange range, SourcePosition position)
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

    private static Document? FindDocumentByFilePath(Solution solution, string filePath)
    {
        foreach (var documentId in solution.GetDocumentIdsWithFilePath(filePath))
        {
            var document = solution.GetDocument(documentId);
            if (document is not null)
            {
                return document;
            }
        }

        return null;
    }

    private static void MergeDocumentEdits(
        ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Builder changesBuilder,
        string uri,
        ImmutableArray<XamlDocumentTextEdit> edits)
    {
        if (edits.IsDefaultOrEmpty)
        {
            return;
        }

        if (changesBuilder.TryGetValue(uri, out var existing))
        {
            changesBuilder[uri] = SortEdits(existing.AddRange(edits));
            return;
        }

        changesBuilder[uri] = SortEdits(edits);
    }

    private static ImmutableArray<XamlDocumentTextEdit> SortEdits(ImmutableArray<XamlDocumentTextEdit> edits)
    {
        return edits
            .OrderByDescending(static edit => edit.Range.Start.Line)
            .ThenByDescending(static edit => edit.Range.Start.Character)
            .ToImmutableArray();
    }

    private static void AddEdit(
        ImmutableArray<XamlDocumentTextEdit>.Builder builder,
        HashSet<string> seen,
        SourceRange range,
        string newText)
    {
        var identity = range.Start.Line + ":" + range.Start.Character + "-" +
                       range.End.Line + ":" + range.End.Character;
        if (!seen.Add(identity))
        {
            return;
        }

        builder.Add(new XamlDocumentTextEdit(range, newText));
    }

    private static bool IsXamlDocument(string uri)
    {
        return uri.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
               uri.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);
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

    private readonly record struct RenameTarget(
        RenameTargetKind Kind,
        SourceRange Range,
        string CurrentName,
        ISymbol? Symbol,
        string? TypeFullName);

    private readonly record struct RoslynRenameTarget(
        RenameTargetKind Kind,
        SourceRange Range,
        string CurrentName,
        ISymbol Symbol);

    private readonly record struct XamlClrRenameTarget(
        RenameTargetKind Kind,
        string TypeFullName,
        string CurrentName);

    private enum RenameTargetKind
    {
        None = 0,
        NamedElement,
        ResourceKey,
        StyleClass,
        PseudoClass,
        ClrType,
        ClrProperty
    }
}
