---
title: "Performance and Benchmarking"
---

# Performance and Benchmarking

AXSG has an explicit performance program. The compiler and language service are expected to stay usable in real projects, so performance work is treated as normal engineering rather than occasional cleanup.

## What is measured

The repo tracks hot paths across:

- parser throughput
- compiler-host discovery, normalization, and graph analysis
- binder and emitter allocation behavior
- language-service request latency
- cross-file reference discovery and cache churn

## How it is validated

The repository uses several complementary layers:

- opt-in microbenchmarks inside the test project (`AXSG_RUN_PERF_TESTS=true`)
- differential build/runtime harnesses
- regression tests on behavior that must stay stable while hot paths are refactored
- phase-by-phase plan documents with measured before/after results

## Why the plans matter

The performance plans in `/plan` are not just notes. They serve as a record of:

- what hotspot was targeted
- what code changed
- what benchmark proved the change was worthwhile
- which candidate optimizations were rejected because they did not actually help

That keeps the project honest: speculative “optimizations” without measurement do not stay.

## Related docs

- [Language Service and Compiler Performance](language-service-and-compiler-performance.md)
- [Testing and Validation](testing-and-validation.md)
- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model.md)
