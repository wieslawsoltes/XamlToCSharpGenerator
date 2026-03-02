# Emitter Redundancy Optimization Plan (2026-03-01)

## Goal
Reduce per-artifact generated C# size and compile overhead by removing duplicated helper code emitted into every generated class, while preserving runtime behavior.

## Findings

| Area | Current redundancy | Impact | Fix |
|---|---|---|---|
| Generated helper methods | Each generated file emits a large helper block (`__TryClearCollection`, include resolution, dictionary merge/add, init, name-scope) | Large source bloat across all generated artifacts, slower parsing/compilation, harder diagnostics | Move helper logic into shared runtime helper class and emit calls to shared APIs |
| Include service provider helper class | Per-file nested `__SourceGenIncludeLoadServiceProvider` class emitted repeatedly | Additional repeated type declarations and method bodies | Move to shared runtime helper implementation |
| BaseUri constructor helper | Per-file `__TryCreateUri` implementation emitted repeatedly | Repeated boilerplate | Move to shared runtime helper `TryCreateUri` |
| Dictionary/include handling | Emitted local methods for style/resource include resolution repeated per file | Repeated complex logic in generated outputs | Centralize into runtime helper and pass document URI |

## Scope
1. Add `SourceGenObjectGraphRuntimeHelpers` in runtime Avalonia layer.
2. Rewire Avalonia emitter to call runtime helper methods.
3. Remove redundant helper block emission from generated code.
4. Update generator tests to validate new emitted shape.
5. Run targeted generator/runtime test set.

## Non-goals
- Functional behavior changes to binding semantics.
- Altering hot reload protocol/runtime state model.

## Acceptance Criteria
- Generated output no longer contains per-file local helper method declarations for collection/dictionary/include/init/name-scope helpers.
- Generated output still compiles and keeps include/style merge behavior.
- Existing generator tests pass after expectation updates.
- No regressions in runtime helper behavior for include resolution and dictionary merge/add paths.

## Implementation Steps
1. Introduce runtime helper service with centralized implementations.
2. Change emitter call sites and generated call templates to use runtime helper APIs.
3. Remove now-obsolete helper method emissions.
4. Update brittle source-shape assertions in tests.
5. Run tests and capture final diff summary.

## Next Phase (Call-Site Compaction)
1. Add generated alias for runtime helper type:
   - `using __AXSGObjectGraph = global::XamlToCSharpGenerator.Runtime.SourceGenObjectGraphRuntimeHelpers;`
2. Rewrite generated helper call sites to alias form (`__AXSGObjectGraph.*`) to reduce repeated fully-qualified tokens.
3. Update generator source-shape tests to assert alias-based emission.
4. Re-run focused generator/runtime tests.

## Next Phase (Known-Type Registration Compaction)
1. Add batch registration API in `SourceGenKnownTypeRegistry`:
   - `RegisterTypes(params Type[] types)`
2. Replace per-known-type emitted registration lines with one emitted batch call.
3. Validate generator/runtime tests for unchanged behavior.

## Next Phase (Registry Lifecycle Compaction)
1. Add a centralized runtime registry-reset API:
   - `SourceGenArtifactRegistryRuntime.ResetDocumentRegistries(string documentUri)`
2. Replace repeated per-registry emitted clear/unregister lines with one emitted runtime call.
3. Update source-shape tests to validate the compact emission contract.
4. Add runtime guard test proving document-scoped reset clears only targeted URI entries.
