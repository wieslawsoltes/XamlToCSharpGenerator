using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignDocumentDescriptor(
    Type RootType,
    string BuildUri,
    string? SourcePath,
    int LiveInstanceCount,
    SourceGenHotDesignDocumentRole DocumentRole = SourceGenHotDesignDocumentRole.Root,
    SourceGenHotDesignArtifactKind ArtifactKind = SourceGenHotDesignArtifactKind.View,
    IReadOnlyList<string>? ScopeHints = null);
