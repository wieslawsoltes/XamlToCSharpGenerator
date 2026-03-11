---
title: "Language Service and Compiler Performance"
---

# Language Service and Compiler Performance

AXSG is optimized around real-time editing. The compiler and language-service layers avoid reflection-heavy and allocation-heavy paths where practical, while keeping the generator stack compatible with `netstandard2.0`.

## Main performance strategies

- span-based tokenization for XAML, selectors, and markup mini-languages
- loop-based hot paths instead of LINQ in parser/compiler/lang-service hotspots
- cache reuse for project discovery, XAML source snapshots, and language-service analysis
- explicit weak-reference retention for large parsed XML documents where warm reuse matters but memory pressure must stay bounded
- generator/runtime emission shapes chosen to keep hot reload and EnC stable

## Compiler hotspots

The biggest compiler-host wins so far came from:

- snapshot normalization and duplicate-path handling
- include URI resolution
- configuration precedence parsing
- transform-configuration aggregation
- class/namespace inference

These paths run across many additional files and can dominate large solution builds if they allocate aggressively.

## Language-service hotspots

The main editor-facing hotspots are:

- project compilation startup
- cross-file reference discovery
- inline C# projection/navigation
- semantic-token and inlay-hint reuse

That is why the current implementation defers `MSBuildWorkspace` creation until the first semantic request and avoids spinning up the server for arbitrary non-XAML editing.

## Validation policy

AXSG does not accept performance work without validation. Each accepted optimization phase adds:

- focused regression tests for semantics
- microbenchmarks for the changed hotspot
- full-suite validation before the branch is considered acceptable

See also:

- [Performance and Benchmarking](performance-and-benchmarking/)
- [Testing and Validation](testing-and-validation/)
- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model/)
