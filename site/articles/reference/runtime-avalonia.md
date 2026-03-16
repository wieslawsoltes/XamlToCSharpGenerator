---
title: "Package: XamlToCSharpGenerator.Runtime.Avalonia"
---

# XamlToCSharpGenerator.Runtime.Avalonia

## Role

Avalonia runtime loader, static resource resolution, inline-code helpers, hot reload, hot design integration, and the in-process runtime MCP host.

## Related namespaces

- <xref:XamlToCSharpGenerator.Runtime>
- <xref:XamlToCSharpGenerator.Runtime.Markup>

## Use it when

- you need the Avalonia runtime layer directly
- you are integrating AXSG hot reload or runtime fallback paths
- you want to expose live hot reload, hot design, or studio state over MCP from a running app

## Major responsibilities

This package owns the Avalonia-specific runtime surface:

- source-generated loader/runtime helpers
- resource and include registries
- inline C# support types
- hot reload and hot design runtime integration
- runtime MCP hosting and event/resource publication for live-process tooling
- known-type and source-info resolution used during runtime fallback

If generated code is correct but the running application still behaves incorrectly during runtime fallback, resource lookup, or hot reload, this package is one of the first places to inspect.

## Typical debugging scenarios

- runtime fallback chose the wrong known type
- static or dynamic resource resolution diverged from expectations
- hot reload applied but visual state transfer was wrong
- inline-code/runtime helper behavior differed from generated expectations

## Typical consumers

- the umbrella runtime package
- sample applications and host apps running generated Avalonia object graphs
- hot reload/mobile workflows that need Avalonia-specific runtime integration

## Related docs

- [Inline C#](../xaml/inline-csharp/)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload/)
- [Hot Reload and Hot Design](../guides/hot-reload-and-hot-design/)
- [MCP Servers and Live Tooling](../guides/mcp-servers-and-live-tooling/)
