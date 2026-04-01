# Language Service Framework Modularization Plan

Date: 2026-04-01

## Goal

Split built-in language-service framework support into separate projects so each framework can be maintained, versioned, and extended independently, while still shipping:

- one default language server tool,
- one default VS Code extension,
- one default in-process language-service experience.

The resulting architecture must also let custom hosts compose their own framework registry without forking the core language-service project.

## Current State

The current multi-framework language-service work is functionally correct, but the framework boundary is still centralized inside `XamlToCSharpGenerator.LanguageService`:

- `XamlLanguageFrameworkCatalog` hardcodes Avalonia, WPF, WinUI, and MAUI descriptors.
- Passive framework profiles for WPF, WinUI, and MAUI live in the same file as the registry and resolution logic.
- Framework inference heuristics are also centralized in the same file.
- The default server/editor packaging is implicitly coupled to those in-project hardcoded descriptors.

That works for first-party support, but it makes third-party framework extensions awkward because the extension point is not a project boundary.

## Target Architecture

### Shared framework composition project

Add `XamlToCSharpGenerator.LanguageService.Framework` to host:

- `XamlLanguageFrameworkInfo`
- `IXamlLanguageFrameworkProvider`
- `XamlLanguageFrameworkRegistry`
- `XamlLanguageFrameworkRegistryBuilder`
- `XamlLanguageFrameworkResolver`
- reusable passive analysis-only framework profile/build-contract helpers

This project becomes the single language-service contract for built-in and custom framework support.

### One project per built-in framework

Add:

- `XamlToCSharpGenerator.LanguageService.Framework.Avalonia`
- `XamlToCSharpGenerator.LanguageService.Framework.Wpf`
- `XamlToCSharpGenerator.LanguageService.Framework.WinUI`
- `XamlToCSharpGenerator.LanguageService.Framework.Maui`

Each project owns only one framework’s:

- `XamlLanguageFrameworkInfo`
- parser/profile configuration
- framework detection heuristics from project/compilation/document markers

### Default built-in bundle

Add `XamlToCSharpGenerator.LanguageService.Framework.All` as the first-party bundle that aggregates the four built-in providers into one registry.

`XamlToCSharpGenerator.LanguageService` will depend on this bundle only for the existing default constructors. New overloads will allow explicit registry injection.

## Implementation Plan

1. Create the new framework composition project and move the framework contracts/resolver there.
2. Move Avalonia language-service support into its own provider project backed by `AvaloniaFrameworkProfile`.
3. Move WPF, WinUI, and MAUI passive analysis-only support into their own provider projects.
4. Replace the old hardcoded catalog with a registry-driven resolver.
5. Update `XamlCompilerAnalysisService` and `XamlLanguageServiceEngine` to accept a registry and keep the current default experience through the built-in bundle.
6. Keep project discovery, URI navigation, namespace-prefix discovery, completion, and type indexing wired against registry-provided framework metadata.
7. Add tests that verify:
   - the default built-in registry contains all four frameworks,
   - explicit custom registries can limit available frameworks,
   - existing multi-framework behavior still works end to end.

## Expected Outcome

After this refactor:

- each built-in framework LS support lives in its own `.csproj`,
- the default server/editor still ships all four frameworks,
- custom hosts can register only the framework providers they want,
- future framework additions no longer require editing a monolithic central catalog file inside the core language-service project.
