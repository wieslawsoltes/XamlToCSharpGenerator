using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkDeferredTemplateEmitterAdapter
{
    bool IsDeferredTemplateNode(ResolvedObjectNode node);

    string BuildCreateDeferredTemplateServiceProviderExpression(
        string parentServiceProviderReference,
        string rootReference,
        string nameScopeReference);

    string BuildCreateTemplateNameScopeExpression(string serviceProviderReference);

    string BuildDeferredTemplateResultExpression(string templateRootReference, string nameScopeReference);

    ImmutableArray<string> EmitTemplateRootNameScopeStatements(
        string nodeReference,
        string nameScopeReference,
        int scopedIndex);
}
