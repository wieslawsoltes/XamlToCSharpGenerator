using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.NoUi.Emission;

public abstract class PilotCodeEmitterBase : IXamlFrameworkEmitter
{
    private static readonly GeneratedSourceHintNameService GeneratedSourceHintNameService = new();
    private static readonly CSharpLiteralEmissionService CSharpLiteralEmissionService = new();

    protected abstract string HintPrefix { get; }
    protected abstract string PublicBuildMethodName { get; }
    protected abstract string PrivateBuildMethodName { get; }

    public (string HintName, string Source) Emit(ResolvedViewModel viewModel)
    {
        var document = viewModel.Document;
        var builder = new StringBuilder(capacity: 4096);

        if (document.IsClassBacked)
        {
            EmitClassBackedView(builder, viewModel);
        }
        else
        {
            EmitClasslessArtifact(builder, viewModel);
        }

        return (BuildHintName(document), builder.ToString());
    }

    private void EmitClassBackedView(StringBuilder builder, ResolvedViewModel viewModel)
    {
        var document = viewModel.Document;
        if (!string.IsNullOrWhiteSpace(document.ClassNamespace))
        {
            builder.Append("namespace ")
                .Append(document.ClassNamespace)
                .AppendLine(";");
            builder.AppendLine();
        }

        builder.Append(viewModel.ClassModifier)
            .Append(" partial class ")
            .Append(document.ClassName)
            .AppendLine();
        builder.AppendLine("{");
        builder.Append("    public global::XamlToCSharpGenerator.NoUi.NoUiObjectNode ")
            .Append(PublicBuildMethodName)
            .AppendLine("()");
        builder.AppendLine("    {");
        builder.Append("        return ")
            .Append(PrivateBuildMethodName)
            .AppendLine("();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public void InitializeComponent(bool loadXaml = true)");
        builder.AppendLine("    {");
        builder.AppendLine("        _ = loadXaml;");
        builder.Append("        _ = ")
            .Append(PrivateBuildMethodName)
            .AppendLine("();");
        builder.AppendLine("    }");
        builder.AppendLine();

        EmitBuildMethod(builder, viewModel, "private static");

        builder.AppendLine("}");
    }

    private void EmitClasslessArtifact(StringBuilder builder, ResolvedViewModel viewModel)
    {
        builder.AppendLine("namespace XamlToCSharpGenerator.Generated;");
        builder.AppendLine();
        builder.Append("public static class ")
            .Append(viewModel.Document.ClassName)
            .AppendLine();
        builder.AppendLine("{");
        EmitBuildMethod(builder, viewModel, "public static");
        builder.AppendLine("}");
    }

    private void EmitBuildMethod(StringBuilder builder, ResolvedViewModel viewModel, string methodModifier)
    {
        builder.Append("    ")
            .Append(methodModifier)
            .Append(" global::XamlToCSharpGenerator.NoUi.NoUiObjectNode ")
            .Append(PrivateBuildMethodName)
            .AppendLine("()");
        builder.AppendLine("    {");
        var nodeIndex = 0;
        var rootVariable = EmitNode(builder, viewModel.RootObject, indentLevel: 2, ref nodeIndex);
        builder.Append("        return ")
            .Append(rootVariable)
            .AppendLine(";");
        builder.AppendLine("    }");
    }

    private static string EmitNode(
        StringBuilder builder,
        ResolvedObjectNode node,
        int indentLevel,
        ref int nodeIndex)
    {
        var variableName = "__n" + nodeIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        nodeIndex++;
            AppendIndentedLine(
                builder,
                indentLevel,
                "var " + variableName + " = new global::XamlToCSharpGenerator.NoUi.NoUiObjectNode(\"" +
            CSharpLiteralEmissionService.EscapeStringLiteral(node.TypeName) + "\");");

        foreach (var propertyAssignment in node.PropertyAssignments)
        {
            AppendIndentedLine(
                builder,
                indentLevel,
                variableName + ".Properties.Add(new global::XamlToCSharpGenerator.NoUi.NoUiPropertyAssignment(\"" +
                CSharpLiteralEmissionService.EscapeStringLiteral(propertyAssignment.PropertyName) + "\", \"" +
                CSharpLiteralEmissionService.EscapeStringLiteral(propertyAssignment.ValueExpression) + "\"));");
        }

        foreach (var propertyElement in node.PropertyElementAssignments)
        {
            AppendIndentedLine(
                builder,
                indentLevel,
                variableName + ".Properties.Add(new global::XamlToCSharpGenerator.NoUi.NoUiPropertyAssignment(\"" +
                CSharpLiteralEmissionService.EscapeStringLiteral(propertyElement.PropertyName + "#objects") + "\", \"" +
                propertyElement.ObjectValues.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"));");
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                var propertyObjectVariable = EmitNode(builder, objectValue, indentLevel, ref nodeIndex);
                AppendIndentedLine(builder, indentLevel, variableName + ".Children.Add(" + propertyObjectVariable + ");");
            }
        }

        foreach (var child in node.Children)
        {
            var childVariableName = EmitNode(builder, child, indentLevel, ref nodeIndex);
            AppendIndentedLine(builder, indentLevel, variableName + ".Children.Add(" + childVariableName + ");");
        }

        return variableName;
    }

    private static void AppendIndentedLine(StringBuilder builder, int indentLevel, string line)
    {
        for (var i = 0; i < indentLevel; i++)
        {
            builder.Append("    ");
        }

        builder.AppendLine(line);
    }

    private string BuildHintName(XamlDocumentModel document)
    {
        var classToken = document.ClassName.Replace('.', '_');
        return GeneratedSourceHintNameService.BuildHintName(HintPrefix + "." + classToken, document.TargetPath, ignoreCaseHash: true);
    }
}
