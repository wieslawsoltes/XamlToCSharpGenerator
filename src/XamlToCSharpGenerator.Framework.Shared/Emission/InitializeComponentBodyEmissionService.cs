using System;
using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class InitializeComponentBodyEmissionService
{
    public delegate void EmitHotReloadRegistrationsDelegate(
        StringBuilder sourceBuilder,
        FrameworkHotReloadScaffoldContext hotReloadScaffoldContext,
        string selfExpression);

    public delegate string SanitizeIdentifierDelegate(string identifier);
    public delegate string EscapeDelegate(string value);

    private readonly EmitHotReloadRegistrationsDelegate _emitHotReloadRegistrations;
    private readonly SanitizeIdentifierDelegate _sanitizeIdentifier;
    private readonly EscapeDelegate _escape;

    public InitializeComponentBodyEmissionService(
        EmitHotReloadRegistrationsDelegate emitHotReloadRegistrations,
        SanitizeIdentifierDelegate sanitizeIdentifier,
        EscapeDelegate escape)
    {
        _emitHotReloadRegistrations = emitHotReloadRegistrations ?? throw new ArgumentNullException(nameof(emitHotReloadRegistrations));
        _sanitizeIdentifier = sanitizeIdentifier ?? throw new ArgumentNullException(nameof(sanitizeIdentifier));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    public void Emit(
        StringBuilder sourceBuilder,
        ResolvedViewModel viewModel,
        string selfExpression,
        string serviceProviderExpression,
        FrameworkHotReloadScaffoldContext hotReloadScaffoldContext)
    {
        sourceBuilder.AppendLine("            var __loadedWithSourceGen = false;");
        sourceBuilder.AppendLine("            if (loadXaml)");
        sourceBuilder.AppendLine("            {");
        if (viewModel.HasXBind)
        {
            sourceBuilder.AppendLine(
                $"                global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ResetXBind({selfExpression});");
        }

        sourceBuilder.AppendLine(
            $"                __PopulateGeneratedObjectGraph({selfExpression}, {serviceProviderExpression});");
        sourceBuilder.AppendLine("                __loadedWithSourceGen = true;");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("            if (!__loadedWithSourceGen)");
        sourceBuilder.AppendLine("            {");
        foreach (var namedElement in viewModel.NamedElements)
        {
            sourceBuilder.AppendLine(
                $"                {selfExpression}.{_sanitizeIdentifier(namedElement.Name)} = ({namedElement.TypeName})global::XamlToCSharpGenerator.Runtime.SourceGenNameReferenceHelper.ResolveByName({selfExpression}, \"{_escape(namedElement.Name)}\")!;");
        }

        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine();

        if (viewModel.EnableHotReload || viewModel.EnableHotDesign)
        {
            _emitHotReloadRegistrations(sourceBuilder, hotReloadScaffoldContext, selfExpression);
        }
    }
}
