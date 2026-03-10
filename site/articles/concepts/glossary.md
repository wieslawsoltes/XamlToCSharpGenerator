---
title: "AXSG Glossary"
---

# AXSG Glossary

This glossary defines the terms that appear repeatedly across the AXSG compiler, runtime, language-service, and docs.

## A

### Ambient `x:DataType`

The active compiled-binding source type for a XAML scope. AXSG uses it to validate binding paths, infer result types, and drive editor features such as completion, hover, and inlay hints.

### API Coverage

The combined narrative-plus-generated-reference surface for a package or namespace. In AXSG, not every shipped artifact is API-first, so API coverage often means both a Lunet-generated namespace tree and a narrative package guide.

## B

### Binding Lowering

The compiler step that converts authored markup such as `{Binding Foo}`, `{Foo}`, or `{$'{Count}x {Name}'}` into the most appropriate generated representation. Lowering can result in a direct compiled binding path, an expression-backed helper, or a runtime descriptor.

### Build-Transitive Package

A package that contributes `.props` and `.targets` into consuming builds instead of shipping a user-facing code API. `XamlToCSharpGenerator.Build` is the main build-transitive AXSG artifact.

## C

### Compiler Host

The framework-agnostic orchestration layer that discovers XAML inputs, configuration files, include graphs, transform rules, and framework profiles before invoking the selected binder/emitter.

### Compiled Binding

A binding that is validated against known CLR symbols at build time and emits generated code or a generated runtime descriptor instead of using reflection-only lookup.

### Control Theme Override Pattern

A local `ControlTheme` that uses the same key as a base theme and `BasedOn` to extend the external or earlier theme. AXSG explicitly allows this pattern and distinguishes it from a real local cycle.

## D

### Deferred Compilation Provider

The language-service layer that postpones `MSBuildWorkspace` creation until the first request that truly needs project compilation state. This keeps extension startup lighter.

### Differential Backend Harness

The test harness that compares generated behavior or artifacts across backend changes while reusing the same generator/runtime output stack. It is part of AXSG's regression strategy for compiler evolution.

## E

### Editor Projection

A temporary synthetic C# document created from inline XAML code so editor tooling can analyze the snippet with full Roslyn semantics while mapping results back into the XAML source file.

### Explicit Source

A binding source that is declared directly in markup, for example `$parent[DataGrid]`, `$self`, `#name`, `ElementName=...`, or typed `RelativeSource`. Explicit sources can remove the need for ambient `x:DataType`.

## F

### Framework Profile

The binder/emitter integration point that teaches the compiler how to interpret and emit framework-specific semantics. `Avalonia` and `NoUi` are AXSG's current framework profiles.

## G

### Generated Artifact

Any compiler-produced output that becomes part of runtime or tooling behavior. This includes generated partial classes, helper methods, registry entries, include graphs, type maps, and editor metadata projections.

### Generated Runtime Descriptor

A compact representation emitted when runtime services need stable metadata instead of fully direct generated code paths. This is used for features such as hot reload, binding helpers, and fallback loaders.

## H

### Hot Design

The runtime/editor integration layer that lets AXSG inspect, mutate, or remotely observe the running visual tree while preserving compiler-driven semantics.

### Hot Reload Stability

The requirement that generated method names, helper identifiers, and descriptor identities remain deterministic across edits so Roslyn Edit-and-Continue can emit valid deltas.

## I

### Include Graph

The normalized graph of XAML includes (`StyleInclude`, merged dictionaries, rooted project paths, `avares://` URIs, linked items). AXSG uses it for diagnostics, navigation, and generated URI registration.

### Inline C#

Valid XAML forms that contain statement-capable or expression-capable C# code, such as `{CSharp Code=...}` or `<CSharp><![CDATA[ ... ]]></CSharp>`.

## L

### Language Service

The shared semantic engine that powers completion, hover, definitions, references, rename, semantic highlighting, and inlay hints for XAML and inline C#.

### Linked XAML Item

A XAML file whose effective project path comes from `Link` or `TargetPath` metadata rather than its physical file path. AXSG resolves references and include URIs against the effective project path.

## M

### Mini-Language Parser

The low-allocation parsing layer used for selectors, binding/event fragments, markup snippets, and helper text scanning. It exists separately from the full XAML parser because those syntaxes appear in many hot paths.

## P

### Property Element

Owner-qualified XAML syntax such as `<Window.IsVisible>` or `<Grid.RowDefinitions>`. AXSG treats the owner token and property token as separate semantic navigation targets.

### Projection Cache

The VS Code extension cache for projected inline-C# virtual documents. It is version-aware so stale projections do not leak into new editor requests.

## R

### Reference Cache

The language-service cache of cross-file XML documents and project source snapshots used to make references, rename, and include resolution fast without rebuilding every parse tree on every request.

### Relative Source Query

A source expression that resolves binding context by structural relationship instead of ambient scope, for example `$parent[Type]` or `RelativeSource AncestorType=...`.

## S

### Selector Token

A meaningful part of a style selector, such as a type token, class token, pseudoclass, or named-element token (`#Name`). AXSG parses selector tokens individually so definitions, references, rename, and hover can work inside selectors.

### Shorthand Expression

A compact expression form such as `{ProductName}`, `{!IsLoading}`, or `{this.RootProperty}` that AXSG analyzes before deciding whether it can lower the expression to a cheaper compiled-binding shape.

### Source Info Registry

The runtime mapping between generated types, source files, and effective XAML URIs used by hot reload, tooling, and diagnostics.

## T

### TemplateBinding

A property reference against the templated control type. AXSG resolves the referenced property semantically so navigation, references, and diagnostics work inside templates and control themes.

### Transform Rule

A configuration document that remaps aliases, namespace choices, or feature conventions without forcing those choices into the stable compiler core.

## U

### URI Navigation

Language-service support that resolves XAML URI property values, such as `Source="/Themes/Fluent.xaml"`, to the correct linked or physical XAML file in the project.

## V

### Virtual C# Document

The internal `csharp`-typed VS Code document generated for inline code interop so existing C# providers can answer selected queries against an AXSG-owned snippet.

## Related

- [Compiler Host and Project Model](compiler-host-and-project-model.md)
- [Binding and Expression Model](binding-and-expression-model.md)
- [Tooling Surface](tooling-surface.md)
- [Troubleshooting](../guides/troubleshooting.md)
