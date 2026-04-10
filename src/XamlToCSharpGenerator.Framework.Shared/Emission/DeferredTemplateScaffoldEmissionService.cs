using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class DeferredTemplateScaffoldEmissionService
{
    private readonly IXamlFrameworkDeferredTemplateEmitterAdapter _adapter;

    public DeferredTemplateScaffoldEmissionService(IXamlFrameworkDeferredTemplateEmitterAdapter adapter)
    {
        _adapter = adapter;
    }

    public bool TryEmitTemplateContentFactory(
        ResolvedObjectNode node,
        string variableName,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        EmitObjectNodeFromSharedContextDelegate emitNode)
    {
        if (!_adapter.IsDeferredTemplateNode(node) ||
            string.IsNullOrWhiteSpace(node.ContentPropertyName) ||
            node.Children.IsDefaultOrEmpty)
        {
            return false;
        }

        var templateRootNode = node.Children[0];
        var factoryParameterName = "__templateServiceProvider";
        var templateNameScopeReference = "__templateScope";
        var deferredTemplateServiceProviderReference = "__deferredTemplateServiceProvider" + nodeCounter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.SourceBuilder.AppendLine(
            context.Indent +
            variableName +
            "." +
            node.ContentPropertyName +
            " = (global::System.Func<global::System.IServiceProvider?, object?>)(" +
            factoryParameterName +
            " =>");
        context.SourceBuilder.AppendLine(context.Indent + "{");

        var nestedIndent = context.Indent + "    ";
        context.SourceBuilder.AppendLine(
            nestedIndent +
            "var " +
            templateNameScopeReference +
            " = " +
            _adapter.BuildCreateTemplateNameScopeExpression(factoryParameterName) +
            ";");
        context.SourceBuilder.AppendLine(
            nestedIndent +
            "var " +
            deferredTemplateServiceProviderReference +
            " = " +
            _adapter.BuildCreateDeferredTemplateServiceProviderExpression(
                factoryParameterName,
                context.RootReference,
                templateNameScopeReference) +
            ";");

        var templateContext = context with
        {
            Indent = nestedIndent,
            EmitNameScopeRegistration = true,
            NameScopeReference = templateNameScopeReference,
            ServiceProviderReference = deferredTemplateServiceProviderReference,
            ParentStackReferences = context.ParentStackReferences
        };

        var templateRootReference = emitNode(
            templateRootNode,
            templateContext,
            ref nodeCounter,
            existingVariableName: null,
            topDownAttachmentTemplate: null,
            completeNameScopeOnNodeCompletion: true);

        foreach (var statement in _adapter.EmitTemplateRootNameScopeStatements(
                     templateRootReference,
                     templateNameScopeReference,
                     nodeCounter))
        {
            templateContext.SourceBuilder.AppendLine(nestedIndent + statement);
        }

        templateContext.SourceBuilder.AppendLine(
            nestedIndent +
            "return " +
            _adapter.BuildDeferredTemplateResultExpression(
                templateRootReference,
                templateNameScopeReference) +
            ";");
        templateContext.SourceBuilder.AppendLine(context.Indent + "});");
        return true;
    }
}
