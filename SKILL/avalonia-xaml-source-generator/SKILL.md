---
name: avalonia-xaml-source-generator
description: Comprehensive guide for choosing, installing, wiring, and troubleshooting XamlToCSharpGenerator (AXSG) packages in Avalonia projects. Use when Codex needs to add AXSG to an Avalonia app, select among the public AXSG packages or tooling artifacts, configure SourceGen MSBuild switches, set up runtime or hot reload support, embed AXSG editor/language-service tooling, or compose lower-level compiler/runtime packages in this repository.
---

# Avalonia Xaml Source Generator

Choose the smallest public AXSG artifact set that satisfies the task, then wire the matching MSBuild and runtime pieces. Prefer app-facing packages first and drop to low-level compiler/runtime packages only when the request is explicitly about custom composition, framework work, or tooling internals.

## Workflow

1. Classify the request before recommending packages.
- Standard Avalonia app: start with `references/package-surface.md` and `references/avalonia-app-setup.md`.
- Hot reload, hot design, runtime fallback, or loader issues: read `references/avalonia-app-setup.md` and `references/configuration-switches.md`.
- VS Code, LSP, or in-app editor work: read `references/tooling-and-lsp.md`.
- Custom compiler host, framework profile, parser reuse, or advanced package composition: read `references/advanced-composition.md`.

2. Prefer the highest-level install surface that matches the request.
- Use `XamlToCSharpGenerator` for normal Avalonia app adoption.
- Use `XamlToCSharpGenerator.Build` only when the user explicitly wants MSBuild-only integration or custom target layering.
- Use low-level compiler/runtime/tooling packages only when the task is about embedding or extending AXSG itself.

3. Include the required wiring, not just the package name.
- Show the package or tool install command.
- Show `AvaloniaXamlCompilerBackend` set to `SourceGen` for app setup.
- Show runtime bootstrap with `UseAvaloniaSourceGeneratedXaml()` when runtime support matters.
- Mention build-side automatic behavior when relevant: SourceGen disables the legacy Avalonia XAML compilation path and the Avalonia name generator.

## Guardrails

- Treat the public surface in `references/package-surface.md` as the supported install matrix.
- Do not recommend internal support projects such as `XamlToCSharpGenerator.DotNetWatch.Proxy` as consumer dependencies.
- Do not mix default Avalonia `XamlIl` instructions with AXSG setup in the same recipe without calling out the backend switch explicitly.
- Preserve the repository AOT/trimming contract. Do not suggest reflection-based runtime/emitted-code workarounds.
- For repo-local examples, mirror the established `SourceGenCrudSample` project pattern instead of inventing new item names or imports.

## References

- `references/package-surface.md`: full public package, tool, and VS Code artifact inventory.
- `references/avalonia-app-setup.md`: standard app setup, bootstrap, and repo-local development wiring.
- `references/tooling-and-lsp.md`: language service, CLI LSP host, VS Code extension, and in-app editor guidance.
- `references/advanced-composition.md`: compiler stack layering and low-level package combinations.
- `references/configuration-switches.md`: canonical AXSG MSBuild switches grouped by scenario.
