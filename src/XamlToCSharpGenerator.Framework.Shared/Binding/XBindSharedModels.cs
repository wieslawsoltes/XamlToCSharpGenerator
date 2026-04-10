using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public enum XBindSourceReferenceKind
{
    DataContext = 0,
    Root = 1,
    Target = 2,
    ElementName = 3,
    TemplatedParent = 4,
    FindAncestor = 5,
    ExplicitSource = 6
}

public readonly record struct XBindPathReference(
    XBindSourceReferenceKind Kind,
    string Path,
    string? ElementName,
    string? RelativeSourceExpression,
    string? SourceExpression);

public readonly record struct ResolvedXBindSourceConfiguration(
    INamedTypeSymbol SourceType,
    XBindPathReference SourceReference);

public readonly record struct XBindLoweredExpression(
    string Expression,
    bool IsTypeReference);

public sealed class XBindLoweringContext
{
    public XBindLoweringContext(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        XBindPathReference defaultSourceReference)
    {
        Compilation = compilation;
        Document = document;
        CurrentNode = currentNode;
        SourceType = sourceType;
        RootType = rootType;
        TargetType = targetType;
        DefaultSourceReference = defaultSourceReference;
    }

    public Compilation Compilation { get; }

    public XamlDocumentModel Document { get; }

    public XamlObjectNode CurrentNode { get; }

    public INamedTypeSymbol SourceType { get; }

    public INamedTypeSymbol RootType { get; }

    public INamedTypeSymbol? TargetType { get; }

    public XBindPathReference DefaultSourceReference { get; }
}

public enum CSharpShorthandResolutionKind
{
    None = 0,
    BindingPath = 1,
    RootExpression = 2,
    Conflict = 3
}

public readonly record struct CSharpShorthandResolutionResult(
    CSharpShorthandResolutionKind Kind,
    string? Path,
    string? ValueExpression,
    string? AccessorExpression,
    string? SourceTypeName,
    string? ResultTypeName,
    string? DiagnosticId,
    string? DiagnosticMessage);
