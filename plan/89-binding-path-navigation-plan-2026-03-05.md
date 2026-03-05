## Goal

Add semantic go-to-definition and find-references support for binding-path symbols in the XAML language service and VS Code extension, including runtime `{Binding ...}` paths and compiler-supported path semantics.

## Problem

Current navigation handles:

- element types
- CLR properties used as XAML attributes and setter properties
- `x:Class`, `x:DataType`
- named elements and resource keys

It does not handle binding-path members such as:

```xaml
<TextBlock Text="{Binding Name}"/>
<TextBlock Text="{Binding Path=Customer.Name}"/>
<TextBlock Text="{Binding ElementName=SubmitButton, Path=Content}"/>
```

The current gap is structural:

- `XamlDefinitionService` and `XamlReferenceService` never semantically classify binding-path tokens.
- binding markup is treated as opaque text except for existing resource / element-name token helpers.
- compiler-grade member-path semantics already exist, but only inside binder internals and are not exposed to the language service.

## Design Principles

- No regex or text-shape heuristics for member resolution.
- Reuse existing binding parsers:
  - `BindingEventMarkupParser`
  - `CompiledBindingPathParser`
  - `XamlMemberPathSemantics`
- Resolve symbols from Roslyn `Compilation` first, then project them back into existing language-service type/property reference flows.
- Keep the feature scoped to semantically resolvable cases; do not guess when the binding source type is unknown.

## Scope

### Phase 1: Binding navigation semantic service

Add a reusable service in `src/XamlToCSharpGenerator.LanguageService/Definitions` that:

- detects whether the cursor is on a binding-path segment or a binding-path type token
- parses the containing binding markup using `BindingEventMarkupParser`
- identifies the active path segment at the current cursor position
- resolves the binding source type for the current binding:
  - inherited `x:DataType`
  - `ElementName=...`
  - `RelativeSource Self`
  - simple ancestor source tokens where the type token is explicitly present
- resolves the member/type symbol chain using Roslyn symbols

Output contract should distinguish:

- property / field segment
- attached-property owner type token
- cast / ancestor type token

### Phase 2: Definition/reference integration

Integrate the new semantic service into:

- `XamlDefinitionService`
- `XamlReferenceService`

Behavior:

- definition on a binding-path property segment opens the CLR declaration for that property/field
- references on a binding-path property segment return:
  - declaration
  - XAML attribute/setter usages already covered by the existing CLR property reference scanner
  - binding-path usages across project XAML files
- definition/reference on binding-path type tokens resolves the CLR type declaration/reference set

### Phase 3: Tests and guard rails

Add focused tests for:

- `{Binding Name}` on `x:DataType`
- `{Binding Path=Customer.Name}` nested path segments
- `{Binding ElementName=SubmitButton, Path=Content}`
- attached property path segment definitions
- binding-path type token definitions/references where supported
- unknown-source cases returning no result instead of false positives

## Implementation Notes

- Extract reusable XML line/column lookup helpers from the inlay-hint service if needed.
- Prefer Roslyn symbol resolution for member chains:
  - resolve `INamedTypeSymbol` source
  - walk path segments
  - support property and field members
  - normalize attached/cast syntax with existing parser semantics
- Reuse existing `CollectPropertyReferences` / `CollectTypeReferences` flows after symbol resolution by mapping resolved Roslyn symbols back to:
  - `AvaloniaTypeInfo`
  - `AvaloniaPropertyInfo`

## Non-Goals For This Slice

- dynamic DataContext inference without `x:DataType`
- full expression-language navigation beyond binding/member-path semantics
- method-call binding navigation

## Acceptance Criteria

- `Go to Definition` on `Name` in `{Binding Name}` opens the CLR property declaration when an `x:DataType` source is available.
- `Find References` on the same token returns declaration plus XAML binding usages.
- Nested path segments navigate independently to the correct owner-member declarations.
- Existing element/property/type/resource navigation remains unchanged.
- New tests pass in `tests/XamlToCSharpGenerator.Tests/LanguageService`.
