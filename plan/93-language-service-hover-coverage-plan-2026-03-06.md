# Language Service Hover Coverage Plan

Date: 2026-03-06

## Goal

Extend XAML hover support in the VS Code language server so it covers the same semantic surface already supported by definitions, references, completions, and inlay hints.

## Current state

Current hover support in `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Hover/XamlHoverService.cs` only handles:

- element names
- attribute names on the current element
- bare `x:` directives

That leaves most semantically rich XAML constructs without hover even though the language service already has typed resolution for them elsewhere.

## Existing semantic coverage not yet used by hover

The following services already resolve symbols or typed targets and should be reused by hover:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlBindingNavigationService.cs`
  - binding path properties
  - binding type tokens such as `AncestorType`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlExpressionBindingNavigationService.cs`
  - expression-binding properties and methods
  - expression result type analysis
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlSelectorNavigationService.cs`
  - selector types
  - style classes
  - pseudoclasses
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlTypeReferenceNavigationResolver.cs`
  - `x:DataType`
  - `x:Class`
  - `{x:Type ...}` payload normalization
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlMarkupExtensionNavigationSemantics.cs`
  - markup extension class tokens
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlResourceReferenceNavigationSemantics.cs`
  - `StaticResource` and `DynamicResource` key ranges

## Root causes

1. Hover is implemented as a standalone lightweight parser path and does not reuse the typed semantic services added later.
2. Some useful span information exists only as private helpers inside definition services, especially for markup-extension argument/value spans.
3. No shared markdown formatter exists for Avalonia types, CLR symbols, resource keys, style classes, pseudoclasses, or binding metadata, so hover output quality is inconsistent by construction.

## Missing hover cases

The following hover cases are missing or incomplete:

- type-valued attribute values:
  - `x:DataType`
  - `x:Class`
  - `TargetType`
  - setter/property values using `{x:Type ...}`
- markup extension class names:
  - `Binding`
  - `CompiledBinding`
  - `StaticResource`
  - `DynamicResource`
  - `TemplateBinding`
  - `RelativeSource`
  - `x:Type`
  - `x:Null`
- binding markup argument names and values:
  - `Path`
  - `ElementName`
  - `Mode`
  - `RelativeSource`
  - `AncestorType`
  - `Converter`
  - `StringFormat`
  - related named arguments already parsed by the binding parser
- binding path members:
  - properties
  - parameterless methods
  - attached property owner tokens and type casts where already resolved
- expression-binding members:
  - properties
  - methods
  - expression result type
- selector tokens:
  - type tokens
  - style classes
  - pseudoclasses
- resource keys:
  - declaration hover
  - reference hover
- named elements:
  - `x:Name`
  - `Name`
  - `ElementName=` references
- attribute properties not currently resolved through richer semantics:
  - attached properties
  - setter `Property` values

## Design constraints

- Do not add a separate heuristic-only hover parser for semantic features.
- Reuse existing typed services where symbol resolution already exists.
- Extract shared span helpers when current logic is private to another service.
- Keep hover deterministic and source-accurate.
- Preserve performance by keeping hover on the cached analysis path only.

## Implementation plan

### Phase A: Shared hover infrastructure

- Add a shared hover markdown formatter for:
  - Avalonia types
  - Avalonia properties
  - CLR symbols
  - resource keys
  - style classes
  - pseudoclasses
  - markup extensions
  - binding argument descriptors
- Add shared XML attribute span helpers so hover can target both attribute names and values precisely.

### Phase B: Semantic hover integration

- Update `XamlHoverService` to resolve hover in this order:
  1. expression-binding members
  2. selector tokens
  3. binding members and binding type tokens
  4. type-reference attribute values
  5. markup-extension class tokens
  6. markup-extension/binding argument names and key values
  7. resources and named elements
  8. fallback element/property/directive hover
- Add hover coverage for `x:DataType`, `x:Class`, binding members, expression members, selector classes/pseudoclasses, and resource keys.

### Phase C: Tests and regression gates

- Add engine tests for:
  - `x:DataType`
  - markup extension class names
  - binding path properties
  - expression methods/properties
  - selector class/pseudoclass tokens
  - resource keys
- Add LSP integration tests for representative hover requests.
- Keep existing hover tests passing.

## Acceptance criteria

- Hover returns semantic markdown for all supported symbol-bearing XAML contexts already handled by definitions/references/completion.
- Hover on `Binding`, `{= ...}`, selector classes/pseudoclasses, `x:DataType`, and resource keys works in both engine tests and LSP tests.
- No new heuristic-only semantic path is introduced when a typed resolver already exists.
