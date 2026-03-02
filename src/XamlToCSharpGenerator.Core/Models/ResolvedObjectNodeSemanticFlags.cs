using System;

namespace XamlToCSharpGenerator.Core.Models;

[Flags]
public enum ResolvedObjectNodeSemanticFlags
{
    None = 0,
    RequiresBaseUriConstructor = 1 << 0,
    IsResourceInclude = 1 << 1,
    IsStyleInclude = 1 << 2,
    StaticResourceMarkupExtension = 1 << 3
}
