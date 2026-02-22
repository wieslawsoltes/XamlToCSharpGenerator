using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignDocumentDescriptor(
    Type RootType,
    string BuildUri,
    string? SourcePath,
    int LiveInstanceCount);
