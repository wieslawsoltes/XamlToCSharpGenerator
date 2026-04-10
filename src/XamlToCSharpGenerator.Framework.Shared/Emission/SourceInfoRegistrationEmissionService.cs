using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class SourceInfoRegistrationEmissionService
{
    private readonly Func<string, string> _escape;

    public SourceInfoRegistrationEmissionService(Func<string, string> escape)
    {
        _escape = escape;
    }

    public void EmitRegistrations(
        ResolvedViewModel viewModel,
        StringBuilder sourceBuilder,
        string escapedUri)
    {
        EmitRegistration(sourceBuilder, escapedUri, "Root", viewModel.Document.ClassName, viewModel.Document.FilePath, viewModel.RootObject.Line, viewModel.RootObject.Column);

        for (var index = 0; index < viewModel.NamedElements.Length; index++)
        {
            var namedElement = viewModel.NamedElements[index];
            EmitRegistration(sourceBuilder, escapedUri, "Name", namedElement.Name, viewModel.Document.FilePath, namedElement.Line, namedElement.Column);
            EmitRegistration(
                sourceBuilder,
                escapedUri,
                "NamedElement",
                "NamedElement:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + namedElement.Name,
                viewModel.Document.FilePath,
                namedElement.Line,
                namedElement.Column);
        }

        for (var index = 0; index < viewModel.Resources.Length; index++)
        {
            var resource = viewModel.Resources[index];
            EmitRegistration(sourceBuilder, escapedUri, "Resource", resource.Key, viewModel.Document.FilePath, resource.Line, resource.Column);
        }

        for (var index = 0; index < viewModel.Templates.Length; index++)
        {
            var template = viewModel.Templates[index];
            EmitRegistration(sourceBuilder, escapedUri, "Template", template.Key ?? template.Kind, viewModel.Document.FilePath, template.Line, template.Column);
        }

        for (var index = 0; index < viewModel.Styles.Length; index++)
        {
            var style = viewModel.Styles[index];
            EmitRegistration(sourceBuilder, escapedUri, "Style", style.Key ?? style.Selector, viewModel.Document.FilePath, style.Line, style.Column);
            EmitStyleSetterRegistrations(sourceBuilder, escapedUri, viewModel.Document.FilePath, style, index);
        }

        for (var index = 0; index < viewModel.ControlThemes.Length; index++)
        {
            var controlTheme = viewModel.ControlThemes[index];
            EmitRegistration(sourceBuilder, escapedUri, "ControlTheme", controlTheme.Key ?? controlTheme.TargetTypeName ?? "Theme", viewModel.Document.FilePath, controlTheme.Line, controlTheme.Column);
            EmitControlThemeSetterRegistrations(sourceBuilder, escapedUri, viewModel.Document.FilePath, controlTheme, index);
        }

        for (var index = 0; index < viewModel.Includes.Length; index++)
        {
            var include = viewModel.Includes[index];
            EmitRegistration(sourceBuilder, escapedUri, "Include", include.Source, viewModel.Document.FilePath, include.Line, include.Column);
        }

        EmitObjectSourceInfoRegistrations(viewModel.RootObject, sourceBuilder, escapedUri, viewModel.Document.FilePath);
        EmitCompatibilityObjectSourceInfoRegistrations(
            viewModel.RootObject,
            sourceBuilder,
            escapedUri,
            viewModel.Document.FilePath,
            "Object:0");
    }

    private void EmitObjectSourceInfoRegistrations(
        ResolvedObjectNode node,
        StringBuilder sourceBuilder,
        string escapedUri,
        string filePath)
    {
        EmitRegistration(sourceBuilder, escapedUri, "Object", node.TypeName, filePath, node.Line, node.Column);

        for (var assignmentIndex = 0; assignmentIndex < node.PropertyAssignments.Length; assignmentIndex++)
        {
            var propertyAssignment = node.PropertyAssignments[assignmentIndex];
            EmitRegistration(sourceBuilder, escapedUri, "Property", propertyAssignment.PropertyName, filePath, propertyAssignment.Line, propertyAssignment.Column);
        }

        for (var propertyElementIndex = 0; propertyElementIndex < node.PropertyElementAssignments.Length; propertyElementIndex++)
        {
            var propertyElement = node.PropertyElementAssignments[propertyElementIndex];
            EmitRegistration(sourceBuilder, escapedUri, "PropertyElement", propertyElement.PropertyName, filePath, propertyElement.Line, propertyElement.Column);
            for (var valueIndex = 0; valueIndex < propertyElement.ObjectValues.Length; valueIndex++)
            {
                EmitObjectSourceInfoRegistrations(propertyElement.ObjectValues[valueIndex], sourceBuilder, escapedUri, filePath);
            }
        }

        for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
        {
            EmitObjectSourceInfoRegistrations(node.Children[childIndex], sourceBuilder, escapedUri, filePath);
        }
    }

    private void EmitCompatibilityObjectSourceInfoRegistrations(
        ResolvedObjectNode node,
        StringBuilder sourceBuilder,
        string escapedUri,
        string filePath,
        string objectPath)
    {
        for (var assignmentIndex = 0; assignmentIndex < node.PropertyAssignments.Length; assignmentIndex++)
        {
            var propertyAssignment = node.PropertyAssignments[assignmentIndex];
            EmitRegistration(
                sourceBuilder,
                escapedUri,
                "Property",
                objectPath +
                "/Property:" +
                assignmentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                propertyAssignment.PropertyName,
                filePath,
                propertyAssignment.Line,
                propertyAssignment.Column);
        }

        for (var eventIndex = 0; eventIndex < node.EventSubscriptions.Length; eventIndex++)
        {
            var eventSubscription = node.EventSubscriptions[eventIndex];
            EmitRegistration(
                sourceBuilder,
                escapedUri,
                "Event",
                objectPath +
                "/Event:" +
                eventIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                eventSubscription.EventName,
                filePath,
                eventSubscription.Line,
                eventSubscription.Column);
        }

        for (var propertyElementIndex = 0; propertyElementIndex < node.PropertyElementAssignments.Length; propertyElementIndex++)
        {
            var propertyElement = node.PropertyElementAssignments[propertyElementIndex];
            var propertyElementPath =
                objectPath +
                "/PropertyElement:" +
                propertyElementIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                propertyElement.PropertyName;
            EmitRegistration(
                sourceBuilder,
                escapedUri,
                "PropertyElement",
                propertyElementPath,
                filePath,
                propertyElement.Line,
                propertyElement.Column);

            for (var valueIndex = 0; valueIndex < propertyElement.ObjectValues.Length; valueIndex++)
            {
                EmitCompatibilityObjectSourceInfoRegistrations(
                    propertyElement.ObjectValues[valueIndex],
                    sourceBuilder,
                    escapedUri,
                    filePath,
                    propertyElementPath +
                    "/Object:" +
                    valueIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
        {
            EmitCompatibilityObjectSourceInfoRegistrations(
                node.Children[childIndex],
                sourceBuilder,
                escapedUri,
                filePath,
                objectPath +
                "/Child:" +
                childIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private void EmitStyleSetterRegistrations(
        StringBuilder sourceBuilder,
        string escapedUri,
        string filePath,
        ResolvedStyleDefinition style,
        int styleIndex)
    {
        var styleIdentity = style.Key ?? style.Selector;
        for (var setterIndex = 0; setterIndex < style.Setters.Length; setterIndex++)
        {
            var setter = style.Setters[setterIndex];
            EmitRegistration(
                sourceBuilder,
                escapedUri,
                "StyleSetter",
                "Style:" +
                styleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                styleIdentity +
                "/Setter:" +
                setterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                setter.PropertyName,
                filePath,
                setter.Line,
                setter.Column);
        }
    }

    private void EmitControlThemeSetterRegistrations(
        StringBuilder sourceBuilder,
        string escapedUri,
        string filePath,
        ResolvedControlThemeDefinition controlTheme,
        int controlThemeIndex)
    {
        var themeIdentity = controlTheme.Key ?? controlTheme.TargetTypeName ?? "Theme";
        for (var setterIndex = 0; setterIndex < controlTheme.Setters.Length; setterIndex++)
        {
            var setter = controlTheme.Setters[setterIndex];
            EmitRegistration(
                sourceBuilder,
                escapedUri,
                "ControlThemeSetter",
                "ControlTheme:" +
                controlThemeIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                themeIdentity +
                "/Setter:" +
                setterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                setter.PropertyName,
                filePath,
                setter.Line,
                setter.Column);
        }
    }

    private void EmitRegistration(
        StringBuilder sourceBuilder,
        string escapedUri,
        string kind,
        string name,
        string filePath,
        int line,
        int column)
    {
        sourceBuilder.AppendLine(
            $"            global::XamlToCSharpGenerator.Runtime.XamlSourceInfoRegistry.Register(\"{escapedUri}\", \"{_escape(kind)}\", \"{_escape(name)}\", \"{_escape(filePath)}\", {line.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {column.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
    }
}
