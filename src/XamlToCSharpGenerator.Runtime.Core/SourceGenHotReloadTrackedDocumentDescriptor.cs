using System;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Describes a hot reload tracked document/type registration.
/// </summary>
public sealed record SourceGenHotReloadTrackedDocumentDescriptor(
    Type TrackingType,
    string? BuildUri,
    string? SourcePath,
    int LiveInstanceCount,
    bool IsSourceWatched);
