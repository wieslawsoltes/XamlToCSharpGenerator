using System;
using System.Globalization;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.NoUi.Emission;

public sealed class NoUiCodeEmitter : IXamlFrameworkEmitter
{
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

    private static void EmitClassBackedView(StringBuilder builder, ResolvedViewModel viewModel)
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
        builder.AppendLine("    public global::XamlToCSharpGenerator.NoUi.NoUiObjectNode BuildNoUiObjectGraph()");
        builder.AppendLine("    {");
        builder.AppendLine("        return __BuildNoUiObjectGraph();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public void InitializeComponent(bool loadXaml = true)");
        builder.AppendLine("    {");
        builder.AppendLine("        _ = loadXaml;");
        builder.AppendLine("        _ = __BuildNoUiObjectGraph();");
        builder.AppendLine("    }");
        builder.AppendLine();

        EmitBuildMethod(builder, viewModel, "private static");

        builder.AppendLine("}");
    }

    private static void EmitClasslessArtifact(StringBuilder builder, ResolvedViewModel viewModel)
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

    private static void EmitBuildMethod(StringBuilder builder, ResolvedViewModel viewModel, string methodModifier)
    {
        builder.Append("    ")
            .Append(methodModifier)
            .AppendLine(" global::XamlToCSharpGenerator.NoUi.NoUiObjectNode __BuildNoUiObjectGraph()");
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
        var variableName = "__n" + nodeIndex.ToString(CultureInfo.InvariantCulture);
        nodeIndex++;
        AppendIndentedLine(
            builder,
            indentLevel,
            "var " + variableName + " = new global::XamlToCSharpGenerator.NoUi.NoUiObjectNode(\"" +
            EscapeStringLiteral(node.TypeName) + "\");");

        foreach (var propertyAssignment in node.PropertyAssignments)
        {
            AppendIndentedLine(
                builder,
                indentLevel,
                variableName + ".Properties.Add(new global::XamlToCSharpGenerator.NoUi.NoUiPropertyAssignment(\"" +
                EscapeStringLiteral(propertyAssignment.PropertyName) + "\", \"" +
                EscapeStringLiteral(propertyAssignment.ValueExpression) + "\"));");
        }

        foreach (var propertyElement in node.PropertyElementAssignments)
        {
            AppendIndentedLine(
                builder,
                indentLevel,
                variableName + ".Properties.Add(new global::XamlToCSharpGenerator.NoUi.NoUiPropertyAssignment(\"" +
                EscapeStringLiteral(propertyElement.PropertyName + "#objects") + "\", \"" +
                propertyElement.ObjectValues.Length.ToString(CultureInfo.InvariantCulture) + "\"));");
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

    private static string EscapeStringLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '"':
                    escaped.Append("\\\"");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                default:
                    escaped.Append(ch);
                    break;
            }
        }

        return escaped.ToString();
    }

    private static string BuildHintName(XamlDocumentModel document)
    {
        var classToken = document.ClassName.Replace('.', '_');
        var hash = ComputeStableHashHex(document.TargetPath);
        return classToken + "." + hash + ".XamlSourceGen.g.cs";
    }

    private static string ComputeStableHashHex(string value)
    {
        // Stable FNV-1a hash for deterministic hint names across machines.
        var hash = 2166136261u;
        foreach (var ch in value.ToLowerInvariant())
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
