using System.Text;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class SourceMappedLineEmissionService
{
    public string NormalizeLineDirectivePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        var normalized = filePath!.Replace('\\', '/');
        return normalized.Replace("\"", "\"\"");
    }

    public void AppendLine(
        StringBuilder sourceBuilder,
        string indent,
        bool emitDebugLineDirectives,
        string lineDirectiveFilePath,
        int lineNumber,
        string statement)
    {
        if (emitDebugLineDirectives && !string.IsNullOrWhiteSpace(lineDirectiveFilePath) && lineNumber > 0)
        {
            sourceBuilder.Append(indent);
            sourceBuilder.AppendLine("// AXSG:XAML");
            sourceBuilder.Append(indent);
            sourceBuilder.Append("#line ");
            sourceBuilder.Append(lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sourceBuilder.Append(" \"");
            sourceBuilder.Append(lineDirectiveFilePath);
            sourceBuilder.AppendLine("\"");
        }

        sourceBuilder.Append(indent);
        sourceBuilder.AppendLine(statement);

        if (emitDebugLineDirectives && !string.IsNullOrWhiteSpace(lineDirectiveFilePath) && lineNumber > 0)
        {
            sourceBuilder.Append(indent);
            sourceBuilder.AppendLine("#line default");
            sourceBuilder.Append(indent);
            sourceBuilder.AppendLine("#line hidden");
        }
    }
}
