---
title: "Testing and Validation"
---

# Testing and Validation

The repo validates compiler/runtime/tooling behavior through:

- parser and binder unit tests
- source-generator output verification tests
- runtime behavior tests
- language-service and LSP integration tests
- differential backend/runtime comparison tests
- package/build integration tests

This matters because AXSG intentionally changes XAML semantics in advanced areas such as compiled bindings, inline C#, shorthand expressions, and hot reload.
