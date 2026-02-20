# Conditional XAML Support Spec and Execution Plan

## Goal
Add full Conditional XAML support to the SourceGen compiler pipeline so conditional namespace aliases can gate:
- element materialization
- attribute/property assignments
- property elements
- resource/template/style/control-theme/include registrations

Behavior should be deterministic in generated C#, with compile-time filtering when conditions are provably false.

## Source Reference
- Microsoft Conditional XAML documentation:
  - https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/conditional-xaml

## Syntax Contract
Conditional XAML is activated via conditional namespace mapping:

```xml
xmlns:cx="https://github.com/avaloniaui?ApiInformation.IsTypePresent('Avalonia.Controls.Button')"
```

Objects/attributes/property-elements using `cx:` are conditionally applied.

Supported condition methods (with optional `ApiInformation.` prefix):
- `IsTypePresent(typeName)`
- `IsTypeNotPresent(typeName)`
- `IsPropertyPresent(typeName, propertyName)`
- `IsPropertyNotPresent(typeName, propertyName)`
- `IsMethodPresent(typeName, methodName)`
- `IsMethodNotPresent(typeName, methodName)`
- `IsEventPresent(typeName, eventName)`
- `IsEventNotPresent(typeName, eventName)`
- `IsEnumNamedValuePresent(enumTypeName, valueName)`
- `IsEnumNamedValueNotPresent(enumTypeName, valueName)`
- `IsApiContractPresent(contractName[, majorVersion[, minorVersion]])`
- `IsApiContractNotPresent(contractName[, majorVersion[, minorVersion]])`

## Pipeline Changes
1. Parse stage
- Normalize namespace URI to base URI (strip `?condition`) for type resolution.
- Capture parsed conditional expression metadata and attach it to:
  - `XamlObjectNode`
  - `XamlPropertyAssignment`
  - `XamlPropertyElement`
  - resource/template/style/control-theme/include definitions
- New diagnostic:
  - `AXSG0120` invalid conditional expression.

2. Bind stage
- Evaluate condition against compilation symbols when possible.
- If condition evaluates `false` at compile-time, skip binding branch (no semantic warning noise for unreachable nodes).
- Preserve condition metadata in resolved model for runtime guards where needed.

3. Emit stage
- Emit only resolved branches that passed compile-time condition evaluation.
- Ensure module-initializer registrations, object graph population, and named field generation contain no condition-false artifacts.

## Diagnostics
- `AXSG0120` (Warning): invalid conditional expression syntax/arity/arguments.

## Acceptance Criteria
- Conditional namespaces no longer break type/property resolution.
- Condition-false branches are removed at bind time when deterministically false.
- Generated output only includes condition-true branches.
- Resource/template/style/theme/include registrations honor conditions.
- Parser/generator/runtime tests validate positive/negative paths.
- Catalog sample contains a dedicated Conditional XAML page with live examples.
- README documents syntax, supported methods, and behavior.

## Execution Plan
1. Introduce conditional expression model + parse/validation helpers.
2. Wire condition metadata through all relevant parser models.
3. Add compile-time condition evaluator in binder and skip false branches.
4. Add emitter/model integration coverage and tests for conditional branch pruning.
5. Add catalog sample page and README section.
6. Run full test/build validation and publish implementation report.
