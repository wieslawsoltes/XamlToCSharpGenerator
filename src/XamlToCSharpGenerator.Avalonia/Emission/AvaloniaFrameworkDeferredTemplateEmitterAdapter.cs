using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkDeferredTemplateEmitterAdapter : IXamlFrameworkDeferredTemplateEmitterAdapter
{
    public static AvaloniaFrameworkDeferredTemplateEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkDeferredTemplateEmitterAdapter()
    {
    }

    public bool IsDeferredTemplateNode(ResolvedObjectNode node)
    {
        return node.HasSemantic(ResolvedObjectNodeSemanticFlags.CanBeDeferredResource) &&
               !node.HasSemantic(ResolvedObjectNodeSemanticFlags.IsResourceDictionary) &&
               !string.IsNullOrWhiteSpace(node.ContentPropertyName);
    }

    public string BuildCreateDeferredTemplateServiceProviderExpression(
        string parentServiceProviderReference,
        string rootReference,
        string nameScopeReference)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenDeferredServiceProviderFactory.CreateDeferredTemplateServiceProvider(" +
               parentServiceProviderReference + ", " +
               rootReference + ", " +
               nameScopeReference + ")";
    }

    public string BuildCreateTemplateNameScopeExpression(string serviceProviderReference)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenDeferredServiceProviderFactory.CreateTemplateNameScope(" +
               serviceProviderReference + ")";
    }

    public string BuildDeferredTemplateResultExpression(string templateRootReference, string nameScopeReference)
    {
        return "new global::Avalonia.Controls.Templates.TemplateResult<global::Avalonia.Controls.Control>(" +
               "(global::Avalonia.Controls.Control)" + templateRootReference + ", " +
               nameScopeReference + ")";
    }

    public ImmutableArray<string> EmitTemplateRootNameScopeStatements(
        string nodeReference,
        string nameScopeReference,
        int scopedIndex)
    {
        return ImmutableArray.Create(
            "if ((object)" + nodeReference + " is global::Avalonia.StyledElement __templateStyledElement" + scopedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") __AXSGObjectGraph.TrySetNameScope(__templateStyledElement" + scopedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + nameScopeReference + ");");
    }
}
