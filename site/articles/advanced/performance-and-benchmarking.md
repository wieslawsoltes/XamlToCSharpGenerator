---
title: "Performance and Benchmarking"
---

# Performance and Benchmarking

AXSG has an explicit performance program around parser hot paths, compiler-host setup, emitter generation, and language-service request latency.

The repo includes:

- microbenchmarks inside the test project gated by `AXSG_RUN_PERF_TESTS=true`
- differential build harnesses
- regression tests that lock perf-sensitive behavior before optimizations land

Look in the plan folder for the phased optimization history and benchmark matrices.
