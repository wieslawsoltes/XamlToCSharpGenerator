---
title: "Global XML Namespaces and Project Configuration"
---

# Global XML Namespaces and Project Configuration

AXSG supports both local `xmlns` declarations and project-wide XML namespace mappings.

## Namespace declaration forms

Supported forms include:

- `xmlns:vm="clr-namespace:MyApp.ViewModels"`
- `xmlns:controls="using:MyApp.Controls"`
- assembly-level XML namespace exports used by default-namespaced features such as `CSharp`

## Global namespace mappings

Project-level namespace mapping matters when you want reusable XAML without repeating local aliases in every file.

AXSG can consume namespace and transform configuration from:

- MSBuild properties/items
- repo or project configuration files
- compiler-host configuration sources merged by precedence

## Why this matters

Namespace configuration feeds both:

- compilation/binding resolution
- editor tooling such as prefix definitions, type completion, hover, and rename

This is why Ctrl/Cmd-click on a prefix can resolve back to the `xmlns:` declaration and why the same prefix can still map to the correct CLR type on the qualified element/member token.

## Related docs

- [Compiler Host and Project Model](../concepts/compiler-host-and-project-model)
- [Configuration Model](../reference/configuration-model)
- [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules)
