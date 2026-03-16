---
title: "Architecture"
---

# Architecture

AXSG is not a single library. It is a layered system made of a compiler host, framework-specific binding and emission layers, runtime services, and editor tooling. This section explains how those layers fit together so you can choose the right package, debug problems in the right subsystem, and understand where a feature actually lives.

## Architectural goals

The project is built around a few explicit goals:

- compile Avalonia XAML into generated C# that can be inspected, debugged, and diffed
- keep framework-specific behavior in dedicated binder/emitter layers instead of leaking it into the host
- support advanced language features such as compiled bindings, shorthand expressions, inline C#, and event bindings without falling back to reflection
- keep runtime responsibilities small and focused on executing generated artifacts, hot reload, and design-time services
- expose the same semantics through editor tooling so diagnostics, navigation, and refactorings behave like the compiler

## Main layers

### Compiler host

The host discovers XAML inputs, configuration documents, transform rules, and project metadata. It normalizes inputs, resolves document identity, runs global include/theme analysis, and invokes the selected framework profile.

### Framework profile

The framework profile translates generic parsed XAML into framework-specific semantics. For Avalonia that includes compiled binding lowering, control themes, selector semantics, `TemplateBinding`, resource/include handling, and generated object-graph construction.

### Emission

Emission turns the bound document model into generated C#. This layer is responsible for stable generated members, helper methods, runtime registration records, and hot-reload-friendly output shapes.

### Runtime

The runtime executes generated bindings, markup helpers, hot reload, hot design, and registry lookups. It also provides fallback behavior where generated code hands off to runtime services.

### Tooling

The language service and VS Code extension project compiler semantics back into the editor. Completion, hover, definitions, references, rename, semantic coloring, inlay hints, and projected inline-C# interop all live here.

## How to use this section

- Start with [Compiler Pipeline](compiler-pipeline/) if you want the end-to-end flow from XAML file to generated output.
- Use [Runtime and Hot Reload](runtime-and-hot-reload/) if you are debugging live update behavior or generated runtime helpers.
- Use [Language Service and VS Code](language-service-and-vscode/) if the issue shows up in the editor before build output changes.
- Use [Unified Remote API and MCP](unified-remote-api-and-mcp/) if the question is about workspace MCP, runtime MCP, preview orchestration, or transport reuse across remote hosts.

## Cross-reference map

- [Concepts](../concepts/) for the core mental model behind the compiler and runtime contracts
- [Reference](../reference/) for package and assembly mapping
- [Guides](../guides/) for task-oriented workflows

- [Compiler Pipeline](compiler-pipeline/)
- [Runtime and Hot Reload](runtime-and-hot-reload/)
- [Language Service and VS Code](language-service-and-vscode/)
- [Unified Remote API and MCP](unified-remote-api-and-mcp/)
