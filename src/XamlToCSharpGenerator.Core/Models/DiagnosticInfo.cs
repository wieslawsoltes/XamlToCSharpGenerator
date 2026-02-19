namespace XamlToCSharpGenerator.Core.Models;

public sealed record DiagnosticInfo(
    string Id,
    string Message,
    string FilePath,
    int Line,
    int Column,
    bool IsError);
