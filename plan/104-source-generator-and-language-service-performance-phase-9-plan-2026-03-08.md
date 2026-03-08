# Phase 9: Compiler Host Global Graph Analysis
Date: 2026-03-08
Status: Implemented and validated

## Goal
Reduce compiler-host global graph analysis cost by removing repeated LINQ sorting and per-recursion edge ordering while preserving include diagnostics and deterministic behavior.

## Scope
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostGlobalGraphTests.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`

## Root Cause
`AnalyzeGlobalDocumentGraph(...)` still used allocation-heavy patterns:
- `documents.OrderBy(...)` on every invocation
- `edges.OrderBy(...)` inside DFS for every visited source
- recursion path `Stack<string>` that was not used for diagnostics
- dictionary setup without capacity hints from the normalized input set

This path runs once per generator evaluation, but it processes the entire source-generated AXAML graph and scales with document count plus include count.

## Optimization Matrix
| Area | Method | Baseline issue | Optimization | Risk | Test coverage |
|---|---|---|---|---|---|
| Compiler host | `AnalyzeGlobalDocumentGraph(ImmutableArray<XamlDocumentModel>, GeneratorOptions)` | LINQ sort of all documents per call | Copy to array and `Array.Sort` once | Low | New direct global-graph tests + existing generator diagnostics tests |
| Compiler host | `AnalyzeGlobalDocumentGraph(...)` | Edge lists sorted repeatedly inside DFS | Sort each edge list once after graph build, then DFS over arrays | Low | New direct cycle/missing-include tests |
| Compiler host | `AnalyzeGlobalDocumentGraph(...)` | Recursion stack allocated and mutated but unused | Remove recursion path stack entirely | Low | New direct cycle test |
| Compiler host | `AnalyzeGlobalDocumentGraph(...)` | Default dictionary growth | Initialize with known capacities from document and edge counts | Low | Existing generator diagnostics tests |
| Tests | global graph diagnostics | No direct unit tests for internal graph analyzer | Add missing include, cycle, duplicate target tests | Low | Added |
| Perf | compiler-host global graph | No dedicated benchmark | Add synthetic graph benchmark | Low | Added |

## Validation Plan
1. Add direct unit tests for missing includes, cycles, and duplicate generated targets.
2. Add a synthetic graph benchmark for the compiler-host global graph analyzer.
3. Run focused tests for the new graph analyzer tests.
4. Run perf slice and keep the change only if benchmark-positive.
5. Run full suite.

## Measured Result
### Global graph analysis benchmark
- baseline: `202.30 ms`
- optimized: `179.18 ms`
- improvement: `11.4%`
- allocations: `362,505,640 B -> 303,808,040 B`
- allocation reduction: `16.2%`

## Validation Result
- Direct graph tests: `5 passed`
- Perf slice (`CompilerMicrobenchmarkTests`): `15 passed`
- Full suite: `1149 passed, 16 skipped, 0 failed` in `4 m 25 s`
