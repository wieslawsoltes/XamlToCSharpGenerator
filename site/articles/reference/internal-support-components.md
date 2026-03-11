---
title: "Internal Support Components"
---

# Internal Support Components

Some repo projects are operational support components rather than shipped NuGet packages or end-user tools.

They still matter for understanding the full AXSG system, especially around hot reload and docs/release plumbing.

## `XamlToCSharpGenerator.DotNetWatch.Proxy`

This project is a small .NET executable used as the named-pipe bridge for `dotnet watch` integration in scenarios where AXSG needs to participate in managed update coordination without relying on IDE-only infrastructure.

Use it when you need to understand:

- the hot reload named-pipe handshake
- watch-process bridging on mobile/hybrid workflows
- why certain hot reload diagnostics mention a proxy process

Source entry point:

- `src/XamlToCSharpGenerator.DotNetWatch.Proxy/Program.cs`

Why it is documented here instead of in generated API:

- it is not a shipped NuGet package
- it is an internal support executable
- its public API surface is effectively empty, so generated API pages would not provide useful guidance

## How it fits into the repo

This component sits beside, not inside, the public package surface:

- runtime hot reload behavior lives in `XamlToCSharpGenerator.Runtime` and `XamlToCSharpGenerator.Runtime.Avalonia`
- the proxy exists only to support the `dotnet watch` transport path

See also:

- [Guides: iOS Hot Reload](../guides/hot-reload-ios/)
- [Concepts: Tooling Surface](../concepts/tooling-surface/)
- [Reference: API Coverage Index](api-coverage-index/)
