# SourceGen First-Class Event Binding - Spec and Implementation Plan

## Goal
Add first-class event binding support to the Avalonia SourceGen compiler so AXAML events can bind directly to view-model commands/methods without code-behind handler wiring, while preserving existing event-handler syntax (`Click="OnClick"`).

## Scope
- New event-binding syntax in AXAML event attributes.
- Binder support with diagnostics and compile-time validation where type info exists.
- Emitter support that generates stable, idempotent event handlers.
- Runtime support for command/method invocation and source resolution.
- Generator/runtime tests.
- Catalog sample page demonstrating event binding scenarios.

## Non-Goals
- Replacing existing code-behind event-handler syntax.
- Requiring behaviors package.
- Supporting arbitrary script/lambda bodies in XAML event attributes.

## Public Syntax
Event attribute values support a new markup extension:

```xml
<Button Click="{EventBinding SaveCommand}" />
<Button Click="{EventBinding Command=SaveCommand}" />
<Button Click="{EventBinding Command=SaveCommand, Parameter=42}" />
<Button Click="{EventBinding Command={Binding SelectedSaveCommand}, Parameter={Binding SelectedItem}}" />
<Button Click="{EventBinding Method=Save}" />
<Button Click="{EventBinding Method=SaveWithArgs, PassEventArgs=True}" />
```

Supported arguments:
- `Command` or `Path`: command path to resolve on source object.
- `Method`: method name to invoke on source object.
- `Parameter` or `CommandParameter`: optional parameter for command/method.
- `PassEventArgs`: optional `true|false`; when `true` and no explicit parameter is provided, event args become the parameter.
- `Source`: `DataContext`, `Root`, or `DataContextThenRoot` (default).

Rules:
- Exactly one of `Command`/`Path` or `Method` must be supplied.
- Existing plain handler syntax remains unchanged (`Click="OnClick"`).

## Compiler Semantics
### Binding phase
For each event assignment:
1. Resolve CLR/routed event as today.
2. If value parses as `{EventBinding ...}`:
- Parse event-binding arguments.
- Resolve delegate signature from event delegate type.
- Generate deterministic wrapper method name (`__AXSG_EventBinding_<Event>_<Line>_<Column>`).
- Emit `ResolvedEventSubscription` with wrapper method name.
- Attach event-binding metadata to the subscription.
3. Otherwise, keep current handler-method path.

### Validation
Diagnostics (AXSG06xx band):
- Invalid event-binding syntax/shape.
- Both `Command` and `Method` specified.
- Neither specified.
- Invalid `Source` token.
- Unsupported event delegate shape for generated wrapper.
- Invalid command/method path tokens.

When `x:DataType` is available and source mode includes `DataContext`:
- Validate command/member path root segment exists.
- Validate method name exists for at least one supported signature.

### Emission
- Generate normal event subscription attach/detach using generated wrapper method names.
- Generate one wrapper instance method per event binding on the partial root class.
- Wrapper forwards to runtime helper with:
- root instance,
- inferred sender/event args from delegate parameters,
- source mode,
- command/method metadata,
- parameter path/literal metadata,
- `PassEventArgs` flag.

This preserves idempotent `-=`/`+=` behavior across hot reload reapply.

## Runtime Semantics
Add `SourceGenEventBindingRuntime` with APIs to:
1. Resolve source object (`DataContext`, `Root`, `DataContextThenRoot`).
2. Resolve command by path and execute with `CanExecute` checks.
3. Resolve method by name and invoke matching signature.
4. Resolve parameter from literal, source path, or event args fallback.

Behavior principles:
- No app crash on failed event-binding invocation; fail silently with debug logging path.
- Command path and method path resolution are case-sensitive first, then case-insensitive fallback.
- Supports dotted member paths (`Parent.Child.Command`).

## Data Model Changes
- Extend `ResolvedEventSubscription` with optional `ResolvedEventBindingDefinition` metadata.
- Add new core models:
- `ResolvedEventBindingDefinition`
- `ResolvedEventBindingParameter`
- `ResolvedEventBindingTargetKind`
- `ResolvedEventBindingSourceMode`

## Test Plan
### Generator tests
- Emits wrapper method + event `-=`/`+=` wiring for command binding.
- Emits wrapper method + event wiring for method binding.
- Invalid event-binding argument combinations emit diagnostics.
- Existing plain handler syntax still emits legacy wiring.

### Runtime tests
- Command invocation with/without parameter.
- Method invocation (`void M()`, `void M(object?)`, `void M(object?, object?)`).
- Source mode behavior (`DataContext`, `Root`, fallback).

### Integration/sample
- Catalog sample page with command/method examples.
- Build/run smoke validation.

## Implementation Steps
1. Add core models and extend `ResolvedEventSubscription`.
2. Implement binder parse/validate path for `{EventBinding ...}`.
3. Implement emitter wrapper method generation + wiring reuse.
4. Add runtime `SourceGenEventBindingRuntime`.
5. Add generator/runtime tests.
6. Add catalog sample tab/page + view model examples.
7. Build and test full solution paths touched.

## Acceptance Criteria
- AXAML event bindings execute commands/methods without code-behind method names.
- Existing event-handler syntax still works.
- Generated code remains idempotent under re-apply/hot reload.
- Tests cover syntax, diagnostics, emission, and runtime dispatch.
- Catalog sample demonstrates working event bindings.
