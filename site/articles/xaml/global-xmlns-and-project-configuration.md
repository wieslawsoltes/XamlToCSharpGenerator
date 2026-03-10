---
title: "Global XML Namespaces and Project Configuration"
---

# Global XML Namespaces and Project Configuration

AXSG supports local `xmlns` declarations, project-wide namespace mappings, and assembly-exported namespace definitions that make authored XAML shorter and tooling-aware.

## Namespace declaration forms

Supported forms include:

- `xmlns:vm="clr-namespace:MyApp.ViewModels"`
- `xmlns:controls="using:MyApp.Controls"`
- assembly-level XML namespace exports used by default-namespaced features such as `CSharp`

## Why project configuration matters

Namespace configuration is not only about parser convenience. It affects:

- type resolution during compilation
- prefix completion and definition navigation in the language service
- shorthand/default-namespace features that depend on assembly metadata exports
- transform and migration scenarios where projects remap namespace usage gradually

## Configuration sources

AXSG can read namespace and transform configuration from:

- MSBuild properties/items
- file-based configuration documents
- transform-rule documents
- framework/profile defaults

These sources are merged by explicit precedence rules in the compiler host.

## Tooling behavior

The language service understands namespace declarations as semantic navigation targets.

Examples:

- Ctrl/Cmd-click on `pages:` in `<pages:SomePage />` resolves to the `xmlns:pages` declaration
- Ctrl/Cmd-click on `SomePage` resolves to the CLR type
- default-namespace surfaces such as `<CSharp>` resolve without requiring a prefixed custom XML namespace in every file

## Design guidance

- use local aliases for clarity when the same file mixes several CLR namespaces
- use project-level mappings when the same aliases or default exports repeat across many files
- keep transform rules deterministic so the same authored prefix resolves consistently in both compiler and editor tooling

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model.md)
- [Configuration Model](../reference/configuration-model.md)
- [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules.md)
- [Navigation and Refactorings](../guides/navigation-and-refactorings.md)
