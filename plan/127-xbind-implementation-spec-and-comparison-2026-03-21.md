# x:Bind Implementation Spec And Comparison

Date: 2026-03-21

## 1. Scope

This document describes how `x:Bind` is implemented in this repository today:

- source generator / semantic binder
- Avalonia runtime adapter
- language service / LSP

It describes the currently implemented surface for the Avalonia source-generated compiler stack, not every `x:Bind` scenario that exists in WinUI/UWP or Uno.

The current runtime package targets Avalonia `11.3.12`:

- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlToCSharpGenerator.Runtime.Avalonia.csproj`

## 2. High-Level Model

The implementation is intentionally not a thin clone of Avalonia `{Binding}` or `{CompiledBinding}`.

Instead, `x:Bind` is modeled as:

1. parse the `x:Bind` markup extension into a typed option model
2. parse the `x:Bind` expression into a dedicated expression AST
3. lower that AST into strongly typed C# against explicit `source`, `root`, and `target` parameters
4. use Roslyn expression/lambda analysis to validate and normalize the lowered code
5. extract observable dependency paths from the AST
6. emit a runtime call that builds an Avalonia `MultiBinding` for forward updates
7. for `TwoWay`, wrap the forward binding with a custom bind-back observer
8. expose matching understanding in the language service for completion, hover, definition, and signature help

The central design choice is:

- forward evaluation is generated typed code
- observation and value transport are delegated to Avalonia binding primitives
- reverse updates are handled by a custom bind-back observer

This keeps emitted/runtime code reflection-free while still integrating with Avalonia's binding engine.

## 3. Main Source Files

### Compiler and shared parsing

- `src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
- `src/XamlToCSharpGenerator.Core/Models/BindingEventMarkupModels.cs`
- `src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/XBindExpressionParser.cs`
- `src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/XBindExpressionNodes.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.XBind.cs`

### Runtime

- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenMarkupExtensionRuntime.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenInlineCodeMultiValueConverter.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenXBindTwoWayBinding.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenNameReferenceHelper.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenXBindBindingModels.cs`

### Language service / LSP

- `src/XamlToCSharpGenerator.LanguageService/Completion/XamlBindingCompletionService.cs`
- `src/XamlToCSharpGenerator.LanguageService/Completion/XamlSemanticSourceTypeResolver.cs`
- `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlBindingNavigationService.cs`
- `src/XamlToCSharpGenerator.LanguageService/SignatureHelp/XamlSignatureHelpService.cs`
- `src/XamlToCSharpGenerator.LanguageService/Hover/XamlHoverMarkdownFormatter.cs`

### Evidence

- `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXBindSourceGeneratorTests.cs`
- `tests/XamlToCSharpGenerator.Tests/Runtime/SourceGenMarkupExtensionRuntimeTests.cs`
- `tests/XamlToCSharpGenerator.Tests/LanguageService/XamlLanguageServiceEngineTests.cs`
- `tests/XamlToCSharpGenerator.Tests/LanguageService/LspServerIntegrationTests.cs`
- `samples/SourceGenXamlCatalogSample/Pages/XBindPage.axaml`
- `samples/SourceGenXamlCatalogSample/Pages/XBindPage.axaml.cs`
- `samples/SourceGenXamlCatalogSample/Pages/XBindPage.Support.cs`

## 4. Supported Surface

### 4.1 Validated and explicitly covered

The following scenarios are implemented and have direct sample and/or test coverage:

| Scenario | Status | Evidence |
| --- | --- | --- |
| Root-scope property access | Validated | generator tests, sample page |
| Root-scope method calls | Validated | generator tests, sample page |
| Static type members | Validated | generator tests, LSP definition test, sample page |
| Named element references as first segment | Validated | generator tests, runtime namescope test, sample page |
| DataTemplate scope via `x:DataType` | Validated | generator tests, LSP completion tests, sample page |
| Root fallback from inside `DataTemplate` | Validated | LSP hover/completion tests, sample page |
| Pathless `{x:Bind}` | Validated | generator tests, sample page |
| Conditional access inside expressions | Validated | generator tests, sample page |
| Indexer access | Validated | sample page and lowering/runtime design |
| `x:DefaultBindMode` inheritance | Validated | sample page and binder tests around generated output |
| `TwoWay` with implicit assignable path | Validated | runtime reentrancy test |
| `TwoWay` with explicit `BindBack` | Validated | generator and runtime tests |
| `Converter`, `ConverterCulture`, `ConverterParameter` | Validated | runtime converter-order test, sample page |
| `StringFormat` | Validated | sample page |
| `FallbackValue` / `TargetNullValue` | Validated | sample page and generated option flow |
| `Delay` with `PropertyChanged` | Validated | generator and runtime tests |
| `UpdateSourceTrigger=LostFocus` | Validated | runtime test |
| Avalonia `BindingPriority` mapping | Validated | generator tests, sample page |
| x:Bind event handlers | Validated | generator tests and sample page |
| LSP completion | Validated | engine and server tests |
| LSP hover and definition | Validated | engine tests |
| LSP signature help | Validated | engine and server tests |

### 4.2 Implemented by design, but not as heavily covered

These are present in the parser/binder/runtime model, but do not currently have the same level of focused regression coverage as the list above:

| Scenario | Implementation note |
| --- | --- |
| Pathless cast syntax such as `{x:Bind (local:Type)}` | parser and lowering support null-operand cast nodes |
| Literal `x:Null`, `x:True`, `x:False` in expressions | parser maps them to literal nodes |
| Attached property access such as `Button22.(Grid.Row)` | parser and lowering support attached-property segments |
| Attached-property bind-back assignment | synthesized reverse path uses `Set<Property>` |
| Inline `DataType=` on `{x:Bind ...}` | parser and binder support it as a local source-type override |

### 4.3 Not currently supported, or intentionally divergent

| Scenario | Status | Notes |
| --- | --- | --- |
| `ConverterLanguage` option name | Supported as alias | normalized onto Avalonia `ConverterCulture` semantics |
| `ElementName=`, `RelativeSource=`, `Source=` inside `x:Bind` | Supported | lowered into typed source-selection metadata and runtime dependency bindings |
| WinUI `Bindings.Initialize()/Update()/StopTracking()` lifecycle surface | Supported for generated class-backed roots | emitted `Bindings` adapter delegates to the source-gen runtime lifecycle registry |
| WinUI `x:Load` integration | Not implemented / not applicable | Avalonia does not expose WinUI `x:Load` semantics |
| Uno-style POCO target x:Bind support | Not a validated surface | current runtime path is built around Avalonia binding application |
| `UpdateSourceTrigger=Explicit` practical commit flow | Supported | pending bind-back values flush through generated `Bindings.Update()` or `SourceGenMarkupExtensionRuntime.UpdateXBind(root)` |

## 5. Markup Extension Spec Implemented Here

## 5.1 Markup option model

`BindingEventMarkupParser.TryParseXBindMarkupCore(...)` turns a parsed markup extension into `XBindMarkup` with these fields:

- `Path`
- `Mode`
- `BindBack`
- `DataType`
- `Converter`
- `ConverterCulture`
- `ConverterParameter`
- `StringFormat`
- `FallbackValue`
- `TargetNullValue`
- `Delay`
- `Priority`
- `UpdateSourceTrigger`

Accepted aliases:

- `StringFormat` or `Format`
- `FallbackValue` or `Fallback`
- `TargetNullValue` or `NullValue`
- `Priority` or `BindingPriority`
- `UpdateSourceTrigger` or `Trigger`

Important semantic differences from WinUI/UWP:

- `ConverterLanguage` is accepted as an alias and normalized to Avalonia `ConverterCulture`
- `DataType`, `Delay`, and `Priority` are Avalonia-oriented extensions

## 5.2 Mode rules

Only these modes are accepted:

- `OneTime`
- `OneWay`
- `TwoWay`

Anything else is rejected with `AXSG0115`.

Default mode resolution:

1. explicit `Mode=...` on the markup
2. nearest inherited `x:DefaultBindMode`
3. fallback `OneTime`

`x:DefaultBindMode` is resolved in `AvaloniaSemanticBinder.ObjectNodeBinding.cs` and is inherited through the semantic binding scope.

## 5.3 Root and template requirements

`x:Bind` requires an `x:Class`-backed root type. If the root type is missing, binding generation fails with `AXSG0116`.

For `DataTemplate` usage:

- if the markup has explicit `DataType=...`, that wins
- otherwise there must be ambient `x:DataType` in scope
- if neither exists, generation fails with `AXSG0110`

## 6. Expression Language Spec

The expression parser is a dedicated mini-language parser in:

- `src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/XBindExpressionParser.cs`

The AST node types are defined in:

- `src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/XBindExpressionNodes.cs`

## 6.1 Grammar shape

Conceptually, the implemented grammar is:

```text
Expression
  := Primary ( Postfix )*

Primary
  := Identifier
   | StringLiteral
   | NumberLiteral
   | NullLiteral
   | BooleanLiteral
   | CastExpression
   | ParenthesizedExpression

Postfix
  := "." Identifier
   | "?." Identifier
   | ".(" TypeToken "." Identifier ")"
   | "(" ArgumentList? ")"
   | "[" ArgumentList "]"

CastExpression
  := "(" TypeToken ")" Primary?
```

Important consequences:

- the surface is path/invocation oriented, not general C#
- there are no arithmetic operators
- there are no comparison operators
- there is no null-coalescing operator
- there are no lambdas
- there is no object creation

That is deliberate: `x:Bind` is compiled from a constrained binding expression language, then validated by Roslyn after lowering.

## 6.2 Literal support

The parser explicitly recognizes:

- numeric literals
- string literals
- `null` and `x:Null`
- `true` / `false`
- `x:True` / `x:False`

String escaping rules implemented by the parser:

- single-quoted strings use `^` escaping
- double-quoted strings use backslash escaping

## 6.3 Supported expression forms

The parser and lowering model support:

- property access
- field access
- method access
- method invocation
- conditional member access with `?.`
- indexers
- attached property access
- casts
- nested expressions such as `Format(Selected?.Name)`
- pathless cast nodes such as `(local:Type)`

## 7. Source Resolution Semantics

The binder lowers each expression against an explicit context:

- `source`
- `root`
- `target`

This is modeled by `XBindLoweringContext` in `AvaloniaSemanticBinder.XBind.cs`.

## 7.1 Default source choice

Outside a `DataTemplate`:

- the default source is the root object

Inside a `DataTemplate`:

- the default source is the current template item, resolved through `DataContext`

This matches WinUI/UWP/Uno's conceptual `x:Bind` source model more closely than Avalonia `{Binding}`.

## 7.2 Initial identifier resolution order

For the first identifier in an expression, the binder resolves in this order:

1. current source type
2. root type, if different from current source type
3. target object type
4. named element in the document
5. CLR type reference

This is implemented in:

- `TryLowerXBindIdentifier(...)`
- `TryResolveXBindIdentifierPathReference(...)`

Important notes:

- root fallback inside `DataTemplate` is intentional and is part of the validated surface
- named elements are resolved from the XAML document at compile time and from Avalonia namescopes at runtime
- target-object fallback is an implementation detail of this Avalonia adaptation; it is not a standard WinUI `x:Bind` rule

## 7.3 Named elements

Compile-time:

- the binder consults `document.NamedElements`

Runtime:

- `ResolveNamedElement<T>(target, root, name)` calls `SourceGenNameReferenceHelper.ResolveByName(...)`
- resolution checks:
  - direct `INameScope`
  - `NameScope.GetNameScope(styledElement)`
  - ancestor logical-tree namescope via `FindNameScope()`

This means the runtime can resolve names from ancestor namescopes, which is important for templates and nested scopes.

## 8. Dependency Extraction And Change Observation

`x:Bind` evaluation is generated code, but change observation still needs binding-engine integration.

This repository solves that by extracting dependency paths from the parsed AST and turning them into child Avalonia bindings.

## 8.1 Dependency model

Dependencies are collected in `CollectXBindDependencies(...)` and encoded as:

- `SourceGenBindingDependency`

with source kinds:

- `DataContext`
- `Root`
- `Target`
- `ElementName`

The main expression source is encoded separately, and duplicate dependency entries are removed.

## 8.2 Path extraction rules

Examples:

- `Title` -> main source path
- `SelectedContact?.Name` -> dependency on `SelectedContact`
- `Editor.Text` -> named-element dependency rooted at `Editor`
- `Contacts[0].Email` -> collection path with literal indexer suffix
- `Format(Editor.Text, SelectedContact?.Name)` -> dependencies from both the invocation target and arguments

The extraction logic is intentionally structural:

- it walks the expression AST
- it does not inspect generated C# text

This is one of the major architectural improvements over heuristic approaches.

## 9. Compiler Lowering

The core lowering entry point is:

- `TryBuildXBindBindingExpression(...)`

It performs the following steps.

## 9.1 Parse and validate

If `Path` is empty:

- the expression lowers to `source`

Otherwise:

1. parse with `XBindExpressionParser`
2. lower to typed C#
3. validate and normalize the result via `CSharpInlineCodeAnalysisService.TryAnalyzeExpression(...)`

The emitted evaluator always has the shape:

```csharp
static (source, root, target) => (object?)(...)
```

with generic parameters:

- `TSource`
- `TRoot`
- `TTarget`

## 9.2 Lowering rules by AST node

### Identifier

- source member -> `source.Member`
- root member -> `root.Member`
- target member -> `target.Member`
- named element -> `ResolveNamedElement<T>(target, root, "Name")`
- type token -> fully qualified type name

### Member access

- normal -> `left.Member`
- conditional -> `left?.Member`

### Attached property access

- lowered to `OwnerType.GetProperty(left)`

### Invocation

- lowered as ordinary C# invocation over the lowered target and lowered arguments

### Indexer

- lowered as ordinary C# indexer syntax

### Cast

- lowered as ordinary C# cast
- if the cast is pathless, it lowers to `((Type)source)`

## 9.3 Diagnostics

Current `x:Bind`-specific diagnostics in the binder include:

- `AXSG0110`: missing or invalid `x:DataType` / `DataType`
- `AXSG0115`: unsupported mode
- `AXSG0116`: missing `x:Class`-backed root type
- `AXSG0117`: invalid expression or invalid option conversion
- `AXSG0118`: invalid `TwoWay` bind-back

## 10. TwoWay Semantics And BindBack

## 10.1 Forward versus reverse paths

Forward updates always come from the generated evaluator.

Reverse updates are handled separately:

- if `Mode != TwoWay`, there is no bind-back path
- if `Mode == TwoWay`, the binder must produce an update lambda

## 10.2 TwoWay with implicit assignment

If there is no explicit `BindBack=...`, the binder tries to synthesize an assignment.

Assignable shapes are:

- identifier
- non-conditional member access
- indexer
- attached property access

Examples that can be synthesized:

- `Alias`
- `SelectedContact`
- `Contacts[0].Name`
- `Button22.(Grid.Row)`

Examples that cannot be synthesized:

- `SelectedContact?.Name`
- `Format(Name)`
- `A + B` if such syntax existed

If synthesis fails, `AXSG0118` is reported and the user must provide explicit `BindBack`.

## 10.3 Explicit BindBack

`BindBack=...` is parsed with the same `XBindExpressionParser`.

Expected shape:

- a callable member path, such as `ApplySearchDraft` or `Model.MyBindBack`

It is not meant to be authored as a fully invoked expression.

The binder emits a lambda of the shape:

```csharp
static (source, value) => source.ApplySearchDraft(CoerceMarkupExtensionValue<T>(value))
```

or, for synthesized assignment:

```csharp
static (source, value) => source.Alias = CoerceMarkupExtensionValue<T>(value)
```

The lambda is then semantically validated by Roslyn against `Action<TSource, object>`.

## 11. Event Binding

Event binding is handled by:

- `TryBuildXBindEventBindingDefinition(...)`

This is distinct from property binding.

## 11.1 Source choice

Outside templates:

- events resolve against the root object

Inside templates:

- events resolve against the template source type
- explicit `DataType=...` may override the ambient type

## 11.2 Supported handler shapes

The implementation mirrors the WinUI/UWP rule set closely:

- exact delegate signature
- no-parameter method
- method whose parameters are assignable from the event parameters

The binder achieves this by building candidate lambda bodies and asking Roslyn which candidate is valid.

For non-invocation handler paths:

- it tries `Handler()`
- then `Handler(arg0)`
- then `Handler(arg0, arg1)`
- and so on

For invocation expressions:

- it uses the authored invocation directly

Examples:

- `Click="{x:Bind HandlePrimaryClick}"`
- `Click="{x:Bind HandleDetailedClick}"`
- `Click="{x:Bind CaptureEditorText(Editor.Text)}"`

Important semantic point:

- event bindings are not change-tracked in the same way as property bindings
- the path is evaluated when the event fires

That matches the WinUI model.

## 12. Runtime Implementation

The runtime entry point is:

- `SourceGenMarkupExtensionRuntime.ProvideXBindExpressionBinding<TSource, TRoot, TTarget>(...)`

## 12.1 Forward path

The runtime builds a `MultiBinding` whose converter is:

- `SourceGenInlineCodeMultiValueConverter<TSource, TRoot, TTarget>`

The first child binding represents the main source.
Additional child bindings represent extracted dependencies.

Dependency-to-binding mapping:

- `Root` -> `new Binding(path) { Source = rootObject }`
- `Target` -> `new Binding(path) { Source = targetObject }`
- `ElementName` -> `new Binding(path) { ElementName = name }`
- `DataContext` -> `new Binding(path)`

The `MultiBinding` carries the binding-facing options:

- `Mode`
- `FallbackValue`
- `TargetNullValue`
- `StringFormat`
- `Converter`
- `ConverterCulture`
- `ConverterParameter`
- `Priority`

## 12.2 Forward evaluation order

The forward converter performs:

1. typed evaluator call
2. optional user converter call
3. Avalonia target coercion

This ordering is intentional and covered by regression tests.

It means the converter sees the raw evaluator result before target-type coercion.

## 12.3 TwoWay runtime path

If the binding is `TwoWay` and there is a bind-back lambda:

1. the runtime creates the forward `MultiBinding`
2. it initiates that binding immediately
3. it wraps the forward source with `InstancedBinding.TwoWay(...)`
4. target-to-source flow is handled by `SourceGenXBindBindBackObserver<TSource>`

This is why `x:Bind` TwoWay in this repository is not just a plain Avalonia `TwoWay` binding. The reverse path is custom.

## 12.4 Reverse update behavior

`SourceGenXBindBindBackObserver<TSource>` performs:

1. reentrancy guard check
2. optional `ConvertBack`
3. trigger policy
4. source resolution
5. bind-back lambda invocation

Reverse source resolution uses `TryResolveDependencySource(...)` against:

- root object
- target object
- named element
- current `DataContext`

## 12.5 UpdateSourceTrigger behavior

Current runtime behavior:

- `Default` -> normalized to `PropertyChanged`
- `PropertyChanged` + `Delay > 0` -> debounce on dispatcher
- `LostFocus` -> stage pending value and flush on `LostFocus`
- `Explicit` -> stage pending value only

Important comparison:

- WinUI/UWP documents `TextBox.Text` defaulting to `LostFocus`
- Avalonia compiled/reflection bindings normalize `Default` to `PropertyChanged`
- this repository follows Avalonia's behavior, not WinUI's TextBox special case

This is an intentional Avalonia semantic adaptation.

## 12.6 Reentrancy guard

Reverse updates use `_isApplying` to avoid recursive bind-back loops.

This prevents:

- setter echo loops
- explicit normalization loops where source normalization updates the target again

This behavior is covered by runtime tests.

## 12.7 Deferred binding resilience

The runtime uses the existing `ApplyBinding(...)` infrastructure for child bindings.
That means x:Bind child bindings benefit from the same deferred-retry behavior already used for:

- missing `DataContext`
- missing templated parent
- missing ancestor

So x:Bind does not require a separate deferred attach system.

## 13. Language Service / LSP Semantics

The language service does not just offer string completions. It mirrors the x:Bind source rules closely.

## 13.1 Source-type resolution

`XamlSemanticSourceTypeResolver.TryResolveXBindSourceType(...)` resolves:

1. explicit local `DataType=` if present on the markup
2. ambient `x:DataType` when inside a `DataTemplate`
3. otherwise the root type

This matches the compiler's scope model.

## 13.2 Completion

At top-level x:Bind completion, the service merges members from:

- current source type
- root type, if different
- current target element type
- named elements in scope

It includes:

- properties
- fields
- methods with parameters

It also supports static-member contexts such as:

- `helpers:UiHelpers.`

This is broader than regular binding completion because x:Bind can call methods and reference types directly.

## 13.3 Hover and definition

`XamlBindingNavigationService` tokenizes and semantically resolves x:Bind paths.

It supports:

- cast type tokens
- attached property owner type tokens
- static type tokens
- member symbols

The initial-segment resolution order in the language service mirrors the binder:

1. source
2. root
3. target
4. named element
5. static type

That is why hover/definition behave consistently for cases like root fallback inside a `DataTemplate`.

## 13.4 Signature help

The LSP signature help surface documents the repository's actual x:Bind syntax:

```text
x:Bind(path, Mode, BindBack, DataType, Converter, ConverterCulture, ConverterParameter, StringFormat, FallbackValue, TargetNullValue, Delay, Priority, UpdateSourceTrigger)
```

This is not a generic WinUI signature string. It reflects the Avalonia-adapted surface implemented here.

## 14. Comparison With WinUI / UWP

## 14.1 Areas that intentionally match

The following semantics are aligned in concept with WinUI/UWP `x:Bind`:

- default source is the page/control root, not `DataContext`
- `DataTemplate` usage requires a compile-time type
- named elements can be referenced directly as the first path segment
- method and function calls are part of the expression language
- static members are supported
- attached property access is supported
- casts are supported
- event binding is supported
- `BindBack` exists for `TwoWay` expressions that are not directly assignable
- `x:DefaultBindMode` changes the inherited default mode for a subtree

## 14.2 Intentional Avalonia-specific differences

This repository is not trying to reproduce WinUI byte-for-byte. It adapts the model to Avalonia.

Key differences:

| Dimension | WinUI/UWP | This repository |
| --- | --- | --- |
| Runtime integration | generated `Bindings` object and WinUI binding manager | generated evaluator + Avalonia `MultiBinding` / `InstancedBinding` |
| Default trigger behavior | `TextBox.Text` has special `LostFocus` default | `Default` normalizes to Avalonia `PropertyChanged` |
| Option naming | `ConverterLanguage` | `ConverterCulture` |
| Extra options | no Avalonia `Priority` | supports `Priority` |
| `Delay` | not part of the WinUI `x:Bind` public property list | supported as Avalonia-oriented extension |
| Local `DataType=` named argument | not part of the WinUI public surface | supported as a local source-type override |
| Root/type/name resolution | generated fields and code-behind mechanics | semantic lowering to `source`, `root`, `target`, namescope lookup |

## 14.3 WinUI features not mirrored as part of the validated surface

Notable WinUI topics that are outside the currently validated Avalonia x:Bind surface:

- WinUI `Bindings.Initialize/Update/StopTracking`
  Implemented for class-backed generated roots as a lightweight adapter over the source-gen runtime lifecycle registry
- WinUI `x:Load` + x:Bind interplay
- WinUI-specific control/property default behaviors that do not map cleanly to Avalonia

## 15. Comparison With Uno

Uno is the closest external reference point because it also implements WinUI-style `x:Bind` via source generation rather than relying on a native WinUI runtime.

## 15.1 What is similar

Both implementations do these things:

- parse an x:Bind-specific expression language
- generate typed accessors instead of using runtime reflection
- keep a dependency list so function bindings can react to upstream changes
- support `BindBack`, events, attached properties, casts, and named elements

## 15.2 What is architecturally different

Uno's implementation is centered around:

- `src/SourceGenerators/Uno.UI.SourceGenerators/XamlGenerator/Utils/XBindExpressionParser.cs`
- `src/Uno.UI/UI/Xaml/Data/Binding.cs`
- `src/Uno.UI/UI/Xaml/Data/BindingHelper.cs`

Uno rewrites x:Bind into:

- an `XBindSelector`
- an optional `XBindBack`
- a `CompiledSource`
- a `XBindPropertyPaths` array

Those are then consumed by Uno's binding engine and activated with helpers such as:

- `ApplyXBind()`
- `SuspendXBind()`

This repository instead emits:

- a typed evaluator delegate
- a typed bind-back delegate
- a typed dependency list

and feeds those into Avalonia runtime primitives through `ProvideXBindExpressionBinding(...)`.

So the conceptual model is similar, but the runtime adapter point is very different:

- Uno extends its own `Binding`/binding engine
- this repository adapts `x:Bind` onto Avalonia `BindingBase`, `MultiBinding`, and `InstancedBinding`

## 15.3 Parser differences

Uno's generator pipeline rewrites expressions to C# and then uses Roslyn syntax rewriting for nullability-aware output.

This repository splits responsibilities more explicitly:

- mini-language parsing in `XamlToCSharpGenerator.MiniLanguageParsing`
- semantic lowering in the Avalonia binder
- Roslyn analysis only after structural lowering

That separation is better aligned with this repository's architectural rules:

- parser-first
- typed semantic model
- deterministic lowering
- no string-shape heuristics

## 15.4 Surface differences relative to Uno

Uno's public docs and tests cover additional WinUI-adjacent surfaces such as:

- `x:Load` scenarios
- resource-dictionary x:Bind samples
- POCO object binding scenarios

This repository's validated surface is narrower and more Avalonia-specific.

At the same time, this repository adds Avalonia-specific extensions that are not part of Uno's documented WinUI-compat surface:

- `ConverterCulture`
- `Priority`
- `Delay`
- local `DataType=` override on the markup extension

## 16. Comparison With Avalonia `{Binding}` / `{CompiledBinding}`

## 16.1 Default source model

Avalonia regular bindings:

- default to `DataContext`

This repository's x:Bind:

- defaults to the root object outside templates
- uses typed template-item scope inside templates

This is the single biggest conceptual difference.

## 16.2 Expression power

Avalonia `{Binding}` and `{CompiledBinding}` are primarily path-based.

This x:Bind implementation adds:

- method invocation
- event binding
- direct static member access
- direct named-element references
- bind-back callbacks for computed `TwoWay` scenarios

## 16.3 Type requirements

Avalonia `{Binding}` can remain dynamic.

This x:Bind implementation requires compile-time types:

- root `x:Class`
- template `x:DataType` or explicit `DataType`

That is why it behaves more like WinUI/UWP/Uno compiled binding than like ordinary Avalonia binding.

## 16.4 Runtime transport

Even though the source semantics differ, the implementation still leans on Avalonia binding infrastructure for:

- change observation
- element name resolution through child bindings
- fallback and target-null behavior
- converter integration
- priority handling
- deferred attachment

So the rule of thumb is:

- source semantics are x:Bind-like
- transport semantics are Avalonia-like

## 16.5 TwoWay behavior

Avalonia native bindings write back through their binding expression/path engine.

This x:Bind implementation writes back through:

- synthesized assignment code
- or explicit generated bind-back callbacks

That is more flexible for computed expressions, but it also means some Avalonia binding engine behaviors are intentionally not reused verbatim.

## 17. Implementation Boundaries And Honest Caveats

The current implementation is strong and usable, but it is important to be precise about its boundaries.

### Stable and mature

- root-scope binding
- template-scope binding
- named elements
- static members
- method calls
- events
- `TwoWay` with assignable paths or explicit bind-back
- converter and formatting options
- delay and lost-focus reverse updates
- language-service support

### Extensions beyond WinUI

- `ConverterCulture`
- `Priority`
- `Delay`
- local `DataType=...` on the markup

### Known gaps or caution points

- `UpdateSourceTrigger=Explicit` now commits pending bind-back values through the generated/runtime `Update` lifecycle call
- target-object fallback exists as an implementation detail and should not be treated as a canonical WinUI semantic
- resource-dictionary and POCO-target x:Bind are not part of the validated surface

## 18. Practical Mental Model

For this repository, the simplest accurate mental model is:

- `x:Bind` is a compiled expression language, not a reflection binding path
- the compiler resolves the expression against typed `source/root/target` scopes
- the runtime observes only the extracted dependencies, not arbitrary dynamic paths
- forward flow is Avalonia `MultiBinding` plus a custom converter
- reverse flow is a custom bind-back observer
- the LSP mirrors the same scope rules so editing feels like the compiler

If that model is kept in mind, most behavior in the implementation becomes predictable.

## 19. External Reference Baseline

- Microsoft Learn x:Bind reference: <https://learn.microsoft.com/en-us/windows/uwp/xaml-platform/x-bind-markup-extension>
- Uno x:Bind documentation: <https://platform.uno/docs/articles/features/windows-ui-xaml-xbind.html>
