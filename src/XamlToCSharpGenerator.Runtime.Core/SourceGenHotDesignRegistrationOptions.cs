namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignRegistrationOptions
{
    public Type? TrackingType { get; init; }

    public string BuildUri { get; init; } = string.Empty;

    public string? SourcePath { get; init; }

    public SourceGenHotDesignDocumentRole DocumentRole { get; init; } = SourceGenHotDesignDocumentRole.Root;

    public SourceGenHotDesignArtifactKind ArtifactKind { get; init; } = SourceGenHotDesignArtifactKind.View;

    public string[]? ScopeHints { get; init; }
}
