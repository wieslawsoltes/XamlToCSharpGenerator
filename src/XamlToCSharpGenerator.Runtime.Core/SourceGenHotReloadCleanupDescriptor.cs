using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotReloadCleanupDescriptor(
    string Token,
    Action<object> CleanupAction);
