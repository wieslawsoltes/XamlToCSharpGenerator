using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class UnsupportedFeatureDiagnosticsService
{
    public sealed record SupportedFeatures(
        bool DocumentResources,
        bool DocumentTemplates,
        bool DocumentStyles,
        bool DocumentControlThemes,
        bool DocumentIncludes,
        bool MarkupExtensions,
        bool CompiledBindingScopes,
        bool CreateSourceInfo,
        bool HotReload,
        bool HotDesign);

    private readonly string _frameworkDisplayName;
    private readonly SupportedFeatures _supportedFeatures;
    private readonly MarkupExpressionParser _markupExpressionParser = new();

    public UnsupportedFeatureDiagnosticsService(
        string frameworkDisplayName,
        SupportedFeatures supportedFeatures)
    {
        _frameworkDisplayName = string.IsNullOrWhiteSpace(frameworkDisplayName)
            ? "Framework"
            : frameworkDisplayName.Trim();
        _supportedFeatures = supportedFeatures ?? throw new ArgumentNullException(nameof(supportedFeatures));
    }

    public void ReportUnsupportedDocumentAndOptionFeatures(
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (!_supportedFeatures.DocumentResources && !document.Resources.IsDefaultOrEmpty)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "document resources");
        }

        if (!_supportedFeatures.DocumentTemplates && !document.Templates.IsDefaultOrEmpty)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "document templates");
        }

        if (!_supportedFeatures.DocumentStyles && !document.Styles.IsDefaultOrEmpty)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "document styles");
        }

        if (!_supportedFeatures.DocumentControlThemes && !document.ControlThemes.IsDefaultOrEmpty)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "control themes");
        }

        if (!_supportedFeatures.DocumentIncludes && !document.Includes.IsDefaultOrEmpty)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "document includes");
        }

        if (!_supportedFeatures.CompiledBindingScopes)
        {
            foreach (var node in EnumerateObjectNodes(document.RootObject))
            {
                if (!string.IsNullOrWhiteSpace(node.DataType))
                {
                    AddDiagnostic(diagnostics, document, node.Line, node.Column, "x:DataType scope directives");
                    break;
                }
            }

            foreach (var node in EnumerateObjectNodes(document.RootObject))
            {
                if (node.CompileBindings.HasValue)
                {
                    AddDiagnostic(diagnostics, document, node.Line, node.Column, "x:CompileBindings directives");
                    break;
                }
            }
        }

        if (!_supportedFeatures.CompiledBindingScopes && options.UseCompiledBindingsByDefault)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "compiled bindings by default");
        }

        if (!_supportedFeatures.CreateSourceInfo && options.CreateSourceInfo)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "source info emission");
        }

        if (!_supportedFeatures.HotReload && (options.HotReloadEnabled || options.IdeHotReloadEnabled))
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "hot reload");
        }

        if (!_supportedFeatures.HotDesign && options.HotDesignEnabled)
        {
            AddDiagnostic(diagnostics, document, document.RootObject.Line, document.RootObject.Column, "hot design");
        }
    }

    public void ReportUnsupportedPropertyMarkupExtension(
        XamlPropertyAssignment property,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (_supportedFeatures.MarkupExtensions ||
            string.IsNullOrWhiteSpace(property.Value) ||
            !_markupExpressionParser.TryParseMarkupExtension(property.Value, out var markupExtension))
        {
            return;
        }

        AddDiagnostic(
            diagnostics,
            document,
            property.Line,
            property.Column,
            "markup extension '" + markupExtension.Name + "'");
    }

    private void AddDiagnostic(
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        int line,
        int column,
        string featureDescription)
    {
        diagnostics.Add(new DiagnosticInfo(
            "AXSG0101",
            _frameworkDisplayName + " does not support " + featureDescription + ".",
            document.FilePath,
            line,
            column,
            false));
    }

    private static IEnumerable<XamlObjectNode> EnumerateObjectNodes(XamlObjectNode node)
    {
        yield return node;

        foreach (var constructorArgument in node.ConstructorArguments)
        {
            foreach (var descendant in EnumerateObjectNodes(constructorArgument))
            {
                yield return descendant;
            }
        }

        foreach (var child in node.ChildObjects)
        {
            foreach (var descendant in EnumerateObjectNodes(child))
            {
                yield return descendant;
            }
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var valueNode in propertyElement.ObjectValues)
            {
                foreach (var descendant in EnumerateObjectNodes(valueNode))
                {
                    yield return descendant;
                }
            }
        }
    }
}
