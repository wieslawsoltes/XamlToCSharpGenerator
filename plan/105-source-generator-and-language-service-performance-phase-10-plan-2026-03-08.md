# Phase 10 - Compiler Host Convention Inference Normalization

Date: 2026-03-08
Status: in progress

## Scope

This phase targets the remaining per-document convention hot path in `XamlSourceGeneratorCompilerHost`:

- `ApplyDocumentConventions(...)`
- `TryInferClassNameFromTargetPath(...)`
- `NormalizeRootNamespace(...)`
- `NormalizeNamespaceSegments(...)`
- `NormalizeIdentifier(...)`

These methods still perform repeated string splitting, intermediate collection materialization, and repeated namespace reconstruction per parsed document. The path runs early in the generator pipeline and is paid once per XAML document, so small per-document savings compound on larger solutions.

## Root-cause analysis

### Current costs

1. `NormalizeRootNamespace(...)`
   - calls `NormalizeNamespaceSegments(...)`
   - materializes `ImmutableArray<string>`
   - immediately `string.Join(...)`s it back into a string

2. `TryInferClassNameFromTargetPath(...)`
   - calls `NormalizeNamespaceSegments(rootNamespace).ToList()`
   - uses `Path.GetDirectoryName(...)`
   - splits directory string with `Split('/')`
   - appends normalized segments to a mutable list
   - joins them back with `string.Join(".", ...)`

3. `NormalizeNamespaceSegments(...)`
   - uses `Split('.')`
   - allocates substring arrays for every namespace normalization call

4. `NormalizeIdentifier(...)`
   - uses `new string(buffer[..].ToArray())`
   - incurs an extra array allocation before string creation

### Compatibility constraints

- Must remain `netstandard2.0` compatible.
- Must preserve authored namespace/class inference semantics exactly.
- Must preserve the current diagnostics behavior in `ApplyDocumentConventions(...)`.
- Must not regress inferred class naming for rooted paths, relative paths, and mixed separators.

## Optimization matrix

| Area | Current method | Problem | Planned change | Test gate | Benchmark |
| --- | --- | --- | --- | --- | --- |
| Convention inference | `TryInferClassNameFromTargetPath` | `Split`, `ToList`, `Path.GetDirectoryName`, repeated joins | Single-pass directory segment scanner and pooled `StringBuilder` assembly | direct inference tests | class inference benchmark |
| Root namespace normalization | `NormalizeRootNamespace` | segment array + join roundtrip | direct normalized-string builder path | direct root namespace tests | class inference benchmark |
| Namespace segment normalization | `NormalizeNamespaceSegments` | `Split('.')` array allocations | manual segment scan | direct namespace tests | class inference benchmark |
| Identifier normalization | `NormalizeIdentifier` | buffer-to-array-to-string copy | construct string from span/char buffer without intermediate array where possible | identifier-focused inference cases | class inference benchmark |
| Convention application | `ApplyDocumentConventions` | repeated work per document | keep logic but feed cheaper inference path | convention application tests | class inference benchmark |

## Testing plan

Add direct tests before optimization:

1. Namespace normalization tests
   - empty/whitespace namespace
   - invalid identifier characters
   - digit-prefixed segments
   - repeated separators and surrounding whitespace

2. Class inference tests
   - rooted target path
   - relative path with nested folders
   - mixed slash/backslash paths
   - inferred namespace appended to normalized root namespace

3. Convention application tests
   - inferred class applied only when symbol exists in compilation
   - diagnostic `AXSG0002` removed only when inference succeeds

## Benchmark plan

Add a dedicated microbenchmark:

- `CompilerHost_ClassInference_Normalization_Outperforms_Baseline`

Benchmark shape:
- pre-generate a few thousand target paths with mixed roots, nested folders, invalid identifier characters, and mixed separators
- compare current baseline helper vs optimized helper
- assert equal checksum
- require measurable speedup and lower allocations

## Exit criteria

- Direct new tests pass.
- New microbenchmark passes with equal semantics.
- Full test suite passes.
- Keep only benchmark-positive changes.

## Measured results

Focused benchmark:
- baseline: `2679.48 ms`
- optimized: `1173.58 ms`
- `56.2%` faster
- allocations: `11,002,454,440 B -> 4,592,524,840 B`
- `58.3%` lower

## Validation

Focused convention tests:
- `19 passed`

Focused benchmark:
- `CompilerHost_ClassInference_Normalization_Outperforms_Baseline` passed

Full perf slice:
- `16 passed`

Full suite:
- `1157 passed`
- `17 skipped`
- `0 failed`
- duration `4 m 16 s`
