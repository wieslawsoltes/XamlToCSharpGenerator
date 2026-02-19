# Test Strategy and Acceptance

## 1. Test Layers

### 1.1 Unit Tests
1. Parser correctness for `x:Class`, names, malformed XML, namespace edge cases.
2. Semantic binder resolution/fallback behavior.
3. Emitter output shape and sanitization.

### 1.2 Generator Driver Tests
1. Roslyn incremental generation with AdditionalFiles.
2. Backend switch option handling.
3. Diagnostic IDs and severity assertions.

### 1.3 Integration Tests
1. Sample Avalonia app builds with `AvaloniaXamlCompilerBackend=SourceGen`.
2. Validate generated files and absence of build-task Xaml compilation invocation.
3. URI registry load path integration through runtime helper contracts.

### 1.4 Regression/Parity Tests
1. Behavior-by-behavior scenario suite aligned with parity matrix.
2. Snapshot comparisons for deterministic generated output.
3. Negative tests for diagnostics equivalence.

### 1.5 Performance Tests
1. Full build timing baseline vs existing backend.
2. Incremental edit timing (single XAML file changed).
3. Memory allocation sampling in parser/binder/emitter pipeline.

## 2. Required Scenarios
1. Malformed XML reports `AXSG0001` with file location.
2. Missing `x:Class` reports `AXSG0002` warning.
3. Named element unresolved type reports `AXSG0100` fallback.
4. Generated source includes `InitializeComponent` and module-initializer registry.
5. TargetPath metadata affects generated URI path deterministically.

## 3. Acceptance Criteria
A release candidate is accepted only if:
1. Unit and generator test suites are green.
2. Integration tests pass across representative Avalonia app shapes.
3. Parity matrix mandatory items are closed or explicitly waived.
4. Deterministic generation checks pass on repeated builds.
5. No critical diagnostics regressions against baseline.

## 4. CI Expectations
1. Run all tests on pull requests.
2. Run parity and performance suites nightly.
3. Publish generated-source artifacts for diagnostics when failures occur.
