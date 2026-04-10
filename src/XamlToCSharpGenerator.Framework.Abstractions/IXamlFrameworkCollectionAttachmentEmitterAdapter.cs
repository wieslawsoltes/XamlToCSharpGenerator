using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkCollectionAttachmentEmitterAdapter
{
    bool ShouldApplyMergedResourceInclude(ResolvedObjectNode node);

    bool ShouldApplyStyleInclude(ResolvedObjectNode node);

    bool TryBuildSpecialChildAttachmentStatement(
        ResolvedObjectNode parentNode,
        string parentReference,
        ResolvedObjectNode childNode,
        string valueExpression,
        ResolvedCollectionAddInstruction? instruction,
        out string statement);

    string BuildApplyMergedResourceIncludeStatement(
        string ownerDictionaryReference,
        string includeValueExpression,
        string documentUriExpression);

    string BuildApplyStyleIncludeStatement(
        string targetCollectionReference,
        string ownerContextReference,
        string includeValueExpression,
        string documentUriExpression);

    string BuildCollectionAddStatement(
        string collectionReference,
        string valueExpression,
        ResolvedCollectionAddInstruction? instruction);
}
