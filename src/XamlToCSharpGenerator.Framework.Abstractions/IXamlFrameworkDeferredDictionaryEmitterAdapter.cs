using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkDeferredDictionaryEmitterAdapter
{
    string NormalizeDictionaryKeyExpression(string propertyName, string keyExpression);

    bool ShouldApplyMergedResourceInclude(ResolvedObjectNode node);

    bool ShouldApplyStyleInclude(ResolvedObjectNode node);

    string BuildCreateDeferredServiceProviderExpression(
        string serviceProviderReference,
        string rootReference,
        string targetObjectReference,
        string targetPropertyExpression,
        string baseUriExpression,
        string parentStackExpression);

    string BuildDictionaryAddStatement(
        string dictionaryReference,
        string keyExpression,
        string valueExpression,
        string documentUriExpression,
        bool isShared = true);
}
