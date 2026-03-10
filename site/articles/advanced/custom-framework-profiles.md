---
title: "Custom Framework Profiles"
---

# Custom Framework Profiles

AXSG is not limited to Avalonia. The compiler host is framework-agnostic and expects a profile layer that knows how to interpret XAML semantics for a concrete target framework.

This article explains how to design that profile boundary so you keep the host generic and the framework integration isolated.

## Relevant packages

- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Framework.Abstractions`
- a concrete profile such as `XamlToCSharpGenerator.Avalonia` or `XamlToCSharpGenerator.NoUi`

## Responsibilities split

### Compiler host responsibilities

The host owns:

- project/additional-file discovery
- configuration precedence
- include graph construction
- document normalization and convention inference
- orchestration of binder/emitter stages
- generator-facing diagnostics and caching

### Framework profile responsibilities

A profile owns:

- mapping XAML elements and members to framework types
- binding, selector, template, and resource semantics
- generated runtime contract shape
- framework-specific diagnostics
- generated code conventions required by the runtime layer

## Design rules

- Keep framework-specific code out of `Compiler` and `Core`.
- Put shared semantic contracts into `Core` or `Framework.Abstractions` only if they are truly framework-neutral.
- Use composition at the profile boundary. The host should ask for services via abstractions and never special-case a concrete profile.
- Preserve deterministic output. A new profile cannot depend on reflection-only runtime behavior if it changes generator determinism.

## Recommended implementation order

1. Start from the minimal profile in `XamlToCSharpGenerator.NoUi`.
2. Define the binder/emitter/runtime contracts needed for your framework.
3. Add parser/binder coverage for the minimal feature set you need.
4. Add runtime helpers only after generated output shape is stable.
5. Add language-service support only after compiler semantics are explicit.

## Testing expectations

A profile is not complete until it has:

- unit tests for parser/binder/emitter behavior
- output-shape tests for generated code
- runtime tests for the supported feature set
- language-service tests if the profile introduces editor-visible semantics

## Common mistakes

- leaking framework-specific assumptions into the generic compiler host
- using runtime reflection where the profile could emit deterministic generated code
- skipping runtime contracts and relying on ad hoc helper calls from generated output
- adding editor/language-service support before the compiler semantics are stable

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model.md)
- [Compiler Pipeline](../architecture/compiler-pipeline.md)
- [Package: XamlToCSharpGenerator.Framework.Abstractions](../reference/packages/framework-abstractions.md)
- [Package: XamlToCSharpGenerator.NoUi](../reference/packages/noui.md)
