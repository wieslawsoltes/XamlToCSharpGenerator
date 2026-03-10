---
title: "Testing and Validation"
---

# Testing and Validation

AXSG changes both compile-time semantics and runtime behavior, so the test strategy is intentionally broad.

## Test layers

### Unit tests

These lock small parser, binder, emitter, runtime-helper, and language-service rules.

### Source-generator output tests

These validate the generated C# shape directly. They are important for:

- stable generated identities
- hot-reload-safe helper emission
- runtime descriptor registration
- exact lowering behavior for new language features

### Runtime tests

These verify generated code and runtime helpers cooperate correctly when features are executed, not just emitted.

### Language-service and LSP integration tests

These make sure editor features follow compiler semantics and survive transport/projection layers.

### Differential and build integration tests

These validate whole-project behavior, package outputs, build graph wiring, and compatibility scenarios.

## Why this matters for docs readers

If you are changing AXSG or integrating deeply with it, the test suite tells you where confidence comes from. A feature page might look simple, but its behavior may be locked by several kinds of tests across compiler, runtime, and editor layers.

## Related docs

- [Performance and Benchmarking](performance-and-benchmarking.md)
- [Compiler Pipeline](../architecture/compiler-pipeline.md)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload.md)
